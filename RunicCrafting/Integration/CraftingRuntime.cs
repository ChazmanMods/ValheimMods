using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class CraftingRuntime
    {
        private static readonly ExactMaterialPlanner Planner = new ExactMaterialPlanner();
        private static readonly ExactMaterialTransactionEngine Engine = new ExactMaterialTransactionEngine(Planner);
        private static ManualLogSource _log;
        private static WorkshopAccessRuntime _workshopAccess;
        private static ContainerQueryRuntime _containerQuery;
        private static readonly object AvailabilityCacheGate = new object();
        private static readonly Dictionary<BreakdownKey, BreakdownEntry> AvailabilityCache =
            new Dictionary<BreakdownKey, BreakdownEntry>();
        private static string _lastStationStatusKey;
        private static float _lastStationStatusAt = -100f;

        [ThreadStatic] private static ActiveOperation _activeOperation;
        [ThreadStatic] private static PendingCraft _pendingCraft;
        [ThreadStatic] private static bool _insidePlacementUpdate;

        internal static bool IsInitialized => _containerQuery != null;

        internal static void Initialize(ManualLogSource log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _workshopAccess = new WorkshopAccessRuntime();
            _containerQuery = new ContainerQueryRuntime(_workshopAccess);
        }

        internal static void Shutdown()
        {
            _pendingCraft = null;
            _insidePlacementUpdate = false;
            CleanupActive(success: false);
            ContainerSpatialIndex.Clear();
            _containerQuery = null;
            _workshopAccess = null;
            _log = null;
            lock (AvailabilityCacheGate) AvailabilityCache.Clear();
            _lastStationStatusKey = null;
            _lastStationStatusAt = -100f;
        }

        internal static void OnConfigurationChanged()
        {
            lock (AvailabilityCacheGate) AvailabilityCache.Clear();
            _lastStationStatusKey = null;
            _lastStationStatusAt = -100f;
        }

        internal static bool CanUseStation(CraftingStation station, Player player, out string reason)
        {
            reason = "vanilla";
            if (!Configuration.Enabled.Value)
            {
                CraftingDiagnostics.TraceGate("station-access", "bypass:mod-disabled");
                return true;
            }
            if (!IsInitialized)
            {
                CraftingDiagnostics.TraceGate("station-access", "bypass:runtime-unavailable");
                return true;
            }
            if (!ValheimReflection.CanMutateLocalPlayer(player))
            {
                CraftingDiagnostics.TraceGate("station-access", "bypass:local-player-owner-required");
                return true;
            }
            WorkshopAccessDecision decision = _workshopAccess.Evaluate(station, player, WorkshopAction.StationUse);
            reason = decision.ReasonCode;
            CraftingDiagnostics.TraceGate(
                "station-access",
                decision.Allowed ? "allowed" : "denied:" + reason);
            return decision.Allowed;
        }

        internal static void ShowStationStatus(CraftingStation station, Player player)
        {
            if (!Configuration.ShowStatusMessages.Value || player == null) return;
            NearbyCraftingFeatureState state = EvaluateNearbyCraftingState(
                player,
                recipe: null,
                station,
                evaluateRecipeRules: false);
            string stationId = station == null ? "none" : ValheimReflection.StationEndpointId(station);
            string key = stationId + "|" + state.ReasonCode;
            float now = Time.realtimeSinceStartup;
            if (string.Equals(_lastStationStatusKey, key, StringComparison.Ordinal) &&
                now - _lastStationStatusAt < 2f)
                return;
            _lastStationStatusKey = key;
            _lastStationStatusAt = now;

            string message = state.IsReady
                ? "Runic Crafting: nearby materials ON (up to " +
                  GetStationRange(station).ToString("0.#", CultureInfo.InvariantCulture) +
                  "m); recipe rows show combined/required totals with C/N/T/M hover details"
                : ExplainNearbyState(state);
            player.Message(MessageHud.MessageType.TopLeft, message);
            CraftingDiagnostics.TraceAction("station-status", state.ReasonCode, message);
        }

        internal static bool BeforeCraft(
            InventoryGui gui,
            Player player,
            Recipe recipe,
            ItemDrop.ItemData upgradeItem,
            bool multiCrafting,
            int multiCraftAmount)
        {
            CraftingDiagnostics.TraceAction("craft-attempt", "received");
            CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;
            if (Configuration.Enabled.Value && IsInitialized && ValheimReflection.CanMutateLocalPlayer(player) &&
                station != null && !CanUseStation(station, player, out string stationReason))
            {
                player.Message(MessageHud.MessageType.Center, "Station use denied: " + stationReason);
                CraftingDiagnostics.TraceAction("craft-cancelled", "station-use-denied:" + stationReason);
                return false;
            }
            NearbyCraftingFeatureState featureState = EvaluateNearbyCraftingState(
                player,
                recipe,
                station,
                evaluateRecipeRules: true);
            if (!featureState.IsReady)
            {
                CraftingDiagnostics.TraceAction("nearby-craft-bypass", featureState.ReasonCode, "vanilla handles the action");
                return true;
            }
            int quality = upgradeItem == null ? 1 : upgradeItem.m_quality + 1;
            if (quality > recipe.m_item.m_itemData.m_shared.m_maxQuality ||
                upgradeItem != null && !player.GetInventory().ContainsItem(upgradeItem))
            {
                CraftingDiagnostics.TraceAction("nearby-craft-bypass", "invalid-upgrade-state", "vanilla handles the action");
                return true;
            }
            int multiplier = multiCrafting ? Math.Max(1, multiCraftAmount) : 1;
            if (!TryBuildRequirements(recipe.m_resources, quality, multiplier, out List<MaterialRequirement> requirements))
            {
                CraftingDiagnostics.TraceAction("nearby-craft-bypass", "no-valid-material-requirements", "vanilla handles the action");
                return true;
            }
            if (InventorySatisfies(player.GetInventory(), requirements))
            {
                CraftingDiagnostics.TraceAction("nearby-craft-bypass", "carried-inventory-satisfies-cost", "vanilla consumes carried materials");
                return true;
            }

            if (station == null)
            {
                CraftingDiagnostics.TraceAction("nearby-craft-bypass", "crafting-station-required", "vanilla handles the action");
                return true;
            }
            float range = GetStationRange(station);
            Vector3 origin = station != null ? station.transform.position : player.transform.position;
            if (upgradeItem == null)
            {
                if (_pendingCraft != null || _activeOperation != null)
                {
                    player.Message(MessageHud.MessageType.Center, "Nearby crafting cancelled: another material operation is active");
                    CraftingDiagnostics.TraceAction("craft-cancelled", "another-material-operation-is-active");
                    return false;
                }
                if (!CheckAvailability(
                        player,
                        station,
                        origin,
                        range,
                        requirements,
                        "runic.crafting.recipe-preview",
                        stationlessAccessAuthorized: false,
                        out MaterialPlan preview,
                        out string previewReason))
                {
                    CraftingDiagnostics.TraceAction("nearby-craft-bypass", previewReason, "vanilla reports the material shortage");
                    return true;
                }
                if (!preview.Lines.Any(line => !line.SourceId.StartsWith("valheim.player:", StringComparison.Ordinal)))
                {
                    CraftingDiagnostics.TraceAction("nearby-craft-bypass", "no-nearby-source-needed", "vanilla consumes carried materials");
                    return true;
                }
                _pendingCraft = new PendingCraft(
                    player,
                    recipe,
                    station,
                    origin,
                    range,
                    requirements,
                    quality,
                    multiplier);
                CraftingDiagnostics.TraceAction("nearby-craft", "pending-vanilla-capacity-check");
                return true;
            }
            if (!TryBeginNearbyOperation(
                    player,
                    station,
                    origin,
                    range,
                    requirements,
                    "runic.crafting.recipe",
                    stationlessAccessAuthorized: false,
                    recipe.m_resources,
                    quality,
                    -1,
                    multiplier,
                    recipe,
                    null,
                    out string reason))
            {
                player.Message(MessageHud.MessageType.Center, "Nearby crafting cancelled: " + reason);
                CraftingDiagnostics.TraceAction("craft-cancelled", reason);
                return false;
            }
            CraftingDiagnostics.TraceAction("nearby-craft", "exact-material-operation-started");
            return true;
        }

        internal static bool AfterCraftCapacityCheck(
            Inventory inventory,
            GameObject prefab,
            bool vanillaAllowed)
        {
            PendingCraft pending = _pendingCraft;
            if (pending == null || inventory == null || prefab == null ||
                !ReferenceEquals(inventory, pending.Player.GetInventory()) ||
                prefab != pending.Recipe.m_item.gameObject) return vanillaAllowed;

            _pendingCraft = null;
            if (!vanillaAllowed)
            {
                CraftingDiagnostics.TraceAction("craft-cancelled", "vanilla-output-capacity-check-failed");
                return false;
            }
            if (TryBeginNearbyOperation(
                    pending.Player,
                    pending.Station,
                    pending.Origin,
                    pending.Range,
                    pending.Requirements,
                    "runic.crafting.recipe",
                    stationlessAccessAuthorized: false,
                    pending.Recipe.m_resources,
                    pending.Quality,
                    -1,
                    pending.Multiplier,
                    pending.Recipe,
                    null,
                    out string reason))
            {
                CraftingDiagnostics.TraceAction("nearby-craft", "exact-material-operation-started");
                return true;
            }

            pending.Player.Message(
                MessageHud.MessageType.Center,
                "Nearby crafting cancelled: " + reason);
            CraftingDiagnostics.TraceAction("craft-cancelled", reason);
            return false;
        }

        internal static void CraftOutputAdded(
            Inventory inventory,
            ItemDrop.ItemData result)
        {
            ActiveOperation active = _activeOperation;
            if (active == null || active.Recipe == null || result == null ||
                !ReferenceEquals(inventory, active.Player.GetInventory())) return;
            active.OutputCreated = true;
        }

        internal static void CancelPendingCraft()
        {
            _pendingCraft = null;
            CraftingDiagnostics.TraceAction("craft-cancelled", "pending-capacity-hook-failed");
        }

        internal static void FinishCraft(Exception exception)
        {
            _pendingCraft = null;
            if (exception != null)
                CraftingDiagnostics.TraceAction("craft-finished", "exception:" + exception.GetType().Name);
            CleanupActive(
                _activeOperation != null &&
                (_activeOperation.VanillaConsumeReached || _activeOperation.OutputCreated));
        }

        internal static void BeginPlacementScope() => _insidePlacementUpdate = true;

        internal static void FinishPlacementScope(Exception exception)
        {
            try
            {
                if (_activeOperation == null) return;
                PlacementLeaseDisposition disposition = PlacementLeasePolicy.Resolve(
                    _activeOperation.OutputCreated || _activeOperation.PlacedObject != null,
                    _activeOperation.VanillaConsumeReached);
                CraftingDiagnostics.TraceAction(
                    "nearby-build-finalize",
                    disposition == PlacementLeaseDisposition.Commit ? "commit" : "rollback",
                    exception == null ? "placement-update-completed" :
                    "placement-update-exception:" + exception.GetType().Name);
                CleanupActive(disposition == PlacementLeaseDisposition.Commit);
            }
            finally
            {
                _insidePlacementUpdate = false;
            }
        }

        internal static bool BeforePlacePiece(Player player, Piece piece, out string reason)
        {
            reason = "vanilla";
            if (!_insidePlacementUpdate)
            {
                CraftingDiagnostics.TraceAction("nearby-build-bypass", "outside-vanilla-placement-scope");
                return true;
            }
            if (player == null || piece == null)
            {
                CraftingDiagnostics.TraceAction("nearby-build-bypass", "invalid-player-or-piece");
                return true;
            }
            CraftingStation station = piece.m_craftingStation == null
                ? null
                : CraftingStation.HaveBuildStationInRange(
                    piece.m_craftingStation.m_name,
                    player.transform.position);
            if (Configuration.Enabled.Value && IsInitialized && ValheimReflection.CanMutateLocalPlayer(player) &&
                station != null && !CanUseStation(station, player, out reason))
            {
                reason = "station-use:" + reason;
                CraftingDiagnostics.TraceAction("build-cancelled", reason);
                return false;
            }
            if (!CanUseNearbyBuilding(player, piece, out string buildingGate))
            {
                CraftingDiagnostics.TraceAction("nearby-build-bypass", buildingGate, "vanilla handles the placement");
                return true;
            }
            if (!TryBuildRequirements(piece.m_resources, 0, 1, out List<MaterialRequirement> requirements))
            {
                CraftingDiagnostics.TraceAction("nearby-build-bypass", "no-valid-material-requirements", "vanilla handles the placement");
                return true;
            }
            if (InventorySatisfies(player.GetInventory(), requirements))
            {
                CraftingDiagnostics.TraceAction("nearby-build-bypass", "carried-inventory-satisfies-cost", "vanilla consumes carried materials");
                return true;
            }
            BuildMaterialQueryScope queryScope = BuildMaterialQueryScopePolicy.Resolve(
                piece.m_craftingStation != null,
                station != null);
            if (queryScope == BuildMaterialQueryScope.RequiredStationUnavailable)
            {
                reason = "required-station-unavailable";
                CraftingDiagnostics.TraceAction("build-cancelled", reason);
                return false;
            }
            bool stationlessAccessAuthorized =
                queryScope == BuildMaterialQueryScope.PlayerLocalStationless;
            float range = stationlessAccessAuthorized
                ? Configuration.SafeStationlessBuildRange
                : GetStationRange(station);
            Vector3 origin = stationlessAccessAuthorized
                ? player.transform.position
                : station.transform.position;
            if (!TryBeginNearbyOperation(
                    player,
                    station,
                    origin,
                    range,
                    requirements,
                    "runic.crafting.build",
                    stationlessAccessAuthorized,
                    piece.m_resources,
                    0,
                    -1,
                    1,
                    null,
                    piece,
                    out reason))
            {
                CraftingDiagnostics.TraceAction("build-cancelled", reason);
                return false;
            }
            CraftingDiagnostics.TraceAction("nearby-build", "exact-material-operation-started");
            return true;
        }

        internal static void PlacePieceReturned(bool succeeded)
        {
            if (_activeOperation == null) return;
            if (succeeded)
            {
                _activeOperation.OutputCreated = true;
                CraftingDiagnostics.TraceAction("nearby-build", "vanilla-piece-created");
            }
            else
            {
                CraftingDiagnostics.TraceAction("nearby-build", "vanilla-placement-returned-false; rolling-back");
                CleanupActive(success: false);
            }
        }

        internal static void PlacementObjectInstantiated(
            GameObject placedObject,
            Player player,
            Piece piece)
        {
            ActiveOperation active = _activeOperation;
            if (!_insidePlacementUpdate || active == null || placedObject == null ||
                !ReferenceEquals(active.Player, player) || !ReferenceEquals(active.Piece, piece)) return;
            // Store the actual Unity object instead of a sticky boolean. If a later placement
            // callback destroys it and then throws, Unity null semantics let finalization restore
            // the cost. If it remains alive, an exception after Instantiate cannot leave a free
            // world piece while the material lease rolls back.
            active.PlacedObject = placedObject;
        }

        internal static bool BeforeHaveRecipeRequirements(
            Player player,
            Recipe recipe,
            bool discover,
            int quality,
            int multiplier,
            ref bool result)
        {
            if (_activeOperation != null && _activeOperation.Player == player &&
                ReferenceEquals(_activeOperation.Recipe, recipe) && !discover &&
                _activeOperation.Quality == quality && _activeOperation.Multiplier == multiplier)
            {
                result = true;
                return false;
            }
            return true;
        }

        internal static void AddNearbyRecipeAvailability(
            Player player,
            Recipe recipe,
            bool discover,
            int quality,
            int multiplier,
            ref bool result)
        {
            if (result)
            {
                CraftingDiagnostics.TraceGate("recipe-preview", "vanilla-requirements-satisfied");
                return;
            }
            if (discover)
            {
                CraftingDiagnostics.TraceGate("recipe-preview", "discovery-check-remains-vanilla");
                return;
            }
            CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;
            NearbyCraftingFeatureState state = EvaluateNearbyCraftingState(
                player,
                recipe,
                station,
                evaluateRecipeRules: true);
            if (!state.IsReady)
            {
                CraftingDiagnostics.TraceGate("recipe-preview", state.ReasonCode);
                return;
            }
            if (!TryBuildRequirements(recipe.m_resources, quality, multiplier, out List<MaterialRequirement> requirements)) return;
            result = CheckAvailability(
                player,
                station,
                station != null ? station.transform.position : player.transform.position,
                GetStationRange(station),
                requirements,
                "runic.crafting.recipe-preview",
                stationlessAccessAuthorized: false,
                out _,
                out string reason);
            CraftingDiagnostics.TraceGate("recipe-preview", result ? "nearby-materials-satisfy-cost" : reason);
        }

        internal static void AddNearbyPieceAvailability(
            Player player,
            Piece piece,
            Player.RequirementMode mode,
            ref bool result)
        {
            if (result || mode != Player.RequirementMode.CanBuild) return;
            NearbyCraftingFeatureState state = EvaluateNearbyBuildingState(
                player,
                piece,
                out CraftingStation station);
            if (!state.IsReady)
            {
                CraftingDiagnostics.TraceGate("build-preview", state.ReasonCode);
                return;
            }
            if (!TryBuildRequirements(piece.m_resources, 0, 1, out List<MaterialRequirement> requirements)) return;
            BuildMaterialQueryScope queryScope = BuildMaterialQueryScopePolicy.Resolve(
                piece.m_craftingStation != null,
                station != null);
            if (queryScope == BuildMaterialQueryScope.RequiredStationUnavailable) return;
            bool stationlessAccessAuthorized =
                queryScope == BuildMaterialQueryScope.PlayerLocalStationless;
            Vector3 origin = stationlessAccessAuthorized
                ? player.transform.position
                : station.transform.position;
            float range = stationlessAccessAuthorized
                ? Configuration.SafeStationlessBuildRange
                : GetStationRange(station);
            result = CheckAvailability(
                player,
                station,
                origin,
                range,
                requirements,
                "runic.crafting.build-preview",
                stationlessAccessAuthorized,
                out _,
                out string reason);
            CraftingDiagnostics.TraceGate("build-preview", result ? "nearby-materials-satisfy-cost" : reason);
        }

        internal static bool BeforeVanillaConsume(
            Player player,
            Piece.Requirement[] requirements,
            int quality,
            int itemQuality,
            int multiplier)
        {
            ActiveOperation active = _activeOperation;
            if (active == null || active.Player != player || !ReferenceEquals(active.Requirements, requirements) ||
                active.Quality != quality || active.ItemQuality != itemQuality || active.Multiplier != multiplier)
                return true;
            active.VanillaConsumeReached = true;
            return false;
        }

        internal static bool TryGetAvailabilityBreakdown(
            Player player,
            Recipe recipe,
            Piece.Requirement requirement,
            int quality,
            int multiplier,
            out int carried,
            out int nearby,
            out int required,
            out NearbyCraftingFeatureState state)
        {
            carried = nearby = required = 0;
            state = new NearbyCraftingFeatureState(false, "requirement-unavailable", "N:OFF(requirement)");
            if (requirement == null || requirement.m_resItem == null || player == null) return false;
            required = checked(requirement.GetAmount(quality) * multiplier);
            if (required <= 0) return false;
            string resource = ValheimReflection.ResourceId(requirement.m_resItem);
            if (resource.Length == 0) return false;
            carried = ValheimReflection.CountRequirementItems(player.GetInventory(), resource);
            CraftingStation station = player.GetCurrentCraftingStation();
            state = EvaluateNearbyCraftingState(
                player,
                recipe,
                station,
                evaluateRecipeRules: true);
            if (!state.IsReady)
            {
                CraftingDiagnostics.TraceGate("requirement-row", state.ReasonCode);
                return true;
            }
            return TryResolveNearbyBreakdown(
                player,
                station,
                resource,
                required,
                carried,
                "requirement-row",
                "runic.crafting.ui",
                station.transform.position,
                GetStationRange(station),
                stationlessAccessAuthorized: false,
                ref nearby,
                ref state);
        }

        internal static bool TryGetBuildAvailabilityBreakdown(
            Player player,
            Piece piece,
            Piece.Requirement requirement,
            out int carried,
            out int nearby,
            out int required,
            out NearbyCraftingFeatureState state)
        {
            carried = nearby = required = 0;
            state = new NearbyCraftingFeatureState(false, "requirement-unavailable", "N:OFF(requirement)");
            if (requirement == null || requirement.m_resItem == null || player == null || piece == null)
                return false;
            required = requirement.GetAmount(0);
            if (required <= 0) return false;
            string resource = ValheimReflection.ResourceId(requirement.m_resItem);
            if (resource.Length == 0) return false;
            carried = ValheimReflection.CountRequirementItems(player.GetInventory(), resource);
            state = EvaluateNearbyBuildingState(player, piece, out CraftingStation station);
            if (!state.IsReady)
            {
                CraftingDiagnostics.TraceGate("build-requirement-row", state.ReasonCode);
                return true;
            }
            BuildMaterialQueryScope queryScope = BuildMaterialQueryScopePolicy.Resolve(
                piece.m_craftingStation != null,
                station != null);
            if (queryScope == BuildMaterialQueryScope.RequiredStationUnavailable)
            {
                state = BuildingFailureState("required-station-unavailable");
                return true;
            }
            bool stationlessAccessAuthorized =
                queryScope == BuildMaterialQueryScope.PlayerLocalStationless;
            Vector3 origin = stationlessAccessAuthorized
                ? player.transform.position
                : station.transform.position;
            float range = stationlessAccessAuthorized
                ? Configuration.SafeStationlessBuildRange
                : GetStationRange(station);
            return TryResolveNearbyBreakdown(
                player,
                station,
                resource,
                required,
                carried,
                "build-requirement-row",
                "runic.crafting.build-ui",
                origin,
                range,
                stationlessAccessAuthorized,
                ref nearby,
                ref state);
        }

        private static bool TryResolveNearbyBreakdown(
            Player player,
            CraftingStation station,
            string resource,
            int required,
            int carried,
            string diagnosticOperation,
            string queryPurpose,
            Vector3 origin,
            float range,
            bool stationlessAccessAuthorized,
            ref int nearby,
            ref NearbyCraftingFeatureState state)
        {
            var cacheKey = new BreakdownKey(
                player.GetPlayerID(),
                stationlessAccessAuthorized
                    ? StationlessAnchorId(origin)
                    : ValheimReflection.StationEndpointId(station),
                resource,
                required);
            float now = Time.realtimeSinceStartup;
            lock (AvailabilityCacheGate)
            {
                if (AvailabilityCache.TryGetValue(cacheKey, out BreakdownEntry cached) && cached.ExpiresAt >= now)
                {
                    nearby = cached.Nearby;
                    CraftingDiagnostics.TraceGate(
                        diagnosticOperation,
                        "ready",
                        "resource=" + resource + "; required=" + required + "; carried=" + carried +
                        "; nearby=" + nearby + "; cached=true");
                    return true;
                }
            }
            var one = new[] { new MaterialRequirement(resource, required) };
            IReadOnlyList<IMutableMaterialSource> sources = _containerQuery.ResolveSources(
                player,
                station,
                origin,
                range,
                one,
                queryPurpose,
                stationlessAccessAuthorized,
                out string queryReason);
            if (IsBlockingQueryReason(queryReason))
            {
                state = QueryFailureState(queryReason);
                CraftingDiagnostics.TraceGate(diagnosticOperation, state.ReasonCode);
                return true;
            }
            foreach (IMutableMaterialSource source in sources)
            {
                MaterialSourceSnapshot snapshot = source.Snapshot();
                if (snapshot.Kind == MaterialSourceKind.NearbyContainer)
                {
                    int available = snapshot.Available(resource);
                    nearby = nearby > int.MaxValue - available ? int.MaxValue : nearby + available;
                }
            }
            lock (AvailabilityCacheGate)
            {
                if (AvailabilityCache.Count >= 512) AvailabilityCache.Clear();
                AvailabilityCache[cacheKey] = new BreakdownEntry(nearby, now + 0.25f);
            }
            CraftingDiagnostics.TraceGate(
                diagnosticOperation,
                "ready",
                "resource=" + resource + "; required=" + required + "; carried=" + carried +
                "; nearby=" + nearby +
                "; source-query=" + queryReason);
            return true;
        }

        private static bool TryBeginNearbyOperation(
            Player player,
            CraftingStation station,
            Vector3 origin,
            float range,
            IReadOnlyList<MaterialRequirement> requirements,
            string purpose,
            bool stationlessAccessAuthorized,
            Piece.Requirement[] vanillaRequirements,
            int quality,
            int itemQuality,
            int multiplier,
            Recipe recipe,
            Piece piece,
            out string reason)
        {
            reason = "insufficient-materials";
            if (_activeOperation != null)
            {
                reason = "another-material-operation-is-active";
                CraftingDiagnostics.TraceAction(purpose, reason);
                return false;
            }
            IReadOnlyList<IMutableMaterialSource> sources = _containerQuery.ResolveSources(
                player,
                station,
                origin,
                range,
                requirements,
                purpose,
                stationlessAccessAuthorized,
                out string queryReason);
            CraftingDiagnostics.TraceAction(
                purpose + ":source-query",
                queryReason,
                "sources=" + sources.Count + "; range=" + range.ToString("0.#", CultureInfo.InvariantCulture) + "m");
            var snapshots = sources.Select(source => source.Snapshot()).ToArray();
            if (Configuration.DetailedLogging.Value)
                CraftingDiagnostics.TraceAction(
                    purpose + ":availability",
                    "evaluated",
                    FormatMaterialAvailability(requirements, snapshots));
            if (!Planner.TryPlan(requirements, snapshots, out MaterialPlan preview, out string planReason))
            {
                if (queryReason.StartsWith("station-use:", StringComparison.Ordinal) ||
                    queryReason.StartsWith("local-materials:", StringComparison.Ordinal))
                    reason = queryReason;
                else reason = planReason;
                CraftingDiagnostics.TraceAction(purpose, reason);
                return false;
            }
            if (!preview.Lines.Any(line => !line.SourceId.StartsWith("valheim.player:", StringComparison.Ordinal)))
            {
                reason = "no-nearby-source-needed";
                CraftingDiagnostics.TraceAction(purpose, reason);
                return false;
            }

            if (!Engine.TryBegin(requirements, sources, out MaterialConsumptionLease lease, out string beginReason))
            {
                reason = beginReason;
                CraftingDiagnostics.TraceAction(purpose, reason);
                return false;
            }

            _activeOperation = new ActiveOperation(
                player,
                vanillaRequirements,
                quality,
                itemQuality,
                multiplier,
                recipe,
                piece,
                lease);
            if (Configuration.DetailedLogging.Value)
                _log?.LogInfo($"Reserved exact {purpose} plan with {lease.Plan.Lines.Count} source lines ({queryReason}).");
            return true;
        }

        private static bool CheckAvailability(
            Player player,
            CraftingStation station,
            Vector3 origin,
            float range,
            IReadOnlyList<MaterialRequirement> requirements,
            string purpose,
            bool stationlessAccessAuthorized,
            out MaterialPlan plan,
            out string reason)
        {
            plan = null;
            reason = "insufficient-materials";
            if (station == null && !stationlessAccessAuthorized)
            {
                reason = "crafting-station-required";
                return false;
            }
            if (station != null && !CanUseStation(station, player, out string stationReason))
            {
                reason = "station-use:" + stationReason;
                return false;
            }
            IReadOnlyList<IMutableMaterialSource> sources = _containerQuery.ResolveSources(
                player,
                station,
                origin,
                range,
                requirements,
                purpose,
                stationlessAccessAuthorized,
                out string queryReason);
            if (IsBlockingQueryReason(queryReason))
            {
                reason = queryReason;
                return false;
            }
            bool available = Planner.TryPlan(
                requirements,
                sources.Select(source => source.Snapshot()),
                out plan,
                out string plannerReason);
            reason = available ? "ok:" + queryReason : plannerReason;
            return available;
        }

        private static NearbyCraftingFeatureState EvaluateNearbyCraftingState(
            Player player,
            Recipe recipe,
            CraftingStation station,
            bool evaluateRecipeRules)
        {
            bool noCostMode = player != null && player.NoCostCheat() ||
                ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
            NearbyCraftingFeatureState preliminary = NearbyCraftingFeatureStateEvaluator.Evaluate(
                Configuration.Enabled.Value,
                Configuration.CraftFromContainers.Value,
                IsInitialized,
                ValheimReflection.CanMutateLocalPlayer(player),
                !evaluateRecipeRules || recipe != null,
                evaluateRecipeRules && recipe != null && recipe.m_requireOnlyOneIngredient,
                noCostMode,
                station != null,
                stationUseAllowed: true,
                stationDenialReason: null,
                localMaterialsAllowed: true,
                localMaterialsDenialReason: null);
            if (!preliminary.IsReady) return preliminary;

            WorkshopAccessDecision stationAccess = _workshopAccess.Evaluate(
                station,
                player,
                WorkshopAction.StationUse);
            WorkshopAccessDecision localMaterialsAccess = stationAccess.Allowed
                ? _workshopAccess.Evaluate(station, player, WorkshopAction.LocalMaterialUse)
                : new WorkshopAccessDecision(false, WorkshopAction.LocalMaterialUse, "station-use-denied");
            return NearbyCraftingFeatureStateEvaluator.Evaluate(
                masterEnabled: true,
                craftFromContainersEnabled: true,
                runtimeAvailable: true,
                localPlayerOwner: true,
                recipeAvailable: true,
                specialOneIngredientRecipe: false,
                noCostMode: false,
                stationPresent: true,
                stationUseAllowed: stationAccess.Allowed,
                stationDenialReason: stationAccess.ReasonCode,
                localMaterialsAllowed: localMaterialsAccess.Allowed,
                localMaterialsDenialReason: localMaterialsAccess.ReasonCode);
        }

        private static NearbyCraftingFeatureState EvaluateNearbyBuildingState(
            Player player,
            Piece piece,
            out CraftingStation station)
        {
            station = null;
            if (!CanUseNearbyBuilding(player, piece, out string gate))
                return BuildingFailureState(gate);
            if (!ValheimReflection.DlcAllows(piece.m_dlc))
                return BuildingFailureState("dlc-entitlement-required");

            if (piece.m_craftingStation == null)
                return new NearbyCraftingFeatureState(true, "ready-stationless", "N:ON");

            station = CraftingStation.HaveBuildStationInRange(
                piece.m_craftingStation.m_name,
                player.transform.position);
            if (station == null)
                return BuildingFailureState("required-station-unavailable");

            WorkshopAccessDecision stationAccess = _workshopAccess.Evaluate(
                station,
                player,
                WorkshopAction.StationUse);
            if (!stationAccess.Allowed)
                return new NearbyCraftingFeatureState(
                    false,
                    "station-use-denied:" + stationAccess.ReasonCode,
                    "N:OFF(access)");
            WorkshopAccessDecision materialsAccess = _workshopAccess.Evaluate(
                station,
                player,
                WorkshopAction.LocalMaterialUse);
            if (!materialsAccess.Allowed)
                return new NearbyCraftingFeatureState(
                    false,
                    "local-material-use-denied:" + materialsAccess.ReasonCode,
                    "N:OFF(access)");
            return new NearbyCraftingFeatureState(true, "ready", "N:ON");
        }

        private static NearbyCraftingFeatureState BuildingFailureState(string reason)
        {
            string label;
            switch (reason)
            {
                case "mod-disabled": label = "N:OFF(mod)"; break;
                case "build-from-containers-disabled": label = "N:OFF(config)"; break;
                case "runtime-unavailable": label = "N:OFF(startup)"; break;
                case "local-player-owner-required": label = "N:OFF(owner)"; break;
                case "no-cost-mode": label = "N:OFF(no-cost)"; break;
                case "required-station-unavailable": label = "N:OFF(station)"; break;
                case "dlc-entitlement-required": label = "N:OFF(dlc)"; break;
                case "stationless-building-disabled": label = "N:OFF(config)"; break;
                default: label = "N:OFF(build)"; break;
            }
            return new NearbyCraftingFeatureState(false, reason, label);
        }

        private static bool CanUseNearbyBuilding(Player player, Piece piece, out string reason)
        {
            reason = "ready";
            if (!Configuration.Enabled.Value) reason = "mod-disabled";
            else if (!Configuration.BuildFromContainers.Value) reason = "build-from-containers-disabled";
            else if (!IsInitialized) reason = "runtime-unavailable";
            else if (!ValheimReflection.CanMutateLocalPlayer(player))
                reason = "local-player-owner-required";
            else if (piece == null) reason = "piece-unavailable";
            else if (!ValheimReflection.DlcAllows(piece.m_dlc)) reason = "dlc-entitlement-required";
            else if (player.NoCostCheat() ||
                     ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()))
                reason = "no-cost-mode";
            else if (piece.m_craftingStation == null)
            {
                if (!Configuration.StationlessBuildFromContainers.Value)
                    reason = "stationless-building-disabled";
                else
                {
                    StationlessPieceDecision decision = Configuration.StationlessPolicy.Evaluate(
                        ValheimReflection.PiecePrefabId(piece));
                    if (!decision.Allowed) reason = decision.ReasonCode;
                }
            }
            return reason == "ready";
        }

        private static bool IsBlockingQueryReason(string reason) =>
            string.Equals(reason, "local-player-owner-required", StringComparison.Ordinal) ||
            string.Equals(reason, "station-required-for-nearby-materials", StringComparison.Ordinal) ||
            reason != null &&
            (reason.StartsWith("station-use:", StringComparison.Ordinal) ||
             reason.StartsWith("local-materials:", StringComparison.Ordinal));

        private static NearbyCraftingFeatureState QueryFailureState(string reason)
        {
            string label = "N:OFF(access)";
            return new NearbyCraftingFeatureState(false, reason, label);
        }

        private static string ExplainNearbyState(NearbyCraftingFeatureState state)
        {
            string reason = state.ReasonCode;
            if (reason == "mod-disabled") return "Runic Crafting: gameplay disabled in configuration";
            if (reason == "craft-from-containers-disabled")
                return "Runic Crafting: nearby materials OFF (CraftFromContainers=false)";
            if (reason == "runtime-unavailable") return "Runic Crafting: nearby materials unavailable; check the BepInEx log";
            if (reason == "local-player-owner-required")
                return "Runic Crafting: nearby materials require the local player owner";
            if (reason == "special-one-ingredient-recipe")
                return "Runic Crafting: this special recipe uses vanilla carried materials";
            if (reason == "no-cost-mode")
                return "Runic Crafting: no-cost mode is active; nearby containers are intentionally unused";
            if (reason == "crafting-station-required")
                return "Runic Crafting: an active crafting station is required for nearby materials";
            if (reason.StartsWith("station-use-denied:", StringComparison.Ordinal))
                return "Runic Crafting: nearby materials OFF because station use is denied (" +
                       reason.Substring("station-use-denied:".Length) + ")";
            if (reason.StartsWith("local-material-use-denied:", StringComparison.Ordinal))
                return "Runic Crafting: nearby materials OFF by this station's Local Material Use policy (" +
                       reason.Substring("local-material-use-denied:".Length) + ")";
            return "Runic Crafting: nearby materials unavailable (" + reason + ")";
        }

        private static bool TryBuildRequirements(
            Piece.Requirement[] source,
            int quality,
            int multiplier,
            out List<MaterialRequirement> requirements)
        {
            requirements = new List<MaterialRequirement>();
            if (source == null || multiplier <= 0) return false;
            try
            {
                foreach (Piece.Requirement requirement in source)
                {
                    if (requirement?.m_resItem == null) continue;
                    int amount = checked(requirement.GetAmount(quality) * multiplier);
                    if (amount <= 0) continue;
                    string resourceId = ValheimReflection.ResourceId(requirement.m_resItem);
                    if (resourceId.Length == 0)
                    {
                        requirements.Clear();
                        return false;
                    }
                    requirements.Add(new MaterialRequirement(resourceId, amount));
                }
                return requirements.Count > 0;
            }
            catch (OverflowException)
            {
                requirements.Clear();
                return false;
            }
        }

        private static string FormatMaterialAvailability(
            IEnumerable<MaterialRequirement> requirements,
            IEnumerable<MaterialSourceSnapshot> sources)
        {
            var required = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var carried = new Dictionary<string, int>(StringComparer.Ordinal);
            var nearby = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MaterialRequirement requirement in requirements)
            {
                required.TryGetValue(requirement.ResourceId, out int quantity);
                required[requirement.ResourceId] = SaturatingAdd(quantity, requirement.Quantity);
            }
            foreach (MaterialSourceSnapshot source in sources)
            {
                Dictionary<string, int> target = source.Kind == MaterialSourceKind.PlayerInventory
                    ? carried
                    : nearby;
                foreach (string resource in required.Keys)
                {
                    int quantity = source.Available(resource);
                    target.TryGetValue(resource, out int current);
                    target[resource] = SaturatingAdd(current, quantity);
                }
            }

            var lines = new List<string>(required.Count);
            foreach (KeyValuePair<string, int> requirement in required)
            {
                carried.TryGetValue(requirement.Key, out int carriedQuantity);
                nearby.TryGetValue(requirement.Key, out int nearbyQuantity);
                lines.Add(
                    requirement.Key + " required=" + requirement.Value +
                    " carried=" + carriedQuantity +
                    " nearby=" + nearbyQuantity +
                    " total=" + SaturatingAdd(carriedQuantity, nearbyQuantity));
            }
            return string.Join("; ", lines);
        }

        private static int SaturatingAdd(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;

        private static bool InventorySatisfies(Inventory inventory, IEnumerable<MaterialRequirement> requirements)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                foreach (MaterialRequirement requirement in requirements)
                {
                    totals.TryGetValue(requirement.ResourceId, out int current);
                    totals[requirement.ResourceId] = checked(current + requirement.Quantity);
                }
            }
            catch (OverflowException)
            {
                return false;
            }
            foreach (KeyValuePair<string, int> requirement in totals)
                if (ValheimReflection.CountRequirementItems(inventory, requirement.Key) < requirement.Value) return false;
            return true;
        }

        private static float GetStationRange(CraftingStation station)
        {
            if (station == null) return Configuration.SafeRangeCap;
            float stationRange = station.GetStationBuildRange();
            if (stationRange <= 0f) stationRange = station.m_rangeBuild;
            return Math.Max(1f, Math.Min(Configuration.SafeRangeCap, stationRange));
        }

        private static string StationlessAnchorId(Vector3 origin)
        {
            // Half-meter cells keep the short UI cache player-local while avoiding stale
            // counts after the player crosses a meaningful container-range boundary.
            int x = (int)Math.Floor(origin.x * 2f);
            int y = (int)Math.Floor(origin.y * 2f);
            int z = (int)Math.Floor(origin.z * 2f);
            return "player-local:" + x + ":" + y + ":" + z;
        }

        private static void CleanupActive(bool success)
        {
            ActiveOperation active = _activeOperation;
            _activeOperation = null;
            if (active == null) return;
            try
            {
                if (success)
                {
                    active.Lease.Commit();
                    CraftingDiagnostics.TraceAction("material-lease", "committed");
                }
                else if (!active.Lease.Rollback())
                    _log?.LogError("Runic Crafting rollback could not restore every removed stack. Stop and inspect inventories before continuing.");
                else CraftingDiagnostics.TraceAction("material-lease", "rolled-back");
            }
            catch (Exception exception)
            {
                _log?.LogError("Runic Crafting could not close its material lease safely: " + exception);
            }
        }

        private sealed class ActiveOperation
        {
            internal ActiveOperation(
                Player player,
                Piece.Requirement[] requirements,
                int quality,
                int itemQuality,
                int multiplier,
                Recipe recipe,
                Piece piece,
                MaterialConsumptionLease lease)
            {
                Player = player;
                Requirements = requirements;
                Quality = quality;
                ItemQuality = itemQuality;
                Multiplier = multiplier;
                Recipe = recipe;
                Piece = piece;
                Lease = lease;
            }

            internal Player Player { get; }
            internal Piece.Requirement[] Requirements { get; }
            internal int Quality { get; }
            internal int ItemQuality { get; }
            internal int Multiplier { get; }
            internal Recipe Recipe { get; }
            internal Piece Piece { get; }
            internal MaterialConsumptionLease Lease { get; }
            internal bool VanillaConsumeReached { get; set; }
            internal bool OutputCreated { get; set; }
            internal GameObject PlacedObject { get; set; }
        }

        private sealed class PendingCraft
        {
            internal PendingCraft(
                Player player,
                Recipe recipe,
                CraftingStation station,
                Vector3 origin,
                float range,
                IReadOnlyList<MaterialRequirement> requirements,
                int quality,
                int multiplier)
            {
                Player = player;
                Recipe = recipe;
                Station = station;
                Origin = origin;
                Range = range;
                Requirements = requirements;
                Quality = quality;
                Multiplier = multiplier;
            }

            internal Player Player { get; }
            internal Recipe Recipe { get; }
            internal CraftingStation Station { get; }
            internal Vector3 Origin { get; }
            internal float Range { get; }
            internal IReadOnlyList<MaterialRequirement> Requirements { get; }
            internal int Quality { get; }
            internal int Multiplier { get; }
        }

        private readonly struct BreakdownKey : IEquatable<BreakdownKey>
        {
            internal BreakdownKey(long playerId, string stationId, string resourceId, int required)
            {
                PlayerId = playerId;
                StationId = stationId;
                ResourceId = resourceId;
                Required = required;
            }

            private long PlayerId { get; }
            private string StationId { get; }
            private string ResourceId { get; }
            private int Required { get; }
            public bool Equals(BreakdownKey other) =>
                PlayerId == other.PlayerId && Required == other.Required &&
                string.Equals(StationId, other.StationId, StringComparison.Ordinal) &&
                string.Equals(ResourceId, other.ResourceId, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is BreakdownKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PlayerId.GetHashCode();
                    hash = (hash * 397) ^ (StationId == null ? 0 : StringComparer.Ordinal.GetHashCode(StationId));
                    hash = (hash * 397) ^ (ResourceId == null ? 0 : StringComparer.Ordinal.GetHashCode(ResourceId));
                    return (hash * 397) ^ Required;
                }
            }
        }

        private readonly struct BreakdownEntry
        {
            internal BreakdownEntry(int nearby, float expiresAt)
            {
                Nearby = nearby;
                ExpiresAt = expiresAt;
            }

            internal int Nearby { get; }
            internal float ExpiresAt { get; }
        }

    }
}
