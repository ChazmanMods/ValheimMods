using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RunicCrafting.Domain;

namespace RunicCrafting.Tests
{
    internal static class CraftingTests
    {
        internal static void UnchangedSpatialMembershipIsNotRebuilt()
        {
            TestAssert.True(
                SpatialMembershipPolicy.CanRetainExistingRegistration(true, true, true),
                "An exact container that remains in the same cell should not churn the index.");
            TestAssert.False(
                SpatialMembershipPolicy.CanRetainExistingRegistration(false, true, true),
                "An unregistered container must be inserted.");
            TestAssert.False(
                SpatialMembershipPolicy.CanRetainExistingRegistration(true, false, true),
                "A moved container must change cells.");
            TestAssert.False(
                SpatialMembershipPolicy.CanRetainExistingRegistration(true, true, false),
                "Instance-ID reuse or stale membership must be repaired.");
        }
        internal static void InventoryIsAlwaysAllocatedBeforeNearbyContainers()
        {
            var planner = new ExactMaterialPlanner();
            var sources = new[]
            {
                Snapshot("container:b", MaterialSourceKind.NearbyContainer, 1f, "wood", 10),
                Snapshot("player", MaterialSourceKind.PlayerInventory, 100f, "wood", 2),
                Snapshot("container:a", MaterialSourceKind.NearbyContainer, 4f, "wood", 10)
            };

            TestAssert.True(planner.TryPlan(
                new[] { new MaterialRequirement("wood", 5) },
                sources,
                out MaterialPlan plan,
                out _));
            TestAssert.Equal("player", plan.Lines[0].SourceId);
            TestAssert.Equal(2, plan.Lines[0].Quantity);
            TestAssert.Equal("container:b", plan.Lines[1].SourceId);
            TestAssert.Equal(3, plan.Lines[1].Quantity);
        }

        internal static void LastResourceRaceHasOneWinner()
        {
            var engine = new ExactMaterialTransactionEngine();
            var source = Fake("container", MaterialSourceKind.NearbyContainer, "iron", 1);
            var start = new ManualResetEventSlim(false);
            int successes = 0;
            Task[] attempts = Enumerable.Range(0, 2).Select(attemptIndex => Task.Run(() =>
            {
                start.Wait();
                if (!engine.TryBegin(
                        new[] { new MaterialRequirement("iron", 1) },
                        new IMutableMaterialSource[] { source },
                        out MaterialConsumptionLease lease,
                        out _)) return;
                Interlocked.Increment(ref successes);
                lease.Commit();
            })).ToArray();
            start.Set();
            Task.WaitAll(attempts);

            TestAssert.Equal(1, successes);
            TestAssert.Equal(0, source.Quantity("iron"));
        }

        internal static void StationAndMaterialPermissionsAreIndependent()
        {
            var profile = new WorkshopAccessProfile(
                "owner",
                WorkshopPolicyKind.Everyone,
                WorkshopPolicyKind.Approved,
                approvedIds: Array.Empty<string>());
            var context = new WorkshopAccessContext(false, false, false);
            var evaluator = new WorkshopAccessEvaluator();

            WorkshopAccessDecision station = evaluator.Evaluate(
                profile,
                WorkshopAction.StationUse,
                "visitor",
                context);
            WorkshopAccessDecision materials = evaluator.Evaluate(
                profile,
                WorkshopAction.LocalMaterialUse,
                "visitor",
                context);

            TestAssert.True(station.Allowed, "Public station use should remain available.");
            TestAssert.False(materials.Allowed, "Public station use must not grant local material consumption.");
        }

        internal static void FailedCraftRestoresEveryEarlierRemoval()
        {
            var engine = new ExactMaterialTransactionEngine();
            var player = Fake("player", MaterialSourceKind.PlayerInventory, "wood", 1);
            var container = Fake("container", MaterialSourceKind.NearbyContainer, "wood", 1);
            container.FailNextTake = true;

            bool began = engine.TryBegin(
                new[] { new MaterialRequirement("wood", 2) },
                new IMutableMaterialSource[] { container, player },
                out MaterialConsumptionLease lease,
                out _);

            TestAssert.False(began);
            TestAssert.True(lease == null);
            TestAssert.Equal(1, player.Quantity("wood"));
            TestAssert.Equal(1, container.Quantity("wood"));
        }

