using System;
using System.Collections.Generic;
using System.Globalization;
using RunicSafety.Api;
using RunicSafety.Services;
using UnityEngine;
using SafetyConfirmationDecision = RunicSafety.Api.ConfirmationDecision;

namespace RunicSafety.Integration
{
    internal sealed class SafetyRuntime : ISafetyStatusService
    {
        private static readonly IReadOnlyList<string> PermanentGates = Array.AsReadOnly(new[]
        {
            "unsupported-destination.interception:policy-only-consumer-registration-required"
        });

        private readonly CorrelatedDiagnosticBuffer _diagnostics;
        private readonly ContextualConfirmationService _confirmations;
        private readonly ProtectedItemPolicy _protection;
        private readonly RecoveryPlanningService _recovery;
        private readonly MigrationBackupService _backups;
        private readonly CompatibilityGate _compatibility;

        internal SafetyRuntime(CorrelatedDiagnosticBuffer diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _confirmations = new ContextualConfirmationService(_diagnostics);
            _protection = new ProtectedItemPolicy(
                _diagnostics,
                () => SafetyConfig.ConfirmRareSacrifice?.Value ?? true,
                () => SafetyConfig.AdministratorBypass?.Value ?? false);
            _recovery = new RecoveryPlanningService(_diagnostics);
            _backups = new MigrationBackupService(_diagnostics);
            _compatibility = new CompatibilityGate(_diagnostics);
        }

        internal IContextualConfirmationService Confirmations => _confirmations;
        internal IProtectedItemPolicy Protection => _protection;
        internal IRecoveryPlanningService Recovery => _recovery;
        internal IMigrationBackupService Backups => _backups;
        internal ICompatibilityGate Compatibility => _compatibility;
        internal ISafetyDiagnosticService DiagnosticService => _diagnostics;

        public bool IsOperational => Plugin.RuntimeReady && (SafetyConfig.Enabled?.Value ?? false);
        public bool InventoryTopologyProviderAttached => _recovery.HasTopologyProvider;
        public IReadOnlyList<string> DisabledGates => PermanentGates;

        internal void Initialize()
        {
            LocalizationBridge.Install(Localization.instance);
            var local = new CompatibilityIdentity(
                Plugin.ModuleId,
                Plugin.Version,
                Plugin.ProtocolVersion,
                ValheimContracts.ReadGameVersion(),
                "native-valheim",
                SafetyConfig.SynchronizedRulesHash());
            CompatibilityDecision decision = _compatibility.Evaluate(local, local);
            if (!decision.MayEnter)
                throw new InvalidOperationException(
                    "The local compatibility identity failed: " + decision.Outcome);
            _diagnostics.Record(
                _diagnostics.NewCorrelationId("startup"),
                "startup",
                "services-ready");
        }

        internal void OnConfigurationChanged()
        {
            _confirmations.Clear();
            _diagnostics.Record(
                _diagnostics.NewCorrelationId("config"),
                "configuration",
                "refreshed");
        }

        internal void Shutdown()
        {
            _confirmations.Clear();
        }

        internal bool AuthorizePieceRemoval(Player player)
        {
            if (player == null) return true;
            Piece piece = player.GetHoveringPiece();
            if (piece == null) return true;

            Container container = piece.GetComponentInChildren<Container>();
            if (!Enabled()) return true;

            bool vehicle = HasVehicle(piece);
            int itemCount = container?.GetInventory()?.NrOfItems() ?? 0;
            if (vehicle && (SafetyConfig.ConfirmVehicleDestruction?.Value ?? true))
                return ConfirmOrMessage(
                    player,
                    SafetyActionKind.VehicleDestruction,
                    "piece:" + ObjectKey(piece),
                    HashState("vehicle", itemCount.ToString(CultureInfo.InvariantCulture),
                        ContainerRevision(container)),
                    "$runicsafety_confirm_vehicle");
            if (itemCount > 0 && (SafetyConfig.ConfirmOccupiedContainer?.Value ?? true))
                return ConfirmOrMessage(
                    player,
                    SafetyActionKind.OccupiedContainerDestruction,
                    "piece:" + ObjectKey(piece),
                    HashState("container", itemCount.ToString(CultureInfo.InvariantCulture),
                        container.GetInventory().NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture),
                        ContainerRevision(container)),
                    "$runicsafety_confirm_container");
            return true;
        }

