using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicProduction.Contracts;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal enum ProductionStationKind
    {
        Smelter = 1,
        Cooking = 2,
        Recipe = 3,
        Fermenter = 4,
        Fireplace = 5
    }

    internal sealed class ProductionStationEntry
    {
        internal ProductionStationEntry(ProductionStationKind kind, Component component)
        {
            Kind = kind;
            Component = component ?? throw new ArgumentNullException(nameof(component));
        }

        internal ProductionStationKind Kind { get; }
        internal Component Component { get; }
    }

    internal sealed class ProductionLinkSelection
    {
        internal long PlayerId;
        internal ProductionStationEntry Station;
        internal ProductionLinkRole Role;
        internal ProductionLinkMouseButton Button;
        internal bool Remove;
        internal float ExpiresAt;
    }

    internal sealed class ProductionEndpoint
    {
        internal StoredProductionLink Link;
        internal Container Container;
        internal ZDO Zdo;
        internal Inventory Inventory;
    }

    internal sealed class ProductionDestination
    {
        internal ProductionEndpoint Endpoint;
        internal MultiReplenishmentCatalog Catalog;
        internal ReplenishmentDestinationRecord Record;
        internal ReplenishmentTargetAuthorization Target;
        internal int TargetIndex;
        internal bool IsReplenishment => Catalog != null;
    }

    internal enum ProductionEndpointMutationState
    {
        Before = 1,
        After = 2,
        Indeterminate = 3
    }

    internal sealed class ProductionMutationIndeterminateException : InvalidOperationException
    {
        internal ProductionMutationIndeterminateException(string message) : base(message) { }
    }

    /// <summary>
    /// Explicit authorized setup may claim the selected pair using native ownership. Background
    /// automation requests owner-approved chest handoff to the current station owner. Mutations
    /// remain synchronous and require native ownership of every participating object.
    /// </summary>
    internal static class ProductionRuntime
    {
        private const int MaximumLoadedStations = 4096;
        private const int MaximumSelections = 16;
        private const float SelectionLifetimeSeconds = 30f;
        private const float ContextHighlightSeconds = 0.35f;
        private const float SuccessfulLinkHighlightSeconds = 12f;

        private static readonly Dictionary<int, ProductionStationEntry> Stations =
            new Dictionary<int, ProductionStationEntry>();
        private static readonly Dictionary<long, ProductionLinkSelection> Selections =
            new Dictionary<long, ProductionLinkSelection>();
        private static readonly HashSet<int> SafetyPausedStations = new HashSet<int>();

        private static CookingStationPrefabPolicy _cookingPolicy;
        private static StockStationPrefabPolicy _recipePolicy;
        private static StockStationPrefabPolicy _cookingInputPolicy;
        private static StockStationPrefabPolicy _fermenterInputPolicy;
        private static FermenterPrefabPolicy _fermenterPolicy;
        private static IngredientReservePolicy _ingredientReserves;
        private static IngredientReservePolicy _fuelReserves;
        private static ReplenishmentTargetReservePolicy _replenishmentReserves;
        private static bool _initialized;
        private static bool _highlightFailureReported;
        private static int _highlightRefreshFrame;
        private static int _highlightRefreshStationId;
        private static float _nextQuantum;
        private static int _stationCursor;

        internal static void Initialize()
        {
            ProductionChestHandoff.Initialize();
            Stations.Clear();
            Selections.Clear();
            SafetyPausedStations.Clear();
            ProductionLinkInput.Reset();
            ProductionLinkHighlight.ClearAll();
            _highlightFailureReported = false;
            _highlightRefreshFrame = -1;
            _highlightRefreshStationId = 0;
            _stationCursor = 0;
            _nextQuantum = 0f;
            RefreshConfiguration();
            _initialized = true;
        }

        internal static void Shutdown()
        {
            ProductionChestHandoff.Shutdown();
            _initialized = false;
            Stations.Clear();
            Selections.Clear();
            SafetyPausedStations.Clear();
            ProductionLinkInput.Reset();
            ProductionLinkHighlight.ClearAll();
            _highlightFailureReported = false;
            _highlightRefreshFrame = -1;
            _highlightRefreshStationId = 0;
            _stationCursor = 0;
            _nextQuantum = 0f;
        }

        internal static void RefreshConfiguration()
        {
            if (!CookingStationPrefabPolicy.TryParse(
                    ProductionConfig.CookingStationPrefabAllowList?.Value ?? string.Empty,
                    ProductionConfig.CookingStationPrefabDenyList?.Value ?? string.Empty,
                    out CookingStationPrefabPolicy cooking,
                    out string cookingFailure))
                throw new InvalidOperationException(cookingFailure);
            if (!StockStationPrefabPolicy.TryParse(
                    ProductionConfig.RecipeStationPrefabAllowList?.Value ?? string.Empty,
                    ProductionConfig.RecipeStationPrefabDenyList?.Value ?? string.Empty,
                    out StockStationPrefabPolicy recipe,
                    out string recipeFailure))
                throw new InvalidOperationException(recipeFailure);
            if (!StockStationPrefabPolicy.TryParse(
                    ProductionConfig.CookingStationPrefabAllowList?.Value ?? string.Empty,
                    ProductionConfig.CookingStationPrefabDenyList?.Value ?? string.Empty,
                    out StockStationPrefabPolicy cookingInputs,
                    out string cookingInputFailure))
                throw new InvalidOperationException(cookingInputFailure);
            if (!StockStationPrefabPolicy.TryParse(
                    ProductionConfig.FermenterPrefabAllowList?.Value ?? string.Empty,
                    ProductionConfig.FermenterPrefabDenyList?.Value ?? string.Empty,
                    out StockStationPrefabPolicy fermenterInputs,
                    out string fermenterInputFailure))
                throw new InvalidOperationException(fermenterInputFailure);
            if (!FermenterPrefabPolicy.TryParse(
                    ProductionConfig.FermenterPrefabAllowList?.Value ?? string.Empty,
                    ProductionConfig.FermenterPrefabDenyList?.Value ?? string.Empty,
                    out FermenterPrefabPolicy fermenter,
                    out string fermenterFailure))
                throw new InvalidOperationException(fermenterFailure);
            if (!IngredientReservePolicy.TryParse(
                    ProductionConfig.DefaultIngredientReserve?.Value ?? 0,
                    ProductionConfig.IngredientReserveRules?.Value ?? string.Empty,
                    out IngredientReservePolicy ingredients,
                    out string ingredientFailure))
                throw new InvalidOperationException(ingredientFailure);
            if (!IngredientReservePolicy.TryParse(
                    ProductionConfig.FuelReserve?.Value ?? 0,
                    string.Empty,
                    out IngredientReservePolicy fuel,
                    out string fuelFailure))
                throw new InvalidOperationException(fuelFailure);
            if (!ReplenishmentTargetReservePolicy.TryParse(
                    ProductionConfig.DefaultReplenishmentReserve?.Value ?? 10,
                    ProductionConfig.ReplenishmentReserveRules?.Value ?? string.Empty,
                    out ReplenishmentTargetReservePolicy replenishment,
                    out string replenishmentFailure))
                throw new InvalidOperationException(replenishmentFailure);

            _cookingPolicy = cooking;
            _recipePolicy = recipe;
            _cookingInputPolicy = cookingInputs;
            _fermenterInputPolicy = fermenterInputs;
            _fermenterPolicy = fermenter;
            _ingredientReserves = ingredients;
            _fuelReserves = fuel;
            _replenishmentReserves = replenishment;
            _nextQuantum = 0f;
        }

        internal static void Register(Smelter station) =>
            Register(ProductionStationKind.Smelter, station);

        internal static void Register(CookingStation station) =>
            Register(ProductionStationKind.Cooking, station);

        internal static void Register(CraftingStation station) =>
            Register(ProductionStationKind.Recipe, station);

        internal static void Register(Fermenter station) =>
            Register(ProductionStationKind.Fermenter, station);

        internal static void Register(Fireplace station) =>
            Register(ProductionStationKind.Fireplace, station);

        internal static void SeedLoadedStations()
        {
            foreach (Container chest in UnityEngine.Object.FindObjectsByType<Container>(
                         FindObjectsSortMode.None)) ProductionChestHandoff.Register(chest);
            foreach (Smelter station in UnityEngine.Object.FindObjectsByType<Smelter>(
                         FindObjectsSortMode.None)) Register(station);
            foreach (CookingStation station in
                     UnityEngine.Object.FindObjectsByType<CookingStation>(
                         FindObjectsSortMode.None)) Register(station);
            foreach (CraftingStation station in
                     UnityEngine.Object.FindObjectsByType<CraftingStation>(
                         FindObjectsSortMode.None)) Register(station);
            foreach (Fermenter station in
                      UnityEngine.Object.FindObjectsByType<Fermenter>(
                          FindObjectsSortMode.None)) Register(station);
            foreach (Fireplace station in
                     UnityEngine.Object.FindObjectsByType<Fireplace>(
                         FindObjectsSortMode.None)) Register(station);
        }

        private static void Register(ProductionStationKind kind, Component component)
        {
            if (!_initialized || component == null) return;
            int id = component.GetInstanceID();
            if (!Stations.ContainsKey(id) && Stations.Count >= MaximumLoadedStations)
            {
                ProductionDiagnostics.Warning(
                    "Production station registration reached its 4096-object bound.");
                return;
            }
            Stations[id] = new ProductionStationEntry(kind, component);
        }

        internal static void Update()
        {
            if (!_initialized) return;
            PruneSelections();
            if (!(ProductionConfig.Enabled?.Value ?? false)) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextQuantum) return;
            float interval = Mathf.Clamp(
                ProductionConfig.StockSchedulerIntervalSeconds?.Value ?? 2f,
                0.5f,
                10f);
            _nextQuantum = now + interval;

            int budget = Mathf.Clamp(
                ProductionConfig.StockSchedulerMaximumOperations?.Value ?? 64,
                1,
                256);
            int[] ids = Stations.Keys.ToArray();
            if (ids.Length == 0) return;
            Array.Sort(ids);
            int start = Math.Abs(_stationCursor) % ids.Length;
            int mutations = 0;
            for (int offset = 0; offset < ids.Length && mutations < budget; offset++)
            {
                int id = ids[(start + offset) % ids.Length];
                if (!Stations.TryGetValue(id, out ProductionStationEntry entry)) continue;
                if (entry.Component == null)
                {
                    Stations.Remove(id);
                    continue;
                }
                try
                {
                    if (RunOne(entry)) mutations++;
                }
                catch (Exception exception)
                {
                    FailStation(entry.Component, "scheduler", exception);
                }
            }
            _stationCursor = (start + 1) % Math.Max(1, ids.Length);
        }

        internal static void TickLocalPlayer(Player player)
        {
            ProductionLinkInput.BeginSample(Time.frameCount);
            if (!_initialized || !(ProductionConfig.Enabled?.Value ?? false) ||
                player == null || player != Player.m_localPlayer || !player.IsOwner()) return;

            GameObject target = player.GetHoverObject();
            bool takesInput = ValheimAccess.PlayerTakesInput(player);
            ProductionLinkMouseButton button = ProductionLinkMouseButton.Left;
            bool remove = false;
            bool hasGesture = takesInput &&
                              ProductionLinkInput.TryReadExactGesture(
                                  Time.frameCount,
                                  out button,
                                  out remove);
            Container pointedContainer = target == null
                ? null
                : target.GetComponentInParent<Container>();
            long playerId = player.GetPlayerID();
            if (hasGesture && pointedContainer != null && playerId != 0L &&
                Selections.TryGetValue(playerId, out ProductionLinkSelection pending))
            {
                // A pending role owns only its exact matching second gesture. A mismatched button
                // or missing Shift on an unlink remains pending and is left to Valheim.
                if (pending.Button == button && pending.Remove == remove)
                {
                    ProductionLinkInput.Consume(Time.frameCount, button);
                    Selections.Remove(playerId);
                    bool success = CommitSelection(
                        player, pending, pointedContainer, pending.Remove, out string pendingDetail);
                    if (success)
                    {
                        if (pending.Remove) ProductionLinkHighlight.Hide(pointedContainer);
                        else ProductionLinkHighlight.Show(
                            pointedContainer, pending.Role, SuccessfulLinkHighlightSeconds);
                    }
                    ValheimAccess.Message(player, pendingDetail);
                }
                return;
            }
            ProductionStationEntry station = null;
            bool classified = target != null &&
                              TryClassifyStation(target, out station);
            if (!classified) return;

            int stationId = station.Component.GetInstanceID();
            if (_highlightRefreshFrame != Time.frameCount ||
                _highlightRefreshStationId != stationId)
            {
                _highlightRefreshFrame = Time.frameCount;
                _highlightRefreshStationId = stationId;
                try { RefreshLinkedHighlights(station, ContextHighlightSeconds); }
                catch (Exception exception)
                {
                    if (!_highlightFailureReported)
                    {
                        _highlightFailureReported = true;
                        ProductionDiagnostics.Warning(
                            "Linked-chest ring rendering failed safely: " +
                            exception.GetType().Name + ".");
                    }
                }
            }
            if (!hasGesture) return;

            // The target is a supported Production station, so this exact chord belongs to
            // Production even when that station ultimately rejects the requested role. Do not
            // let the same click attack, block, or perform a secondary attack.
            ProductionLinkInput.Consume(Time.frameCount, button);
            TryBeginLinkSelection(
                player,
                station,
                target,
                button,
                remove,
                out string detail);
            ValheimAccess.Message(player, detail);
        }

        private static bool RunOne(ProductionStationEntry entry)
        {
            if (entry?.Component == null || !ValheimAccess.IsNativeOwner(entry.Component))
                return false;
            if (SafetyPausedStations.Contains(entry.Component.GetInstanceID())) return false;
            // Warm every explicit role, including output, before the next native machine tick.
            // Requests are bounded and asynchronous; unresolved chests never become inventories.
            foreach (ProductionLinkRole role in new[] { ProductionLinkRole.Input, ProductionLinkRole.Fuel,
                         ProductionLinkRole.Output, ProductionLinkRole.Replenishment })
                foreach (StoredProductionLink link in RoleLinks(entry.Component, role))
                    if (TryReadAuthorizedEndpoint(entry.Component, link, true,
                            out Container chest, out _, out _))
                        ProductionChestHandoff.TryAcquire(entry.Component, chest, link.OwnerId);
            TryActivatePendingReplenishment(entry);
            switch (entry.Kind)
            {
                case ProductionStationKind.Smelter:
                    return RunSmelter((Smelter)entry.Component);
                case ProductionStationKind.Cooking:
                    return RunCooking((CookingStation)entry.Component);
                case ProductionStationKind.Recipe:
                    return RunRecipe((CraftingStation)entry.Component);
                case ProductionStationKind.Fermenter:
                    return RunFermenter((Fermenter)entry.Component);
                case ProductionStationKind.Fireplace:
                    return RunFireplace((Fireplace)entry.Component);
                default:
                    return false;
            }
        }

        private static void TryActivatePendingReplenishment(ProductionStationEntry station)
        {
            if (station?.Component == null ||
                !StationSupportsRole(station.Kind, ProductionLinkRole.Replenishment)) return;
            ZDO stationZdo = ValheimAccess.Zdo(station.Component);
            string stationId = ValheimAccess.StableId(station.Component);
            StoredRecordState planState = MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                stationZdo,
                stationId,
                false,
                out MultiReplenishmentCatalog plans,
                out _);
            if (planState == StoredRecordState.Invalid ||
                !TryReadOrMigrateReplenishmentRoleLinks(
                    station.Component,
                    plans,
                    out ProductionRoleLinkCatalog links)) return;
            // The role catalog is the authoritative user-visible link set. A stale signed plan
            // that is not present here is inert and must never resurrect orphan automation.
            foreach (StoredProductionLink link in links.Links)
            {
                ReplenishmentDestinationRecord current = plans?.Destinations.FirstOrDefault(
                    value => SameTarget(
                        value.Link, link.TargetToken, link.TargetPrefabHash));
                bool storedPrincipalMatches = current?.Plan != null &&
                    current.Plan.AuthorizedPlayerId == link.OwnerId;
                long principalId = storedPrincipalMatches
                    ? current.Plan.AuthorizedPlayerId
                    : link.OwnerId;
                string principalName = storedPrincipalMatches
                    ? current.Plan.AuthorizedPlayerName
                    : "Production link " + link.OwnerId;
                if (principalId == 0L || principalId != link.OwnerId ||
                    string.IsNullOrWhiteSpace(principalName)) continue;
                if (!TryResolveEndpoint(
                        station.Component,
                        link,
                        true,
                        out ProductionEndpoint endpoint,
                        out _)) continue;
                StoredProductionLink planLink = link;
                ReplenishmentPlan previousPlan = current != null &&
                    current.Link.Revision == planLink.Revision
                        ? current.Plan
                        : null;
                if (!TryBuildPlan(
                        station,
                        stationId,
                        planLink,
                        endpoint.Inventory,
                        principalId,
                        principalName,
                        previousPlan,
                        out ReplenishmentPlan preview,
                        out _) ||
                    current != null && current.Link.Revision == planLink.Revision &&
                    SameTargetSet(current.Plan, preview)) continue;
                TryActivateReplenishmentDestination(
                    station,
                    stationZdo,
                    endpoint.Container,
                    endpoint.Inventory,
                    planLink,
                    principalId,
                    principalName,
                    out _);
                // One bounded metadata refresh per station quantum is enough.
                break;
            }
        }

        private static bool TryReadOrMigrateReplenishmentRoleLinks(
            Component station,
            MultiReplenishmentCatalog plans,
            out ProductionRoleLinkCatalog links)
        {
            links = null;
            ZDO stationZdo = ValheimAccess.Zdo(station);
            StoredRecordState state = ProductionRoleLinkCatalogStore.Read(
                stationZdo,
                ProductionLinkRole.Replenishment,
                out links);
            if (state == StoredRecordState.Valid) return links != null;
            if (state == StoredRecordState.Invalid) return false;

            var candidates = new List<StoredProductionLink>();
            StoredProductionLink singleton = ProductionLinkStore.Load(
                stationZdo, ProductionLinkRole.Replenishment);
            if (singleton != null) candidates.Add(singleton);
            if (plans != null)
                foreach (ReplenishmentDestinationRecord record in plans.Destinations)
                    if (record?.Link != null && !candidates.Any(value => SameTarget(
                            value,
                            record.Link.TargetToken,
                            record.Link.TargetPrefabHash)))
                        candidates.Add(record.Link);

            links = ProductionRoleLinkCatalogPolicy.Empty(
                ProductionLinkRole.Replenishment);
            ProductionRoleLinkCatalog expectedLinks = links;
            // Migration preserves every already-authorized legacy destination up to the hard
            // storage bound. A lowered soft setting gates only future interactive additions.
            int maximum = ProductionRoleLinkCatalog.HardMaximumLinks;
            foreach (StoredProductionLink candidate in candidates)
            {
                ReplenishmentDestinationRecord signed = plans?.Destinations.FirstOrDefault(
                    value => candidate != null && SameTarget(
                        value.Link,
                        candidate.TargetToken,
                        candidate.TargetPrefabHash));
                long authorizedId = signed?.Plan?.AuthorizedPlayerId ?? candidate?.OwnerId ?? 0L;
                if (candidate == null || candidate.OwnerId != authorizedId ||
                    !TryResolveEndpoint(
                        station,
                        candidate,
                        true,
                        out ProductionEndpoint endpoint,
                        out _) ||
                    !ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                        links,
                        endpoint.Link,
                        maximum,
                        out ProductionRoleLinkCatalog updated,
                        out _,
                        out _)) continue;
                links = updated;
            }
            if (links.Links.Count == 0) return true;
            return ProductionRoleLinkCatalogStore.Publish(
                stationZdo, expectedLinks, links);
        }

        private static bool SameTargetSet(
            ReplenishmentPlan left,
            ReplenishmentPlan right)
        {
            if (left == null || right == null ||
                left.Targets.Count != right.Targets.Count ||
                left.AdapterKind != right.AdapterKind ||
                left.AuthorizedPlayerId != right.AuthorizedPlayerId ||
                !string.Equals(
                    left.AuthorizedPlayerName,
                    right.AuthorizedPlayerName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    left.StationPrefabId,
                    right.StationPrefabId,
                    StringComparison.Ordinal)) return false;
            for (int index = 0; index < left.Targets.Count; index++)
            {
                ReplenishmentTargetAuthorization a = left.Targets[index];
                ReplenishmentTargetAuthorization b = right.Targets[index];
                if (!string.Equals(a.OutputPrefabId, b.OutputPrefabId, StringComparison.Ordinal) ||
                    a.ProducerKind != b.ProducerKind ||
                    !string.Equals(a.ProducerId, b.ProducerId, StringComparison.Ordinal) ||
                    !a.ProducerSignatureMatches(b.CopyProducerSignature()) ||
                    a.OutputAmount != b.OutputAmount ||
                    !string.Equals(
                        a.RequiredStationName,
                        b.RequiredStationName,
                        StringComparison.Ordinal) ||
                    a.RequiredStationLevel != b.RequiredStationLevel ||
                    a.AuthorizedPlayerId != b.AuthorizedPlayerId ||
                    !string.Equals(
                        a.AuthorizedPlayerName,
                        b.AuthorizedPlayerName,
                        StringComparison.Ordinal) ||
                    a.Requirements.Count != b.Requirements.Count) return false;
                for (int requirement = 0; requirement < a.Requirements.Count; requirement++)
                    if (!string.Equals(
                            a.Requirements[requirement].PrefabId,
                            b.Requirements[requirement].PrefabId,
                            StringComparison.Ordinal) ||
                        a.Requirements[requirement].Amount !=
                        b.Requirements[requirement].Amount) return false;
            }
            return true;
        }

        private static bool RunSmelter(Smelter station)
        {
            if (station == null || station.m_maxOre <= 0) return false;
            if (ValheimAccess.QueueSize(station) < station.m_maxOre &&
                TryPullSmelterInput(station)) return true;
            return station.m_fuelItem != null && station.m_maxFuel > 0 &&
                   ValheimAccess.Fuel(station) + 0.001f < station.m_maxFuel &&
                   TryFuelSmelter(station);
        }

        private static bool TryPullSmelterInput(Smelter station)
        {
            foreach (ProductionEndpoint source in ResolveRoleEndpoints(
                         station, ProductionLinkRole.Input))
            foreach (string prefab in ExactPrefabs(source.Inventory))
            {
                if (ValheimAccess.Conversion(station, prefab) == null) continue;
                if (!ExactStockInventoryMutation.TryPrepareSource(
                        source.Inventory,
                        new[] { new ReplenishmentRequirement(prefab, 1) },
                        _ingredientReserves,
                        out StockInventoryTransition transition,
                        out _,
                        out _)) continue;
                int before = ValheimAccess.QueueSize(station);
                string tail = ValheimAccess.QueueTail(station, before);
                bool cheatedBefore = ValheimAccess.SmelterQueuedCheated(station);
                if (!ApplySourceToStation(
                        station,
                        source,
                        transition,
                        () => ValheimAccess.QueueOre(
                            station, prefab, transition.ConsumedCheated),
                        () => ValheimAccess.RestoreQueueTail(
                            station, before, tail, cheatedBefore),
                        () => ValheimAccess.QueueSize(station) == before &&
                              string.Equals(
                                  ValheimAccess.QueueTail(station, before),
                                  tail,
                                  StringComparison.Ordinal) &&
                              ValheimAccess.SmelterQueuedCheated(station) == cheatedBefore,
                        () => ValheimAccess.QueueSize(station) == before + 1 &&
                              ValheimAccess.SmelterQueuedCheated(station) ==
                              transition.ConsumedCheated))
                    continue;
                Stop(station, ProductionStopCode.Ready, "pulled " + prefab);
                return true;
            }
            return false;
        }

        private static bool TryFuelSmelter(Smelter station)
        {
            string fuelPrefab = ValheimAccess.PrefabName(station.m_fuelItem.gameObject);
            if (!StockDomainValidation.IsExactPrefabId(fuelPrefab)) return false;
            foreach (ProductionEndpoint source in ResolveFuelEndpoints(station))
            {
                if (!ExactStockInventoryMutation.TryPrepareSource(
                        source.Inventory,
                        new[] { new ReplenishmentRequirement(fuelPrefab, 1) },
                        _fuelReserves,
                        out StockInventoryTransition transition,
                        out _, out _)) continue;
                float before = ValheimAccess.Fuel(station);
                bool applied = ApplySourceToStation(
                    station,
                    source,
                    transition,
                    () => ValheimAccess.SetFuel(station, before + 1f),
                    () => ValheimAccess.SetFuel(station, before),
                    () => Math.Abs(ValheimAccess.Fuel(station) - before) < 0.001f,
                    () => Math.Abs(ValheimAccess.Fuel(station) - (before + 1f)) < 0.001f);
                if (applied)
                {
                    Stop(station, ProductionStopCode.Ready, "supplied fuel");
                    return true;
                }
            }
            return false;
        }

        private static bool RunFireplace(Fireplace station)
        {
            if (station == null || !station.m_canRefill || station.m_infiniteFuel ||
                station.m_fuelItem == null || station.m_maxFuel <= 0f) return false;
            float before = ValheimAccess.FireplaceFuel(station);
            if (Mathf.Ceil(before) >= station.m_maxFuel) return false;
            string fuelPrefab = ValheimAccess.PrefabName(station.m_fuelItem.gameObject);
            if (!StockDomainValidation.IsExactPrefabId(fuelPrefab)) return false;
            foreach (ProductionEndpoint source in ResolveFuelEndpoints(station))
            {
                if (!ExactStockInventoryMutation.TryPrepareSource(
                        source.Inventory,
                        new[] { new ReplenishmentRequirement(fuelPrefab, 1) },
                        _fuelReserves,
                        out StockInventoryTransition transition,
                        out _, out _)) continue;
                float after = Math.Min(station.m_maxFuel, before + 1f);
                bool applied = ApplySourceToStation(
                    station,
                    source,
                    transition,
                    () => ValheimAccess.SetFireplaceFuel(station, after),
                    () => ValheimAccess.SetFireplaceFuel(station, before),
                    () => Math.Abs(ValheimAccess.FireplaceFuel(station) - before) < 0.001f,
                    () => Math.Abs(ValheimAccess.FireplaceFuel(station) - after) < 0.001f);
                if (applied)
                {
                    Stop(station, ProductionStopCode.Ready, "supplied " + fuelPrefab);
                    return true;
                }
            }
            return false;
        }

        private static bool RunCooking(CookingStation station)
        {
            if (!CookingStationCompatibility.TryValidate(
                    station, _cookingPolicy, true,
                    out string prefabId, out string compatibilityFailure))
            {
                Stop(station, ProductionStopCode.StationUnusable, compatibilityFailure);
                return false;
            }
            if (!CookingStationCompatibility.TryValidateState(
                    station, out string stateFailure))
            {
                Stop(station, ProductionStopCode.StationUnusable, stateFailure);
                return false;
            }
            if (!TimedCookingStockModel.TryDescribe(
                    station, prefabId,
                    out TimedCookingStationDescriptor descriptor,
                    out string descriptorFailure))
            {
                Stop(station, ProductionStopCode.StationUnusable, descriptorFailure);
                return false;
            }
            if (TryRouteCookingOutput(station, descriptor)) return true;
            if (station.m_useFuel && TryFuelCooking(station, descriptor)) return true;
            return TryFillCookingSlot(station, descriptor);
        }

        private static bool TryRouteCookingOutput(
            CookingStation station,
            TimedCookingStationDescriptor descriptor)
        {
            for (int slot = 0; slot < descriptor.SlotCount; slot++)
            {
                ValheimAccess.GetCookingSlot(
                    station, slot,
                    out string item, out float elapsed, out CookingSlotStatus status,
                    out bool cheated);
                if (string.IsNullOrEmpty(item) ||
                    status != CookingSlotStatus.Done &&
                    status != CookingSlotStatus.Burnt) continue;
                if (!TryResolveOutputDestination(
                        new ProductionStationEntry(
                            ProductionStationKind.Cooking, station),
                        item,
                        1,
                        ReplenishmentProducerKind.TimedCooking,
                        out ProductionDestination destination)) continue;
                var output = new StockOutputDefinition(
                    item, 1, 1, 0, 0L, string.Empty, cheated);
                if (!ExactStockInventoryMutation.TryPrepareDestination(
                        destination.Endpoint.Inventory,
                        output,
                        out StockInventoryTransition transition,
                        out _)) continue;
                if (!ApplyDestinationFromStation(
                        station,
                        destination.Endpoint,
                        transition,
                        () => ValheimAccess.SetCookingSlot(
                            station, slot, string.Empty, 0f,
                            CookingSlotStatus.NotDone),
                        () => ValheimAccess.SetCookingSlot(
                            station, slot, item, elapsed, status, cheated),
                        () => CookingSlotMatches(
                            station, slot, item, elapsed, status, cheated),
                        () => CookingSlotIsEmpty(station, slot))) continue;
                AdvanceDestination(ValheimAccess.Zdo(station), destination);
                Stop(station, ProductionStopCode.Ready, "stored " + item);
                return true;
            }
            return false;
        }

        private static bool TryFuelCooking(
            CookingStation station,
            TimedCookingStationDescriptor descriptor)
        {
            if (!descriptor.UsesFuel || string.IsNullOrEmpty(descriptor.FuelPrefabId) ||
                ValheimAccess.CookingFuel(station) + 0.001f >= descriptor.MaximumFuel)
                return false;
            foreach (ProductionEndpoint source in ResolveFuelEndpoints(station))
            {
                if (!ExactStockInventoryMutation.TryPrepareSource(
                        source.Inventory,
                        new[] { new ReplenishmentRequirement(descriptor.FuelPrefabId, 1) },
                        _fuelReserves,
                        out StockInventoryTransition transition,
                        out _, out _)) continue;
                float before = ValheimAccess.CookingFuel(station);
                if (ApplySourceToStation(
                        station,
                        source,
                        transition,
                        () => ValheimAccess.SetCookingFuel(station, before + 1f),
                        () => ValheimAccess.SetCookingFuel(station, before),
                        () => Math.Abs(
                            ValheimAccess.CookingFuel(station) - before) < 0.001f,
                        () => Math.Abs(
                            ValheimAccess.CookingFuel(station) - (before + 1f)) < 0.001f))
                    return true;
            }
            return false;
        }

        private static bool TryFillCookingSlot(
            CookingStation station,
            TimedCookingStationDescriptor descriptor)
        {
            int emptySlot = -1;
            for (int slot = 0; slot < descriptor.SlotCount; slot++)
            {
                ValheimAccess.GetCookingSlot(
                    station, slot, out string item, out _, out _);
                if (string.IsNullOrEmpty(item))
                {
                    emptySlot = slot;
                    break;
                }
            }
            if (emptySlot < 0) return false;

            var entry = new ProductionStationEntry(
                ProductionStationKind.Cooking, station);
            string required = string.Empty;
            ProductionDestination planned = null;
            bool hasPlannedDestination = TrySelectPlannedDestination(
                    entry,
                    ReplenishmentProducerKind.TimedCooking,
                    out planned);
            if (hasPlannedDestination)
            {
                if (planned.Target.Requirements.Count != 1) return false;
                required = planned.Target.Requirements[0].PrefabId;
            }
            else if (HasReplenishmentRecord(
                         ValheimAccess.Zdo(station), station)) return false;
            IEnumerable<ProductionEndpoint> cookingSources;
            if (hasPlannedDestination)
            {
                cookingSources = ResolveRecipeInputs(
                    station,
                    planned.Endpoint,
                    planned.Target.AuthorizedPlayerId);
            }
            else
            {
                StoredProductionLink ingredientLink = RoleLinks(
                        station, ProductionLinkRole.Input)
                    .FirstOrDefault(value => value != null && value.OwnerId != 0L);
                cookingSources = ingredientLink == null
                    ? ResolveRoleEndpoints(station, ProductionLinkRole.Input)
                    : ResolveRecipeInputs(station, null, ingredientLink.OwnerId);
            }
            foreach (ProductionEndpoint source in cookingSources)
            {
                IEnumerable<string> prefabs = string.IsNullOrEmpty(required)
                    ? ExactPrefabs(source.Inventory)
                    : new[] { required };
                foreach (string prefab in prefabs)
                {
                    if (!descriptor.TryFromInput(prefab, out _)) continue;
                    if (!ExactStockInventoryMutation.TryPrepareSource(
                            source.Inventory,
                            new[] { new ReplenishmentRequirement(prefab, 1) },
                            _ingredientReserves,
                            out StockInventoryTransition transition,
                            out _, out _)) continue;
                    int slot = emptySlot;
                    if (ApplySourceToStation(
                            station,
                            source,
                            transition,
                            () => ValheimAccess.SetCookingSlot(
                                station, slot, prefab, 0f,
                                CookingSlotStatus.NotDone,
                                transition.ConsumedCheated),
                            () => ValheimAccess.SetCookingSlot(
                                station, slot, string.Empty, 0f,
                                CookingSlotStatus.NotDone),
                            () => CookingSlotIsEmpty(station, slot),
                            () => CookingSlotMatches(
                                station, slot, prefab,
                                CookingSlotStatus.NotDone,
                                transition.ConsumedCheated))) return true;
                }
            }
            return false;
        }

        private static bool RunRecipe(CraftingStation station)
        {
            if (!RecipeStationCompatibility.TryValidate(
                    station, _recipePolicy, true,
                    out string prefabId,
                    out _, out _, out _)) return false;
            string stationId = ValheimAccess.StableId(station);
            if (string.IsNullOrEmpty(stationId)) return false;
            ZDO stationZdo = ValheimAccess.Zdo(station);
            if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                    stationZdo, stationId, false,
                    out MultiReplenishmentCatalog catalog,
                    out _) != StoredRecordState.Valid ||
                catalog.Destinations.Count == 0) return false;

            foreach (ReplenishmentDestinationRecord record in
                     catalog.ActiveRoundRobinOrder())
            {
                ReplenishmentPlan plan = record.Plan;
                if (plan.AdapterKind != ReplenishmentProducerKind.DirectRecipe ||
                    plan.Targets.Count == 0 ||
                    !IsActiveReplenishmentLink(station, record.Link)) continue;
                for (int offset = 0; offset < plan.Targets.Count; offset++)
                {
                int targetIndex = (plan.Cursor + offset) % plan.Targets.Count;
                ReplenishmentTargetAuthorization target = plan.Targets[targetIndex];
                if (!RecipeProducerResolver.TryResolveAuthorized(
                        station,
                        prefabId,
                        target,
                        _cookingInputPolicy,
                        _fermenterInputPolicy,
                        out _, out _)) continue;
                if (!TryResolveEndpoint(
                        station, record.Link, false,
                        out ProductionEndpoint destination, out _)) continue;
                int currentOutput = ExactStockInventoryMutation.CountExact(
                    destination.Inventory, target.OutputPrefabId,
                    matchWorldLevel: false);
                if (currentOutput <= 0 ||
                    !ReplenishmentNeedsStock(target.OutputPrefabId, currentOutput, 0))
                    continue;

                List<ProductionEndpoint> inputs = ResolveRecipeInputs(
                        station,
                        destination,
                        target.AuthorizedPlayerId)
                    .Take(NearbyIngredientContainerIndex.HardMaximumSourceChests)
                    .ToList();
                if (inputs.Count == 0 ||
                    !ExactStockInventoryMutation.TryPrepareCompositeSources(
                        inputs.Select(value => value.Inventory).ToList(),
                        target.Requirements,
                        _ingredientReserves,
                        out ExactStockCompositeSourcePlan sourcePlan,
                        out _)) continue;
                var output = new StockOutputDefinition(
                    target.OutputPrefabId,
                    target.OutputAmount,
                    1,
                    0,
                    target.AuthorizedPlayerId,
                    target.AuthorizedPlayerName,
                    sourcePlan.ConsumedCheated && !ValheimAccess.BypassCheatChecks);
                if (!ExactStockInventoryMutation.TryPrepareDestination(
                        destination.Inventory,
                        output,
                        out StockInventoryTransition destinationTransition,
                        out _) ||
                    !ApplyCompositeSourcesToDestination(
                        station,
                        inputs,
                        sourcePlan,
                        destination,
                        destinationTransition)) continue;
                AdvanceDestination(
                    stationZdo,
                    new ProductionDestination
                    {
                        Endpoint = destination,
                        Catalog = catalog,
                        Record = record,
                        Target = target,
                        TargetIndex = targetIndex
                    });
                Stop(station, ProductionStopCode.Ready,
                    "crafted " + target.OutputPrefabId);
                return true;
                }
            }
            return false;
        }

        private static IEnumerable<ProductionEndpoint> ResolveRecipeInputs(
            Component station,
            ProductionEndpoint destination,
            long actorId)
        {
            var seen = new HashSet<ZDOID>();
            HashSet<ZDOID> protectedDestinations = ProtectedDestinationIds(station);
            foreach (ProductionEndpoint linked in ResolveRoleEndpoints(
                         station, ProductionLinkRole.Input))
            {
                if (linked?.Zdo == null ||
                    ReferenceEquals(linked.Container, destination?.Container) ||
                    !seen.Add(linked.Zdo.m_uid)) continue;
                // An explicit Input link is the only role allowed to opt a chest back into
                // ingredient sourcing (including legacy worlds with overlapping metadata).
                protectedDestinations.Remove(linked.Zdo.m_uid);
                yield return linked;
            }
            if (!(ProductionConfig.RecipeNearbyIngredientsEnabled?.Value ?? false)) yield break;
            int maximum = Mathf.Clamp(
                ProductionConfig.RecipeNearbyMaximumSourceChests?.Value ?? 32,
                1,
                NearbyIngredientContainerIndex.HardMaximumSourceChests);
            NearbyIngredientContainerQueryResult query = NearbyIngredientContainerIndex.Query(
                station.transform.position, LinkRange, maximum);
            if (query.Truncated) yield break;
            foreach (NearbyIngredientContainerCandidate candidate in query.Candidates)
            {
                Container container = candidate.Container;
                if (container == null || ReferenceEquals(container, destination?.Container) ||
                    !NearbyIngredientContainerIndex.IsStaticNonWagon(container)) continue;
                ZDO zdo = ValheimAccess.Zdo(container);
                if (zdo == null || protectedDestinations.Contains(zdo.m_uid) ||
                    !seen.Add(zdo.m_uid) ||
                    !ValheimAccess.ContainerAllows(container, actorId) ||
                    !ValheimAccess.WardAllows(container.transform.position, actorId) ||
                    !ProductionChestHandoff.TryAcquire(station, container, actorId) ||
                    !ValheimAccess.TrySynchronizeLocallyOwnedContainer(
                        container, out Inventory inventory)) continue;
                yield return new ProductionEndpoint
                {
                    Container = container,
                    Zdo = zdo,
                    Inventory = inventory
                };
            }
        }

        private static HashSet<ZDOID> ProtectedDestinationIds(Component station)
        {
            var protectedIds = new HashSet<ZDOID>();
            if (station == null) return protectedIds;
            foreach (ProductionLinkRole role in new[]
                     {
                         ProductionLinkRole.Fuel,
                         ProductionLinkRole.Output,
                         ProductionLinkRole.Replenishment
                     })
                foreach (StoredProductionLink link in RoleLinks(station, role))
                    AddProtectedTarget(protectedIds, link);
            return protectedIds;
        }

        private static void AddProtectedTarget(
            ISet<ZDOID> protectedIds,
            StoredProductionLink link)
        {
            if (protectedIds == null || link == null) return;
            ProductionIdentityStatus status = ProductionEndpointIdentity.ResolveStable(
                link, out _, out ZDO current, out _);
            if (status == ProductionIdentityStatus.Ready && current != null)
                protectedIds.Add(current.m_uid);
            else if (!link.Target.IsNone()) protectedIds.Add(link.Target);
        }

        private static bool RunFermenter(Fermenter station)
        {
            if (!FermenterCompatibility.TryValidate(
                    station, _fermenterPolicy, true,
                    out FermenterDescriptor descriptor, out _) ||
                !FermenterCompatibility.TryValidateState(
                    station, descriptor,
                    out FermenterStationState state, out _)) return false;
            if (state.IsEmpty) return TryFillFermenter(station, descriptor);
            if (ValheimAccess.FermenterStatus(station) != FermenterSlotStatus.Ready ||
                !descriptor.TryFromInput(
                    state.InputPrefabId,
                    out FermenterConversionDescriptor conversion)) return false;
            if (!TryResolveOutputDestination(
                    new ProductionStationEntry(
                        ProductionStationKind.Fermenter, station),
                    conversion.OutputPrefabId,
                    conversion.OutputAmount,
                    ReplenishmentProducerKind.Fermenter,
                    out ProductionDestination destination)) return false;
            var output = new StockOutputDefinition(
                conversion.OutputPrefabId,
                conversion.OutputAmount,
                1,
                0,
                0L,
                string.Empty,
                ValheimAccess.FermenterOutputCheated(station));
            if (!ExactStockInventoryMutation.TryPrepareDestination(
                    destination.Endpoint.Inventory,
                    output,
                    out StockInventoryTransition transition,
                    out _)) return false;
            if (!ApplyDestinationFromStation(
                    station,
                    destination.Endpoint,
                    transition,
                    () => ValheimAccess.SetFermenterState(
                        station, string.Empty, 0L),
                    () => state.Apply(station),
                    () => FermenterStateMatches(
                        station, state.InputPrefabId, state.StartTicks, state.Cheated),
                    () => string.IsNullOrEmpty(
                        ValheimAccess.FermenterContent(station)) &&
                          ValheimAccess.FermenterStartTicks(station) == 0L &&
                          !ValheimAccess.FermenterCheated(station))) return false;
            AdvanceDestination(ValheimAccess.Zdo(station), destination);
            Stop(station, ProductionStopCode.Ready,
                "stored " + conversion.OutputPrefabId);
            return true;
        }

        private static bool TryFillFermenter(
            Fermenter station,
            FermenterDescriptor descriptor)
        {
            ValheimAccess.RefreshFermenterCover(station);
            if (!ValheimAccess.FermenterCovered(station)) return false;
            var entry = new ProductionStationEntry(
                ProductionStationKind.Fermenter, station);
            string required = string.Empty;
            ProductionDestination planned = null;
            bool hasPlannedDestination = TrySelectPlannedDestination(
                    entry,
                    ReplenishmentProducerKind.Fermenter,
                    out planned);
            if (hasPlannedDestination)
            {
                if (planned.Target.Requirements.Count != 1) return false;
                required = planned.Target.Requirements[0].PrefabId;
            }
            else if (HasReplenishmentRecord(
                         ValheimAccess.Zdo(station), station)) return false;
            IEnumerable<ProductionEndpoint> fermenterSources = hasPlannedDestination
                ? ResolveRecipeInputs(
                    station,
                    planned.Endpoint,
                    planned.Target.AuthorizedPlayerId)
                : ResolveRoleEndpoints(station, ProductionLinkRole.Input);
            foreach (ProductionEndpoint source in fermenterSources)
            {
                IEnumerable<string> prefabs = string.IsNullOrEmpty(required)
                    ? ExactPrefabs(source.Inventory)
                    : new[] { required };
                foreach (string prefab in prefabs)
                {
                    if (!descriptor.TryFromInput(prefab, out _)) continue;
                    if (!ExactStockInventoryMutation.TryPrepareSource(
                            source.Inventory,
                            new[] { new ReplenishmentRequirement(prefab, 1) },
                            _ingredientReserves,
                            out StockInventoryTransition transition,
                            out _, out _)) continue;
                    long ticks = ValheimAccess.NetworkTimeTicks();
                    if (ApplySourceToStation(
                            station,
                            source,
                            transition,
                            () => ValheimAccess.SetFermenterState(
                                station, prefab, ticks, transition.ConsumedCheated),
                            () => ValheimAccess.SetFermenterState(
                                station, string.Empty, 0L),
                            () => FermenterStateMatches(
                                station, string.Empty, 0L),
                            () => string.Equals(
                                      ValheimAccess.FermenterContent(station),
                                      prefab,
                                      StringComparison.Ordinal) &&
                                  ValheimAccess.FermenterStartTicks(station) == ticks &&
                                  ValheimAccess.FermenterCheated(station) ==
                                  transition.ConsumedCheated))
                        return true;
                }
            }
            return false;
        }

        internal static bool TryDepositProducedOutput(
            Smelter station,
            string inputPrefab,
            int amount)
        {
            if (!_initialized || station == null || amount <= 0 ||
                !(ProductionConfig.Enabled?.Value ?? false) ||
                !ValheimAccess.IsNativeOwner(station)) return false;
            // An indeterminate current batch is suppressed at the failure site. Later batches
            // bypass Production entirely and use vanilla Spawn until reload reconstructs truth.
            if (SafetyPausedStations.Contains(station.GetInstanceID())) return false;
            Smelter.ItemConversion conversion =
                ValheimAccess.Conversion(station, inputPrefab);
            string outputPrefab = conversion?.m_to == null
                ? string.Empty
                : ValheimAccess.PrefabName(conversion.m_to.gameObject);
            if (!StockDomainValidation.IsExactPrefabId(outputPrefab)) return false;
            var output = new StockOutputDefinition(
                outputPrefab, amount, 1, 0, 0L, string.Empty,
                ValheimAccess.SmelterOutputCheated(station));
            foreach (ProductionEndpoint destination in ResolveRoleEndpoints(
                         station, ProductionLinkRole.Output))
            {
                if (!ExactStockInventoryMutation.TryPrepareDestination(
                        destination.Inventory,
                        output,
                        out StockInventoryTransition transition,
                        out _)) continue;
                if (!EndpointStillOwned(destination) ||
                    !transition.MatchesBefore(destination.Inventory)) continue;
                try
                {
                    transition.ApplyAfter(destination.Inventory);
                    ProductionEndpointMutationState state = ResolveEndpointMutation(
                        destination, transition);
                    if (state == ProductionEndpointMutationState.After) return true;
                    if (state == ProductionEndpointMutationState.Before) continue;
                    SafetyPausedStations.Add(station.GetInstanceID());
                    ProductionDiagnostics.Warning(
                        "Smelter output suppressed vanilla fallback after an indeterminate " +
                        "destination publication; reload to resolve persisted state.");
                    return true;
                }
                catch (Exception exception)
                {
                    ProductionEndpointMutationState state = ResolveEndpointMutation(
                        destination, transition);
                    if (state == ProductionEndpointMutationState.After) return true;
                    if (state == ProductionEndpointMutationState.Before)
                    {
                        ProductionDiagnostics.Warning(
                            "Smelter output tried the next linked chest after a proven rollback: " +
                            exception.GetType().Name + ".");
                        continue;
                    }
                    SafetyPausedStations.Add(station.GetInstanceID());
                    ProductionDiagnostics.Warning(
                        "Smelter output suppressed vanilla fallback after an indeterminate " +
                        "destination failure: " + exception.GetType().Name + ".");
                    return true;
                }
            }
            return false;
        }

        internal static bool TryHandleInteraction(
            Player player,
            GameObject target,
            bool hold,
            bool alternateUse,
            out string detail)
        {
            detail = string.Empty;
            // Linking is sampled from the exact Alt+mouse edge in TickLocalPlayer. Valheim's
            // ordinary Interact/alternate-use path is never repurposed by Production.
            return false;
        }

        private static bool TryBeginLinkSelection(
            Player player,
            ProductionStationEntry station,
            GameObject stationTarget,
            ProductionLinkMouseButton button,
            bool remove,
            out string detail)
        {
            detail = string.Empty;
            if (player == null || station?.Component == null) return false;
            long playerId = player.GetPlayerID();
            if (playerId == 0L) return false;

            ProductionLinkRole requested =
                ProductionLinkGesturePolicy.RequestedRole(button);
            ProductionLinkRole role = RefineInputRoleForTarget(
                station, stationTarget, requested);
            if (player != Player.m_localPlayer || !player.IsOwner() ||
                ValheimAccess.Zdo(station.Component) == null ||
                !ValheimAccess.ComponentWithinReach(station.Component, player.transform.position,
                    Mathf.Clamp(player.m_maxInteractDistance, 1f, 10f)) ||
                !ValheimAccess.WardAllows(station.Component.transform.position, playerId))
                return Fail("The station is out of reach or ward access is denied.", out detail);
            if (!StationSupportsRole(station.Kind, role) ||
                !ValidateStation(station, out detail))
            {
                if (string.IsNullOrEmpty(detail))
                    detail = role + " is not supported by this station.";
                return true;
            }
            if (Selections.TryGetValue(playerId, out ProductionLinkSelection current) &&
                ReferenceEquals(current.Station.Component, station.Component) &&
                current.Role == role && current.Remove == remove)
            {
                Selections.Remove(playerId);
                detail = "Production link selection cancelled.";
            }
            else
            {
                if (!Selections.ContainsKey(playerId) &&
                    Selections.Count >= MaximumSelections)
                {
                    detail = "Too many link selections are active; try again shortly.";
                    return true;
                }
                Selections[playerId] = new ProductionLinkSelection
                {
                    PlayerId = playerId,
                    Station = station,
                    Role = role,
                    Button = button,
                    Remove = remove,
                    ExpiresAt = Time.realtimeSinceStartup + SelectionLifetimeSeconds
                };
                string displayRole = DisplayRole(role);
                string gesture = GestureName(button, remove);
                detail = remove
                    ? "Selected " + displayRole + " unlink. Use " + gesture +
                      " on a linked chest within 30 seconds."
                    : "Selected " + displayRole + ". Use " + gesture +
                      " on a chest within 30 seconds.";
            }
            return true;
        }

        private static bool CommitSelection(
            Player player,
            ProductionLinkSelection selection,
            Container container,
            bool remove,
            out string detail)
        {
            detail = string.Empty;
            if (player == null || player != Player.m_localPlayer || !player.IsOwner() ||
                selection == null || selection.Station?.Component == null ||
                selection.ExpiresAt < Time.realtimeSinceStartup ||
                selection.PlayerId != player.GetPlayerID())
                return Fail("The production link selection expired.", out detail);
            Component station = selection.Station.Component;
            ZDO stationZdo = ValheimAccess.Zdo(station);
            ZDO targetZdo = ValheimAccess.Zdo(container);
            if (stationZdo == null || targetZdo == null ||
                ReferenceEquals(stationZdo, targetZdo) ||
                !NearbyIngredientContainerIndex.IsStaticNonWagon(container))
                return Fail("The selected endpoint is not a static exact chest.", out detail);
            float interactionRange = Mathf.Clamp(player.m_maxInteractDistance, 1f, 10f);
            if (!ValheimAccess.ContainerWithinReach(
                    container, player.transform.position, interactionRange))
                return Fail("The chest is outside interaction range.", out detail);
            Vector3 stationPosition = stationZdo.GetPosition();
            Vector3 targetPosition = targetZdo.GetPosition();
            if (!ValheimAccess.IsFinite(stationPosition) ||
                !ValheimAccess.IsFinite(targetPosition) ||
                (stationPosition - targetPosition).sqrMagnitude > LinkRange * LinkRange)
                return Fail("The chest is outside the configured link range.", out detail);
            long actorId = player.GetPlayerID();
            if (!ValheimAccess.ContainerAllows(container, actorId) ||
                !ValheimAccess.WardAllows(stationPosition, actorId) ||
                !ValheimAccess.WardAllows(targetPosition, actorId))
                return Fail("Vanilla chest or ward access denied this link.", out detail);
            if (!StationSupportsRole(selection.Station.Kind, selection.Role) ||
                !ValidateStation(selection.Station, out detail))
                return Fail("The selected station no longer supports this production role. " + detail, out detail);
            if (!ValheimAccess.TryPrepareLinkOwnership(
                    player, station, stationZdo, container, targetZdo, LinkRange, out detail))
                return false;
            if (!ValheimAccess.TrySynchronizeLocallyOwnedContainer(
                    container, out Inventory inventory))
                return Fail("The chest is busy or not synchronized.", out detail);
            if (!ProductionEndpointIdentity.TryCaptureCurrentTarget(
                    targetZdo.m_uid,
                    out string token,
                    out int prefabHash,
                    out Vector3 exactPosition,
                    out string identityFailure))
                return Fail(identityFailure, out detail);
            if (remove)
                return RemoveExactLink(
                    selection.Station,
                    selection.Role,
                    stationZdo,
                    token,
                    prefabHash,
                    out detail);
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(
                    stationZdo,
                    selection.Role,
                    out ProductionRoleLinkCatalog catalog) != StoredRecordState.Valid)
                return Fail("The selected role's link catalog is unavailable.", out detail);
            StoredProductionLink previous = catalog.FindTarget(token, prefabHash);
            StoredProductionLink candidate = ProductionLinkStore.CreateDetached(
                selection.Role,
                targetZdo.m_uid,
                exactPosition,
                actorId,
                ValheimAccess.Creator(station),
                ValheimAccess.Creator(container),
                previous);
            int maximum = Mathf.Clamp(
                ProductionConfig.MaximumLinksPerRole?.Value ?? 8,
                1,
                ProductionRoleLinkCatalog.HardMaximumLinks);
            if (!ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                    catalog,
                    candidate,
                    maximum,
                    out ProductionRoleLinkCatalog updated,
                    out StoredProductionLink published,
                    out detail))
                return Fail(
                    string.IsNullOrEmpty(detail)
                        ? "The role-link catalog rejected the link."
                        : detail,
                    out detail);

            MultiReplenishmentCatalog priorReplenishment = null;
            if (selection.Role == ProductionLinkRole.Replenishment)
            {
                string stationId = ValheimAccess.StableId(selection.Station.Component);
                if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                        stationZdo,
                        stationId,
                        true,
                        out priorReplenishment,
                        out detail) != StoredRecordState.Valid ||
                    !TryActivateReplenishmentDestination(
                        selection.Station,
                        stationZdo,
                        container,
                        inventory,
                        published,
                        player,
                        out detail))
                    return Fail(
                        string.IsNullOrEmpty(detail)
                            ? "The Replenishment plan did not publish."
                            : detail,
                        out detail);
            }

            if (!ProductionRoleLinkCatalogStore.Publish(stationZdo, catalog, updated))
            {
                // A replenishment plan is deliberately materialized while inert so its exact
                // authorized player ID/name survives an empty chest and dedicated restart. If
                // the authoritative role CAS fails, roll that inert body back best-effort; it
                // cannot automate because every plan consumer requires an exact role link.
                if (priorReplenishment != null)
                {
                    string stationId = ValheimAccess.StableId(selection.Station.Component);
                    if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                            stationZdo,
                            stationId,
                            false,
                            out MultiReplenishmentCatalog currentReplenishment,
                            out _) == StoredRecordState.Valid)
                    {
                        MultiReplenishmentCatalogCommitCode rollback =
                            MultiReplenishmentCatalogStore.TryCommit(
                                stationZdo,
                                currentReplenishment,
                                priorReplenishment);
                        if (rollback != MultiReplenishmentCatalogCommitCode.Committed &&
                            rollback != MultiReplenishmentCatalogCommitCode.NoChange)
                            ProductionDiagnostics.Warning(
                                "An inert Replenishment plan could not be rolled back after " +
                                "the role-link publication failed: " + rollback + ".");
                    }
                }
                return Fail("The role-link catalog did not publish.", out detail);
            }
            detail = "Linked as " + DisplayRole(selection.Role).ToLowerInvariant() +
                     " for " + StationName(selection.Station.Component);
            return true;
        }

        private static bool AddOrRefreshDestination(
            ProductionStationEntry station,
            ZDO stationZdo,
            Container container,
            Inventory inventory,
            ZDO targetZdo,
            string targetToken,
            int targetPrefabHash,
            Vector3 targetPosition,
            StoredProductionLink suppliedLink,
            long actorId,
            string actorName,
            out string detail)
        {
            detail = string.Empty;
            string stationId = ValheimAccess.StableId(station.Component);
            if (string.IsNullOrEmpty(stationId) ||
                MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                    stationZdo, stationId, true,
                    out MultiReplenishmentCatalog catalog,
                    out detail) != StoredRecordState.Valid) return false;

            StoredProductionLink candidate = suppliedLink ?? ProductionLinkStore.CreateDetached(
                ProductionLinkRole.Replenishment,
                targetZdo.m_uid,
                targetPosition,
                actorId,
                ValheimAccess.Creator(station.Component),
                ValheimAccess.Creator(container));
            bool refresh = catalog.TryGetByTargetIdentity(
                candidate, out ReplenishmentDestinationRecord current);
            // Explicit re-linking supplies the freshly published role link (new position and
            // revision); periodic reconciliation supplies current.Link. In both cases the
            // supplied candidate is the authority the signed plan must bind to.
            StoredProductionLink link = candidate;
            ReplenishmentPlan previous = refresh &&
                current.Link.Revision == link.Revision
                    ? current.Plan
                    : null;
            if (!TryBuildPlan(
                    station,
                    stationId,
                    link,
                    inventory,
                    actorId,
                    actorName,
                    previous,
                    out ReplenishmentPlan plan,
                    out detail)) return false;

            MultiReplenishmentCatalog updated;
            ReplenishmentCatalogChangeCode change;
            bool changed = refresh
                ? MultiReplenishmentCatalogPolicy.TryRefresh(
                    catalog,
                    current.LinkId,
                    link,
                    plan,
                    out updated,
                    out _,
                    out change)
                : MultiReplenishmentCatalogPolicy.TryAdd(
                    catalog,
                    link,
                    plan,
                    // The authoritative role catalog already applied the user-facing soft
                    // limit when the link was created. Plan activation must preserve an
                    // existing empty link after that setting is lowered.
                    MultiReplenishmentCatalog.HardMaximumDestinations,
                    out updated,
                    out _,
                    out change);
            if (!changed)
                return Fail(
                    "The destination catalog rejected the change: " + change + ".",
                    out detail);
            MultiReplenishmentCatalogCommitCode commit =
                MultiReplenishmentCatalogStore.TryCommit(
                    stationZdo, catalog, updated);
            if (commit != MultiReplenishmentCatalogCommitCode.Committed &&
                commit != MultiReplenishmentCatalogCommitCode.NoChange)
                return Fail(
                    "The destination catalog did not publish: " + commit + ".",
                    out detail);
            detail = refresh
                ? "Replenishment targets refreshed from the physical exemplars."
                : "Replenishment destination added from the physical exemplars.";
            return true;
        }

        private static bool TryActivateReplenishmentDestination(
            ProductionStationEntry station,
            ZDO stationZdo,
            Container container,
            Inventory inventory,
            StoredProductionLink link,
            Player actor,
            out string detail)
        {
            detail = string.Empty;
            if (station?.Component == null || stationZdo == null || container == null ||
                inventory == null || link == null || actor == null)
                return false;
            return TryActivateReplenishmentDestination(
                station,
                stationZdo,
                container,
                inventory,
                link,
                actor.GetPlayerID(),
                actor.GetPlayerName(),
                out detail);
        }

        private static bool TryActivateReplenishmentDestination(
            ProductionStationEntry station,
            ZDO stationZdo,
            Container container,
            Inventory inventory,
            StoredProductionLink link,
            long actorId,
            string actorName,
            out string detail)
        {
            detail = string.Empty;
            if (station?.Component == null || stationZdo == null || container == null ||
                inventory == null || link == null || actorId == 0L ||
                string.IsNullOrWhiteSpace(actorName)) return false;
            ZDO targetZdo = ValheimAccess.Zdo(container);
            if (targetZdo == null) return false;
            return AddOrRefreshDestination(
                station,
                stationZdo,
                container,
                inventory,
                targetZdo,
                link.TargetToken,
                link.TargetPrefabHash,
                link.ExpectedPosition,
                link,
                actorId,
                actorName,
                out detail);
        }

        private static bool TryBuildPlan(
            ProductionStationEntry station,
            string stationId,
            StoredProductionLink link,
            Inventory inventory,
            long actorId,
            string actorName,
            ReplenishmentPlan previous,
            out ReplenishmentPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            switch (station.Kind)
            {
                case ProductionStationKind.Cooking:
                {
                    var cooking = (CookingStation)station.Component;
                    if (!CookingStationCompatibility.TryValidate(
                            cooking, _cookingPolicy, true,
                            out string prefab, out failure) ||
                        !TimedCookingStockModel.TryDescribe(
                            cooking, prefab,
                            out TimedCookingStationDescriptor descriptor,
                            out failure)) return false;
                    return TimedCookingPlanBuilder.TryBuild(
                        descriptor,
                        inventory,
                        link,
                        stationId,
                        actorId,
                        actorName,
                        previous,
                        out plan,
                        out failure);
                }
                case ProductionStationKind.Recipe:
                {
                    var recipe = (CraftingStation)station.Component;
                    if (!RecipeStationCompatibility.TryValidate(
                            recipe, _recipePolicy, true,
                            out string prefab,
                            out _, out _, out failure)) return false;
                    return RecipePlanBuilder.TryBuild(
                        recipe,
                        stationId,
                        prefab,
                        link,
                        inventory,
                        actorId,
                        actorName,
                        previous,
                        _cookingInputPolicy,
                        _fermenterInputPolicy,
                        out plan,
                        out failure);
                }
                case ProductionStationKind.Fermenter:
                {
                    var fermenter = (Fermenter)station.Component;
                    if (!FermenterCompatibility.TryValidate(
                            fermenter, _fermenterPolicy, true,
                            out FermenterDescriptor descriptor,
                            out failure)) return false;
                    return FermenterPlanBuilder.TryBuild(
                        descriptor,
                        inventory,
                        link,
                        stationId,
                        actorId,
                        actorName,
                        previous,
                        out plan,
                        out failure);
                }
                default:
                    return Fail(
                        "This station does not support Replenishment destinations.",
                        out failure);
            }
        }

        private static bool RemoveExactLink(
            ProductionStationEntry station,
            ProductionLinkRole role,
            ZDO stationZdo,
            string token,
            int prefabHash,
            out string detail)
        {
            detail = string.Empty;
            StoredRecordState roleState = ProductionRoleLinkCatalogStore.Read(
                stationZdo, role, out ProductionRoleLinkCatalog roleCatalog);
            if (roleState == StoredRecordState.Invalid)
                return Fail("The selected role's link catalog is unavailable.", out detail);
            bool roleCatalogWasAbsent = roleState == StoredRecordState.Absent;
            if (roleCatalogWasAbsent &&
                ProductionRoleLinkCatalogStore.ReadWithLegacy(
                    stationZdo, role, out roleCatalog) != StoredRecordState.Valid)
                return Fail("The selected role's legacy link is unavailable.", out detail);

            ProductionRoleLinkCatalog updatedRoleCatalog = null;
            bool hasRoleLink = roleCatalog.FindTarget(token, prefabHash) != null;
            if (hasRoleLink && !ProductionRoleLinkCatalogPolicy.TryRemove(
                        roleCatalog,
                        token,
                        prefabHash,
                        out updatedRoleCatalog,
                        out detail)) return false;

            if (role != ProductionLinkRole.Replenishment)
            {
                if (!hasRoleLink)
                    return Fail("That chest is not linked for the selected role.", out detail);
                if (!ProductionRoleLinkCatalogStore.Publish(
                        stationZdo, roleCatalog, updatedRoleCatalog))
                    return Fail("The link removal did not publish.", out detail);
                detail = DisplayRole(role) + " Link removed for " +
                         StationName(station.Component);
                return true;
            }

            // The authoritative role link is removed first. Every plan consumer checks exact
            // role-link ID and revision, so any stale signed body becomes inert immediately.
            string stationId = ValheimAccess.StableId(station.Component);
            StoredRecordState planState = MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                stationZdo, stationId, false,
                out MultiReplenishmentCatalog catalog,
                out detail);
            ReplenishmentDestinationRecord current = planState == StoredRecordState.Valid
                ? catalog.Destinations.FirstOrDefault(
                    value => SameTarget(value.Link, token, prefabHash))
                : null;
            if (!hasRoleLink && current == null)
                return Fail("That chest is not a Replenishment destination.", out detail);
            if (hasRoleLink || roleCatalogWasAbsent && current != null)
            {
                // Publishing even an explicit empty catalog tombstones legacy/plan-only state
                // so later migration cannot resurrect a successfully removed relation.
                ProductionRoleLinkCatalog authoritative = hasRoleLink
                    ? updatedRoleCatalog
                    : roleCatalog;
                if (!ProductionRoleLinkCatalogStore.Publish(
                        stationZdo, roleCatalog, authoritative))
                    return Fail(
                        "The Replenishment link removal did not publish.",
                        out detail);
            }
            if (current != null)
            {
                if (MultiReplenishmentCatalogPolicy.TryRemove(
                        catalog,
                        current.LinkId,
                        out MultiReplenishmentCatalog updated,
                        out _,
                        out ReplenishmentCatalogChangeCode change))
                {
                    MultiReplenishmentCatalogCommitCode commit =
                        MultiReplenishmentCatalogStore.TryCommit(
                            stationZdo, catalog, updated);
                    if (commit != MultiReplenishmentCatalogCommitCode.Committed &&
                        commit != MultiReplenishmentCatalogCommitCode.NoChange)
                        ProductionDiagnostics.Warning(
                            "An inert replenishment plan body could not be cleaned: " +
                            commit + ".");
                }
                else ProductionDiagnostics.Warning(
                    "An inert replenishment plan body could not be removed: " + change + ".");
            }
            detail = "Replenishment Link removed for " + StationName(station.Component);
            return true;
        }

        private static bool TryResolveSingleton(
            Component station,
            ProductionLinkRole role,
            out ProductionEndpoint endpoint,
            out string failure)
        {
            foreach (StoredProductionLink link in RoleLinks(station, role))
            {
                if (TryResolveEndpoint(
                        station, link, true, out endpoint, out failure)) return true;
            }
            endpoint = null;
            failure = DisplayRole(role) + " link is missing or unavailable.";
            return false;
        }

        private static IReadOnlyList<StoredProductionLink> RoleLinks(
            Component station,
            ProductionLinkRole role)
        {
            ZDO zdo = ValheimAccess.Zdo(station);
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(
                    zdo, role, out ProductionRoleLinkCatalog catalog) !=
                StoredRecordState.Valid || catalog == null)
                return Array.Empty<StoredProductionLink>();
            return catalog.Links;
        }

        private static IEnumerable<ProductionEndpoint> ResolveRoleEndpoints(
            Component station,
            ProductionLinkRole role)
        {
            foreach (StoredProductionLink link in RoleLinks(station, role))
                if (TryResolveEndpoint(
                        station, link, true,
                        out ProductionEndpoint endpoint, out _))
                    yield return endpoint;
        }

        private static bool IsActiveReplenishmentLink(
            Component station,
            StoredProductionLink link)
        {
            if (station == null || link == null) return false;
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(
                    ValheimAccess.Zdo(station),
                    ProductionLinkRole.Replenishment,
                    out ProductionRoleLinkCatalog links) != StoredRecordState.Valid)
                return false;
            StoredProductionLink authoritative = links?.FindTarget(
                link.TargetToken, link.TargetPrefabHash);
            return authoritative != null &&
                   authoritative.Revision == link.Revision &&
                   string.Equals(
                       authoritative.LinkId,
                       link.LinkId,
                       StringComparison.Ordinal);
        }

        private static IEnumerable<ProductionEndpoint> ResolveFuelEndpoints(Component station)
        {
            // New links keep fuel physically and semantically separate. Existing worlds used
            // ordinary Input links for fuel, so retain that path only until a Fuel Input link
            // has been designated for the station.
            IReadOnlyList<StoredProductionLink> fuelLinks = RoleLinks(
                station, ProductionLinkRole.Fuel);
            ProductionLinkRole sourceRole = fuelLinks.Count > 0
                ? ProductionLinkRole.Fuel
                : ProductionLinkRole.Input;
            return ResolveRoleEndpoints(station, sourceRole);
        }

        private static bool TryResolveEndpoint(
            Component station,
            StoredProductionLink link,
            bool singleton,
            out ProductionEndpoint endpoint,
            out string failure)
        {
            endpoint = null;
            failure = string.Empty;
            if (station == null || link == null ||
                !ValheimAccess.IsNativeOwner(station))
                return Fail("The station is not a native local owner.", out failure);

            if (!TryReadAuthorizedEndpoint(station, link, singleton,
                    out Container container, out ZDO targetZdo, out failure)) return false;
            if (!ProductionChestHandoff.TryAcquire(station, container, link.OwnerId))
                return Fail("Waiting for the linked chest's current owner to hand it over.", out failure);
            if (string.IsNullOrEmpty(link.TargetToken))
            {
                if (!ProductionEndpointIdentity.TryGetOrEnsureToken(
                        ValheimAccess.View(container), out string token) ||
                    !ProductionLinkStore.TryUpgradeResolvedTarget(
                        ValheimAccess.Zdo(station), link, targetZdo, token, targetZdo.GetPrefab(),
                        out StoredProductionLink upgraded))
                    return Fail("The legacy chest identity could not be upgraded.", out failure);
                link = upgraded;
            }
            if (!ValheimAccess.TrySynchronizeLocallyOwnedContainer(container, out Inventory inventory))
                return Fail("The exact chest is busy or not synchronized.", out failure);
            endpoint = new ProductionEndpoint
            {
                Link = link, Container = container, Zdo = targetZdo, Inventory = inventory
            };
            return true;
        }

        // Read-only access proof, shared by the requester and the current chest owner.
        // It never resolves authorization from a caller-supplied player name or presence.
        private static bool TryReadAuthorizedEndpoint(Component station, StoredProductionLink link,
            bool allowLegacy, out Container container, out ZDO targetZdo, out string failure)
        {
            container = null;
            targetZdo = null;
            failure = string.Empty;
            if (station == null || link == null || link.OwnerId == 0L) return false;
            ProductionIdentityStatus status = ProductionEndpointIdentity.ResolveStable(
                link, out container, out targetZdo,
                out string identityFailure);
            if (status == ProductionIdentityStatus.Missing && allowLegacy &&
                ProductionEndpointIdentity.TryResolveUniqueLegacyTarget(
                    link, out container, out targetZdo, out identityFailure))
                status = ProductionIdentityStatus.Ready;
            if (status != ProductionIdentityStatus.Ready || container == null ||
                targetZdo == null)
                return Fail(identityFailure, out failure);

            ZNetView targetView = ValheimAccess.View(container);
            ZDO stationZdo = ValheimAccess.Zdo(station);
            if (targetView == null || !targetView.IsValid() ||
                stationZdo == null)
                return Fail(
                    "The exact chest or station is unavailable.", out failure);
            Vector3 stationPosition = stationZdo.GetPosition();
            Vector3 currentPosition = targetZdo.GetPosition();
            float tolerance = Mathf.Clamp(
                ProductionConfig.MovedTargetTolerance?.Value ?? 0.75f,
                0.1f,
                3f);
            if (!ValheimAccess.IsFinite(stationPosition) ||
                !ValheimAccess.IsFinite(currentPosition) ||
                (currentPosition - link.ExpectedPosition).sqrMagnitude >
                tolerance * tolerance)
                return Fail("The linked chest moved beyond its saved tolerance.", out failure);
            if ((currentPosition - stationPosition).sqrMagnitude > LinkRange * LinkRange)
                return Fail("The linked chest is outside range.", out failure);
            if (ValheimAccess.Creator(station) != link.StationOwnerId ||
                ValheimAccess.Creator(container) != link.TargetOwnerId)
                return Fail("A linked piece's creator identity changed.", out failure);
            if (!ValheimAccess.ContainerAllows(container, link.OwnerId) ||
                !ValheimAccess.WardAllows(stationPosition, link.OwnerId) ||
                !ValheimAccess.WardAllows(currentPosition, link.OwnerId))
                return Fail("Current vanilla chest or ward access denies automation.", out failure);
            return NearbyIngredientContainerIndex.IsStaticNonWagon(container);
        }

        internal static Component FindStation(ZDOID id)
        {
            GameObject root = ZNetScene.instance?.FindInstance(id);
            if (root == null) return null;
            foreach (Component candidate in root.GetComponentsInChildren<Component>())
                if ((candidate is Smelter || candidate is CookingStation ||
                     candidate is CraftingStation || candidate is Fermenter || candidate is Fireplace) &&
                    ValheimAccess.Zdo(candidate)?.m_uid == id) return candidate;
            return null;
        }

        internal static bool AuthorizesChest(Component station, Container chest, long principal)
        {
            if (station == null || chest == null || principal == 0L ||
                !station.gameObject.activeInHierarchy || !chest.isActiveAndEnabled ||
                !NearbyIngredientContainerIndex.IsStaticNonWagon(chest)) return false;
            foreach (ProductionLinkRole role in new[] { ProductionLinkRole.Input,
                         ProductionLinkRole.Fuel, ProductionLinkRole.Output, ProductionLinkRole.Replenishment })
                foreach (StoredProductionLink link in RoleLinks(station, role))
                    if (link.OwnerId == principal &&
                        TryReadAuthorizedEndpoint(station, link, true, out Container target, out _, out _) &&
                        ReferenceEquals(target, chest)) return true;

            // Nearby recipe inputs use an existing input/replenishment link's principal;
            // they cannot invent an authorization or borrow an unrelated output/fuel link.
            if (!(ProductionConfig.RecipeNearbyIngredientsEnabled?.Value ?? false) ||
                !(station is CraftingStation || station is CookingStation || station is Fermenter)) return false;
            ZDO stationZdo = ValheimAccess.Zdo(station);
            ZDO chestZdo = ValheimAccess.Zdo(chest);
            if (stationZdo == null || chestZdo == null ||
                ProtectedDestinationIds(station).Contains(chestZdo.m_uid)) return false;
            bool authorized = false;
            foreach (ProductionLinkRole role in new[] { ProductionLinkRole.Input, ProductionLinkRole.Replenishment })
                foreach (StoredProductionLink link in RoleLinks(station, role))
                    if (link.OwnerId == principal && TryReadAuthorizedEndpoint(
                            station, link, true, out _, out _, out _)) authorized = true;
            Vector3 source = stationZdo.GetPosition();
            Vector3 targetPosition = chestZdo.GetPosition();
            return authorized && ValheimAccess.IsFinite(source) && ValheimAccess.IsFinite(targetPosition) &&
                (source - targetPosition).sqrMagnitude <= LinkRange * LinkRange &&
                ValheimAccess.ContainerAllows(chest, principal) &&
                ValheimAccess.WardAllows(source, principal) && ValheimAccess.WardAllows(targetPosition, principal);
        }

        private static bool TrySelectPlannedDestination(
            ProductionStationEntry station,
            ReplenishmentProducerKind kind,
            out ProductionDestination destination)
        {
            destination = null;
            string stationId = ValheimAccess.StableId(station.Component);
            ZDO zdo = ValheimAccess.Zdo(station.Component);
            if (string.IsNullOrEmpty(stationId) ||
                MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                    zdo, stationId, false,
                    out MultiReplenishmentCatalog catalog,
                    out _) != StoredRecordState.Valid ||
                catalog.Destinations.Count == 0) return false;
            foreach (ReplenishmentDestinationRecord record in
                     catalog.ActiveRoundRobinOrder())
            {
                ReplenishmentPlan plan = record.Plan;
                if (record.State != ReplenishmentDestinationState.Active ||
                    plan.AdapterKind != kind || plan.Targets.Count == 0 ||
                    !IsActiveReplenishmentLink(station.Component, record.Link)) continue;
                if (!TryResolveEndpoint(
                        station.Component, record.Link, false,
                        out ProductionEndpoint endpoint, out _)) continue;
                for (int offset = 0; offset < plan.Targets.Count; offset++)
                {
                int targetIndex = (plan.Cursor + offset) % plan.Targets.Count;
                ReplenishmentTargetAuthorization target = plan.Targets[targetIndex];
                int current = ExactStockInventoryMutation.CountExact(
                    endpoint.Inventory,
                    target.OutputPrefabId,
                    matchWorldLevel: false);
                int inFlight = CountInFlightOutput(
                    station,
                    kind,
                    target.OutputPrefabId);
                if (current <= 0 ||
                    inFlight > 0 ||
                    !ReplenishmentNeedsStock(target.OutputPrefabId, current, inFlight) ||
                    !TargetIsCurrent(
                        station, record, target, endpoint.Inventory)) continue;
                destination = new ProductionDestination
                {
                    Endpoint = endpoint,
                    Catalog = catalog,
                    Record = record,
                    Target = target,
                    TargetIndex = targetIndex
                };
                return true;
                }
            }
            return false;
        }

        private static bool TryResolveOutputDestination(
            ProductionStationEntry station,
            string outputPrefab,
            int outputAmount,
            ReplenishmentProducerKind kind,
            out ProductionDestination destination)
        {
            destination = null;
            ZDO zdo = ValheimAccess.Zdo(station.Component);
            string stationId = ValheimAccess.StableId(station.Component);
            StoredRecordState catalogState =
                MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                    zdo,
                    stationId,
                    false,
                    out MultiReplenishmentCatalog catalog,
                    out _);
            bool hasCatalog = catalogState == StoredRecordState.Valid &&
                              catalog.Destinations.Count > 0;
            if (catalogState == StoredRecordState.Invalid) return false;
            if (hasCatalog)
            {
                ProductionDestination belowReserve = null;
                ProductionDestination completionFallback = null;
                int largestDeficit = int.MinValue;
                foreach (ReplenishmentDestinationRecord record in
                         catalog.ActiveRoundRobinOrder())
                {
                    if (record.State != ReplenishmentDestinationState.Active ||
                        record.Plan.AdapterKind != kind ||
                        !IsActiveReplenishmentLink(station.Component, record.Link)) continue;
                    for (int index = 0; index < record.Plan.Targets.Count; index++)
                    {
                        ReplenishmentTargetAuthorization target = record.Plan.Targets[index];
                        if (!string.Equals(
                                target.OutputPrefabId,
                                outputPrefab,
                                StringComparison.Ordinal) ||
                            !TryResolveEndpoint(
                                station.Component, record.Link, false,
                                out ProductionEndpoint endpoint, out _)) continue;
                        int current = ExactStockInventoryMutation.CountExact(
                            endpoint.Inventory,
                            outputPrefab,
                            matchWorldLevel: false);
                        // This product is already physically complete at the station. Always
                        // honor its bound replenishment destination while the exemplar remains
                        // current; reserve checks belong to work admission, not completion.
                        if (current <= 0 || !TargetIsCurrent(
                                station, record, target, endpoint.Inventory)) continue;
                        var candidate = new ProductionDestination
                        {
                            Endpoint = endpoint,
                            Catalog = catalog,
                            Record = record,
                            Target = target,
                            TargetIndex = index
                        };
                        var completed = new StockOutputDefinition(
                            outputPrefab,
                            Math.Max(1, outputAmount),
                            1,
                            0,
                            target.AuthorizedPlayerId,
                            target.AuthorizedPlayerName);
                        if (!ExactStockInventoryMutation.TryPrepareDestination(
                                endpoint.Inventory, completed, out _, out _)) continue;
                        if (completionFallback == null) completionFallback = candidate;
                        if (_replenishmentReserves.TryGetTargetReserve(
                                outputPrefab, out int reserve) && current < reserve)
                        {
                            int deficit = reserve - current;
                            if (belowReserve == null || deficit > largestDeficit)
                            {
                                belowReserve = candidate;
                                largestDeficit = deficit;
                            }
                        }
                    }
                }
                // Timed work is admitted conservatively one unbound batch at a time. Route that
                // completed batch to the largest current deficit; if external changes satisfied
                // every reserve meanwhile, still finish into a valid matching exemplar chest.
                destination = belowReserve ?? completionFallback;
                if (destination != null) return true;
            }
            var throughput = new StockOutputDefinition(
                outputPrefab, Math.Max(1, outputAmount), 1, 0, 0L, string.Empty);
            foreach (ProductionEndpoint endpoint in ResolveRoleEndpoints(
                         station.Component, ProductionLinkRole.Output))
            {
                if (!ExactStockInventoryMutation.TryPrepareDestination(
                        endpoint.Inventory,
                        throughput,
                        out _, out _)) continue;
                destination = new ProductionDestination { Endpoint = endpoint };
                return true;
            }
            return false;
        }

        private static bool TargetIsCurrent(
            ProductionStationEntry station,
            ReplenishmentDestinationRecord record,
            ReplenishmentTargetAuthorization target,
            Inventory destinationInventory)
        {
            string stationId = ValheimAccess.StableId(station.Component);
            switch (station.Kind)
            {
                case ProductionStationKind.Cooking:
                {
                    var cooking = (CookingStation)station.Component;
                    return CookingStationCompatibility.TryValidate(
                               cooking, _cookingPolicy, true,
                               out string prefab, out _) &&
                           TimedCookingStockModel.TryDescribe(
                               cooking, prefab,
                               out TimedCookingStationDescriptor descriptor, out _) &&
                           TimedCookingPlanBuilder.TryValidate(
                               record.Plan, descriptor, record.Link,
                               stationId, out _) &&
                           TimedCookingPlanBuilder.TryResolveAuthorized(
                               descriptor, target, out _, out _);
                }
                case ProductionStationKind.Recipe:
                {
                    var recipe = (CraftingStation)station.Component;
                    return RecipeStationCompatibility.TryValidate(
                               recipe, _recipePolicy, true,
                               out string prefab, out _, out _, out _) &&
                           RecipeProducerResolver.TryResolveAuthorized(
                               recipe,
                               prefab,
                               target,
                               _cookingInputPolicy,
                               _fermenterInputPolicy,
                               out _, out _);
                }
                case ProductionStationKind.Fermenter:
                {
                    var fermenter = (Fermenter)station.Component;
                    return FermenterCompatibility.TryValidate(
                               fermenter, _fermenterPolicy, true,
                               out FermenterDescriptor descriptor, out _) &&
                           FermenterPlanBuilder.TryValidate(
                               record.Plan,
                               descriptor,
                               record.Link,
                               stationId,
                               destinationInventory,
                               out _);
                }
                default:
                    return false;
            }
        }

        private static int CountInFlightOutput(
            ProductionStationEntry station,
            ReplenishmentProducerKind kind,
            string outputPrefabId)
        {
            if (station?.Component == null ||
                !StockDomainValidation.IsExactPrefabId(outputPrefabId)) return 0;
            if (kind == ReplenishmentProducerKind.TimedCooking &&
                station.Component is CookingStation cooking &&
                CookingStationCompatibility.TryValidate(
                    cooking, _cookingPolicy, true, out string prefabId, out _) &&
                TimedCookingStockModel.TryDescribe(
                    cooking, prefabId,
                    out TimedCookingStationDescriptor descriptor, out _))
            {
                int count = 0;
                for (int slot = 0; slot < descriptor.SlotCount; slot++)
                {
                    ValheimAccess.GetCookingSlot(
                        cooking, slot, out string item, out _, out _);
                    if (string.IsNullOrEmpty(item)) continue;
                    if (string.Equals(item, outputPrefabId, StringComparison.Ordinal) ||
                        descriptor.TryFromInput(
                            item, out TimedCookingConversionDescriptor conversion) &&
                        string.Equals(
                            conversion.OutputPrefabId,
                            outputPrefabId,
                            StringComparison.Ordinal))
                        count++;
                }
                return count;
            }
            if (kind == ReplenishmentProducerKind.Fermenter &&
                station.Component is Fermenter fermenter &&
                FermenterCompatibility.TryValidate(
                    fermenter, _fermenterPolicy, true,
                    out FermenterDescriptor fermenterDescriptor, out _) &&
                fermenterDescriptor.TryFromInput(
                    ValheimAccess.FermenterContent(fermenter),
                    out FermenterConversionDescriptor fermenterConversion) &&
                string.Equals(
                    fermenterConversion.OutputPrefabId,
                    outputPrefabId,
                    StringComparison.Ordinal))
                return Math.Max(0, fermenterConversion.OutputAmount);
            return 0;
        }

        private static void AdvanceDestination(
            ZDO stationZdo,
            ProductionDestination destination)
        {
            if (stationZdo == null || destination == null ||
                !destination.IsReplenishment ||
                destination.Record?.Plan == null ||
                destination.Record.Plan.Targets.Count == 0) return;
            ReplenishmentPlan plan = destination.Record.Plan;
            ReplenishmentPlan advanced = plan.WithCursor(
                (destination.TargetIndex + 1) % plan.Targets.Count);
            if (!MultiReplenishmentCatalogPolicy.TryAdvanceAfterSelection(
                    destination.Catalog,
                    destination.Record.LinkId,
                    advanced,
                    out MultiReplenishmentCatalog updated,
                    out _, out _)) return;
            MultiReplenishmentCatalogCommitCode commit =
                MultiReplenishmentCatalogStore.TryCommit(
                    stationZdo, destination.Catalog, updated);
            if (commit != MultiReplenishmentCatalogCommitCode.Committed &&
                commit != MultiReplenishmentCatalogCommitCode.NoChange)
                ProductionDiagnostics.Warning(
                    "A completed batch could not advance replenishment fairness: " +
                    commit + ".");
        }

        private static bool ApplySourceToStation(
            Component station,
            ProductionEndpoint source,
            StockInventoryTransition transition,
            Action mutateStation,
            Action restoreStation,
            Func<bool> stationRestored,
            Func<bool> stationMatches)
        {
            if (!ValheimAccess.IsNativeOwner(station) ||
                !EndpointStillOwned(source) ||
                !transition.MatchesBefore(source.Inventory)) return false;
            try
            {
                transition.ApplyAfter(source.Inventory);
                if (ResolveEndpointMutation(source, transition) !=
                    ProductionEndpointMutationState.After)
                    throw new InvalidOperationException(
                        "source publication did not persist exactly");
                mutateStation();
                if (!ValheimAccess.IsNativeOwner(station) ||
                    !stationMatches() ||
                    ResolveEndpointMutation(source, transition) !=
                        ProductionEndpointMutationState.After)
                    throw new InvalidOperationException("station mutation did not match");
                return true;
            }
            catch
            {
                bool stationBefore = false;
                try
                {
                    restoreStation();
                    stationBefore = ValheimAccess.IsNativeOwner(station) &&
                                    stationRestored();
                }
                catch { stationBefore = false; }
                ProductionEndpointMutationState sourceState =
                    ResolveEndpointMutation(source, transition);
                if (sourceState == ProductionEndpointMutationState.After)
                {
                    try
                    {
                        transition.ApplyBefore(source.Inventory);
                    }
                    catch { }
                    sourceState = ResolveEndpointMutation(source, transition);
                }
                if (stationBefore && sourceState == ProductionEndpointMutationState.Before)
                    return false;
                PauseAfterIndeterminate(station, "source-to-station");
                return false;
            }
        }

        private static bool ApplyDestinationFromStation(
            Component station,
            ProductionEndpoint destination,
            StockInventoryTransition transition,
            Action mutateStation,
            Action restoreStation,
            Func<bool> stationRestored,
            Func<bool> stationMatches)
        {
            if (!ValheimAccess.IsNativeOwner(station) ||
                !EndpointStillOwned(destination) ||
                !transition.MatchesBefore(destination.Inventory)) return false;
            try
            {
                transition.ApplyAfter(destination.Inventory);
                if (ResolveEndpointMutation(destination, transition) !=
                    ProductionEndpointMutationState.After)
                    throw new InvalidOperationException(
                        "destination publication did not persist exactly");
                mutateStation();
                if (!ValheimAccess.IsNativeOwner(station) ||
                    !stationMatches() ||
                    ResolveEndpointMutation(destination, transition) !=
                        ProductionEndpointMutationState.After)
                    throw new InvalidOperationException("station mutation did not match");
                return true;
            }
            catch
            {
                bool stationBefore = false;
                try
                {
                    restoreStation();
                    stationBefore = ValheimAccess.IsNativeOwner(station) &&
                                    stationRestored();
                }
                catch { stationBefore = false; }
                ProductionEndpointMutationState destinationState =
                    ResolveEndpointMutation(destination, transition);
                if (destinationState == ProductionEndpointMutationState.After)
                {
                    try
                    {
                        transition.ApplyBefore(destination.Inventory);
                    }
                    catch { }
                    destinationState = ResolveEndpointMutation(destination, transition);
                }
                if (stationBefore &&
                    destinationState == ProductionEndpointMutationState.Before)
                    return false;
                PauseAfterIndeterminate(station, "station-to-destination");
                return false;
            }
        }

        private static bool ApplyCompositeSourcesToDestination(
            Component station,
            IReadOnlyList<ProductionEndpoint> sources,
            ExactStockCompositeSourcePlan sourcePlan,
            ProductionEndpoint destination,
            StockInventoryTransition destinationTransition)
        {
            if (!ValheimAccess.IsNativeOwner(station) || sources == null ||
                sourcePlan == null || sourcePlan.SourceCount != sources.Count ||
                !EndpointStillOwned(destination) ||
                !destinationTransition.MatchesBefore(destination.Inventory)) return false;
            foreach (ExactStockCompositeSourceEntry entry in sourcePlan.Entries)
            {
                if (entry.SourceIndex < 0 || entry.SourceIndex >= sources.Count) return false;
                ProductionEndpoint source = sources[entry.SourceIndex];
                if (source == null || ReferenceEquals(source.Container, destination.Container) ||
                    !EndpointStillOwned(source) ||
                    !entry.Transition.MatchesBefore(source.Inventory)) return false;
            }
            var applied = new List<ExactStockCompositeSourceEntry>();
            bool destinationApplied = false;
            try
            {
                foreach (ExactStockCompositeSourceEntry entry in sourcePlan.Entries)
                {
                    ProductionEndpoint source = sources[entry.SourceIndex];
                    // Register the attempt before publication. A callback may throw after it
                    // has already persisted the new state, and rollback must still include it.
                    applied.Add(entry);
                    entry.Transition.ApplyAfter(source.Inventory);
                    if (ResolveEndpointMutation(source, entry.Transition) !=
                        ProductionEndpointMutationState.After)
                        throw new InvalidOperationException("composite source changed");
                }
                if (!EndpointStillOwned(destination))
                    throw new InvalidOperationException("composite destination ownership changed");
                destinationApplied = true;
                destinationTransition.ApplyAfter(destination.Inventory);
                if (ResolveEndpointMutation(destination, destinationTransition) !=
                        ProductionEndpointMutationState.After ||
                    !applied.All(entry =>
                        ResolveEndpointMutation(
                            sources[entry.SourceIndex],
                            entry.Transition) == ProductionEndpointMutationState.After))
                    throw new InvalidOperationException("composite inventory readback failed");
                return true;
            }
            catch
            {
                bool rollbackProven = ValheimAccess.IsNativeOwner(station);
                if (destinationApplied)
                {
                    ProductionEndpointMutationState state =
                        ResolveEndpointMutation(destination, destinationTransition);
                    if (state == ProductionEndpointMutationState.After)
                    {
                        try { destinationTransition.ApplyBefore(destination.Inventory); }
                        catch { }
                        state = ResolveEndpointMutation(destination, destinationTransition);
                    }
                    rollbackProven &= state == ProductionEndpointMutationState.Before;
                }
                for (int index = applied.Count - 1; index >= 0; index--)
                {
                    ExactStockCompositeSourceEntry entry = applied[index];
                    ProductionEndpoint source = sources[entry.SourceIndex];
                    ProductionEndpointMutationState state =
                        ResolveEndpointMutation(source, entry.Transition);
                    if (state == ProductionEndpointMutationState.After)
                    {
                        try { entry.Transition.ApplyBefore(source.Inventory); }
                        catch { }
                        state = ResolveEndpointMutation(source, entry.Transition);
                    }
                    rollbackProven &= state == ProductionEndpointMutationState.Before;
                }
                if (rollbackProven) return false;
                PauseAfterIndeterminate(station, "composite-source-to-destination");
                return false;
            }
        }

        private static bool EndpointStillOwned(ProductionEndpoint endpoint)
        {
            if (endpoint?.Container == null || endpoint.Zdo == null) return false;
            ZNetView view = ValheimAccess.View(endpoint.Container);
            return view != null && view.IsValid() && view.IsOwner() &&
                   view.GetZDO() != null &&
                   view.GetZDO().m_uid == endpoint.Zdo.m_uid &&
                   ValheimAccess.ContainerWritable(endpoint.Container);
        }

        private static ProductionEndpointMutationState ResolveEndpointMutation(
            ProductionEndpoint endpoint,
            StockInventoryTransition transition)
        {
            try
            {
                if (endpoint?.Container == null || transition == null ||
                    !EndpointStillOwned(endpoint) ||
                    !ValheimAccess.TrySynchronizeLocallyOwnedContainer(
                        endpoint.Container, out Inventory persisted))
                    return ProductionEndpointMutationState.Indeterminate;
                endpoint.Inventory = persisted;
                if (transition.MatchesBefore(persisted))
                    return ProductionEndpointMutationState.Before;
                if (transition.MatchesAfter(persisted))
                    return ProductionEndpointMutationState.After;
                return ProductionEndpointMutationState.Indeterminate;
            }
            // Classification is a no-throw boundary. Any Valheim view, inventory, callback,
            // serialization, or fingerprint exception is uncertainty, never permission to retry.
            catch { return ProductionEndpointMutationState.Indeterminate; }
        }

        private static void PauseAfterIndeterminate(
            Component station,
            string operation)
        {
            if (station != null) SafetyPausedStations.Add(station.GetInstanceID());
            ProductionDiagnostics.Warning(
                "Production paused this station for the session after an indeterminate " +
                operation + " mutation. Reloading resolves state from Valheim's persisted inventory.");
            throw new ProductionMutationIndeterminateException(operation);
        }

        private static IEnumerable<string> ExactPrefabs(Inventory inventory)
        {
            if (inventory == null) return Enumerable.Empty<string>();
            return inventory.GetAllItems()
                .Where(item => item != null && item.m_stack > 0)
                .Select(ValheimAccess.PrefabName)
                .Where(StockDomainValidation.IsExactPrefabId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        private static bool CookingSlotIsEmpty(CookingStation station, int slot)
        {
            ValheimAccess.GetCookingSlot(
                station, slot, out string item, out float elapsed,
                out CookingSlotStatus status, out bool cheated);
            return string.IsNullOrEmpty(item) && elapsed == 0f &&
                   status == CookingSlotStatus.NotDone && !cheated;
        }

        private static bool CookingSlotMatches(
            CookingStation station,
            int slot,
            string item,
            CookingSlotStatus expected,
            bool expectedCheated = false)
        {
            return CookingSlotMatches(
                station, slot, item, 0f, expected, expectedCheated);
        }

        private static bool CookingSlotMatches(
            CookingStation station,
            int slot,
            string item,
            float expectedElapsed,
            CookingSlotStatus expected,
            bool expectedCheated = false)
        {
            ValheimAccess.GetCookingSlot(
                station, slot, out string current, out float elapsed,
                out CookingSlotStatus status, out bool cheated);
            return string.Equals(current, item, StringComparison.Ordinal) &&
                   elapsed == expectedElapsed && status == expected &&
                   cheated == expectedCheated;
        }

        private static bool FermenterStateMatches(
            Fermenter station,
            string expectedContent,
            long expectedStartTicks,
            bool expectedCheated = false)
        {
            return string.Equals(
                       ValheimAccess.FermenterContent(station),
                       expectedContent,
                       StringComparison.Ordinal) &&
                   ValheimAccess.FermenterStartTicks(station) == expectedStartTicks &&
                   ValheimAccess.FermenterCheated(station) == expectedCheated;
        }

        private static bool ValidateStation(
            ProductionStationEntry station,
            out string failure)
        {
            failure = string.Empty;
            switch (station.Kind)
            {
                case ProductionStationKind.Smelter:
                    return true;
                case ProductionStationKind.Cooking:
                    return CookingStationCompatibility.TryValidate(
                        (CookingStation)station.Component,
                        _cookingPolicy,
                        true,
                        out _,
                        out failure);
                case ProductionStationKind.Recipe:
                    return RecipeStationCompatibility.TryValidate(
                        (CraftingStation)station.Component,
                        _recipePolicy,
                        true,
                        out _, out _, out _, out failure);
                case ProductionStationKind.Fermenter:
                    return FermenterCompatibility.TryValidate(
                        (Fermenter)station.Component,
                        _fermenterPolicy,
                        true,
                        out _,
                        out failure);
                case ProductionStationKind.Fireplace:
                {
                    var fireplace = (Fireplace)station.Component;
                    if (fireplace != null && fireplace.m_canRefill && !fireplace.m_infiniteFuel &&
                        fireplace.m_fuelItem != null && fireplace.m_maxFuel > 0f &&
                        StockDomainValidation.IsExactPrefabId(
                            ValheimAccess.PrefabName(fireplace.m_fuelItem.gameObject))) return true;
                    return Fail("This fire or lamp has no supported native fuel slot.", out failure);
                }
                default:
                    return Fail("Unsupported production station.", out failure);
            }
        }

        private static bool TryClassifyStation(
            GameObject target,
            out ProductionStationEntry station)
        {
            station = null;
            Smelter smelter = target.GetComponentInParent<Smelter>();
            if (smelter != null)
            {
                station = new ProductionStationEntry(
                    ProductionStationKind.Smelter, smelter);
                return true;
            }
            CookingStation cooking = target.GetComponentInParent<CookingStation>();
            if (cooking != null)
            {
                station = new ProductionStationEntry(
                    ProductionStationKind.Cooking, cooking);
                return true;
            }
            Fermenter fermenter = target.GetComponentInParent<Fermenter>();
            if (fermenter != null)
            {
                station = new ProductionStationEntry(
                    ProductionStationKind.Fermenter, fermenter);
                return true;
            }
            Fireplace fireplace = target.GetComponentInParent<Fireplace>();
            if (fireplace != null)
            {
                station = new ProductionStationEntry(
                    ProductionStationKind.Fireplace, fireplace);
                return true;
            }
            CraftingStation recipe = target.GetComponentInParent<CraftingStation>();
            if (recipe != null)
            {
                station = new ProductionStationEntry(
                    ProductionStationKind.Recipe, recipe);
                return true;
            }
            return false;
        }

        private static bool StationSupportsRole(
            ProductionStationKind station,
            ProductionLinkRole role)
        {
            switch (station)
            {
                case ProductionStationKind.Smelter:
                    return role == ProductionLinkRole.Input ||
                           role == ProductionLinkRole.Fuel ||
                           role == ProductionLinkRole.Output;
                case ProductionStationKind.Cooking:
                    return role == ProductionLinkRole.Input ||
                           role == ProductionLinkRole.Fuel ||
                           role == ProductionLinkRole.Output ||
                           role == ProductionLinkRole.Replenishment;
                case ProductionStationKind.Recipe:
                    return role == ProductionLinkRole.Input ||
                           role == ProductionLinkRole.Output ||
                           role == ProductionLinkRole.Replenishment;
                case ProductionStationKind.Fermenter:
                    return role == ProductionLinkRole.Input ||
                           role == ProductionLinkRole.Output ||
                           role == ProductionLinkRole.Replenishment;
                case ProductionStationKind.Fireplace:
                    return role == ProductionLinkRole.Input ||
                           role == ProductionLinkRole.Fuel;
                default:
                    return false;
            }
        }

        private static ProductionLinkRole RefineInputRoleForTarget(
            ProductionStationEntry station,
            GameObject target,
            ProductionLinkRole requested)
        {
            if (requested != ProductionLinkRole.Input || station?.Component == null)
                return requested;
            if (station.Kind == ProductionStationKind.Fireplace)
                return ProductionLinkRole.Fuel;
            Switch fuelControl = null;
            if (station.Component is Smelter smelter)
                fuelControl = smelter.m_addWoodSwitch;
            else if (station.Component is CookingStation cooking && cooking.m_useFuel)
                fuelControl = cooking.m_addFuelSwitch;
            return IsTargetInsideControl(target, fuelControl)
                ? ProductionLinkRole.Fuel
                : ProductionLinkRole.Input;
        }

        private static bool IsTargetInsideControl(GameObject target, Switch control)
        {
            if (target == null || control == null || control.transform == null)
                return false;
            Transform targetTransform = target.transform;
            return targetTransform != null &&
                   (ReferenceEquals(targetTransform, control.transform) ||
                    targetTransform.IsChildOf(control.transform));
        }

        private static bool HasReplenishmentRecord(ZDO zdo, Component station)
        {
            string stationId = ValheimAccess.StableId(station);
            StoredRecordState state = MultiReplenishmentRuntimeSupport.ReadOrMigrate(
                zdo,
                stationId,
                false,
                out MultiReplenishmentCatalog catalog,
                out _);
            return state == StoredRecordState.Invalid ||
                   state == StoredRecordState.Valid &&
                   catalog.Destinations.Any(value =>
                       IsActiveReplenishmentLink(station, value.Link));
        }

        private static bool SameTarget(
            StoredProductionLink link,
            string token,
            int prefabHash) =>
            link != null && link.TargetPrefabHash == prefabHash &&
            ProductionEndpointIdentity.IsCanonicalToken(token) &&
            string.Equals(link.TargetToken, token, StringComparison.Ordinal);

        private static void RefreshLinkedHighlights(
            ProductionStationEntry station,
            float lifetimeSeconds)
        {
            if (station?.Component == null) return;
            ZDO zdo = ValheimAccess.Zdo(station.Component);
            if (zdo == null) return;

            foreach (StoredProductionLink link in RoleLinks(
                         station.Component, ProductionLinkRole.Input))
            {
                RefreshLinkedHighlight(link, ProductionLinkRole.Input, lifetimeSeconds);
            }
            foreach (StoredProductionLink link in RoleLinks(
                         station.Component, ProductionLinkRole.Fuel))
            {
                RefreshLinkedHighlight(link, ProductionLinkRole.Input, lifetimeSeconds);
            }
            foreach (StoredProductionLink link in RoleLinks(
                         station.Component, ProductionLinkRole.Output))
            {
                RefreshLinkedHighlight(link, ProductionLinkRole.Output, lifetimeSeconds);
            }
            foreach (StoredProductionLink link in RoleLinks(
                         station.Component, ProductionLinkRole.Replenishment))
            {
                RefreshLinkedHighlight(link, ProductionLinkRole.Replenishment, lifetimeSeconds);
            }

            // The role catalog (including its legacy singleton fallback) is authoritative.
            // Signed plan records absent from it are inert and intentionally not displayed.
        }

        private static string LinkTargetKey(StoredProductionLink link) =>
            link == null
                ? string.Empty
                : (link.TargetToken ?? string.Empty) + ":" + link.TargetPrefabHash;

        private static void RefreshLinkedHighlight(
            StoredProductionLink link,
            ProductionLinkRole role,
            float lifetimeSeconds)
        {
            if (link == null) return;
            ProductionIdentityStatus status = ProductionEndpointIdentity.ResolveStable(
                link,
                out Container container,
                out _,
                out _);
            if (status == ProductionIdentityStatus.Missing &&
                ProductionEndpointIdentity.TryResolveUniqueLegacyTarget(
                    link,
                    out container,
                    out _,
                    out _))
                status = ProductionIdentityStatus.Ready;
            if (status == ProductionIdentityStatus.Ready && container != null)
                ProductionLinkHighlight.Show(container, role, lifetimeSeconds);
        }

        private static void PruneSelections()
        {
            float now = Time.realtimeSinceStartup;
            long[] expired = Selections
                .Where(value => value.Value == null ||
                                value.Value.Station?.Component == null ||
                                value.Value.ExpiresAt < now)
                .Select(value => value.Key)
                .ToArray();
            foreach (long key in expired) Selections.Remove(key);
        }

        internal static void AppendHover(
            Component station,
            ProductionLinkRole role,
            ref string text)
        {
            if (!_initialized || station == null) return;
            if ((ProductionConfig.ShowStatusOverlay?.Value ?? true) &&
                ProductionDiagnostics.TryGetStop(
                    ValheimAccess.StableId(station),
                    out ProductionStopCode stopCode))
                text += "\nRunic Production: " + stopCode;
            ZDO zdo = ValheimAccess.Zdo(station);
            int inputCount = DistinctRoleCount(station, ProductionLinkRole.Input);
            int fuelCount = DistinctRoleCount(station, ProductionLinkRole.Fuel);
            int outputCount = DistinctRoleCount(station, ProductionLinkRole.Output);
            int replenishmentCount = ReplenishmentLinkCount(station, zdo);
            text += "\n<color=#FFD27A><b>Runic Production Links</b></color>" +
                    "\nInput: " + FormatLinkCount(inputCount) +
                    "\nFuel Input: " + FormatLinkCount(fuelCount) +
                    "\nOutput: " + FormatLinkCount(outputCount) +
                    "\nReplenishment: " + FormatLinkCount(replenishmentCount);
            if (!(ProductionConfig.ShowControlHints?.Value ?? true)) return;
            text += "\n[<color=yellow><b>Alt+Left Mouse</b></color>] Input" +
                    " (aim at the fuel control for Fuel Input)" +
                    "  [<color=yellow><b>Alt+Right Mouse</b></color>] Output" +
                    "  [<color=yellow><b>Alt+Middle Mouse</b></color>] Replenishment" +
                    "\nRepeat the same gesture on a chest within 30 seconds." +
                    " Add Shift at both steps to remove a link.";
        }

        private static int DistinctRoleCount(
            Component station,
            params ProductionLinkRole[] roles)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProductionLinkRole role in roles)
                foreach (StoredProductionLink link in RoleLinks(station, role))
                    targets.Add(LinkTargetKey(link));
            targets.Remove(string.Empty);
            return targets.Count;
        }

        private static int ReplenishmentLinkCount(Component station, ZDO zdo)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoredProductionLink link in RoleLinks(
                         station, ProductionLinkRole.Replenishment))
                targets.Add(LinkTargetKey(link));
            targets.Remove(string.Empty);
            return targets.Count;
        }

        private static string FormatLinkCount(int count) =>
            count <= 0 ? "empty" : count == 1 ? "1 chest" : count + " chests";

        private static string DisplayRole(ProductionLinkRole role) =>
            role == ProductionLinkRole.Fuel ? "Fuel Input" : role.ToString();

        private static string GestureName(
            ProductionLinkMouseButton button,
            bool remove)
        {
            string mouse = button == ProductionLinkMouseButton.Right
                ? "Right Mouse Button"
                : button == ProductionLinkMouseButton.Middle
                    ? "Middle Mouse Button"
                    : "Left Mouse Button";
            return (remove ? "Shift+Alt+" : "Alt+") + mouse;
        }

        private static string StationName(Component station)
        {
            if (station == null) return "station";
            string name = string.Empty;
            try
            {
                FieldInfo field = station.GetType().GetField(
                    "m_name",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                name = field?.GetValue(station) as string ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && Localization.instance != null)
                    name = Localization.instance.Localize(name);
            }
            catch { name = string.Empty; }
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            name = ValheimAccess.PrefabName(station.gameObject) ?? string.Empty;
            if (name.StartsWith("piece_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(6);
            name = name.Replace('_', ' ').Trim();
            return string.IsNullOrEmpty(name) ? "station" : name;
        }

        private static bool ReplenishmentNeedsStock(
            string prefabId,
            int currentCount,
            int inFlightCount)
        {
            return _replenishmentReserves != null &&
                   _replenishmentReserves.NeedsProduction(
                       prefabId,
                       currentCount,
                       Math.Max(0, inFlightCount),
                       out _);
        }

        private static ProductionStationKind KindOf(Component station)
        {
            if (station is Smelter) return ProductionStationKind.Smelter;
            if (station is CookingStation) return ProductionStationKind.Cooking;
            if (station is CraftingStation) return ProductionStationKind.Recipe;
            if (station is Fermenter) return ProductionStationKind.Fermenter;
            return ProductionStationKind.Fireplace;
        }

        internal static void FailHook(string hook, Exception exception) =>
            ProductionDiagnostics.Warning(
                "Production hook " + hook + " failed safely: " +
                exception.GetType().Name + ": " + exception.Message);

        internal static void FailStation(
            Component station,
            string operation,
            Exception exception)
        {
            string id = station == null ? string.Empty : ValheimAccess.StableId(station);
            ProductionDiagnostics.StopChanged(
                id,
                ProductionStopCode.InternalFailure,
                operation + ":" + exception.GetType().Name);
            ProductionDiagnostics.Warning(
                "Production station operation failed safely: " + operation + " " +
                exception.GetType().Name + ": " + exception.Message);
        }

        private static void Stop(
            Component station,
            ProductionStopCode code,
            string detail) =>
            ProductionDiagnostics.StopChanged(
                ValheimAccess.StableId(station), code, detail);

        private static bool ShiftHeld =>
            ValheimAccess.KeyboardKeyHeld(KeyCode.LeftShift) ||
            ValheimAccess.KeyboardKeyHeld(KeyCode.RightShift) ||
            ValheimAccess.RawControllerButtonHeld("JoyNextSnap");

        private static float LinkRange => Mathf.Clamp(
            ProductionConfig.MaximumLinkRange?.Value ?? 12f,
            2f,
            30f);

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