        internal static void ExactCostIsConsumedOnceAcrossSources()
        {
            var engine = new ExactMaterialTransactionEngine();
            var player = Fake("player", MaterialSourceKind.PlayerInventory, "stone", 2);
            var container = Fake("container", MaterialSourceKind.NearbyContainer, "stone", 5);

            TestAssert.True(engine.TryBegin(
                new[] { new MaterialRequirement("stone", 4) },
                new IMutableMaterialSource[] { container, player },
                out MaterialConsumptionLease lease,
                out _));
            lease.Commit();
            lease.Commit();

            TestAssert.Equal(0, player.Quantity("stone"));
            TestAssert.Equal(3, container.Quantity("stone"));
            TestAssert.Equal(4, 2 + (5 - container.Quantity("stone")));
        }

        internal static void DlcPrerequisiteFailsClosed()
        {
            TestAssert.True(DlcEligibility.IsAllowed(string.Empty, false, false),
                "A piece without a DLC requirement remains eligible.");
            TestAssert.False(DlcEligibility.IsAllowed("paid-dlc", false, false),
                "A missing DLC manager cannot authorize an entitlement.");
            TestAssert.False(DlcEligibility.IsAllowed("paid-dlc", true, false),
                "An uninstalled DLC cannot be bypassed by nearby materials.");
            TestAssert.True(DlcEligibility.IsAllowed("paid-dlc", true, true),
                "An installed DLC remains eligible for the material check.");
        }

        internal static void DisabledNearbyCraftingHasAnExplicitStatus()
        {
            NearbyCraftingFeatureState masterOff = EvaluateState(
                masterEnabled: false,
                craftFromContainersEnabled: false);
            TestAssert.False(masterOff.IsReady);
            TestAssert.Equal("mod-disabled", masterOff.ReasonCode);
            TestAssert.Equal("N:OFF(mod)", masterOff.DisplayLabel);

            NearbyCraftingFeatureState featureOff = EvaluateState(
                masterEnabled: true,
                craftFromContainersEnabled: false);
            TestAssert.False(featureOff.IsReady);
            TestAssert.Equal("craft-from-containers-disabled", featureOff.ReasonCode);
            TestAssert.Equal("N:OFF(config)", featureOff.DisplayLabel);
        }

        internal static void RuntimePrerequisitesReportTheFirstActionableGate()
        {
            NearbyCraftingFeatureState remote = EvaluateState(localPlayerOwner: false);
            TestAssert.Equal("local-player-owner-required", remote.ReasonCode);
            TestAssert.Equal("N:OFF(owner)", remote.DisplayLabel);

            NearbyCraftingFeatureState noCost = EvaluateState(noCostMode: true);
            TestAssert.Equal("no-cost-mode", noCost.ReasonCode);
            TestAssert.Equal("N:OFF(no-cost)", noCost.DisplayLabel);

            NearbyCraftingFeatureState noStation = EvaluateState(stationPresent: false);
            TestAssert.Equal("crafting-station-required", noStation.ReasonCode);
            TestAssert.Equal("N:OFF(station)", noStation.DisplayLabel);
        }

        internal static void WorkshopDenialsRetainTheirExactReason()
        {
            NearbyCraftingFeatureState stationDenied = EvaluateState(
                stationUseAllowed: false,
                stationDenialReason: "ward-denied");
            TestAssert.Equal("station-use-denied:ward-denied", stationDenied.ReasonCode);
            TestAssert.Equal("N:OFF(access)", stationDenied.DisplayLabel);

            NearbyCraftingFeatureState materialsDenied = EvaluateState(
                localMaterialsAllowed: false,
                localMaterialsDenialReason: "not-approved");
            TestAssert.Equal("local-material-use-denied:not-approved", materialsDenied.ReasonCode);
            TestAssert.Equal("N:OFF(access)", materialsDenied.DisplayLabel);
        }

        internal static void RequirementRowsShowCountsOrTheBlockingState()
        {
            NearbyCraftingFeatureState ready = EvaluateState();
            TestAssert.True(ready.IsReady);
            TestAssert.Equal(
                "5\nC:2 N:4 T:6 M:0",
                CraftingRequirementDisplay.Format(5, 2, 4, ready));

            NearbyCraftingFeatureState disabled = EvaluateState(craftFromContainersEnabled: false);
            TestAssert.Equal(
                "5\nC:2 N:OFF(config) M:3",
                CraftingRequirementDisplay.Format(5, 2, 0, disabled));
        }