        internal bool AuthorizePortalOverwrite(TeleportWorld portal, string newText)
        {
            if (!Enabled() || !(SafetyConfig.ConfirmPortalOverwrite?.Value ?? true) || portal == null) return true;
            string current = portal.GetText() ?? string.Empty;
            string replacement = newText ?? string.Empty;
            if (current.Length == 0 || string.Equals(current, replacement, StringComparison.Ordinal)) return true;
            SafetyConfirmationDecision decision = _confirmations.Evaluate(
                new ConfirmationRequest(
                    SafetyActionKind.PortalOverwrite,
                    "portal:" + ObjectKey(portal),
                    HashState(current, replacement),
                    SafetyConfig.ConfirmationWindow),
                DateTime.UtcNow);
            if (decision.MayProceed) return true;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$runicsafety_confirm_portal");
            return false;
        }

        internal bool AuthorizeSmelterOre(Smelter station, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            ItemDrop.ItemData candidate = item ?? FindSmelterItem(station, user?.GetInventory());
            return AuthorizeItem(candidate, ProtectionDestination.SmelterInput, station, user);
        }

        internal bool AuthorizeSmelterFuel(Smelter station, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            ItemDrop.ItemData candidate = item ?? FindNamedItem(user?.GetInventory(), station?.m_fuelItem);
            return AuthorizeItem(candidate, ProtectionDestination.SmelterFuel, station, user);
        }

        internal bool AuthorizeCookingFood(CookingStation station, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            ItemDrop.ItemData candidate = item ?? FindCookingItem(station, user?.GetInventory());
            return AuthorizeItem(candidate, ProtectionDestination.CookingStation, station, user);
        }

        internal bool AuthorizeCookingFuel(CookingStation station, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            ItemDrop.ItemData candidate = item ?? FindNamedItem(user?.GetInventory(), station?.m_fuelItem);
            return AuthorizeItem(candidate, ProtectionDestination.CookingFuel, station, user);
        }

        internal bool AuthorizeFermenter(Fermenter station, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            return AuthorizeItem(item, ProtectionDestination.Fermenter, station, user);
        }

        internal bool AuthorizeItemStand(ItemStand stand, Humanoid user, ItemDrop.ItemData item)
        {
            if (!Enabled() || !ProtectionEnabled()) return true;
            return AuthorizeItem(item, ProtectionDestination.ItemStand, stand, user);
        }

        internal bool AuthorizeIncineratorClient(Incinerator incinerator, Humanoid user)
        {
            if (!Enabled() || !ProtectionEnabled() || incinerator?.m_container?.GetInventory() == null) return true;
            Inventory inventory = incinerator.m_container.GetInventory();
            AggregateProtection aggregate = EvaluateInventory(
                inventory,
                ProtectionDestination.Obliterator,
                IsLocalAdministrator());
            if (aggregate.Denied)
            {
                ShowProtectionMessage(user, aggregate.Reason);
                return false;
            }
            if (!aggregate.ConfirmationRequired) return true;

            SafetyConfirmationDecision confirmation = _confirmations.Evaluate(
                new ConfirmationRequest(
                    SafetyActionKind.RareItemSacrifice,
                    "client-incinerator:" + ObjectKey(incinerator),
                    InventoryFingerprint(inventory),
                    SafetyConfig.ConfirmationWindow),
                DateTime.UtcNow);
            if (!confirmation.MayProceed)
                user?.Message(MessageHud.MessageType.Center, "$runicsafety_confirm_rare");

            // The first vanilla request is allowed to reach the current ZDO owner.
            // Its independently keyed owner-side gate records and rejects that first request.
            return true;
        }

