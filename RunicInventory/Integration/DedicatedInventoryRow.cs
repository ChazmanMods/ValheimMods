using System;
using System.Collections.Generic;
using System.Globalization;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Integration
{
    internal sealed partial class InventoryRuntime
    {
        private bool _rowMigration;
        private bool _restoringEnabled;

        internal bool CanDisable(out string message)
        {
            message = string.Empty;
            if (_player && _player == Player.m_localPlayer && _playerLoadInProgress)
            { message = "Cannot disable RunicInventory while your inventory is loading."; return false; }
            if (!_player || _player != Player.m_localPlayer ||
                _player.m_customData?.ContainsKey(DedicatedRowPlan.MetadataKey) != true) return true;
            message = "Cannot disable RunicInventory while your inventory is loading or unavailable.";
            if (_playerLoadInProgress || _rowMigration || !IsAuthoritativeLocal(_player) || _inventory == null) return false;
            if (!int.TryParse(_player.m_customData[DedicatedRowPlan.MetadataKey], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int rows) || rows < 4 || rows > 9) return false;
            var planItems = new List<DedicatedRowItem>();
            foreach (ItemDrop.ItemData item in _inventory.GetAllItems())
            {
                if (item == null) return false;
                planItems.Add(new DedicatedRowItem(new InventorySlotCoordinate(item.m_gridPos.x, item.m_gridPos.y),
                    ValheimContracts.Category(item), item.m_equipped));
            }
            bool quiver = BetterArcheryCompatibility.Active;
            if (DedicatedRowPlan.TryCreate(rows, false, _layout?.SpecialRow ?? rows, planItems, null,
                    out _, out _, out string reason, quiver, quiver ? rows + 1 : -1))
            { message = string.Empty; return true; }
            message = reason == "extra-row.clear-space-before-shrinking"
                ? "Cannot disable RunicInventory: there is no room to move the items in the extra row. Free normal inventory slots first."
                : "Cannot disable RunicInventory until the inventory layout is valid.";
            return false;
        }

        private void RestoreEnabled(string message)
        {
            _restoringEnabled = true;
            try { InventoryConfig.Enabled.Value = true; }
            finally { _restoringEnabled = false; }
            _disableCleanupPending = false;
            Notify(message);
        }

        internal bool HandlesNativeInventorySize(Player player) => !_disposed && !_batch &&
            player && player == Player.m_localPlayer &&
            ((InventoryConfig.Enabled?.Value ?? false) ||
             player.m_customData?.ContainsKey(DedicatedRowPlan.MetadataKey) == true);

        internal void SetNativeInventorySize(Player player, int rows)
        {
            // Do not let vanilla shrink first and DropInvalidItems before migrating our row.
            Rebind(player, "native-pocket-size", rows);
        }

        // Last barrier before vanilla's destructive out-of-bounds cleanup. This remains
        // available even if the UI topology was invalidated by another mod's resize.
        internal bool AllowInvalidItemCleanup(Humanoid actor)
        {
            if (_disposed || _batch || !actor || actor != Player.m_localPlayer ||
                actor != _player || _player.m_customData?.ContainsKey(DedicatedRowPlan.MetadataKey) != true)
                return true;
            Inventory inventory = actor.GetInventory();
            if (inventory == null || !ReferenceEquals(inventory, _inventory)) return false;
            var positions = new List<CleanupCoordinate>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                positions.Add(item == null ? new CleanupCoordinate(-1, -1) :
                    new CleanupCoordinate(item.m_gridPos.x, item.m_gridPos.y));
            }
            var decision = InventoryCleanupPolicy.Evaluate(inventory.GetWidth(), inventory.GetHeight(), positions, out int height);
            if (decision == InventoryCleanupDecision.NativeAllowed) return true;
            // Never remove/recreate items or invoke DropItem as a recovery action.
            if (decision == InventoryCleanupDecision.RetainAndExpand)
            {
                inventory.SetHeight(height);
                InventoryGui.instance?.SetInventorySize(height);
            }
            FailClosed("topology.external-resize-cleanup-prevented");
            Diagnostics.Warn("Prevented automatic item ejection after an inventory dimension change. Items retained; height=" + inventory.GetHeight() + ".");
            return false;
        }

        private bool EnsureDedicatedRow(int? requestedNativeRows, out string reason)
        {
            reason = "extra-row.not-ready";
            if (_rowMigration || _playerLoadInProgress || !IsAuthoritativeLocal(_player) ||
                _inventory == null || _inventory.GetWidth() != 8 || _player.m_customData == null) return false;
            bool enabled = InventoryConfig.Enabled?.Value ?? false;
            var data = _player.m_customData;
            bool hadMarker = data.TryGetValue(DedicatedRowPlan.MetadataKey, out string marker);
            if (!enabled && !hadMarker) return true;
            int nativeRows = 4;
            if (_player.TryGetUniqueKeyValue("invrows", out string nativeValue) &&
                !int.TryParse(nativeValue, NumberStyles.None, CultureInfo.InvariantCulture, out nativeRows))
            { reason = "extra-row.invalid-native-size"; return false; }
            int previousNativeRows = nativeRows;
            if (requestedNativeRows.HasValue) nativeRows = requestedNativeRows.Value;
            if (nativeRows < 4 || nativeRows > 9) { reason = "extra-row.unsupported-native-size"; return false; }
            int oldRoleRow = -1;
            if (hadMarker && (!int.TryParse(marker, NumberStyles.None, CultureInfo.InvariantCulture, out oldRoleRow) ||
                              oldRoleRow < 4 || oldRoleRow > 9))
            { reason = "extra-row.invalid-marker"; return false; }
            bool quiver = BetterArcheryCompatibility.Active;
            bool hadQuiverMarker = data.TryGetValue(DedicatedRowPlan.QuiverMetadataKey, out string quiverMarker);
            int originalQuiverRow = BetterArcheryCompatibility.QuiverRow;
            int previousQuiverRow = -1;
            if (hadQuiverMarker)
            {
                if (!int.TryParse(quiverMarker, NumberStyles.None, CultureInfo.InvariantCulture, out int oldNativeRows) ||
                    oldNativeRows < 4 || oldNativeRows > 9 || hadMarker && oldRoleRow != oldNativeRows)
                { reason = "extra-row.invalid-quiver-marker"; return false; }
                previousQuiverRow = oldNativeRows + 1;
                if (hadMarker) oldRoleRow += 2;
            }
            else if (quiver)
                previousQuiverRow = previousNativeRows + 1;
            bool hadTopology = data.TryGetValue(TopologyPersistenceCodec.MetadataKey, out string previousPayload);
            PersistedTopologyState previous = null;
            if (hadTopology && !TopologyPersistenceCodec.TryDecode(previousPayload, out previous, out reason)) return false;
            if (hadMarker && previous != null && previous.Height != oldRoleRow + 1)
            { reason = "extra-row.metadata-mismatch"; return false; }
            // Pre-extra-row versions reserved the old native bottom row.
            if (!hadMarker && previous != null) oldRoleRow = previous.Height - 1;
            int targetHeight = nativeRows + (quiver ? 2 : 0) + (enabled ? 1 : 0);
            int originalHeight = _inventory.GetHeight();
            if (originalHeight < 4 || originalHeight > 12) { reason = "extra-row.unsupported-live-size"; return false; }
            var items = _inventory.GetAllItems();
            int captureHeight = originalHeight;
            foreach (ItemDrop.ItemData item in items)
            {
                if (item == null || item.m_gridPos.y < 0 || item.m_gridPos.y >= 12)
                { reason = "extra-row.invalid-item-position"; return false; }
                captureHeight = Math.Max(captureHeight, item.m_gridPos.y + 1);
            }
            var planItems = new List<DedicatedRowItem>();
            foreach (ItemDrop.ItemData item in items)
                planItems.Add(new DedicatedRowItem(new InventorySlotCoordinate(item.m_gridPos.x, item.m_gridPos.y),
                    ValheimContracts.Category(item), item.m_equipped));
            if (!DedicatedRowPlan.TryCreate(nativeRows, enabled, oldRoleRow, planItems,
                    previous?.LockedSlots(), out InventorySlotCoordinate[] positions, out var locks, out reason,
                    quiver, previousQuiverRow))
            {
                _inventory.SetHeight(captureHeight);
                InventoryGui.instance?.SetInventorySize(captureHeight);
                return false;
            }
            string nextPayload = null;
            if (enabled)
            {
                if (!TopologyLayout.TryCreate(8, targetHeight, out TopologyLayout nextLayout, out reason) ||
                    !TopologyPersistenceCodec.TryEncode(nextLayout, locks, out nextPayload, out reason)) return false;
            }
            if (!TryEnterMutation("runic.inventory/dedicated-row", out IDisposable lease))
            { reason = "extra-row.transaction-busy"; return false; }
            IReadOnlyList<ItemMutationEvidence> before = null;
            _rowMigration = true;
            _inventory.m_onChanged -= OnInventoryChanged;
            try
            {
                using (lease)
                {
                    // Load deliberately permits saved coordinates outside the initial native height.
                    // Expand for evidence capture; never discard or recreate those item instances.
                    _inventory.SetHeight(captureHeight);
                    if (!InventoryEvidence.TryCaptureMutation(_inventory, out before, out reason))
                        throw new InvalidOperationException(reason);
                    _inventory.SetHeight(targetHeight);
                    for (int i = 0; i < items.Count; i++)
                        items[i].m_gridPos = new Vector2i(positions[i].X, positions[i].Y);
                    if (!InventoryEvidence.VerifyUnchangedExceptPosition(_inventory, before, out reason))
                        throw new InvalidOperationException(reason);
                    if (enabled)
                    {
                        data[DedicatedRowPlan.MetadataKey] = nativeRows.ToString(CultureInfo.InvariantCulture);
                        data[TopologyPersistenceCodec.MetadataKey] = nextPayload;
                    }
                    else
                    {
                        data.Remove(DedicatedRowPlan.MetadataKey);
                        data.Remove(TopologyPersistenceCodec.MetadataKey);
                    }
                    if (quiver)
                    {
                        data[DedicatedRowPlan.QuiverMetadataKey] = nativeRows.ToString(CultureInfo.InvariantCulture);
                        BetterArcheryCompatibility.SetQuiverRow(nativeRows + 1);
                    }
                    else data.Remove(DedicatedRowPlan.QuiverMetadataKey);
                    // Native pocket progression excludes the dedicated Runic row.
                    if (requestedNativeRows.HasValue)
                        _player.AddUniqueKeyValue("invrows", nativeRows.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception exception)
            {
                InventoryEvidence.RestorePositions(before);
                _inventory.SetHeight(captureHeight); // keep every restored item accessible, including loaded extra-row items
                if (hadMarker) data[DedicatedRowPlan.MetadataKey] = marker;
                else data.Remove(DedicatedRowPlan.MetadataKey);
                if (hadTopology) data[TopologyPersistenceCodec.MetadataKey] = previousPayload;
                else data.Remove(TopologyPersistenceCodec.MetadataKey);
                if (hadQuiverMarker) data[DedicatedRowPlan.QuiverMetadataKey] = quiverMarker;
                else data.Remove(DedicatedRowPlan.QuiverMetadataKey);
                if (quiver) BetterArcheryCompatibility.SetQuiverRow(originalQuiverRow);
                reason = "extra-row.migration-failed";
                Diagnostics.Error(exception, "Dedicated row migration rolled back without removing items.");
                return false;
            }
            finally
            {
                _inventory.m_onChanged += OnInventoryChanged;
                _rowMigration = false;
                InventoryGui.instance?.SetInventorySize(_inventory.GetHeight());
            }
            reason = "ok";
            return true;
        }

        // Restrictions apply independently of optional sorting/lock-provider availability.
        // Deserialization is exempt: loaded items are migrated after the entire player loads.
        private bool HasDedicatedRow(Inventory inventory) => !_disposed && !_playerLoadInProgress && !_rowMigration &&
            (InventoryConfig.Enabled?.Value ?? false) && _player == Player.m_localPlayer &&
            ReferenceEquals(inventory, _inventory) &&
            _player?.m_customData?.ContainsKey(DedicatedRowPlan.MetadataKey) == true;

        internal bool AllowPositionedAddition(Inventory inventory, ItemDrop.ItemData item, int x, int y, bool loading)
        {
            if (loading || !HasDedicatedRow(inventory)) return true;
            if (x < 0 || x >= inventory.GetWidth() || y < 0 || y >= inventory.GetHeight()) return false;
            if (IsQuiverReservedRow(y))
                return y == inventory.GetHeight() - 2 && x < 3 &&
                    item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Ammo && BetterArcheryCompatibility.Equipped;
            return y != inventory.GetHeight() - 1 ||
                   item != null && TopologyLayout.Accepts((InventoryRoleKind)(x + 1), ValheimContracts.Category(item));
        }

        private bool HasQuiverLayout => BetterArcheryCompatibility.Active && _player?.m_customData != null &&
            _player.m_customData.ContainsKey(DedicatedRowPlan.QuiverMetadataKey) &&
            _player.m_customData.ContainsKey(DedicatedRowPlan.MetadataKey);
        private bool IsQuiverReservedRow(int y) => HasQuiverLayout && _inventory != null &&
            (y == _inventory.GetHeight() - 3 || y == _inventory.GetHeight() - 2);
    }
}