        internal static void CompactRequirementAmountUsesCombinedAvailability()
        {
            NearbyCraftingFeatureState ready = EvaluateState();
            TestAssert.Equal(
                "6/5",
                CraftingRequirementDisplay.FormatCompactAmount(5, 2, 4, ready));
            TestAssert.Equal(
                "Runic materials — Carried: 2 | Nearby: 4 | Total: 6 | Required: 5 | Missing: 0",
                CraftingRequirementDisplay.FormatTooltipBreakdown(5, 2, 4, ready));

            NearbyCraftingFeatureState disabled = EvaluateState(craftFromContainersEnabled: false);
            TestAssert.Equal(
                "2/5",
                CraftingRequirementDisplay.FormatCompactAmount(5, 2, 99, disabled));
            TestAssert.Equal(
                "Runic materials — Carried: 2 | N:OFF(config) | Required: 5 | Missing: 3",
                CraftingRequirementDisplay.FormatTooltipBreakdown(5, 2, 99, disabled));
        }

        internal static void NearbySufficiencyStopsTheVanillaShortagePulse()
        {
            NearbyCraftingFeatureState ready = EvaluateState();
            TestAssert.True(CraftingRequirementDisplay.CombinedTotalSatisfies(5, 0, 5, ready));
            TestAssert.False(CraftingRequirementDisplay.CombinedTotalSatisfies(5, 0, 4, ready));

            NearbyCraftingFeatureState disabled = EvaluateState(craftFromContainersEnabled: false);
            TestAssert.False(CraftingRequirementDisplay.CombinedTotalSatisfies(5, 0, 50, disabled));
        }

        internal static void PlacementLifecycleCommitsOnlyAfterVanillaProgress()
        {
            TestAssert.Equal(
                PlacementLeaseDisposition.Rollback,
                PlacementLeasePolicy.Resolve(outputCreated: false, vanillaConsumeReached: false));
            TestAssert.Equal(
                PlacementLeaseDisposition.Commit,
                PlacementLeasePolicy.Resolve(outputCreated: true, vanillaConsumeReached: false));
            TestAssert.Equal(
                PlacementLeaseDisposition.Commit,
                PlacementLeasePolicy.Resolve(outputCreated: false, vanillaConsumeReached: true));
        }

        internal static void SmelterPlacementConsumesStoneAndCoresExactlyOnce()
        {
            var engine = new ExactMaterialTransactionEngine();
            var player = new FakeMaterialSource(
                "player",
                MaterialSourceKind.PlayerInventory,
                0f,
                new Dictionary<string, int>
                {
                    ["Stone"] = 2,
                    ["SurtlingCore"] = 5
                });
            var chest = new FakeMaterialSource(
                "station-chest",
                MaterialSourceKind.NearbyContainer,
                1f,
                new Dictionary<string, int>
                {
                    ["Stone"] = 25
                });

            TestAssert.True(engine.TryBegin(
                SmelterRequirements(),
                new IMutableMaterialSource[] { chest, player },
                out MaterialConsumptionLease lease,
                out _));
            TestAssert.Equal(
                PlacementLeaseDisposition.Commit,
                PlacementLeasePolicy.Resolve(outputCreated: true, vanillaConsumeReached: false));
            lease.Commit();
            lease.Commit();

            TestAssert.Equal(0, player.Quantity("Stone"));
            TestAssert.Equal(0, player.Quantity("SurtlingCore"));
            TestAssert.Equal(7, chest.Quantity("Stone"));
        }