        internal bool AuthorizeIncineratorOwner(Incinerator incinerator, long sender)
        {
            if (!Enabled() || !ProtectionEnabled() || incinerator?.m_container?.GetInventory() == null) return true;
            Inventory inventory = incinerator.m_container.GetInventory();
            AggregateProtection aggregate = EvaluateInventory(
                inventory,
                ProtectionDestination.Obliterator,
                IsSenderAdministrator(sender));
            if (aggregate.Denied) return false;
            if (!aggregate.ConfirmationRequired) return true;
            SafetyConfirmationDecision confirmation = _confirmations.Evaluate(
                new ConfirmationRequest(
                    SafetyActionKind.RareItemSacrifice,
                    "owner-incinerator:" + sender.ToString(CultureInfo.InvariantCulture) + ":" + ObjectKey(incinerator),
                    InventoryFingerprint(inventory),
                    SafetyConfig.ConfirmationWindow),
                DateTime.UtcNow);
            return confirmation.MayProceed;
        }

        internal TombstoneAuditState BeginTombstoneAudit(Player player)
        {
            if (!Enabled() || player == null || player.GetInventory() == null) return default;
            Inventory inventory = player.GetInventory();
            bool serialized = VerifySerialization(inventory, out int serializedBytes);
            long playerId = player.GetPlayerID();
            InventoryTopologySnapshot topology = null;
            if (_recovery.HasTopologyProvider &&
                !_recovery.TryCaptureTopology(playerId, out topology, out string failure))
            {
                topology = new InventoryTopologySnapshot(
                    "missing-adapter", "invalid", 0, 0, 0, false, "missing");
                _diagnostics.Record(
                    _diagnostics.NewCorrelationId("topology"),
                    "topology",
                    BoundCode(failure, "provider-unavailable"),
                    SafetyDiagnosticSeverity.Warning);
            }
            RecoveryPlan plan = _recovery.Plan(new RecoveryPlanningRequest(
                playerId,
                inventory.GetWidth(),
                inventory.GetHeight(),
                inventory.NrOfItems(),
                serialized,
                topology));
            return new TombstoneAuditState(
                true,
                inventory.NrOfItems(),
                serializedBytes,
                plan);
        }

        internal void CompleteTombstoneAudit(Player player, TombstoneAuditState state)
        {
            if (!state.Active) return;
            _diagnostics.Record(
                state.Plan.CorrelationId,
                "tombstone",
                state.Plan.IsLosslessPlan ? "vanilla-call-completed" : "recovery-plan-unresolved",
                state.Plan.IsLosslessPlan
                    ? SafetyDiagnosticSeverity.Information
                    : SafetyDiagnosticSeverity.Warning);
            if (!state.Plan.IsLosslessPlan)
                player?.Message(MessageHud.MessageType.TopLeft, "$runicsafety_recovery_unsafe");
        }

        private bool AuthorizeItem(
            ItemDrop.ItemData item,
            ProtectionDestination destination,
            Component target,
            Humanoid user)
        {
            if (item == null) return true;
            string correlation = _diagnostics.NewCorrelationId("destination");
            ItemLockState lockState = InventoryProtectionAdapter.Resolve(
                item, destination, out bool inventoryIntegrationPresent);
            ItemProtectionDecision decision = _protection.Evaluate(new ItemProtectionRequest(
                Describe(item, lockState),
                destination,
                correlation,
                IsLocalAdministrator(),
                inventoryIntegrationPresent,
                item));
            if (decision.Outcome == ProtectionOutcome.Allow) return true;
            if (decision.Outcome == ProtectionOutcome.Deny)
            {
                ShowProtectionMessage(user, decision.Reason);
                return false;
            }
            SafetyConfirmationDecision confirmation = _confirmations.Evaluate(
                new ConfirmationRequest(
                    SafetyActionKind.ProtectedDestination,
                    "destination:" + ((int)destination).ToString(CultureInfo.InvariantCulture) +
                    ":" + ObjectKey(target),
                    ItemFingerprint(item),
                    SafetyConfig.ConfirmationWindow),
                DateTime.UtcNow);
            if (confirmation.MayProceed) return true;
            user?.Message(MessageHud.MessageType.Center, "$runicsafety_confirm_rare");
            return false;
        }

