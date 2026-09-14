using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RunicInventory.Api;
using RunicInventory.Capabilities;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory.Integration
{
    internal sealed class EquipmentAdditionState
    {
        internal EquipmentAdditionState(
            Player player,
            Inventory inventory,
            InventoryRoleKind? expectedRole,
            IReadOnlyList<ItemDrop.ItemData> before)
        {
            Player = player;
            Inventory = inventory;
            ExpectedRole = expectedRole;
            Before = before;
        }

        internal Player Player { get; }
        internal Inventory Inventory { get; }
        internal InventoryRoleKind? ExpectedRole { get; }
        internal IReadOnlyList<ItemDrop.ItemData> Before { get; }
    }

    internal readonly struct StackCapacityKey : IEquatable<StackCapacityKey>
    {
        internal StackCapacityKey(string sharedName, int quality, int worldLevel)
        {
            SharedName = sharedName ?? string.Empty;
            Quality = quality;
            WorldLevel = worldLevel;
        }

        internal string SharedName { get; }
        internal int Quality { get; }
        internal int WorldLevel { get; }
        public bool Equals(StackCapacityKey other) =>
            Quality == other.Quality && WorldLevel == other.WorldLevel &&
            string.Equals(SharedName, other.SharedName, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StackCapacityKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(SharedName);
                hash = (hash * 397) ^ Quality;
                return (hash * 397) ^ WorldLevel;
            }
        }
    }

    internal sealed partial class InventoryRuntime : IInventoryTopologyService, IInventoryProtectionService,
        IInventoryStatusService, IItemProtectionQuery, IDisposable
    {
        private const int MaximumProtectionDiagnostics = 32;
        private readonly bool _batch;
        private readonly int _mainThreadId;
        private readonly Dictionary<StackCapacityKey, int> _stackCapacity =
            new Dictionary<StackCapacityKey, int>();
        private readonly HashSet<string> _protectionDiagnostics =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly object _protectionDiagnosticGate = new object();
        private Player _player;
        private Inventory _inventory;
        private TopologyLayout _layout;
        private PersistedTopologyState _persisted;
        private InventoryTopologySnapshot _snapshot;
        private PickupFilterSet _filters;
        private InventoryAuthorityMode _mode;
        private string _reasonCode = "runtime.not-initialized";
        private string _statusText = "Runic Inventory: waiting for the local player.";
        private long _generation;
        private int _freePickupSlots;
        private float _cachedWeight;
        private bool _topologyActive;
        private bool _playerLoadInProgress;
        private bool _loadMetadataRefreshed;
        private int _equipmentTransitionDepth;
        private int _repairAllowanceDepth;
        private int _mutationActive;
        private bool _equipmentRefreshPending;
        private bool _equipmentTransitionFaulted;
        private bool _disableCleanupPending;
        private bool _rebuilding;
        private bool _disposed;
        private float _nextMessageTime;
        private readonly Rect[] _roleSlotRects = new Rect[TopologyLayout.RequiredWidth];
        private static readonly string[] RoleLabels =
            { "Helmet", "Chest", "Legs", "Cape", "Utility", "Quick 1", "Quick 2", "Quick 3" };

        internal InventoryRuntime(bool batch)
        {
            _batch = batch;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _filters = PickupFilterSet.Parse(InventoryConfig.FilteredPickupItems?.Value ?? string.Empty);
            _mode = batch ? InventoryAuthorityMode.BatchInert : InventoryAuthorityMode.Unavailable;
            _reasonCode = batch ? "authority.batch-inert" : "player.not-loaded";
        }

        public string ProviderId => Plugin.ModuleId;
        internal bool TopologyActive => _topologyActive && !_disposed && !_playerLoadInProgress &&
                                        _mode == InventoryAuthorityMode.AuthoritativeLocal &&
                                        IsAuthoritativeLocal(_player) && LiveDimensionsMatch();
        internal InventoryAuthorityMode Mode => _mode;
        internal bool AcceptsInput => CanEnforceLocks();

        internal void Initialize()
        {
            if (_batch)
            {
                RebuildStatusOnly();
                return;
            }
            Rebind(Player.m_localPlayer, "startup");
        }

        internal void Tick()
        {
            if (_disposed || _batch) return;
            if (!ReferenceEquals(_player, Player.m_localPlayer)) Rebind(Player.m_localPlayer, "local-player-changed");
            if (!_player || _inventory == null) return;
            if (_disableCleanupPending)
            {
                if (InventoryConfig.Enabled?.Value ?? false)
                {
                    _disableCleanupPending = false;
                    Rebind(Player.m_localPlayer, "disable-cleanup-cancelled");
                }
                else
                {
                    if (!TryCompleteDisableCleanup()) return;
                    Rebind(Player.m_localPlayer, "disable-cleanup-completed");
                }
                if (!_player || _inventory == null) return;
            }
            if (_layout != null && !LiveDimensionsMatch())
            {
                FailClosed("topology.runtime-dimension-changed");
                return;
            }
            bool authoritative = IsAuthoritativeLocal(_player);
            if ((_mode == InventoryAuthorityMode.AuthoritativeLocal && !authoritative) ||
                (_mode == InventoryAuthorityMode.RemoteDedicatedCompatibility && authoritative))
            {
                Rebind(_player, "authority-changed");
                if (!_player || _inventory == null) return;
            }
            if (!AcceptsInput) return;

            bool inventoryVisible = InventoryGui.instance != null && InventoryGui.IsVisible();
            if (inventoryVisible)
            {
                if (KeyboardInput.ShortcutDown(InventoryConfig.Sort.Value)) SortSelectedRows("keyboard");
                if (KeyboardInput.ShortcutDown(InventoryConfig.ToggleLock.Value)) ToggleFocusedLock("keyboard");
            }
            else if (ValheimContracts.PlayerMayTakeInput(_player))
            {
                if (KeyboardInput.ShortcutDown(InventoryConfig.Quick1.Value)) UseQuick(InventoryRoleKind.Quick1, "keyboard");
                else if (KeyboardInput.ShortcutDown(InventoryConfig.Quick2.Value)) UseQuick(InventoryRoleKind.Quick2, "keyboard");
                else if (KeyboardInput.ShortcutDown(InventoryConfig.Quick3.Value)) UseQuick(InventoryRoleKind.Quick3, "keyboard");
            }
            ControllerInput.Tick(this, inventoryVisible, !inventoryVisible && ValheimContracts.PlayerMayTakeInput(_player));
        }

        internal void Draw()
        {
            if (_disposed || _batch || InventoryGui.instance == null || !InventoryGui.IsVisible()) return;
            if (ValheimContracts.InventoryModalVisible()) return;
            if (TopologyActive)
            {
                if (InventoryConfig.ShowRoleLabels?.Value ?? true) DrawRoleOverlay();
            }
            if (CanEnforceLocks()) DrawLockedSlotOverlay();
            if (InventoryConfig.ShowInventoryStatus?.Value ?? false)
            {
                float width = Math.Min(500f, Math.Max(300f, Screen.width - 20f));
                GUI.Box(new Rect(Math.Max(10f, Screen.width - width - 10f), 72f, width, 190f), _statusText);
            }
        }

        private void DrawRoleOverlay()
        {
            InventoryGrid grid = InventoryGui.instance?.m_playerGrid;
            if (!grid || _layout == null ||
                !ValheimContracts.TryBottomRowScreenRects(grid, _layout.SpecialRow, _roleSlotRects)) return;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            try
            {
                GUI.depth = -650;
                Rect first = _roleSlotRects[0];
                Rect last = _roleSlotRects[_roleSlotRects.Length - 1];
                Rect outline = new Rect(first.xMin - 2f, Math.Min(first.yMin, last.yMin) - 2f,
                    last.xMax - first.xMin + 4f, Math.Max(first.yMax, last.yMax) - Math.Min(first.yMin, last.yMin) + 4f);
                GUI.color = new Color(0.70f, 0.43f, 0.13f, 0.48f);
                GUI.DrawTexture(new Rect(outline.xMin, outline.yMin, outline.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(outline.xMin, outline.yMax - 1f, outline.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(outline.xMin, outline.yMin, 1f, outline.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(outline.xMax - 1f, outline.yMin, 1f, outline.height), Texture2D.whiteTexture);

                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Math.Max(8, Math.Min(11, (int)(_roleSlotRects[0].width / 7f))),
                    clipping = TextClipping.Clip,
                    wordWrap = false
                };
                for (int i = 0; i < _roleSlotRects.Length; i++)
                {
                    Rect slot = _roleSlotRects[i];
                    GUI.color = new Color(0f, 0f, 0f, 0.70f);
                    GUI.DrawTexture(
                        new Rect(slot.x + 1f, slot.y + 1f, slot.width - 2f, 17f),
                        Texture2D.whiteTexture);
                    Rect label = new Rect(slot.x + 2f, slot.y + 1f, slot.width - 4f, 16f);
                    style.normal.textColor = new Color(0.05f, 0.03f, 0.01f, 0.92f);
                    GUI.Label(new Rect(label.x + 1f, label.y + 1f, label.width, label.height), RoleLabels[i], style);
                    style.normal.textColor = new Color(1f, 0.78f, 0.34f, 0.98f);
                    GUI.Label(label, RoleLabels[i], style);
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private void DrawLockedSlotOverlay()
        {
            InventoryGrid grid = InventoryGui.instance?.m_playerGrid;
            if (!grid || _layout == null || _persisted == null) return;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            try
            {
                GUI.depth = -660;
                GUI.color = new Color(1f, 0.84f, 0.08f, 1f);
                foreach (InventorySlotCoordinate coordinate in _persisted.LockedSlots())
                {
                    if (!ValheimContracts.TrySlotScreenRect(
                            grid, coordinate.X, coordinate.Y, out Rect slot)) continue;
                    DrawOutline(new Rect(
                        slot.xMin - 2f,
                        slot.yMin - 2f,
                        slot.width + 4f,
                        slot.height + 4f), 4f);
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private static void DrawOutline(Rect rect, float thickness)
        {
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
        }

        internal void OnConfigurationChanged()
        {
            if (_disposed || _restoringEnabled) return;
            if (!(InventoryConfig.Enabled?.Value ?? false) && !CanDisable(out string disableReason))
            {
                RestoreEnabled(disableReason);
                return;
            }
            PickupFilterSet next = PickupFilterSet.Parse(InventoryConfig.FilteredPickupItems.Value);
            _filters = next;
            if (next.Truncated) Diagnostics.Warn("Pickup filter exceeded 128 safe unique rules; additional entries were ignored.");
            ControllerBindings.Invalidate();
            ControllerChordSession.Reset();
            if (InventoryConfig.Enabled?.Value ?? false) _disableCleanupPending = false;
            Rebind(Player.m_localPlayer, "configuration-changed");
        }

        internal void OnLocalPlayerChanged(Player player) => Rebind(player, "set-local-player");

        internal void OnPlayerLoadStarted(Player player)
        {
            if (_disposed || player != _player || player != Player.m_localPlayer) return;
            _playerLoadInProgress = true;
            _loadMetadataRefreshed = false;
            // Player.Load merges custom data rather than clearing it. Do not let freshly
            // bootstrapped or previously loaded row markers leak into the incoming character.
            player.m_customData?.Remove(DedicatedRowPlan.MetadataKey);
            player.m_customData?.Remove(DedicatedRowPlan.QuiverMetadataKey);
            player.m_customData?.Remove(TopologyPersistenceCodec.MetadataKey);
        }

        internal void OnPlayerLoadCompleted(Player player)
        {
            if (_disposed || player != Player.m_localPlayer) return;
            _playerLoadInProgress = false;
            _loadMetadataRefreshed = false;
            Rebind(player, "player-load-completed");
        }

        internal void OnPlayerLoadFaulted(Player player)
        {
            if (_disposed || player != _player) return;
            _playerLoadInProgress = false;
            _loadMetadataRefreshed = false;
            FailClosed("player.load-faulted");
        }

        internal void OnNativeInventorySizeChanged(Player player, int previousHeight)
        {
            if (_disposed || _batch || !player || player != Player.m_localPlayer) return;
            Inventory inventory = player.GetInventory();
            if (inventory == null || !ReferenceEquals(inventory, _inventory))
            {
                Rebind(player, "native-inventory-resize-rebind");
                return;
            }

            int currentWidth = inventory.GetWidth();
            int currentHeight = inventory.GetHeight();
            if (currentHeight == previousHeight)
            {
                if (_layout != null && !_layout.MatchesNativeDimensions(currentWidth, currentHeight))
                    Rebind(player, "native-inventory-resize-noop");
                return;
            }
            if (!IsAuthoritativeLocal(player) || _layout == null || _persisted == null ||
                _layout.Width != currentWidth || _layout.Height != previousHeight ||
                _persisted.Width != _layout.Width || _persisted.Height != _layout.Height ||
                currentHeight <= previousHeight)
            {
                Rebind(player, "native-inventory-resize-unsupported");
                FailClosed(currentHeight < previousHeight
                    ? "topology.native-shrink-unsupported"
                    : "topology.native-resize-unsupported");
                return;
            }
            if (!TopologyLayout.TryCreate(currentWidth, currentHeight,
                    out TopologyLayout resizedLayout, out string layoutReason))
            {
                Rebind(player, "native-inventory-resize-invalid");
                FailClosed(layoutReason);
                return;
            }
            if (!NativeInventoryResizePlan.TryCreate(
                    _layout,
                    resizedLayout,
                    _persisted.LockedSlots(),
                    out IReadOnlyList<InventorySlotCoordinate> resizedLocks,
                    out string planReason))
            {
                FailClosed(planReason);
                return;
            }
            if (!TopologyPersistenceCodec.TryEncode(
                    resizedLayout, resizedLocks, out string resizedPayload, out string encodeReason))
            {
                FailClosed(encodeReason);
                return;
            }
            if (!TopologyPersistenceCodec.TryDecode(
                    resizedPayload, out PersistedTopologyState resizedState, out string decodeReason))
            {
                FailClosed(decodeReason);
                return;
            }
            if (player.m_customData == null ||
                !player.m_customData.TryGetValue(
                    TopologyPersistenceCodec.MetadataKey, out string previousPayload) ||
                !TopologyPersistenceCodec.TryDecode(
                    previousPayload, out PersistedTopologyState previousState, out _) ||
                previousState.Width != _layout.Width || previousState.Height != _layout.Height)
            {
                FailClosed("persistence.resize-source-unavailable");
                return;
            }
            if (!InventoryEvidence.TryCaptureMutation(
                    inventory, out IReadOnlyList<ItemMutationEvidence> before, out string evidenceReason))
            {
                FailClosed(evidenceReason);
                return;
            }

            var changes = new List<PositionChange<ItemDrop.ItemData, Vector2i>>(before.Count);
            foreach (ItemMutationEvidence record in before)
            {
                int destinationRow = NativeInventoryResizePlan.MapRow(
                    record.Coordinate.y, _layout.SpecialRow, resizedLayout.SpecialRow);
                if (destinationRow == record.Coordinate.y) continue;
                changes.Add(new PositionChange<ItemDrop.ItemData, Vector2i>(
                    record.Item,
                    record.Coordinate,
                    new Vector2i(record.Coordinate.x, destinationRow)));
            }
            if (!TryEnterMutation("runic.inventory/native-pocket-resize", out IDisposable lease))
            {
                FailClosed("topology.native-resize-transaction-busy");
                return;
            }

            bool eventDetached = false;
            bool committed = false;
            Exception failure = null;
            Exception rollbackFailure = null;
            using (lease)
            {
                try
                {
                    inventory.m_onChanged -= OnInventoryChanged;
                    eventDetached = true;
                    if (changes.Count == 0)
                    {
                        committed = TryWritePersistedMetadata(resizedPayload, out string writeReason);
                        if (!committed) failure = new InvalidOperationException(writeReason);
                    }
                    else
                    {
                        bool publishResizedMetadata = false;
                        string verifyReason = "evidence.verification-not-run";
                        committed = AtomicPositionTransaction.TryCommit(
                            changes,
                            (item, coordinate) => item.m_gridPos = coordinate,
                            () =>
                            {
                                bool valid = InventoryEvidence.VerifyUnchangedExceptPosition(
                                                 inventory, before, out verifyReason) &&
                                             ResizeDestinationsMatch(
                                                 before, _layout.SpecialRow, resizedLayout.SpecialRow);
                                publishResizedMetadata = valid;
                                return valid;
                            },
                            () =>
                            {
                                if (publishResizedMetadata)
                                {
                                    publishResizedMetadata = false;
                                    if (!TryWritePersistedMetadata(resizedPayload, out string writeReason))
                                        throw new InvalidOperationException(writeReason);
                                }
                                else
                                {
                                    RestorePersistedMetadata(previousPayload);
                                }
                            },
                            out failure,
                            out rollbackFailure);
                    }
                    if (committed)
                    {
                        _layout = resizedLayout;
                        _persisted = resizedState;
                        _mode = InventoryAuthorityMode.AuthoritativeLocal;
                        _reasonCode = "ok";
                        _topologyActive = true;
                    }
                }
                finally
                {
                    if (eventDetached) inventory.m_onChanged += OnInventoryChanged;
                }
            }
            if (!committed)
            {
                if (rollbackFailure != null)
                    Diagnostics.Error(rollbackFailure,
                        "Native pocket resize restored item positions but metadata rollback faulted.");
                Diagnostics.Error(failure ?? new InvalidOperationException("Native resize did not commit."),
                    "Native pocket resize migration rolled back.");
                FailClosed("topology.native-resize-migration-failed");
                return;
            }

            ValheimContracts.NotifyChanged(inventory);
            RebuildCache("native-pocket-resize", verifySerialization: true);
            Diagnostics.Trace(
                "Native pocket inventory expanded from " + previousHeight + " to " + currentHeight +
                " rows; Runic's role row and lock metadata migrated atomically.");
        }

        internal void FailClosed(string reasonCode)
        {
            _repairAllowanceDepth = 0;
            _topologyActive = false;
            _snapshot = null;
            _mode = _batch ? InventoryAuthorityMode.BatchInert : InventoryAuthorityMode.MigrationSafeCompatibility;
            _reasonCode = BoundReason(reasonCode, "runtime.fail-closed");
            RebuildStatusOnly();
        }

        public bool TryCapture(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode)
        {
            snapshot = null;
            if (_disposed)
            {
                failureCode = "provider.disposed";
                return false;
            }
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                failureCode = "thread.main-required";
                return false;
            }
            if (!_player || playerId == 0L || playerId != _player.GetPlayerID())
            {
                failureCode = "player.not-local";
                return false;
            }
            if (_layout != null && !LiveDimensionsMatch())
            {
                FailClosed("topology.runtime-dimension-changed");
                failureCode = "topology.runtime-dimension-changed";
                return false;
            }
            bool authoritative = IsAuthoritativeLocal(_player);
            if ((_mode == InventoryAuthorityMode.AuthoritativeLocal && !authoritative) ||
                (_mode == InventoryAuthorityMode.RemoteDedicatedCompatibility && authoritative))
            {
                failureCode = "authority.transition-pending";
                return false;
            }
            if (_snapshot == null || !_snapshot.SerializationVerified)
            {
                RebuildCache("api-capture", verifySerialization: true);
                if (_snapshot == null || !_snapshot.SerializationVerified)
                {
                    failureCode = _snapshot == null ? _reasonCode : "serialization.unverified";
                    return false;
                }
            }
            snapshot = _snapshot;
            failureCode = "ok";
            return true;
        }

        public bool TryIsLocked(
            long playerId,
            InventorySlotCoordinate coordinate,
            out bool locked,
            out string failureCode)
        {
            locked = false;
            if (_disposed || Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                failureCode = _disposed ? "provider.disposed" : "thread.main-required";
                return false;
            }
            if (!_player || playerId == 0L || playerId != _player.GetPlayerID())
            {
                failureCode = "player.not-local";
                return false;
            }
            if (!CanEnforceLocks())
            {
                failureCode = "authority.local-required";
                return false;
            }
            if (_persisted == null || _layout == null || !_layout.InBounds(coordinate) ||
                _persisted.Width != _layout.Width || _persisted.Height != _layout.Height)
            {
                failureCode = "locks.topology-unavailable";
                return false;
            }
            locked = _persisted.IsLocked(coordinate.X, coordinate.Y);
            failureCode = "ok";
            return true;
        }

        public bool TryGetProtection(object nativeItem, out ItemProtectionState state)
        {
            state = ItemProtectionState.Unknown;
            if (!(nativeItem is ItemDrop.ItemData item))
            {
                TraceProtectionDecision("not-applicable.not-native-item");
                return false;
            }
            // A null config entry is startup uncertainty, not an explicit disable. Stable modes in
            // which Inventory itself yields to vanilla are also outside the active lock domain;
            // optional consumers must not turn those compatibility states into a global gameplay
            // denial merely because this plugin is installed.
            ItemProtectionAvailability availability = ItemProtectionAvailabilityPolicy.Classify(
                InventoryConfig.Enabled?.Value ?? true,
                _mode,
                _reasonCode,
                _disposed,
                _playerLoadInProgress);
            if (availability == ItemProtectionAvailability.NotApplicable)
            {
                TraceProtectionDecision("not-applicable.provider-inactive-" + (int)_mode);
                return false;
            }
            if (availability == ItemProtectionAvailability.Unknown)
            {
                TraceProtectionDecision(
                    _disposed
                        ? "unknown.provider-disposed"
                        : _playerLoadInProgress
                            ? "unknown.player-load-in-progress"
                            : "unknown.provider-state");
                return true;
            }

            // In an enabled authoritative mode, provider/lifecycle unavailability cannot prove
            // that the reference belongs to an external inventory. Keep the query in-domain
            // Unknown so a consumer cannot mutate through a rebind or authority transition.
            if (_inventory == null)
            {
                TraceProtectionDecision("unknown.inventory-unavailable");
                return true;
            }
            if (!_player)
            {
                TraceProtectionDecision("unknown.player-unavailable");
                return true;
            }
            // A native item may belong to this local inventory, but Unity inventory access is a
            // main-thread contract. It is in the provider's possible domain; uncertainty is not
            // permission to let a consumer reinterpret it as NotApplicable.
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                TraceProtectionDecision("unknown.thread-main-required");
                return true;
            }

            try
            {
                List<ItemDrop.ItemData> items = _inventory.GetAllItems();
                ItemProtectionDomainEvidence domain = ItemProtectionDomain.Evaluate(
                    items, item, InventoryTopologySnapshot.MaximumNativeSlots);
                // False has the narrow Core meaning NotApplicable: the bounded authoritative
                // native membership list proves this ItemData is outside the governed inventory.
                if (domain == ItemProtectionDomainEvidence.NotApplicable)
                {
                    TraceProtectionDecision("not-applicable.foreign-inventory-item");
                    return false;
                }
                // From here onward the item is in-domain. Every incomplete or malformed proof is
                // a successful query with Unknown, so consumers fail closed rather than applying
                // an unrelated out-of-domain policy.
                if (domain != ItemProtectionDomainEvidence.ExactCurrentMember)
                {
                    TraceProtectionDecision("unknown.domain-membership-indeterminate");
                    return true;
                }
                if (item.m_shared == null || item.m_stack <= 0)
                {
                    TraceProtectionDecision("unknown.domain-item-shape-invalid");
                    return true;
                }
                if (!CanEnforceLocks())
                {
                    TraceProtectionDecision("unknown.topology-or-authority-inactive");
                    return true;
                }
                if (_layout == null || _persisted == null)
                {
                    TraceProtectionDecision("unknown.topology-unavailable");
                    return true;
                }
                if (!LiveDimensionsMatch())
                {
                    TraceProtectionDecision("unknown.dimensions-live-mismatch");
                    return true;
                }
                if (_persisted.Width != _layout.Width || _persisted.Height != _layout.Height)
                {
                    TraceProtectionDecision("unknown.dimensions-persisted-mismatch");
                    return true;
                }

                int itemX = item.m_gridPos.x;
                int itemY = item.m_gridPos.y;
                if (itemX < 0 || itemX >= _layout.Width || itemY < 0 || itemY >= _layout.Height)
                {
                    TraceProtectionDecision("unknown.domain-item-coordinate-invalid");
                    return true;
                }

                ulong occupiedLow = 0UL;
                ulong occupiedHigh = 0UL;
                for (int index = 0; index < items.Count; index++)
                {
                    ItemDrop.ItemData candidate = items[index];
                    if (candidate == null || candidate.m_shared == null || candidate.m_stack <= 0 ||
                        candidate.m_gridPos.x < 0 || candidate.m_gridPos.x >= _layout.Width ||
                        candidate.m_gridPos.y < 0 || candidate.m_gridPos.y >= _layout.Height)
                    {
                        TraceProtectionDecision("unknown.domain-member-shape-invalid");
                        return true;
                    }

                    int slot = candidate.m_gridPos.y * _layout.Width + candidate.m_gridPos.x;
                    if (slot < 64)
                    {
                        ulong bit = 1UL << slot;
                        if ((occupiedLow & bit) != 0UL)
                        {
                            TraceProtectionDecision("unknown.domain-duplicate-coordinate");
                            return true;
                        }
                        occupiedLow |= bit;
                    }
                    else
                    {
                        ulong bit = 1UL << (slot - 64);
                        if ((occupiedHigh & bit) != 0UL)
                        {
                            TraceProtectionDecision("unknown.domain-duplicate-coordinate");
                            return true;
                        }
                        occupiedHigh |= bit;
                    }
                }

                // Re-read the cheap native evidence after the bounded scan. This rejects a stale
                // reference, coordinate mutation, collection replacement, or topology transition
                // instead of classifying an item that is no longer in the proven local inventory.
                if (item.m_gridPos.x != itemX || item.m_gridPos.y != itemY)
                {
                    TraceProtectionDecision("unknown.reference-item-coordinate-mutated");
                    return true;
                }
                List<ItemDrop.ItemData> refreshedItems = _inventory.GetAllItems();
                if (!SameItemReferences(items, refreshedItems))
                {
                    TraceProtectionDecision("unknown.reference-membership-list-replaced");
                    return true;
                }
                if (!ReferenceEquals(_inventory.GetItemAt(itemX, itemY), item))
                {
                    TraceProtectionDecision("unknown.reference-coordinate-occupant-mutated");
                    return true;
                }
                if (!CanEnforceLocks())
                {
                    TraceProtectionDecision("unknown.topology-transition-during-proof");
                    return true;
                }
                if (!LiveDimensionsMatch())
                {
                    TraceProtectionDecision("unknown.dimensions-transition-during-proof");
                    return true;
                }

                state = _repairAllowanceDepth > 0
                    ? ItemProtectionState.Unlocked
                    : ItemProtectionDomain.ClassifyExactMember(
                        TopologyActive && itemY == _layout.SpecialRow || IsQuiverReservedRow(itemY),
                        _persisted.IsLocked(itemX, itemY));
                return true;
            }
            catch (Exception exception)
            {
                state = ItemProtectionState.Unknown;
                TraceProtectionDecision("unknown.exception", exception);
                return true;
            }
        }

        public InventoryFeatureStatus Snapshot()
        {
            if (_disposed) return new InventoryFeatureStatus(InventoryAuthorityMode.Unavailable, false, "provider.disposed");
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                return new InventoryFeatureStatus(InventoryAuthorityMode.Unavailable, false, "thread.main-required");
            if (_layout != null && !LiveDimensionsMatch())
                return new InventoryFeatureStatus(
                    InventoryAuthorityMode.MigrationSafeCompatibility, false, "topology.runtime-dimension-changed");
            bool authoritative = IsAuthoritativeLocal(_player);
            if ((_mode == InventoryAuthorityMode.AuthoritativeLocal && !authoritative) ||
                (_mode == InventoryAuthorityMode.RemoteDedicatedCompatibility && authoritative))
                return new InventoryFeatureStatus(
                    InventoryAuthorityMode.MigrationSafeCompatibility, false, "authority.transition-pending");
            return new InventoryFeatureStatus(_mode, TopologyActive, _reasonCode);
        }

        internal bool TryFindEmptySlot(Inventory inventory, bool topFirst, out Vector2i result)
        {
            result = new Vector2i(-1, -1);
            if ((!TopologyActive && !HasDedicatedRow(inventory)) || !ReferenceEquals(inventory, _inventory)) return false;
            int generalRows = inventory.GetHeight() - (HasQuiverLayout ? 3 : 1);
            if (topFirst)
            {
                for (int y = 0; y < generalRows; y++)
                    for (int x = 0; x < inventory.GetWidth(); x++)
                        if (inventory.GetItemAt(x, y) == null)
                        {
                            result = new Vector2i(x, y);
                            return true;
                        }
            }
            else
            {
                for (int y = generalRows - 1; y >= 0; y--)
                    for (int x = 0; x < inventory.GetWidth(); x++)
                        if (inventory.GetItemAt(x, y) == null)
                        {
                            result = new Vector2i(x, y);
                            return true;
                        }
            }
            return true; // handled, with (-1,-1) proving no ordinary slot exists
        }

        internal bool AllowGridDrop(
            Inventory destination,
            Inventory source,
            ItemDrop.ItemData item,
            int amount,
            Vector2i destinationPosition)
        {
            if (_disposed || item == null) return true;
            if (!AllowPositionedAddition(destination, item, destinationPosition.x, destinationPosition.y, false))
            {
                Notify("Runic Inventory: that slot accepts only its labeled equipment or quick-use item type.");
                return false;
            }
            // A swap must validate the item moving back into the source role as well.
            ItemDrop.ItemData displaced = destination?.GetItemAt(destinationPosition.x, destinationPosition.y);
            if (displaced != null && !AllowPositionedAddition(source, displaced, item.m_gridPos.x, item.m_gridPos.y, false))
                return false;
            if (ReferenceEquals(source, _inventory) && IsLocked(item))
            {
                Notify("Runic Inventory: that slot is locked.");
                return false;
            }
            if (ReferenceEquals(destination, _inventory) && IsLocked(destinationPosition.x, destinationPosition.y) &&
                (!ReferenceEquals(source, _inventory) || item.m_gridPos.x != destinationPosition.x ||
                 item.m_gridPos.y != destinationPosition.y))
            {
                Notify("Runic Inventory: the destination slot is locked.");
                return false;
            }
            if ((!TopologyActive && !HasDedicatedRow(destination)) || !ReferenceEquals(destination, _inventory) || _layout == null) return true;
            if (destinationPosition.x < 0 || destinationPosition.x >= _layout.Width ||
                destinationPosition.y < 0 || destinationPosition.y >= _layout.Height) return true;
            if (_layout.TryRoleAt(destinationPosition.x, destinationPosition.y, out InventoryRoleKind targetRole) &&
                !TopologyLayout.Accepts(targetRole, ValheimContracts.Category(item)))
            {
                Notify("Runic Inventory: that item does not belong in " + RoleLabel(targetRole) + ".");
                return false;
            }
            if (ReferenceEquals(source, _inventory) && item.m_equipped &&
                _layout.TryRoleAt(item.m_gridPos.x, item.m_gridPos.y, out InventoryRoleKind sourceRole) &&
                IsEquipmentRole(sourceRole) &&
                (destinationPosition.x != item.m_gridPos.x || destinationPosition.y != item.m_gridPos.y))
            {
                Notify("Runic Inventory: unequip that item before moving it out of its equipment role.");
                return false;
            }
            return amount > 0;
        }

        internal void AdjustCanAddItem(
            Inventory inventory,
            ItemDrop.ItemData item,
            int requestedStack,
            ref bool result)
        {
            if (!result || !TopologyActive || !ReferenceEquals(inventory, _inventory) || item?.m_shared == null) return;
            int requested = requestedStack <= 0 ? item.m_stack : requestedStack;
            if (requested <= 0)
            {
                result = false;
                return;
            }
            _stackCapacity.TryGetValue(
                new StackCapacityKey(item.m_shared.m_name, item.m_quality, item.m_worldLevel),
                out int existingCapacity);
            long capacity = existingCapacity + (long)_freePickupSlots * Math.Max(1, item.m_shared.m_maxStackSize);
            result = capacity >= requested;
        }

        internal void AfterGridDrop(Inventory destination, Vector2i position, bool succeeded)
        {
            if (!succeeded || !TopologyActive || !ReferenceEquals(destination, _inventory) || _layout == null ||
                !_layout.TryRoleAt(position.x, position.y, out InventoryRoleKind role) || !IsEquipmentRole(role)) return;
            ItemDrop.ItemData item = _inventory.GetItemAt(position.x, position.y);
            if (item == null || item.m_equipped || !TopologyLayout.Accepts(role, ValheimContracts.Category(item))) return;
            try { _player.EquipItem(item, true); }
            catch (Exception exception) { Diagnostics.Error(exception, "Equipment role could not invoke vanilla EquipItem; the carried item remains intact."); }
        }

        internal bool AllowSelectedAction(InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier modifier)
        {
            if (_disposed || item == null || grid == null || !ReferenceEquals(grid.GetInventory(), _inventory)) return true;
            if (modifier != InventoryGrid.Modifier.Move && modifier != InventoryGrid.Modifier.Drop) return true;
            if (!IsLocked(item)) return true;
            Notify("Runic Inventory: that slot is locked.");
            return false;
        }

        internal bool AllowItemAction(Humanoid actor, Inventory inventory, ItemDrop.ItemData item, string action)
        {
            if (SlotLockUsePolicy.AllowsUse(action)) return true;
            if (_disposed || actor != _player || !ReferenceEquals(inventory, _inventory) || item == null || !IsLocked(item))
                return true;
            Notify("Runic Inventory: unlock that slot before " + action + ".");
            return false;
        }

        internal bool AllowStationItem(Humanoid actor, ItemDrop.ItemData item, string action) =>
            AllowItemAction(actor, actor?.GetInventory(), item, action);

        internal bool AllowEquip(Humanoid actor, ItemDrop.ItemData item)
        {
            if (_disposed || actor != _player || item == null || !CanEnforceLocks() || _layout == null) return true;
            if (_playerLoadInProgress && !_loadMetadataRefreshed && !TryRefreshLoadMetadata()) return true;
            if (!TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out InventoryRoleKind role)) return true;
            InventorySlotCoordinate target = _layout.Coordinate(role);
            ItemDrop.ItemData occupant = _inventory.GetItemAt(target.X, target.Y);
            if (IsLocked(item) && !ReferenceEquals(occupant, item))
            {
                Notify("Runic Inventory: unlock that item before equipping it.");
                return false;
            }
            if (!IsLocked(target.X, target.Y)) return true;
            if (ReferenceEquals(occupant, item)) return true;
            Notify("Runic Inventory: unlock the " + RoleLabel(role) + " before replacing it.");
            return false;
        }

        internal bool BeginEquipmentTransition(Humanoid actor, ItemDrop.ItemData item)
        {
            if (_disposed || actor != _player || item == null || !TopologyActive ||
                !TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out _)) return false;
            if (_equipmentTransitionDepth == int.MaxValue)
            {
                FailClosed("equipment.transition-depth-exceeded");
                return false;
            }
            _equipmentTransitionDepth++;
            return true;
        }

        internal bool AllowCrafting(InventoryGui gui)
        {
            ItemDrop.ItemData selected = ValheimContracts.CraftingCommitItem(gui);
            if (selected == null || !IsLocked(selected)) return true;
            Notify("Runic Inventory: unlock the selected item before upgrading or processing it.");
            return false;
        }

        internal bool AllowPickup(Humanoid actor, GameObject worldObject)
        {
            if (_disposed || !(InventoryConfig.Enabled?.Value ?? false) || actor != _player || !worldObject || _filters.Count == 0)
                return true;
            ItemDrop drop = worldObject.GetComponent<ItemDrop>();
            ItemDrop.ItemData item = drop?.m_itemData;
            if (item?.m_shared == null || item.m_shared.m_questItem) return true;
            return !_filters.Matches(ValheimContracts.PrefabId(item), item.m_shared.m_name);
        }

        internal EquipmentAdditionState BeginEquipmentAddition(Humanoid actor, ItemDrop.ItemData expectedItem = null)
        {
            if (_disposed || actor != _player || !TopologyActive || _inventory == null || _layout == null)
                return null;
            InventoryRoleKind? expectedRole = null;
            if (expectedItem != null)
            {
                if (!TopologyLayout.TryEquipmentRole(ValheimContracts.Category(expectedItem), out InventoryRoleKind role))
                    return null;
                InventorySlotCoordinate target = _layout.Coordinate(role);
                if (_inventory.GetItemAt(target.X, target.Y) != null) return null;
                expectedRole = role;
            }
            List<ItemDrop.ItemData> items = _inventory.GetAllItems();
            if (items == null || items.Count > InventoryTopologySnapshot.MaximumNativeSlots) return null;
            return new EquipmentAdditionState(_player, _inventory, expectedRole, new List<ItemDrop.ItemData>(items));
        }

        internal void CompleteEquipmentAddition(EquipmentAdditionState state, bool succeeded)
        {
            if (!succeeded || state == null || state.Player != _player || state.Inventory != _inventory || !TopologyActive)
                return;
            List<ItemDrop.ItemData> items = _inventory.GetAllItems();
            ItemDrop.ItemData candidate = null;
            InventoryRoleKind candidateRole = default;
            foreach (ItemDrop.ItemData item in items)
            {
                bool existed = false;
                foreach (ItemDrop.ItemData prior in state.Before)
                {
                    if (!ReferenceEquals(prior, item)) continue;
                    existed = true;
                    break;
                }
                if (existed || !TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out InventoryRoleKind role) ||
                    (state.ExpectedRole.HasValue && state.ExpectedRole.Value != role)) continue;
                InventorySlotCoordinate target = _layout.Coordinate(role);
                if (_inventory.GetItemAt(target.X, target.Y) != null || candidate != null) return;
                candidate = item;
                candidateRole = role;
            }
            if (candidate == null) return;
            InventorySlotCoordinate finalTarget = _layout.Coordinate(candidateRole);
            if (_inventory.GetItemAt(finalTarget.X, finalTarget.Y) != null) return;
            ((Humanoid)_player).EquipItem(candidate, true);
        }

        internal void AppendPickupPreview(ItemDrop drop, ref string hoverText)
        {
            if (_disposed || !(InventoryConfig.Enabled?.Value ?? false) ||
                !(InventoryConfig.ShowPickupPreview?.Value ?? false) || !_player || !drop ||
                drop.m_itemData?.m_shared == null || hoverText == null || hoverText.Length > 8192) return;
            Vector3 offset = drop.transform.position - _player.transform.position;
            if (offset.sqrMagnitude > 25f) return; // never disclose through remote/build-camera hover
            ItemDrop.ItemData item = drop.m_itemData;
            int stack = Math.Max(0, item.m_stack);
            int maximum = Math.Max(1, item.m_shared.m_maxStackSize);
            _stackCapacity.TryGetValue(
                new StackCapacityKey(item.m_shared.m_name, item.m_quality, item.m_worldLevel),
                out int stackCapacity);
            bool filtered = !item.m_shared.m_questItem &&
                            _filters.Matches(ValheimContracts.PrefabId(item), item.m_shared.m_name);
            float unitWeight;
            try { unitWeight = Math.Max(0f, item.GetWeight(1)); }
            catch (Exception) { return; }
            PickupDecision decision = PickupPlanner.Evaluate(
                stack,
                maximum,
                stackCapacity,
                _freePickupSlots,
                unitWeight,
                Math.Max(0f, _cachedWeight),
                Math.Max(0f, _player.GetMaxCarryWeight()),
                filtered);
            string suffix;
            if (decision.Filtered) suffix = "\nRunic pickup: filtered; quest items always remain allowed.";
            else if (decision.OverflowItems > 0)
                suffix = "\nRunic pickup: " + decision.OverflowItems + " item(s) would overflow available safe slots.";
            else if (decision.Encumbered)
                suffix = "\nRunic pickup: fits, but would encumber you (" + decision.ResultingWeight.ToString("0.#") + ").";
            else suffix = "\nRunic pickup: fits; projected weight " + decision.ResultingWeight.ToString("0.#") + ".";
            if (suffix.Length <= 192 && hoverText.Length + suffix.Length <= 8384) hoverText += suffix;
        }

        internal void OnEquipped(Humanoid actor, ItemDrop.ItemData item, bool succeeded)
        {
            if (!succeeded || actor != _player || !TopologyActive || item == null || !item.m_equipped || _layout == null) return;
            if (!TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out InventoryRoleKind role)) return;
            RelocateEquippedItem(item, role);
        }

        internal void EndEquipmentTransition(bool started, Exception failure)
        {
            if (!started || _disposed) return;
            if (_equipmentTransitionDepth <= 0)
            {
                FailClosed("equipment.transition-scope-invalid");
                return;
            }
            if (failure != null) _equipmentTransitionFaulted = true;
            _equipmentTransitionDepth--;
            if (_equipmentTransitionDepth != 0) return;

            bool refresh = _equipmentRefreshPending;
            bool faulted = _equipmentTransitionFaulted;
            _equipmentRefreshPending = false;
            _equipmentTransitionFaulted = false;
            if (faulted)
            {
                FailClosed("equipment.native-call-faulted");
                return;
            }
            if (refresh) RebuildCache("equipment-transition-completed", verifySerialization: false);
        }

        internal bool ShouldBlockStorageAction(string methodName)
        {
            if (!TopologyActive) return false;
            Notify("Runic Storage " + SafeWord(methodName) + " is paused while native special slots are active; a topology-aware peer is required.");
            return true;
        }

        internal void SortSelectedRows(string source)
        {
            if (!RequireAuthoritativeTopology("sort")) return;
            if (!SelectedRowPolicy.TryParse(InventoryConfig.SortRows.Value, _layout, out IReadOnlyList<int> rows, out string rowReason))
            {
                Notify("Runic Inventory: sort rows were rejected (" + rowReason + ").");
                return;
            }
            if (HasQuiverLayout)
            {
                var ordinaryRows = new List<int>();
                foreach (int row in rows) if (!IsQuiverReservedRow(row)) ordinaryRows.Add(row);
                rows = ordinaryRows;
            }
            if (rows.Count == 0) return;
            if (!TryEnterMutation("runic.inventory/sort", out IDisposable lease))
            {
                Notify("Runic Inventory: another inventory transaction is active; sort was skipped.");
                return;
            }
            using (lease)
            {
                if (!InventoryEvidence.TryCaptureMutation(_inventory, out IReadOnlyList<ItemMutationEvidence> before, out string evidenceReason))
                {
                    Notify("Runic Inventory: sort proof failed (" + evidenceReason + ").");
                    return;
                }
                var descriptors = new List<SortItemDescriptor>(before.Count);
                var bySource = new Dictionary<int, ItemDrop.ItemData>();
                foreach (ItemMutationEvidence record in before)
                {
                    ItemDrop.ItemData item = record.Item;
                    string stableName = item.m_shared?.m_name;
                    if (string.IsNullOrEmpty(stableName) || stableName.Length > 160)
                    {
                        Notify("Runic Inventory: an item has no bounded stable sort identity; nothing moved.");
                        return;
                    }
                    float weight;
                    try { weight = Math.Max(0f, item.GetWeight(item.m_stack)); }
                    catch (Exception) { weight = 0f; }
                    var coordinate = new InventorySlotCoordinate(item.m_gridPos.x, item.m_gridPos.y);
                    descriptors.Add(new SortItemDescriptor(
                        coordinate, (int)item.m_shared.m_itemType, stableName, Math.Max(0, item.m_quality), weight, item.m_equipped));
                    bySource.Add(item.m_gridPos.y * _layout.Width + item.m_gridPos.x, item);
                }
                if (!SafeSortPlanner.TryPlan(
                        _layout, descriptors, _persisted.LockedSlots(), rows, out SafeSortPlan plan, out string planReason))
                {
                    Notify("Runic Inventory: sort was rejected (" + planReason + ").");
                    return;
                }
                if (!plan.ChangesAnything)
                {
                    Notify("Runic Inventory: the selected safe region is already sorted.");
                    return;
                }
                var destinations = new Dictionary<int, Vector2i>();
                foreach (SortMove move in plan.Moves)
                {
                    int key = move.Source.Y * _layout.Width + move.Source.X;
                    if (!bySource.ContainsKey(key) || destinations.ContainsKey(key))
                    {
                        Notify("Runic Inventory: sort sources changed before commit; nothing moved.");
                        return;
                    }
                    destinations.Add(key, new Vector2i(move.Destination.X, move.Destination.Y));
                }
                var changes = new List<PositionChange<ItemDrop.ItemData, Vector2i>>(before.Count);
                foreach (ItemMutationEvidence record in before)
                {
                    int key = record.Coordinate.y * _layout.Width + record.Coordinate.x;
                    Vector2i destination = destinations.TryGetValue(key, out Vector2i planned)
                        ? planned
                        : record.Coordinate;
                    changes.Add(new PositionChange<ItemDrop.ItemData, Vector2i>(record.Item, record.Coordinate, destination));
                }
                string verifyReason = "evidence.verification-not-run";
                bool committed = AtomicPositionTransaction.TryCommit(
                    changes,
                    (target, coordinate) => target.m_gridPos = coordinate,
                    () => InventoryEvidence.VerifyUnchangedExceptPosition(_inventory, before, out verifyReason),
                    () => ValheimContracts.NotifyChanged(_inventory),
                    out Exception failure,
                    out Exception rollbackFailure);
                if (!committed)
                {
                    if (rollbackFailure != null)
                        Diagnostics.Error(rollbackFailure, "Sort positions were restored in memory but rollback publication faulted.");
                    Diagnostics.Error(failure ?? new InvalidOperationException(verifyReason),
                        "Regional sort rolled back without changing item metadata.");
                    Notify("Runic Inventory: sort failed and every restorable original position was restored.");
                    return;
                }
                Notify("Runic Inventory: sorted " + plan.MovableCount + " stack(s) in the selected safe region.");
            }
        }

        internal void ToggleFocusedLock(string source)
        {
            if (!RequireLockState()) return;
            InventoryGrid grid = InventoryGui.instance?.m_playerGrid;
            if (!grid || !ReferenceEquals(grid.GetInventory(), _inventory) ||
                !ValheimContracts.TryFocusedSlot(grid, out Vector2i focused) ||
                focused.x < 0 || focused.x >= _layout.Width || focused.y < 0 || focused.y >= _layout.Height)
            {
                Notify("Runic Inventory: focus a player inventory slot before toggling its lock.");
                return;
            }
            ToggleLockAt(focused);
        }

        internal bool TryTogglePointerLock(InventoryGrid grid, UIInputHandler clicked)
        {
            if (_disposed || !ZInput.GetKey(KeyCode.LeftAlt, false) ||
                InventoryGui.instance == null || !InventoryGui.IsVisible() ||
                !grid || !ReferenceEquals(grid, InventoryGui.instance.m_playerGrid) ||
                !ReferenceEquals(grid.GetInventory(), _inventory))
                return false;

            if (!RequireLockState()) return true;
            if (!ValheimContracts.TryClickedSlot(grid, clicked, out Vector2i focused) ||
                focused.x < 0 || focused.x >= _layout.Width ||
                focused.y < 0 || focused.y >= _layout.Height)
            {
                Notify("Runic Inventory: point at a player inventory slot before toggling its lock.");
                return true;
            }
            ToggleLockAt(focused);
            return true;
        }

        private void ToggleLockAt(Vector2i focused)
        {
            if (!TryEnterMutation("runic.inventory/lock-metadata", out IDisposable lease))
            {
                Notify("Runic Inventory: another inventory transaction is active; the lock was unchanged.");
                return;
            }
            using (lease)
            {
                var coordinate = new InventorySlotCoordinate(focused.x, focused.y);
                var locks = new List<InventorySlotCoordinate>(_persisted.LockedSlots());
                bool wasLocked = locks.Remove(coordinate);
                if (!wasLocked) locks.Add(coordinate);
                string payload;
                string encodeReason;
                string writeReason = "not-attempted";
                string decodeReason = "not-attempted";
                PersistedTopologyState next = null;
                bool encoded = TopologyPersistenceCodec.TryEncode(_layout, locks, out payload, out encodeReason);
                bool written = encoded && TryWritePersistedMetadata(payload, out writeReason);
                bool decoded = written && TopologyPersistenceCodec.TryDecode(payload, out next, out decodeReason);
                if (!encoded || !written || !decoded)
                {
                    Notify("Runic Inventory: lock change was rejected (" + FirstFailure(encodeReason, writeReason, decodeReason) + ").");
                    return;
                }
                _persisted = next;
                RebuildCache("lock-changed");
                Notify("Runic Inventory: slot " + (focused.x + 1) + "," + (focused.y + 1) +
                       (wasLocked ? " unlocked." : " locked; protected from Quick Stack and Store All."));
            }
        }

        internal bool BeginRepairAllowance()
        {
            if (_disposed || _batch) return false;
            _repairAllowanceDepth++;
            return true;
        }

        internal void EndRepairAllowance(bool entered)
        {
            if (!entered) return;
            _repairAllowanceDepth = Math.Max(0, _repairAllowanceDepth - 1);
        }

        internal void UseQuick(InventoryRoleKind role, string source)
        {
            if (!RequireAuthoritativeTopology("quick-slot")) return;
            if (source == "keyboard" && BetterArcheryCompatibility.QuiverShortcutDown())
            {
                Notify("Runic Inventory: this shortcut selects quiver ammunition. Choose different Quick 1-3 keys to use both features.");
                return;
            }
            InventorySlotCoordinate coordinate = _layout.Coordinate(role);
            ItemDrop.ItemData item = _inventory.GetItemAt(coordinate.X, coordinate.Y);
            if (item == null)
            {
                Notify("Runic Inventory: " + RoleLabel(role) + " is empty.");
                return;
            }
            // A retained quick-slot item remains usable; locking protects its position
            // and storage transfers, not the native UseItem action below.
            if (!TopologyLayout.Accepts(role, ValheimContracts.Category(item)))
            {
                FailClosed("topology.quick-role-invalid");
                Notify("Runic Inventory: the quick-slot topology changed; no item was used.");
                return;
            }
            if (!TryEnterMutation("runic.inventory/quick-use", out IDisposable lease))
            {
                Notify("Runic Inventory: another inventory transaction is active; no item was used.");
                return;
            }
            using (lease)
            {
                try { _player.UseItem(_inventory, item, false); }
                catch (Exception exception)
                {
                    Diagnostics.Error(exception, "Vanilla quick-slot UseItem faulted; no synthetic consumption was attempted.");
                    Notify("Runic Inventory: Valheim rejected that manual use.");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playerLoadInProgress = false;
            _loadMetadataRefreshed = false;
            _disableCleanupPending = false;
            _repairAllowanceDepth = 0;
            DetachInventory();
            _snapshot = null;
            _layout = null;
            _persisted = null;
            _stackCapacity.Clear();
            _topologyActive = false;
        }

        private void Rebind(Player player, string reason, int? requestedNativeRows = null)
        {
            if (_disposed) return;
            _repairAllowanceDepth = 0;
            DetachInventory();
            _player = player;
            if (_batch)
            {
                _mode = InventoryAuthorityMode.BatchInert;
                _reasonCode = "authority.batch-inert";
                RebuildStatusOnly();
                return;
            }
            if (!player)
            {
                _mode = InventoryAuthorityMode.Unavailable;
                _reasonCode = "player.not-loaded";
                RebuildStatusOnly();
                return;
            }
            _inventory = player.GetInventory();
            if (_inventory == null)
            {
                _mode = InventoryAuthorityMode.Unavailable;
                _reasonCode = "inventory.missing";
                RebuildStatusOnly();
                return;
            }
            _inventory.m_onChanged += OnInventoryChanged;
            bool authoritative = IsAuthoritativeLocal(player);
            if (authoritative && !_playerLoadInProgress && !EnsureDedicatedRow(requestedNativeRows, out string rowReason))
            {
                // Also reject unsafe disabling from config files or while no character was loaded.
                if (!(InventoryConfig.Enabled?.Value ?? false) &&
                    player.m_customData?.ContainsKey(DedicatedRowPlan.MetadataKey) == true)
                {
                    RestoreEnabled(rowReason == "extra-row.clear-space-before-shrinking"
                        ? "Cannot disable RunicInventory: there is no room to move the items in the extra row. Free normal inventory slots first."
                        : "Cannot disable RunicInventory: the extra-row items could not be safely relocated.");
                    Rebind(player, "unsafe-disable-rejected");
                    return;
                }
                _disableCleanupPending = false;
                FailClosed(rowReason);
                Notify("Runic Inventory: " + (rowReason == "extra-row.clear-space-before-shrinking"
                    ? "there is no room to move the extra-row items. Free normal inventory slots first."
                    : "equipment row could not be prepared (" + rowReason + "). No items were removed."));
                return;
            }
            if (!(InventoryConfig.Enabled?.Value ?? false))
            {
                // Disabling the feature is also an authoritative metadata-removal request. Discover
                // and finish that request before validating an optional active topology: malformed,
                // legacy, or oversized native dimensions must not strand Runic metadata forever.
                _disableCleanupPending = _disableCleanupPending ||
                    player.m_customData != null &&
                    player.m_customData.ContainsKey(TopologyPersistenceCodec.MetadataKey);
                if (_disableCleanupPending && authoritative && !TryCompleteDisableCleanup())
                    return;
                _mode = InventoryAuthorityMode.Disabled;
                _reasonCode = "feature.disabled";
                RebuildStatusOnly();
                return;
            }
            if (!TopologyLayout.TryCreate(_inventory.GetWidth(), _inventory.GetHeight(), out _layout, out string layoutReason))
            {
                _mode = InventoryAuthorityMode.MigrationSafeCompatibility;
                _reasonCode = layoutReason;
                RebuildCache("layout-invalid");
                return;
            }

            string payload = null;
            bool hasPayload = player.m_customData != null &&
                              player.m_customData.TryGetValue(TopologyPersistenceCodec.MetadataKey, out payload);
            if (hasPayload)
            {
                if (!TopologyPersistenceCodec.TryDecode(payload, out _persisted, out string persistenceReason) ||
                    _persisted.Width != _layout.Width || _persisted.Height != _layout.Height)
                {
                    _persisted = null;
                    _mode = authoritative
                        ? InventoryAuthorityMode.MigrationSafeCompatibility
                        : InventoryAuthorityMode.RemoteDedicatedCompatibility;
                    _reasonCode = persistenceReason == "ok" ? "persistence.topology-mismatch" : persistenceReason;
                    RebuildCache("persistence-invalid");
                    return;
                }
            }
            else
            {
                if (!CreateEmptyPersistedState(_layout, out PersistedTopologyState empty, out string emptyReason))
                {
                    _mode = InventoryAuthorityMode.MigrationSafeCompatibility;
                    _reasonCode = emptyReason;
                    RebuildCache("persistence-empty-failed");
                    return;
                }
                _persisted = empty;
            }

            if (!ValidateRoleContents(out string contentReason))
            {
                _mode = authoritative
                    ? InventoryAuthorityMode.MigrationSafeCompatibility
                    : InventoryAuthorityMode.RemoteDedicatedCompatibility;
                _reasonCode = contentReason;
                RebuildCache("role-content-invalid");
                return;
            }
            if (!authoritative)
            {
                _mode = InventoryAuthorityMode.RemoteDedicatedCompatibility;
                _reasonCode = "authority.local-player-owner-required";
                RebuildCache("non-owner-compatibility", verifySerialization: true);
                return;
            }
            if (!hasPayload)
            {
                string initial;
                string encodeReason;
                string writeReason = "not-attempted";
                bool encoded = TopologyPersistenceCodec.TryEncode(
                    _layout, Array.Empty<InventorySlotCoordinate>(), out initial, out encodeReason);
                bool written = false;
                if (encoded && TryEnterMutation("runic.inventory/topology-bootstrap", out IDisposable lease))
                {
                    using (lease) written = TryWritePersistedMetadata(initial, out writeReason);
                }
                else if (encoded) writeReason = "persistence.transaction-busy";
                if (!encoded || !written)
                {
                    _mode = InventoryAuthorityMode.MigrationSafeCompatibility;
                    _reasonCode = encodeReason == "ok" ? writeReason : encodeReason;
                    RebuildCache("bootstrap-persistence-failed");
                    return;
                }
            }
            _mode = InventoryAuthorityMode.AuthoritativeLocal;
            _reasonCode = "ok";
            _topologyActive = true;
            RebuildCache(reason, verifySerialization: true);
        }

        private void DetachInventory()
        {
            if (_inventory != null) _inventory.m_onChanged -= OnInventoryChanged;
            _inventory = null;
            _layout = null;
            _persisted = null;
            _snapshot = null;
            _topologyActive = false;
            _equipmentTransitionDepth = 0;
            _equipmentRefreshPending = false;
            _equipmentTransitionFaulted = false;
            _stackCapacity.Clear();
            _freePickupSlots = 0;
            _cachedWeight = 0f;
        }

        private void OnInventoryChanged()
        {
            if (_equipmentTransitionDepth > 0)
            {
                _equipmentRefreshPending = true;
                return;
            }
            try { RebuildCache("inventory-changed", verifySerialization: false); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, "Inventory change cache rebuild failed closed.");
                FailClosed("cache.rebuild-failed");
            }
        }

        private void RebuildCache(string cause, bool verifySerialization = false)
        {
            if (_rebuilding || _inventory == null) return;
            _rebuilding = true;
            try
            {
                _stackCapacity.Clear();
                _freePickupSlots = 0;
                _cachedWeight = 0f;
                // A disabled runtime has no topology cache to maintain. In particular, do not scan
                // dimensions which were irrelevant to the disable-cleanup decision.
                if (_mode == InventoryAuthorityMode.Disabled)
                {
                    _snapshot = null;
                    _topologyActive = false;
                    RebuildStatusOnly();
                    return;
                }
                if (_layout != null && !LiveDimensionsMatch())
                {
                    FailClosed("topology.runtime-dimension-changed");
                    return;
                }
                _cachedWeight = Math.Max(0f, _inventory.GetTotalWeight());
                List<ItemDrop.ItemData> items = _inventory.GetAllItems();
                if (items == null || items.Count > InventoryTopologySnapshot.MaximumNativeSlots)
                {
                    _snapshot = null;
                    _reasonCode = "topology.item-bound-exceeded";
                    _topologyActive = false;
                    RebuildStatusOnly();
                    return;
                }
                var positions = new HashSet<int>();
                bool roleRecoveryCandidate =
                    _mode == InventoryAuthorityMode.MigrationSafeCompatibility &&
                    IsRoleContentCompatibilityReason(_reasonCode) && _layout != null && _persisted != null &&
                    IsAuthoritativeLocal(_player);
                foreach (ItemDrop.ItemData item in items)
                {
                    if (item == null || item.m_shared == null || item.m_stack <= 0 ||
                        item.m_gridPos.x < 0 || item.m_gridPos.x >= _inventory.GetWidth() ||
                        item.m_gridPos.y < 0 || item.m_gridPos.y >= _inventory.GetHeight() ||
                        !positions.Add(item.m_gridPos.y * _inventory.GetWidth() + item.m_gridPos.x))
                    {
                        _snapshot = null;
                        _reasonCode = "topology.item-proof-failed";
                        _topologyActive = false;
                        RebuildStatusOnly();
                        return;
                    }
                    int maximum = Math.Max(1, item.m_shared.m_maxStackSize);
                    int capacity = Math.Max(0, maximum - item.m_stack);
                    if (capacity > 0)
                    {
                        var key = new StackCapacityKey(item.m_shared.m_name, item.m_quality, item.m_worldLevel);
                        _stackCapacity.TryGetValue(key, out int existing);
                        _stackCapacity[key] = existing > int.MaxValue - capacity ? int.MaxValue : existing + capacity;
                    }
                }
                bool reserveSpecialRow = TopologyActive || roleRecoveryCandidate;
                for (int y = 0; y < _inventory.GetHeight(); y++)
                {
                    for (int x = 0; x < _inventory.GetWidth(); x++)
                    {
                        if (_inventory.GetItemAt(x, y) != null) continue;
                        if (reserveSpecialRow && _layout != null && y == _layout.SpecialRow || IsQuiverReservedRow(y)) continue;
                        _freePickupSlots++;
                    }
                }
                string contentReason = "topology.unavailable";
                bool rolesValid = _layout != null && _persisted != null &&
                                  _persisted.Width == _layout.Width && _persisted.Height == _layout.Height &&
                                  ValidateRoleContents(out contentReason);
                if (!rolesValid)
                {
                    _snapshot = null;
                    if (_layout != null && _persisted != null) _reasonCode = contentReason;
                    if (_mode == InventoryAuthorityMode.AuthoritativeLocal)
                    {
                        _mode = InventoryAuthorityMode.MigrationSafeCompatibility;
                        _topologyActive = false;
                    }
                    RebuildStatusOnly();
                    return;
                }
                if (_mode == InventoryAuthorityMode.MigrationSafeCompatibility &&
                    IsRoleContentCompatibilityReason(_reasonCode) && IsAuthoritativeLocal(_player))
                {
                    _mode = InventoryAuthorityMode.AuthoritativeLocal;
                    _reasonCode = "ok";
                    _topologyActive = true;
                }
                else if (_mode == InventoryAuthorityMode.AuthoritativeLocal &&
                         IsAuthoritativeLocal(_player))
                {
                    // A drag/equip transition may briefly expose an incomplete ItemData shape.
                    // A later complete native snapshot reactivates the row automatically.
                    _reasonCode = "ok";
                    _topologyActive = true;
                }

                var roles = new List<InventoryRoleSnapshot>(8);
                var hashText = new StringBuilder(1024)
                    .Append(_layout.Width).Append('|').Append(_layout.Height).Append('|').Append((int)_mode);
                foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
                {
                    InventorySlotCoordinate coordinate = _layout.Coordinate(role);
                    ItemDrop.ItemData item = _inventory.GetItemAt(coordinate.X, coordinate.Y);
                    bool locked = _persisted.IsLocked(coordinate.X, coordinate.Y);
                    string prefab = string.Empty;
                    string fingerprint = string.Empty;
                    int stack = 0;
                    bool equipped = false;
                    if (item != null)
                    {
                        if (!InventoryEvidence.TryFingerprint(item, true, out fingerprint))
                        {
                            _snapshot = null;
                            _reasonCode = "topology.item-evidence-unbounded";
                            _topologyActive = false;
                            RebuildStatusOnly();
                            return;
                        }
                        prefab = ValheimContracts.PrefabId(item);
                        stack = item.m_stack;
                        equipped = item.m_equipped;
                    }
                    roles.Add(new InventoryRoleSnapshot(
                        role, coordinate, item != null, equipped, locked, stack, prefab, fingerprint));
                    hashText.Append('|').Append((int)role).Append(':').Append(coordinate).Append(':')
                        .Append(locked ? 1 : 0).Append(':').Append(fingerprint);
                }
                IReadOnlyList<InventorySlotCoordinate> locks = _persisted.LockedSlots();
                foreach (InventorySlotCoordinate coordinate in locks) hashText.Append("|L:").Append(coordinate);
                string serializationReason = "serialization.not-requested";
                bool serialized = verifySerialization &&
                                  InventoryEvidence.TryDeterministicSave(_inventory, out serializationReason);
                if (verifySerialization && !serialized)
                    Diagnostics.Trace("Topology serialization proof failed: " + serializationReason);
                if (_generation == long.MaxValue)
                {
                    _snapshot = null;
                    _topologyActive = false;
                    _reasonCode = "topology.generation-exhausted";
                    RebuildStatusOnly();
                    return;
                }
                _generation++;
                _snapshot = new InventoryTopologySnapshot(
                    Plugin.ModuleId,
                    Plugin.ProtocolVersion,
                    _generation,
                    _mode,
                    _layout.Width,
                    _layout.Height,
                    items.Count,
                    serialized,
                    roles,
                    locks,
                    InventoryEvidence.HashTopology(hashText.ToString()));
                RebuildStatus(roles, locks, serialized, verifySerialization);
                Diagnostics.Trace("Topology cache rebuilt cause=" + SafeWord(cause) + " generation=" + _generation + ".");
            }
            finally { _rebuilding = false; }
        }

        private void RebuildStatus(
            IReadOnlyList<InventoryRoleSnapshot> roles,
            IReadOnlyList<InventorySlotCoordinate> lockedSlots,
            bool serializationVerified,
            bool serializationAttempted)
        {
            var text = new StringBuilder(768)
                .Append("Runic Inventory — ").Append(_mode).Append("\n")
                .Append("Status: ").Append(_reasonCode).Append(" | native 8×")
                .Append(_layout?.Height ?? 0).Append(" | save proof ")
                .Append(serializationVerified ? "verified" : serializationAttempted ? "FAILED" : "pending API capture")
                .Append("\nBottom row: ");
            for (int index = 0; index < roles.Count; index++)
            {
                InventoryRoleSnapshot role = roles[index];
                if (index > 0) text.Append("  ");
                text.Append(RoleShort(role.Role)).Append('=');
                if (!role.Occupied) text.Append("empty");
                else text.Append(SafePrefab(role.PrefabId)).Append('×').Append(role.Stack);
                if (role.Locked) text.Append("[LOCK]");
            }
            text.Append("\nLocks: ");
            if (lockedSlots.Count == 0) text.Append("none");
            else
            {
                int shown = Math.Min(16, lockedSlots.Count);
                for (int index = 0; index < shown; index++)
                {
                    if (index > 0) text.Append(' ');
                    text.Append(lockedSlots[index]);
                }
                if (shown < lockedSlots.Count) text.Append(" +").Append(lockedSlots.Count - shown);
            }
            text.Append("\nKeyboard: Alt+1/2/3 use | Alt+I sort | focus slot then Alt+L lock")
                .Append("\nController: validated ModifierAction chords only. Dedicated clients use their owning local Player.");
            _statusText = text.Length <= 1024 ? text.ToString() : text.ToString(0, 1024);
        }

        private void RebuildStatusOnly()
        {
            _statusText = "Runic Inventory — " + _mode + "\nStatus: " + _reasonCode +
                          "\nNo Runic item mutation is authorized. Native Valheim inventory behavior remains available." +
                          CompatibilityRemedy();
        }

        private string CompatibilityRemedy()
        {
            try
            {
                if (_layout == null || _inventory == null) return string.Empty;
                if (_reasonCode == "topology.clear-incompatible-special-row")
                {
                    foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
                    {
                        InventorySlotCoordinate coordinate = _layout.Coordinate(role);
                        ItemDrop.ItemData item = _inventory.GetItemAt(coordinate.X, coordinate.Y);
                        if (item == null || TopologyLayout.Accepts(role, ValheimContracts.Category(item))) continue;
                        return "\nMove " + SafePrefab(ValheimContracts.PrefabId(item)) + " out of bottom slot " +
                               (coordinate.X + 1) + " (" + RoleLabel(role) + "). The row reactivates automatically when valid.";
                    }
                }
                else if (_reasonCode == "topology.equipped-item-outside-role")
                {
                    List<ItemDrop.ItemData> items = _inventory.GetAllItems();
                    if (items == null || items.Count > InventoryTopologySnapshot.MaximumNativeSlots) return string.Empty;
                    foreach (ItemDrop.ItemData item in items)
                    {
                        if (item == null || !item.m_equipped ||
                            !TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out InventoryRoleKind role))
                            continue;
                        InventorySlotCoordinate coordinate = _layout.Coordinate(role);
                        if (item.m_gridPos.x == coordinate.X && item.m_gridPos.y == coordinate.Y) continue;
                        return "\nUnequip " + SafePrefab(ValheimContracts.PrefabId(item)) +
                               "; its canonical role is bottom slot " + (coordinate.X + 1) + " (" + RoleLabel(role) +
                               "). The row reactivates automatically when valid.";
                    }
                }
                return string.Empty;
            }
            catch (Exception)
            {
                return "\nClear incompatible bottom-row items or unequip out-of-role equipment; the row reactivates when valid.";
            }
        }

        private static bool IsRoleContentCompatibilityReason(string reasonCode) =>
            reasonCode == "topology.clear-incompatible-special-row" ||
            reasonCode == "topology.equipped-item-outside-role";

        private bool ValidateRoleContents(out string reasonCode)
        {
            if (_layout == null || _inventory == null)
            {
                reasonCode = "topology.unavailable";
                return false;
            }
            foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
            {
                InventorySlotCoordinate coordinate = _layout.Coordinate(role);
                ItemDrop.ItemData item = _inventory.GetItemAt(coordinate.X, coordinate.Y);
                if (item != null && !TopologyLayout.Accepts(role, ValheimContracts.Category(item)))
                {
                    reasonCode = "topology.clear-incompatible-special-row";
                    return false;
                }
            }
            if (_playerLoadInProgress)
            {
                reasonCode = "ok";
                return true;
            }
            List<ItemDrop.ItemData> items = _inventory.GetAllItems();
            if (items == null || items.Count > InventoryTopologySnapshot.MaximumNativeSlots)
            {
                reasonCode = "topology.item-bound-exceeded";
                return false;
            }
            foreach (ItemDrop.ItemData item in items)
            {
                if (item == null || !item.m_equipped ||
                    !TopologyLayout.TryEquipmentRole(ValheimContracts.Category(item), out InventoryRoleKind role))
                    continue;
                InventorySlotCoordinate coordinate = _layout.Coordinate(role);
                if (item.m_gridPos.x != coordinate.X || item.m_gridPos.y != coordinate.Y ||
                    !ReferenceEquals(_inventory.GetItemAt(coordinate.X, coordinate.Y), item))
                {
                    reasonCode = "topology.equipped-item-outside-role";
                    return false;
                }
            }
            reasonCode = "ok";
            return true;
        }

        private void RelocateEquippedItem(ItemDrop.ItemData item, InventoryRoleKind role)
        {
            InventorySlotCoordinate target = _layout.Coordinate(role);
            if (item.m_gridPos.x == target.X && item.m_gridPos.y == target.Y) return;
            if (IsLocked(item) || IsLocked(target.X, target.Y))
            {
                FailClosed("equipment.relocation-locked");
                Notify("Runic Inventory: equipment relocation was blocked by a slot lock; special roles are paused.");
                return;
            }
            if (!TryEnterMutation("runic.inventory/equipment-relocate", out IDisposable lease))
            {
                FailClosed("equipment.relocation-transaction-busy");
                Notify("Runic Inventory: equipment relocation conflicted with another transaction; special roles are paused.");
                return;
            }
            using (lease)
            {
                if (!InventoryEvidence.TryCaptureMutation(_inventory, out IReadOnlyList<ItemMutationEvidence> before, out string proofReason))
                {
                    Diagnostics.Warn("Equipment relocation skipped: " + proofReason + ".");
                    FailClosed("equipment.relocation-proof-failed");
                    return;
                }
                Vector2i source = item.m_gridPos;
                ItemDrop.ItemData occupant = _inventory.GetItemAt(target.X, target.Y);
                var changes = new List<PositionChange<ItemDrop.ItemData, Vector2i>>(before.Count);
                foreach (ItemMutationEvidence record in before)
                {
                    Vector2i destination = ReferenceEquals(record.Item, item)
                        ? new Vector2i(target.X, target.Y)
                        : ReferenceEquals(record.Item, occupant) ? source : record.Coordinate;
                    changes.Add(new PositionChange<ItemDrop.ItemData, Vector2i>(record.Item, record.Coordinate, destination));
                }
                string verifyReason = "evidence.verification-not-run";
                bool committed = AtomicPositionTransaction.TryCommit(
                    changes,
                    (targetItem, coordinate) => targetItem.m_gridPos = coordinate,
                    () => InventoryEvidence.VerifyUnchangedExceptPosition(_inventory, before, out verifyReason),
                    () => ValheimContracts.NotifyChanged(_inventory),
                    out Exception failure,
                    out Exception rollbackFailure);
                if (!committed)
                {
                    if (rollbackFailure != null)
                        Diagnostics.Error(rollbackFailure, "Equipment positions were restored in memory but rollback publication faulted.");
                    Diagnostics.Error(failure ?? new InvalidOperationException(verifyReason), "Equipment position relocation rolled back.");
                    FailClosed("equipment.relocation-failed");
                }
            }
        }

        private bool RequireAuthoritativeTopology(string action)
        {
            if (TopologyActive && _mode == InventoryAuthorityMode.AuthoritativeLocal && IsAuthoritativeLocal(_player)) return true;
            Notify("Runic Inventory: " + SafeWord(action) + " requires the owning local player; mode is " + _mode + ".");
            return false;
        }

        private bool IsLocked(ItemDrop.ItemData item) =>
            _repairAllowanceDepth == 0 && CanEnforceLocks() && item != null && _persisted != null && _layout != null &&
            item.m_gridPos.x >= 0 && item.m_gridPos.x < _layout.Width &&
            item.m_gridPos.y >= 0 && item.m_gridPos.y < _layout.Height &&
            _persisted.Width == _layout.Width && _persisted.Height == _layout.Height &&
            _persisted.IsLocked(item.m_gridPos.x, item.m_gridPos.y);

        private bool IsLocked(int x, int y) =>
            _repairAllowanceDepth == 0 && CanEnforceLocks() && _persisted != null && _layout != null &&
            x >= 0 && x < _layout.Width && y >= 0 && y < _layout.Height &&
            _persisted.Width == _layout.Width && _persisted.Height == _layout.Height && _persisted.IsLocked(x, y);

        private bool CanEnforceLocks() =>
            !_disposed && !_playerLoadInProgress && (InventoryConfig.Enabled?.Value ?? false) &&
            IsAuthoritativeLocal(_player) && LiveDimensionsMatch() && _persisted != null &&
            _persisted.Width == _layout.Width && _persisted.Height == _layout.Height &&
            (TopologyActive || ItemProtectionAvailabilityPolicy.SupportsIndependentLocks(_mode, _reasonCode));

        private bool RequireLockState()
        {
            if (CanEnforceLocks()) return true;
            Notify("Runic Inventory: slot locks are unavailable (" + _reasonCode + ").");
            return false;
        }

        private bool LiveDimensionsMatch() =>
            _inventory != null && _layout != null &&
            _layout.MatchesNativeDimensions(_inventory.GetWidth(), _inventory.GetHeight());

        private bool TryWritePersistedMetadata(string payload, out string reasonCode)
        {
            reasonCode = "persistence.write-failed";
            if (!IsAuthoritativeLocal(_player) || payload == null ||
                payload.Length > TopologyPersistenceCodec.MaximumPayloadCharacters || _player.m_customData == null)
                return false;
            bool hadPrevious = _player.m_customData.TryGetValue(TopologyPersistenceCodec.MetadataKey, out string previous);
            try
            {
                _player.m_customData[TopologyPersistenceCodec.MetadataKey] = payload;
                if (!_player.m_customData.TryGetValue(TopologyPersistenceCodec.MetadataKey, out string stored) ||
                    !string.Equals(stored, payload, StringComparison.Ordinal) ||
                    !TopologyPersistenceCodec.TryDecode(stored, out _, out _))
                    throw new InvalidOperationException("Persisted metadata did not verify after assignment.");
                reasonCode = "ok";
                return true;
            }
            catch (Exception)
            {
                try
                {
                    if (hadPrevious) _player.m_customData[TopologyPersistenceCodec.MetadataKey] = previous;
                    else _player.m_customData.Remove(TopologyPersistenceCodec.MetadataKey);
                }
                catch (Exception) { }
                return false;
            }
        }

        private void RestorePersistedMetadata(string payload)
        {
            if (_player?.m_customData == null || string.IsNullOrEmpty(payload))
                throw new InvalidOperationException("Previous topology metadata is unavailable.");
            _player.m_customData[TopologyPersistenceCodec.MetadataKey] = payload;
            if (!_player.m_customData.TryGetValue(
                    TopologyPersistenceCodec.MetadataKey, out string restored) ||
                !string.Equals(restored, payload, StringComparison.Ordinal))
                throw new InvalidOperationException("Previous topology metadata did not restore.");
        }

        private static bool ResizeDestinationsMatch(
            IReadOnlyList<ItemMutationEvidence> before,
            int previousSpecialRow,
            int currentSpecialRow)
        {
            if (before == null) return false;
            foreach (ItemMutationEvidence record in before)
            {
                if (record?.Item == null) return false;
                int expectedRow = NativeInventoryResizePlan.MapRow(
                    record.Coordinate.y, previousSpecialRow, currentSpecialRow);
                if (record.Item.m_gridPos.x != record.Coordinate.x ||
                    record.Item.m_gridPos.y != expectedRow)
                    return false;
            }
            return true;
        }

        private bool TryRefreshLoadMetadata()
        {
            _loadMetadataRefreshed = true;
            if (!_playerLoadInProgress || _player?.m_customData == null || _layout == null)
            {
                FailClosed("player.load-metadata-unavailable");
                return false;
            }
            if (!_player.m_customData.TryGetValue(TopologyPersistenceCodec.MetadataKey, out string payload))
            {
                if (CreateEmptyPersistedState(_layout, out PersistedTopologyState empty, out string emptyReason))
                {
                    _persisted = empty;
                    return true;
                }
                FailClosed(emptyReason);
                return false;
            }
            if (!TopologyPersistenceCodec.TryDecode(payload, out PersistedTopologyState persisted, out string reason) ||
                persisted.Width != _layout.Width || persisted.Height != _layout.Height)
            {
                FailClosed(reason == "ok" ? "persistence.topology-mismatch" : reason);
                return false;
            }
            _persisted = persisted;
            return true;
        }

        private bool TryCompleteDisableCleanup()
        {
            if (!_disableCleanupPending) return true;
            if (InventoryConfig.Enabled?.Value ?? false)
            {
                _disableCleanupPending = false;
                return true;
            }
            if (!IsAuthoritativeLocal(_player))
            {
                FailClosed("config.disable-owner-pending");
                return false;
            }
            if (!EnsureDedicatedRow(null, out string rowReason))
            {
                FailClosed(rowReason);
                return false;
            }
            if (!TryEnterMutation(
                    "runic.inventory/config-disable",
                    out IDisposable lease))
            {
                FailClosed("config.disable-transaction-busy");
                return false;
            }
            using (lease)
            {
                if (!TryRemovePersistedMetadata(out string removalReason))
                {
                    FailClosed(removalReason);
                    return false;
                }
            }
            _disableCleanupPending = false;
            Diagnostics.Trace("Disabled Inventory topology metadata cleanup committed.");
            return true;
        }

        private bool TryRemovePersistedMetadata(out string reasonCode)
        {
            reasonCode = "persistence.remove-failed";
            if (!IsAuthoritativeLocal(_player) || _player.m_customData == null) return false;
            if (!_player.m_customData.TryGetValue(TopologyPersistenceCodec.MetadataKey, out string previous))
            {
                reasonCode = "ok";
                return true;
            }
            try
            {
                _player.m_customData.Remove(TopologyPersistenceCodec.MetadataKey);
                if (_player.m_customData.ContainsKey(TopologyPersistenceCodec.MetadataKey))
                    throw new InvalidOperationException("Metadata removal did not commit.");
                reasonCode = "ok";
                return true;
            }
            catch (Exception)
            {
                try { _player.m_customData[TopologyPersistenceCodec.MetadataKey] = previous; }
                catch (Exception) { }
                return false;
            }
        }

        private static bool CreateEmptyPersistedState(
            TopologyLayout layout,
            out PersistedTopologyState state,
            out string reasonCode)
        {
            state = null;
            if (!TopologyPersistenceCodec.TryEncode(layout, Array.Empty<InventorySlotCoordinate>(), out string payload, out reasonCode))
                return false;
            return TopologyPersistenceCodec.TryDecode(payload, out state, out reasonCode);
        }

        private bool TryEnterMutation(string boundary, out IDisposable lease)
        {
            lease = null;
            if (_disposed || Interlocked.CompareExchange(ref _mutationActive, 1, 0) != 0)
                return false;
            lease = new MutationScope(this);
            return true;
        }

        private sealed class MutationScope : IDisposable
        {
            private InventoryRuntime _owner;

            internal MutationScope(InventoryRuntime owner) => _owner = owner;

            public void Dispose()
            {
                InventoryRuntime owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null) Interlocked.Exchange(ref owner._mutationActive, 0);
            }
        }

        private static bool IsAuthoritativeLocal(Player player) =>
            player && player == Player.m_localPlayer && player.IsOwner();

        private void TraceProtectionDecision(string decisionCode, Exception exception = null)
        {
            string decision = BoundReason(decisionCode, "protection.unknown");
            bool unknown = decision.StartsWith("unknown.", StringComparison.Ordinal);
            if (!unknown && !(InventoryConfig.VerboseDiagnostics?.Value ?? false)) return;
            string topology = BoundReason(_reasonCode, "runtime.unknown");
            string exceptionType = exception == null ? string.Empty : SafeWord(exception.GetType().Name);
            string signature = decision + "|" + (int)_mode + "|" + topology + "|" + exceptionType;
            lock (_protectionDiagnosticGate)
            {
                if (_protectionDiagnostics.Count >= MaximumProtectionDiagnostics ||
                    !_protectionDiagnostics.Add(signature)) return;
            }
            string message = "Item protection decision=" + decision + " mode=" + _mode +
                             " topology=" + topology +
                             (exceptionType.Length == 0 ? "." : " exception=" + exceptionType + ".");
            if (unknown) Diagnostics.Warn(message);
            else Diagnostics.Trace(message);
        }

        private void Notify(string text)
        {
            if (!_player) return;
            if (Time.unscaledTime < _nextMessageTime) return;
            _nextMessageTime = Time.unscaledTime + 0.35f;
            _player.Message(MessageHud.MessageType.Center, text, 0, null);
        }

        private static string RoleLabel(InventoryRoleKind role) =>
            role == InventoryRoleKind.Quick1 ? "quick slot 1" :
            role == InventoryRoleKind.Quick2 ? "quick slot 2" :
            role == InventoryRoleKind.Quick3 ? "quick slot 3" : role.ToString().ToLowerInvariant() + " equipment";

        private static string RoleShort(InventoryRoleKind role) =>
            role == InventoryRoleKind.Quick1 ? "Q1" :
            role == InventoryRoleKind.Quick2 ? "Q2" :
            role == InventoryRoleKind.Quick3 ? "Q3" : role.ToString();

        private static bool IsEquipmentRole(InventoryRoleKind role) => (int)role >= 1 && (int)role <= 5;

        private static bool SameItemReferences(
            IReadOnlyList<ItemDrop.ItemData> expected,
            IReadOnlyList<ItemDrop.ItemData> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count) return false;
            for (int index = 0; index < expected.Count; index++)
                if (!ReferenceEquals(expected[index], actual[index])) return false;
            return true;
        }

        private static string SafePrefab(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 28) text = text.Substring(0, 28);
            return text.Replace("<", string.Empty).Replace(">", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        }

        private static string SafeWord(string value)
        {
            string text = value ?? "action";
            var safe = new StringBuilder(Math.Min(48, text.Length));
            for (int index = 0; index < text.Length && safe.Length < 48; index++)
            {
                char character = text[index];
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_') safe.Append(character);
            }
            return safe.Length == 0 ? "action" : safe.ToString();
        }

        private static string BoundReason(string value, string fallback)
        {
            string text = value ?? string.Empty;
            if (text.Length == 0 || text.Length > 96) return fallback;
            for (int index = 0; index < text.Length; index++)
                if (!(char.IsLetterOrDigit(text[index]) || text[index] == '.' || text[index] == '-')) return fallback;
            return text;
        }

        private static string FirstFailure(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrEmpty(value) && !string.Equals(value, "ok", StringComparison.Ordinal)) return value;
            return "unknown";
        }
    }
}