        internal static void RejectedSmelterPlacementRestoresStoneAndCores()
        {
            var engine = new ExactMaterialTransactionEngine();
            var player = new FakeMaterialSource(
                "player",
                MaterialSourceKind.PlayerInventory,
                0f,
                new Dictionary<string, int>
                {
                    ["Stone"] = 2,
                    ["SurtlingCore"] = 5
                });
            var chest = new FakeMaterialSource(
                "station-chest",
                MaterialSourceKind.NearbyContainer,
                1f,
                new Dictionary<string, int>
                {
                    ["Stone"] = 25
                });

            TestAssert.True(engine.TryBegin(
                SmelterRequirements(),
                new IMutableMaterialSource[] { chest, player },
                out MaterialConsumptionLease lease,
                out _));
            TestAssert.Equal(
                PlacementLeaseDisposition.Rollback,
                PlacementLeasePolicy.Resolve(outputCreated: false, vanillaConsumeReached: false));
            TestAssert.True(lease.Rollback());

            TestAssert.Equal(2, player.Quantity("Stone"));
            TestAssert.Equal(5, player.Quantity("SurtlingCore"));
            TestAssert.Equal(25, chest.Quantity("Stone"));
        }

        internal static void VanillaStationlessDefaultsCoverRequestedBuildables()
        {
            StationlessPiecePolicy policy = StationlessPiecePolicy.VanillaDefaults();

            TestAssert.Equal(42, policy.AllowedRuleCount,
                "The audited non-Feast vanilla stationless default set changed unexpectedly.");
            TestAssert.True(policy.Evaluate("fire_pit").Allowed);
            TestAssert.True(policy.Evaluate("bonfire").Allowed);
            TestAssert.True(policy.Evaluate("piece_cookingstation").Allowed);
            TestAssert.True(policy.Evaluate("piece_workbench").Allowed);
            TestAssert.True(policy.Evaluate("wood_stack").Allowed);
            TestAssert.False(policy.Evaluate("modded_magic_station").Allowed,
                "Unknown modded stationless pieces must remain opt-in.");
            TestAssert.False(policy.Evaluate("FeastMeadows").Allowed,
                "Special feast placement remains opt-in until its lifecycle is explicitly supported.");
        }

        internal static void StationlessPrefabRulesAreExactAndDenyWins()
        {
            StationlessPiecePolicy policy = StationlessPiecePolicy.Parse(
                "fire_pit; ModdedKitchen",
                "FIRE_PIT");

            StationlessPieceDecision denied = policy.Evaluate("fire_pit(Clone)");
            TestAssert.False(denied.Allowed);
            TestAssert.Equal("stationless-prefab-denied:fire_pit", denied.ReasonCode);
            TestAssert.True(policy.Evaluate("moddedkitchen").Allowed,
                "Exact prefab rules should be case-insensitive for configuration usability.");
            TestAssert.False(policy.Evaluate("moddedkitchen_extension").Allowed,
                "Rules must not grant prefix or substring matches.");
        }

        internal static void StationlessWildcardRequiresExplicitOptIn()
        {
            TestAssert.False(StationlessPiecePolicy.Parse(string.Empty, string.Empty)
                .Evaluate("unknown_piece").Allowed);
            TestAssert.True(StationlessPiecePolicy.Parse("*", string.Empty)
                .Evaluate("unknown_piece").Allowed);
            TestAssert.False(StationlessPiecePolicy.Parse("*", "*")
                .Evaluate("unknown_piece").Allowed,
                "The deny list must override an allow wildcard.");
            TestAssert.False(StationlessPiecePolicy.Parse(new string('x', 16385), string.Empty)
                .Evaluate("x").Allowed,
                "Oversized configuration text must fail closed.");
        }

        internal static void InvalidStationlessDenyRulesDenyEverything()
        {
            StationlessPiecePolicy oversized = StationlessPiecePolicy.Parse(
                "fire_pit",
                new string('x', 16385));
            TestAssert.False(oversized.Evaluate("fire_pit").Allowed,
                "An oversized deny list must deny all instead of erasing its safety boundary.");

            string tooManyRules = string.Join(",", Enumerable.Range(0, 257)
                .Select(index => "piece_" + index));
            StationlessPiecePolicy overCapacity = StationlessPiecePolicy.Parse(
                "fire_pit",
                tooManyRules);
            TestAssert.False(overCapacity.Evaluate("fire_pit").Allowed,
                "A deny list beyond the bounded rule count must deny all instead of truncating open.");

            StationlessPiecePolicy malformed = StationlessPiecePolicy.Parse(
                "fire_pit",
                new string('z', 129));
            TestAssert.False(malformed.Evaluate("fire_pit").Allowed,
                "A malformed deny entry must fail closed.");
        }