        private AggregateProtection EvaluateInventory(
            Inventory inventory,
            ProtectionDestination destination,
            bool administrator)
        {
            bool confirmation = false;
            ProtectionReason reason = ProtectionReason.None;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int index = 0; index < items.Count; index++)
            {
                ItemDrop.ItemData item = items[index];
                ItemLockState lockState = InventoryProtectionAdapter.Resolve(
                    item, out bool inventoryIntegrationPresent);
                string correlation = _diagnostics.NewCorrelationId("obliterate");
                ItemProtectionDecision decision = _protection.Evaluate(new ItemProtectionRequest(
                    Describe(item, lockState),
                    destination,
                    correlation,
                    administrator,
                    inventoryIntegrationPresent,
                    item));
                if (decision.Outcome == ProtectionOutcome.Deny)
                    return new AggregateProtection(true, false, decision.Reason);
                if (decision.Outcome == ProtectionOutcome.RequireConfirmation)
                {
                    confirmation = true;
                    reason = decision.Reason;
                }
            }
            return new AggregateProtection(false, confirmation, reason);
        }

        private static ProtectedItemDescriptor Describe(
            ItemDrop.ItemData item,
            ItemLockState lockState)
        {
            string prefab = item?.m_dropPrefab != null
                ? item.m_dropPrefab.name
                : item?.m_shared?.m_name ?? string.Empty;
            return new ProtectedItemDescriptor(
                prefab,
                item?.m_equipped ?? false,
                item?.m_shared?.m_questItem ?? false,
                lockState,
                SafetyConfig.IsRarePrefab(prefab));
        }

        private static ItemDrop.ItemData FindSmelterItem(Smelter station, Inventory inventory)
        {
            if (station == null || inventory == null) return null;
            foreach (Smelter.ItemConversion conversion in station.m_conversion)
            {
                ItemDrop source = conversion?.m_from;
                if (source == null) continue;
                ItemDrop.ItemData candidate = inventory.GetItem(source.m_itemData.m_shared.m_name);
                if (candidate != null) return candidate;
            }
            return null;
        }

        private static ItemDrop.ItemData FindCookingItem(CookingStation station, Inventory inventory)
        {
            if (station == null || inventory == null) return null;
            foreach (CookingStation.ItemConversion conversion in station.m_conversion)
            {
                ItemDrop source = conversion?.m_from;
                if (source == null) continue;
                ItemDrop.ItemData candidate = inventory.GetItem(source.m_itemData.m_shared.m_name);
                if (candidate != null) return candidate;
            }
            return null;
        }

        private static ItemDrop.ItemData FindNamedItem(Inventory inventory, ItemDrop source)
        {
            if (inventory == null || source == null) return null;
            return inventory.GetItem(source.m_itemData.m_shared.m_name);
        }

        private static bool VerifySerialization(Inventory inventory, out int bytes)
        {
            bytes = 0;
            try
            {
                var package = new ZPackage();
                inventory.Save(package);
                bytes = package.Size();
                return bytes > 0;
            }
            catch (Exception) { return false; }
        }

        private bool ConfirmOrMessage(
            Player player,
            SafetyActionKind action,
            string context,
            string fingerprint,
            string message)
        {
            SafetyConfirmationDecision decision = _confirmations.Evaluate(
                new ConfirmationRequest(action, context, fingerprint, SafetyConfig.ConfirmationWindow),
                DateTime.UtcNow);
            if (decision.MayProceed) return true;
            player.Message(MessageHud.MessageType.Center, message);
            return false;
        }

        private static bool HasVehicle(Piece piece) =>
            piece.GetComponentInParent<Ship>() != null || piece.GetComponentInChildren<Ship>() != null ||
            piece.GetComponentInParent<Vagon>() != null || piece.GetComponentInChildren<Vagon>() != null;

        private static string ContainerRevision(Container container)
        {
            try
            {
                ZNetView view = container?.GetComponent<ZNetView>();
                return view?.GetZDO()?.DataRevision.ToString(CultureInfo.InvariantCulture) ?? "0";
            }
            catch (Exception) { return "0"; }
        }

        private static string ObjectKey(Component component)
        {
            if (component == null) return "none";
            try
            {
                ZNetView view = component.GetComponent<ZNetView>() ?? component.GetComponentInParent<ZNetView>();
                ZDO zdo = view?.GetZDO();
                if (zdo != null) return zdo.m_uid.ToString();
            }
            catch (Exception) { }
            return component.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static string ItemFingerprint(ItemDrop.ItemData item) => HashState(
            item?.m_dropPrefab?.name ?? item?.m_shared?.m_name ?? string.Empty,
            (item?.m_stack ?? 0).ToString(CultureInfo.InvariantCulture),
            (item?.m_quality ?? 0).ToString(CultureInfo.InvariantCulture),
            (item?.m_variant ?? 0).ToString(CultureInfo.InvariantCulture));

        private static string InventoryFingerprint(Inventory inventory)
        {
            ulong hash = 14695981039346656037UL;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int index = 0; index < items.Count; index++)
            {
                string value = ItemFingerprint(items[index]);
                AppendHash(ref hash, value);
            }
            AppendHash(ref hash, items.Count.ToString(CultureInfo.InvariantCulture));
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static string HashState(params string[] values)
        {
            ulong hash = 14695981039346656037UL;
            foreach (string value in values) AppendHash(ref hash, value ?? string.Empty);
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static void AppendHash(ref ulong hash, string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                hash ^= (byte)character;
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
            hash ^= 255;
            hash *= 1099511628211UL;
        }

        private static bool Enabled() => SafetyConfig.Enabled?.Value ?? false;
        private static bool ProtectionEnabled() => SafetyConfig.ProtectedDestinations?.Value ?? true;
        private static bool IsLocalAdministrator()
        {
            try { return ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost(); }
            catch (Exception) { return false; }
        }

        private static bool IsSenderAdministrator(long sender)
        {
            try
            {
                return ZNet.instance != null && ZNet.instance.IsServer() &&
                       sender == ZNet.GetUID() &&
                       ZNet.instance.LocalPlayerIsAdminOrHost();
            }
            catch (Exception) { return false; }
        }

        private static void ShowProtectionMessage(Humanoid user, ProtectionReason reason)
        {
            string message = reason == ProtectionReason.ProviderUnavailable
                ? "$runicsafety_provider_missing"
                : "$runicsafety_protected_denied";
            user?.Message(MessageHud.MessageType.Center, message);
        }

        private static string BoundCode(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length <= 64 ? trimmed : trimmed.Substring(0, 64);
        }

        private readonly struct AggregateProtection
        {
            internal AggregateProtection(bool denied, bool confirmationRequired, ProtectionReason reason)
            {
                Denied = denied;
                ConfirmationRequired = confirmationRequired;
                Reason = reason;
            }
            internal bool Denied { get; }
            internal bool ConfirmationRequired { get; }
            internal ProtectionReason Reason { get; }
        }

    }

    internal readonly struct TombstoneAuditState
    {
        internal TombstoneAuditState(bool active, int itemCount, int serializedBytes, RecoveryPlan plan)
        {
            Active = active;
            ItemCount = itemCount;
            SerializedBytes = serializedBytes;
            Plan = plan;
        }
        internal bool Active { get; }
        internal int ItemCount { get; }
        internal int SerializedBytes { get; }
        internal RecoveryPlan Plan { get; }
    }
}