        internal static void StationlessFirePlacementConsumesExactMaterialsOnce()
        {
            var engine = new ExactMaterialTransactionEngine();
            var player = new FakeMaterialSource(
                "player",
                MaterialSourceKind.PlayerInventory,
                0f,
                new Dictionary<string, int> { ["Wood"] = 1, ["Stone"] = 0 });
            var chest = new FakeMaterialSource(
                "player-local-chest",
                MaterialSourceKind.NearbyContainer,
                1f,
                new Dictionary<string, int> { ["Wood"] = 1, ["Stone"] = 5 });
            var requirements = new[]
            {
                new MaterialRequirement("Wood", 2),
                new MaterialRequirement("Stone", 5)
            };

            TestAssert.True(engine.TryBegin(
                requirements,
                new IMutableMaterialSource[] { chest, player },
                out MaterialConsumptionLease lease,
                out _));
            lease.Commit();
            lease.Commit();

            TestAssert.Equal(0, player.Quantity("Wood"));
            TestAssert.Equal(0, chest.Quantity("Wood"));
            TestAssert.Equal(0, chest.Quantity("Stone"));
        }

        internal static void RejectedStationlessPlacementRestoresEverySource()
        {
            var engine = new ExactMaterialTransactionEngine();
            FakeMaterialSource player = Fake("player", MaterialSourceKind.PlayerInventory, "Wood", 1);
            FakeMaterialSource chest = Fake("player-local-chest", MaterialSourceKind.NearbyContainer, "Wood", 1);

            TestAssert.True(engine.TryBegin(
                new[] { new MaterialRequirement("Wood", 2) },
                new IMutableMaterialSource[] { chest, player },
                out MaterialConsumptionLease lease,
                out _));
            TestAssert.True(lease.Rollback());

            TestAssert.Equal(1, player.Quantity("Wood"));
            TestAssert.Equal(1, chest.Quantity("Wood"));
        }

        internal static void MissingRequiredStationNeverFallsBackToPlayerLocalScope()
        {
            TestAssert.Equal(
                BuildMaterialQueryScope.RequiredStationUnavailable,
                BuildMaterialQueryScopePolicy.Resolve(
                    pieceRequiresCraftingStation: true,
                    requiredStationResolved: false));
            TestAssert.Equal(
                BuildMaterialQueryScope.StationLocal,
                BuildMaterialQueryScopePolicy.Resolve(
                    pieceRequiresCraftingStation: true,
                    requiredStationResolved: true));
            TestAssert.Equal(
                BuildMaterialQueryScope.PlayerLocalStationless,
                BuildMaterialQueryScopePolicy.Resolve(
                    pieceRequiresCraftingStation: false,
                    requiredStationResolved: false));
        }

        private static MaterialRequirement[] SmelterRequirements() => new[]
        {
            new MaterialRequirement("Stone", 20),
            new MaterialRequirement("SurtlingCore", 5)
        };

        private static NearbyCraftingFeatureState EvaluateState(
            bool masterEnabled = true,
            bool craftFromContainersEnabled = true,
            bool runtimeAvailable = true,
            bool localPlayerOwner = true,
            bool recipeAvailable = true,
            bool specialOneIngredientRecipe = false,
            bool noCostMode = false,
            bool stationPresent = true,
            bool stationUseAllowed = true,
            string stationDenialReason = "allowed",
            bool localMaterialsAllowed = true,
            string localMaterialsDenialReason = "allowed") =>
            NearbyCraftingFeatureStateEvaluator.Evaluate(
                masterEnabled,
                craftFromContainersEnabled,
                runtimeAvailable,
                localPlayerOwner,
                recipeAvailable,
                specialOneIngredientRecipe,
                noCostMode,
                stationPresent,
                stationUseAllowed,
                stationDenialReason,
                localMaterialsAllowed,
                localMaterialsDenialReason);

        private static MaterialSourceSnapshot Snapshot(
            string id,
            MaterialSourceKind kind,
            float distance,
            string resource,
            int quantity) =>
            new MaterialSourceSnapshot(id, kind, distance, new Dictionary<string, int> { [resource] = quantity });

        private static FakeMaterialSource Fake(
            string id,
            MaterialSourceKind kind,
            string resource,
            int quantity) =>
            new FakeMaterialSource(id, kind, 1f, new Dictionary<string, int> { [resource] = quantity });

    }
}
