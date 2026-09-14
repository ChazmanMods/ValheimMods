using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using RunicAgriculture.Core;
using RunicAgriculture.Integration;

namespace RunicAgriculture.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("row pattern is centered and fills right-to-left", RowPatternIsCentered);
            Run("configured Grid remains complete through 40x40", BasicGridScalesToSeeds);
            Run("legacy shape migration selects the obvious Grid default once", GridIsTheMigratedDefault);
            Run("all patterns are deterministic and bounded", PatternsAreDeterministicAndBounded);
            Run("mirroring preserves the pattern footprint", MirroringPreservesFootprint);
            Run("pattern requests enforce hard bounds", PatternRequestsEnforceBounds);
            Run("limited resources fill shapes center-out and grids right-to-left", LimitedResourcesUseRequestedFillOrder);
            Run("multi-resource availability combines inventory sources", CombinedResourcesBudgetExactly);
            Run("cultivator wheel routing rotates and edits without overlap", WheelRoutingIsContextual);
            Run("native build hints replace the custom panel and explain invalid previews", NativeHintsExplainPreview);
            Run("placement validation keeps stable denial order", PlacementValidationIsStable);
            Run("preview validity is not masked by stamina or durability", PreviewIgnoresActionBudget);
            Run("invalid early grid cells backfill from later valid cells", PreviewBackfillsAfterInvalidCells);
            Run("batch planning skips invalid positions and truncates costs", BatchPlannerTruncates);
            Run("batch planning can block invalid or unaffordable batches", BatchPlannerBlocks);
            Run("replant offers are bounded and crop exact", ReplantOffersAreExact);
            Run("harvest accepts only ready Pickable objects", HarvestPolicyRequiresReadyPickable);
            Run("area harvest is independent while replant stays contextual", AreaHarvestAndReplantAreSeparated);
            Run("harvest matches exact prefabs and prioritizes the aimed object", HarvestPolicyIsExact);
            Run("planting uses native owner-local placement", PlantingUsesNativePlacement);
            Run("Valheim 1.0 placement and inventory notification signatures are exact",
                ValheimMutationSignaturesAreExact);
            Run("free-build and no-cost modes do not charge seeds", FreeBuildDoesNotChargeSeeds);
            Run("nearby seed chests are bounded, authorized, and owner-local", NearbySeedChestsAreSafe);
            Run("red preview is reserved for resource shortage", PreviewColorsAreUnambiguous);
            Run("harvest uses native Pickable interaction and ward access", HarvestUsesNativeInteraction);
            Run("mutation exclusion is local and always releasable", MutationExclusionIsLocal);
            Run("gameplay assembly has no Foundation references", AssemblyHasNoFoundationReferences);
            Run("project and manifest have no Foundation dependencies", PackagingIsIndependent);
            Run("durable and authority runtimes were removed", DurableArchitectureIsAbsent);
            Run("candidate version is 1.0.3", VersionIsUnchanged);

            System.Console.WriteLine(
                $"RunicAgriculture focused tests: {_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                System.Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                System.Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
            }
        }

        private static void RowPatternIsCentered()
        {
            IReadOnlyList<PlanarPoint> points = new AgriculturePatternService().Generate(
                new PatternRequest(PlantPattern.Row, 1, 5, 2d, 5));
            TestAssert.Equal(5, points.Count, "A five-column row must contain five points.");
            for (int index = 0; index < points.Count; index++)
            {
                TestAssert.Near(4d - index * 2d, points[index].Right, 1e-9,
                    "Row positions must fill from the player's right toward the left.");
                TestAssert.Near(0d, points[index].Forward, 1e-9,
                    "A row must remain on its forward axis.");
            }
        }

        private static void BasicGridScalesToSeeds()
        {
            var service = new AgriculturePatternService();
            IReadOnlyList<PlanarPoint> one = service.Generate(
                new PatternRequest(PlantPattern.Grid, 1, 1, 1.5d, 1));
            TestAssert.Equal(1, one.Count, "A 1x1 Grid must contain exactly one seed cell.");
            TestAssert.Near(0d, one[0].Right, 1e-9, "The 1x1 Grid is not centered.");
            TestAssert.Near(0d, one[0].Forward, 1e-9, "The 1x1 Grid is not centered.");

            IReadOnlyList<PlanarPoint> ten = service.Generate(
                new PatternRequest(PlantPattern.Grid, 10, 10, 1.5d, 100));
            TestAssert.Equal(100, ten.Count, "A 10x10 Grid must expose all 100 cells.");

            IReadOnlyList<PlanarPoint> twenty = service.Generate(
                new PatternRequest(PlantPattern.Grid, 20, 20, 1.5d, 400));
            TestAssert.Equal(400, twenty.Count, "A 20x20 Grid must remain complete.");
            IReadOnlyList<PlanarPoint> forty = service.Generate(
                new PatternRequest(PlantPattern.Grid, 40, 40, 1.5d,
                    PatternRequest.AbsoluteMaximumPoints));
            TestAssert.Equal(1600, forty.Count, "A valid 40x40 Grid was silently truncated.");
            TestAssert.Equal(1600, PlantingGridPolicy.DefaultPreviewLimit,
                "The hard preview safety boundary no longer supports 40x40.");

            var validations = new PlacementValidationResult[twenty.Count];
            for (int index = 0; index < validations.Length; index++) validations[index] = Valid();
            int selected = PlantingGridPolicy.SelectableSeedCount(97, validations.Length, false);
            BatchPlan seedClamped = BatchPlanner.PlanPreview(validations, selected);
            TestAssert.Equal(400, seedClamped.Decisions.Count,
                "The 20x20 preview was truncated before ground validation.");
            TestAssert.Equal(97, seedClamped.SuccessfulCount,
                "A 20x20 Grid did not select every one of the 97 available planting actions.");
            TestAssert.Equal(AgricultureReasonCodes.NoSeeds, seedClamped.Decisions[97].ReasonCode,
                "Ground-valid cells outside the seed budget are not identified distinctly.");
        }

        private static void GridIsTheMigratedDefault()
        {
            TestAssert.Equal(PlantPattern.Grid, PlantingGridPolicy.DefaultPattern,
                "The basic rectangular Grid is not the default pattern.");
            TestAssert.Equal(PlantPattern.Grid,
                PlantingGridPolicy.MigrateToDefaultGrid(false, PlantPattern.HalfCircle),
                "The one-time migration left an opaque shaped pattern active.");
            TestAssert.Equal(PlantPattern.HalfCircle,
                PlantingGridPolicy.MigrateToDefaultGrid(true, PlantPattern.HalfCircle),
                "The migration overwrote a later deliberate pattern selection.");

            PatternEditState initial = new PatternEditState(1, 1, false, 0.5d, 0.5d);
            PatternEditState rows = PatternEditor.Apply(
                PlantPattern.Grid, initial, PatternEditAction.IncreaseRows);
            PatternEditState columns = PatternEditor.Apply(
                PlantPattern.Grid, initial, PatternEditAction.IncreaseColumns);
            TestAssert.Equal(2, rows.Rows, "Grid rows are not independently incrementable.");
            TestAssert.Equal(1, rows.Columns, "Changing Grid rows changed its columns.");
            TestAssert.Equal(1, columns.Rows, "Changing Grid columns changed its rows.");
            TestAssert.Equal(2, columns.Columns, "Grid columns are not independently incrementable.");
        }

        private static void PatternsAreDeterministicAndBounded()
        {
            var service = new AgriculturePatternService();
            foreach (PlantPattern pattern in Enum.GetValues(typeof(PlantPattern)))
            {
                var request = new PatternRequest(pattern, 50, 50, 1.75d, 50, false, 0.2d, 0.8d);
                IReadOnlyList<PlanarPoint> first = service.Generate(request);
                IReadOnlyList<PlanarPoint> second = service.Generate(request);
                TestAssert.True(first.Count > 0 && first.Count <= PatternRequest.AbsoluteMaximumPoints,
                    pattern + " must remain non-empty and bounded.");
                TestAssert.Equal(first.Count, second.Count, pattern + " count changed between runs.");
                for (int index = 0; index < first.Count; index++)
                    TestAssert.True(first[index].Equals(second[index]),
                        pattern + " output changed between identical requests.");
            }
        }

        private static void MirroringPreservesFootprint()
        {
            var service = new AgriculturePatternService();
            IReadOnlyList<PlanarPoint> normal = service.Generate(new PatternRequest(
                PlantPattern.Trapezoid, 9, 11, 1.5d, 50, false, 0.2d, 0.8d));
            IReadOnlyList<PlanarPoint> mirrored = service.Generate(new PatternRequest(
                PlantPattern.Trapezoid, 9, 11, 1.5d, 50, true, 0.2d, 0.8d));
            TestAssert.Equal(normal.Count, mirrored.Count, "Mirroring changed point count.");
            for (int index = 0; index < normal.Count; index++)
            {
                bool found = mirrored.Any(point =>
                    Math.Abs(point.Right + normal[index].Right) < 1e-9 &&
                    Math.Abs(point.Forward - normal[index].Forward) < 1e-9);
                TestAssert.True(found,
                    "Mirroring did not preserve and invert the pattern footprint.");
            }
        }

        private static void PatternRequestsEnforceBounds()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new PatternRequest(PlantPattern.Grid, 257, 1, 1d, 1),
                "Rows above the hard limit must be rejected.");
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new PatternRequest(
                    PlantPattern.Grid,
                    1,
                    1,
                    1d,
                    PatternRequest.AbsoluteMaximumPoints + 1),
                "Point caps above the hard limit must be rejected.");
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new PatternRequest(PlantPattern.Grid, 1, 1, 0d, 1),
                "Non-positive spacing must be rejected.");
        }

        private static void LimitedResourcesUseRequestedFillOrder()
        {
            var service = new AgriculturePatternService();
            IReadOnlyList<PlanarPoint> grid = service.Generate(
                new PatternRequest(PlantPattern.Grid, 5, 5, 1d, 7));
            TestAssert.Equal(7, grid.Count, "The requested limited Grid count changed.");
            for (int index = 0; index < 5; index++)
                TestAssert.Near(2d, grid[index].Right, 1e-9,
                    "The rightmost Grid column was not filled first.");
            TestAssert.Near(1d, grid[5].Right, 1e-9,
                "The Grid did not advance one column to the left.");
            for (int index = 1; index < grid.Count; index++)
                TestAssert.True(grid[index - 1].Right >= grid[index].Right,
                    "Grid resources did not fill right-to-left.");

            IReadOnlyList<PlanarPoint> row = service.Generate(
                new PatternRequest(PlantPattern.Row, 1, 5, 1d, 5));
            for (int index = 0; index < row.Count; index++)
                TestAssert.Near(2d - index, row[index].Right, 1e-9,
                    "A Row did not fill right-to-left.");

            IReadOnlyList<PlanarPoint> circle = service.Generate(
                new PatternRequest(PlantPattern.Circle, 7, 7, 1d, 20));
            double previousDistance = -1d;
            for (int index = 0; index < circle.Count; index++)
            {
                double distance = circle[index].Right * circle[index].Right +
                                  circle[index].Forward * circle[index].Forward;
                TestAssert.True(distance >= previousDistance,
                    "A shaped pattern did not fill from its centre outward.");
                previousDistance = distance;
            }
        }

        private static void CombinedResourcesBudgetExactly()
        {
            var requirements = new[]
            {
                new PlantResourceRequirement("Seed", 1),
                new PlantResourceRequirement("Fertilizer", 2),
                new PlantResourceRequirement("Seed", 2)
            };
            var combined = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Seed"] = 105,
                ["Fertilizer"] = 40
            };
            TestAssert.Equal(20,
                SeedResourceMath.MaximumPlantings(requirements, combined),
                "Duplicate per-plant requirements or a second resource were budgeted incorrectly.");
        }

        private static void WheelRoutingIsContextual()
        {
            TestAssert.Equal(AgricultureWheelTarget.Rotation,
                AgricultureWheelRouter.Resolve(false, false, false, 1d),
                "Bare wheel must rotate the active cultivator pattern.");
            TestAssert.Equal(AgricultureWheelTarget.Rows,
                AgricultureWheelRouter.Resolve(true, false, false, 1d),
                "Alt+wheel must edit rows.");
            TestAssert.Equal(AgricultureWheelTarget.Columns,
                AgricultureWheelRouter.Resolve(false, true, false, 1d),
                "Shift+wheel must edit columns.");
            TestAssert.Equal(AgricultureWheelTarget.Spacing,
                AgricultureWheelRouter.Resolve(true, true, false, 1d),
                "Alt+Shift+wheel must edit spacing.");
            TestAssert.Equal(AgricultureWheelTarget.None,
                AgricultureWheelRouter.Resolve(false, false, true, 1d),
                "Ctrl+wheel must remain unowned by Agriculture.");

            string runtime = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "AgricultureRuntime.cs"));
            string consumption = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "AgricultureControlBarPatches.cs"));
            TestAssert.True(runtime.Contains("ButtonDown(\"Attack\")", StringComparison.Ordinal),
                "Ordinary left-click is not the planting confirmation.");
            TestAssert.True(runtime.Contains("MinimumSpacingFor", StringComparison.Ordinal),
                "Crop-specific spacing is not enforced by the live editor.");
            TestAssert.True(consumption.Contains("ConsumePlace", StringComparison.Ordinal),
                "The accepted batch click can leak into vanilla single placement.");
        }

        private static void NativeHintsExplainPreview()
        {
            string root = FindModuleRoot();
            string bar = File.ReadAllText(Path.Combine(
                root, "Integration", "AgricultureControlBar.cs"));
            string runtime = File.ReadAllText(Path.Combine(
                root, "Integration", "AgricultureRuntime.cs"));
            TestAssert.True(bar.Contains("m_buildHints", StringComparison.Ordinal),
                "Agriculture is not leasing Valheim's native build-hint panel.");
            TestAssert.True(bar.Contains("TMP_Text", StringComparison.Ordinal),
                "Native hint labels are not being updated through TextMeshPro.");
            TestAssert.True(bar.Contains("requestedScale", StringComparison.Ordinal),
                "The configurable compact native HUD scale is not applied.");
            TestAssert.True(runtime.Contains("Rows  − / +", StringComparison.Ordinal) &&
                            runtime.Contains("Columns  − / +", StringComparison.Ordinal),
                "The bottom HUD does not expose independent row and column decrement/increment controls.");
            TestAssert.False(bar.Contains("GUI.Window", StringComparison.Ordinal) ||
                             bar.Contains("GUI.Box", StringComparison.Ordinal),
                "The retired custom Agriculture panel is still present.");
            TestAssert.True(runtime.Contains("out string commitReason", StringComparison.Ordinal),
                "A successful commit can still overwrite the actual stop reason.");

            MethodInfo summary = RequiredMethod(typeof(AgricultureRuntime), "PreviewIssueSummary");
            string message = (string)summary.Invoke(null, new object[]
            {
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [AgricultureReasonCodes.SpacingBlocked] = 7,
                    [AgricultureReasonCodes.SlopeInvalid] = 2
                }
            });
            TestAssert.Equal("7 too close, 2 too steep", message,
                "The native hint does not explain each amber blocked-preview cause.");
        }

        private static void PlacementValidationIsStable()
        {
            var allValid = new PlacementValidationInputs(
                true, true, true, true, true, true, true, true, true, true);
            TestAssert.Equal(AgricultureReasonCodes.Valid,
                PlacementValidation.Evaluate(allValid).ReasonCode,
                "A valid placement was denied.");
            var wardDenied = new PlacementValidationInputs(
                true, true, true, true, true, true, false, false, false, true);
            TestAssert.Equal(AgricultureReasonCodes.WardDenied,
                PlacementValidation.Evaluate(wardDenied).ReasonCode,
                "Ward denial must precede later no-build and player checks.");
        }

        private static void PreviewIgnoresActionBudget()
        {
            var validations = new[] { Valid(), Valid(), Valid(), Valid() };
            BatchPlan actionLimited = BatchPlanner.Plan(
                validations,
                new PlacementBudget(96, 1, 1),
                InvalidPositionPolicy.SkipInvalid,
                ResourceShortfallPolicy.TruncatePredictably);
            TestAssert.Equal(1, actionLimited.SuccessfulCount,
                "The regression fixture no longer represents a stamina/tool-limited action budget.");

            BatchPlan preview = BatchPlanner.PlanPreview(validations);
            TestAssert.Equal(4, preview.SuccessfulCount,
                "Transient stamina/tool durability still turns plantable ground red.");
            for (int index = 0; index < preview.Decisions.Count; index++)
                TestAssert.True(preview.Decisions[index].ShouldPlace,
                    "A ground-valid preview cell was masked by an unrelated action budget.");

            MethodInfo livePreview = RequiredMethod(
                typeof(AgricultureRuntime),
                "BuildValidatedPreview");
            TestAssert.True(IlReader.Calls(
                    livePreview,
                    typeof(BatchPlanner),
                    nameof(BatchPlanner.PlanPreview)),
                "The live preview does not use the geometry-only readiness plan.");

            string runtime = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "AgricultureRuntime.cs"));
            TestAssert.True(runtime.Contains("ChargeBatchActionCosts(player, batchTool)",
                    StringComparison.Ordinal),
                "The batch no longer charges one explicit stamina/tool action.");
            TestAssert.True(runtime.Contains("NearbySeedResourceService.TryDebitOne(",
                    StringComparison.Ordinal),
                "Per-cell resource debit no longer combines personal and nearby sources.");
        }

        private static void PreviewBackfillsAfterInvalidCells()
        {
            var validations = new PlacementValidationResult[100];
            for (int index = 0; index < validations.Length; index++) validations[index] = Valid();
            for (int index = 0; index < 4; index++)
                validations[index] = new PlacementValidationResult(
                    false,
                    AgricultureReasonCodes.SlopeInvalid);

            BatchPlan preview = BatchPlanner.PlanPreview(validations, 96);
            TestAssert.Equal(96, preview.SuccessfulCount,
                "Four invalid early cells prevented the final four valid cells from backfilling.");
            for (int index = 0; index < 4; index++)
            {
                TestAssert.False(preview.Decisions[index].ShouldPlace,
                    "An invalid early cell was selected for planting.");
                TestAssert.Equal(AgricultureReasonCodes.SlopeInvalid,
                    preview.Decisions[index].ReasonCode,
                    "The invalid early cell lost its blocked-preview reason.");
            }
            for (int index = 4; index < 100; index++)
                TestAssert.True(preview.Decisions[index].ShouldPlace,
                    "A later valid cell did not backfill an invalid earlier cell.");

            string runtime = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "AgricultureRuntime.cs"));
            string generator = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Core", "PatternGenerator.cs"));
            TestAssert.True(generator.Contains("PlantingFillOrder.Order(pattern, candidates)",
                    StringComparison.Ordinal),
                "The generated footprint does not use the requested deterministic fill order.");
            TestAssert.False(runtime.Contains("OutsideInPatternOrder", StringComparison.Ordinal),
                "The retired outside-in seed order still controls the live footprint.");
            TestAssert.False(runtime.Contains("SeedBoundedPreviewLimit", StringComparison.Ordinal),
                "The live preview still truncates geometry before terrain validation.");

            var excess = new RuntimePreviewPosition(
                default,
                default,
                false,
                AgricultureReasonCodes.NoSeeds);
            TestAssert.True(excess.IsGroundValid,
                "A valid cell outside the seed budget is still classified as invalid ground.");
            TestAssert.True(excess.IsResourceShortage,
                "A resource-short cell is not classified as the red shortage state.");
        }

        private static void BatchPlannerTruncates()
        {
            var validations = new[]
            {
                Valid(),
                new PlacementValidationResult(false, AgricultureReasonCodes.SlopeInvalid),
                Valid(),
                Valid()
            };
            BatchPlan plan = BatchPlanner.Plan(
                validations,
                new PlacementBudget(2, 4, 4),
                InvalidPositionPolicy.SkipInvalid,
                ResourceShortfallPolicy.TruncatePredictably);
            TestAssert.False(plan.Blocked, "A truncating plan was blocked.");
            TestAssert.Equal(2, plan.SuccessfulCount, "The plan exceeded its seed budget.");
            TestAssert.Equal(AgricultureReasonCodes.SlopeInvalid, plan.Decisions[1].ReasonCode,
                "The invalid-position reason was not preserved.");
            TestAssert.Equal(AgricultureReasonCodes.NoSeeds, plan.Decisions[3].ReasonCode,
                "The exhausted resource was not reported exactly.");
        }

        private static void BatchPlannerBlocks()
        {
            BatchPlan invalid = BatchPlanner.Plan(
                new[] { Valid(), new PlacementValidationResult(false, AgricultureReasonCodes.BiomeInvalid) },
                new PlacementBudget(2, 2, 2),
                InvalidPositionPolicy.BlockConfirmation,
                ResourceShortfallPolicy.TruncatePredictably);
            TestAssert.True(invalid.Blocked, "Invalid-position blocking was not honored.");
            TestAssert.Equal(AgricultureReasonCodes.InvalidBatchBlocked, invalid.ReasonCode,
                "Invalid batch used the wrong reason.");

            BatchPlan costs = BatchPlanner.Plan(
                new[] { Valid(), Valid() },
                new PlacementBudget(1, 2, 2),
                InvalidPositionPolicy.SkipInvalid,
                ResourceShortfallPolicy.BlockConfirmation);
            TestAssert.True(costs.Blocked, "Cost blocking was not honored.");
            TestAssert.Equal(AgricultureReasonCodes.CostBatchBlocked, costs.ReasonCode,
                "Cost block used the wrong reason.");
        }

        private static void ReplantOffersAreExact()
        {
            var confirmation = new ReplantConfirmation<int>(2);
            confirmation.Offer("Carrot", new[] { 1, 2, 3 });
            TestAssert.Equal(2, confirmation.Positions.Count, "Replant offer exceeded its cap.");
            TestAssert.False(confirmation.TryConfirm("Turnip", out _, out string mismatch),
                "A different crop confirmed the offer.");
            TestAssert.Equal(AgricultureReasonCodes.ReplantCropMismatch, mismatch,
                "Crop mismatch used the wrong reason.");
            TestAssert.True(confirmation.TryConfirm("Carrot", out IReadOnlyList<int> positions, out _),
                "The exact crop did not confirm.");
            TestAssert.Equal(2, positions.Count, "Confirmed positions changed.");
            TestAssert.False(confirmation.IsPending, "Confirmation did not consume the offer.");
        }

        private static void HarvestPolicyRequiresReadyPickable()
        {
            TestAssert.True(HarvestPickableBatchPolicy.Supports(HarvestComponentContract.Pickable),
                "Pickable must be supported.");
            TestAssert.False(HarvestPickableBatchPolicy.Supports(HarvestComponentContract.PickableItem),
                "PickableItem must remain excluded.");
            TestAssert.True(HarvestPickableBatchPolicy.IsReady(true, true, true, false, false, false),
                "A ready Pickable was denied.");
            TestAssert.False(HarvestPickableBatchPolicy.IsReady(true, true, true, true, false, false),
                "An already-picked object was accepted.");
            TestAssert.False(HarvestPickableBatchPolicy.IsReady(true, true, true, false, true, true),
                "A tar-blocked object was accepted.");
        }

        private static void AreaHarvestAndReplantAreSeparated()
        {
            TestAssert.True(HarvestPickableBatchPolicy.AllowsAreaHarvest(true),
                "A permitted Pickable was rejected by area harvest.");
            TestAssert.False(HarvestPickableBatchPolicy.AllowsAreaHarvest(false),
                "Area harvest remained active after access was denied.");

            TestAssert.True(HarvestPickableBatchPolicy.OffersReplant(true, true, "Carrot"),
                "A valid replant context was rejected.");
            TestAssert.False(HarvestPickableBatchPolicy.OffersReplant(false, true, "Carrot"),
                "A replant offer remained active while replant was disabled.");
            TestAssert.False(HarvestPickableBatchPolicy.OffersReplant(true, false, "Carrot"),
                "A replant offer remained active without planting authorization.");
            TestAssert.False(HarvestPickableBatchPolicy.OffersReplant(true, true, string.Empty),
                "A wild Pickable without a matching plant created a replant offer.");
        }

        private static void HarvestPolicyIsExact()
        {
            TestAssert.True(HarvestPickableBatchPolicy.IsExactPrefab("Carrot", 7, "Carrot", 7),
                "An exact prefab was rejected.");
            TestAssert.False(HarvestPickableBatchPolicy.IsExactPrefab("Carrot", 7, "Carrot", 8),
                "A hash mismatch was accepted.");
            TestAssert.True(HarvestPickableBatchPolicy.Compare(
                    true, 100f, 9, 9, false, 0f, 1, 1) < 0,
                "The aimed object was not prioritized.");
        }

        private static void PlantingUsesNativePlacement()
        {
            MethodInfo execute = RequiredMethod(typeof(AgricultureRuntime), "ExecuteBatch");
            TestAssert.True(IlReader.Calls(execute, typeof(Player), nameof(Player.PlacePiece)),
                "Planting no longer calls Player.PlacePiece.");
            TestAssert.False(IlReader.CallsNamed(execute,
                    "Runic.Foundation.Transactions.RunicMutationGate", "TryEnter"),
                "Planting still uses the suite-wide mutation gate.");
        }

        private static void ValheimMutationSignaturesAreExact()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public |
                                     BindingFlags.NonPublic;
            MethodInfo place = typeof(Player).GetMethod(
                nameof(Player.PlacePiece),
                all,
                null,
                new[]
                {
                    typeof(Piece), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion),
                    typeof(bool), typeof(bool)
                },
                null);
            TestAssert.NotNull(place, "Valheim 1.0 Player.PlacePiece signature is unavailable.");

            MethodInfo changed = typeof(Inventory).GetMethod(
                "Changed",
                all,
                null,
                new[] { typeof(bool), typeof(bool) },
                null);
            TestAssert.NotNull(changed, "Valheim 1.0 Inventory.Changed signature is unavailable.");
        }

        private static void FreeBuildDoesNotChargeSeeds()
        {
            TestAssert.True(PlantingGridPolicy.ConsumesSeedResources(false, false),
                "A normal planting action must consume its per-cell seed resources.");
            TestAssert.False(PlantingGridPolicy.ConsumesSeedResources(true, false),
                "The world free-build key must suppress seed consumption.");
            TestAssert.False(PlantingGridPolicy.ConsumesSeedResources(false, true),
                "The player's no-cost mode must suppress seed consumption.");
            TestAssert.False(PlantingGridPolicy.ConsumesSeedResources(true, true),
                "Combined free-build modes must suppress seed consumption.");
        }

        private static void NearbySeedChestsAreSafe()
        {
            string source = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "NearbySeedResources.cs"));
            foreach (string required in new[]
            {
                "HardMaximumRangeMeters = 30f",
                "HardMaximumCandidates = 64",
                "player.GetInventory()",
                "PrivateArea.CheckAccess(",
                "CheckAccessMethod.Invoke(",
                "container.IsOwner()",
                "view.IsOwner()",
                "zdo.GetOwner() != ZNet.GetUID()",
                "container.m_rootObjectOverride",
                "m_nview",
                "InventoryMatchesZdo",
                "GetByteArray(ZDOVars.s_items)",
                "AgricultureInventoryPayloadComparison.MatchesLoaded",
                "ValidateSnapshotRoundTrip"
            })
                TestAssert.True(source.Contains(required, StringComparison.Ordinal),
                    "Nearby chest safety contract is missing: " + required);
            foreach (string forbidden in new[]
            {
                "ClaimOwnership", "SetOwner(", "InvokeRPC(", "ZRoutedRpc"
            })
                TestAssert.False(source.Contains(forbidden, StringComparison.Ordinal),
                    "Agriculture introduced forbidden ownership/RPC behavior: " + forbidden);

            int playerFirst = source.IndexOf(
                "new ResourceSource(player.GetInventory()", StringComparison.Ordinal);
            int nearbyAfter = source.IndexOf(
                "NearbySeedContainerIndex.Query", playerFirst, StringComparison.Ordinal);
            TestAssert.True(playerFirst >= 0 && nearbyAfter > playerFirst,
                "Personal inventory is not deterministically consumed before nearby chests.");
        }

        private static void PreviewColorsAreUnambiguous()
        {
            var blocked = new RuntimePreviewPosition(
                default, default, false, AgricultureReasonCodes.SlopeInvalid);
            var shortage = new RuntimePreviewPosition(
                default, default, false, AgricultureReasonCodes.NoSeeds);
            TestAssert.False(blocked.IsGroundValid,
                "Invalid terrain was misclassified as a resource shortage.");
            TestAssert.False(blocked.IsResourceShortage,
                "Invalid terrain would still receive the red shortage color.");
            TestAssert.True(shortage.IsGroundValid && shortage.IsResourceShortage,
                "Resource shortage no longer receives its unique red classification.");

            string preview = File.ReadAllText(Path.Combine(
                FindModuleRoot(), "Integration", "PreviewPool.cs"));
            TestAssert.True(preview.Contains("Color amber", StringComparison.Ordinal) &&
                            preview.Contains("Color shortage", StringComparison.Ordinal),
                "The preview does not define separate blocked and shortage colors.");
            TestAssert.False(preview.Contains("Color blue", StringComparison.Ordinal),
                "The retired blue ghost color remains in the preview renderer.");
        }

        private static void HarvestUsesNativeInteraction()
        {
            MethodInfo harvest = RequiredMethod(typeof(AgricultureRuntime), "TryAreaHarvest");
            TestAssert.True(IlReader.Calls(harvest, typeof(Pickable), nameof(Pickable.Interact)),
                "Area harvest no longer uses Pickable.Interact.");
            TestAssert.True(IlReader.Calls(harvest, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)),
                "Area harvest no longer checks ward access.");
        }

        private static void MutationExclusionIsLocal()
        {
            MethodInfo enter = RequiredMethod(typeof(AgricultureRuntime), "TryEnterMutation");
            TestAssert.True(IlReader.Calls(enter, typeof(Interlocked), nameof(Interlocked.CompareExchange)),
                "Agriculture's batch exclusion is not process-local.");
            FieldInfo field = typeof(AgricultureRuntime).GetField(
                "_mutationActive", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.NotNull(field, "Agriculture's local mutation flag is missing.");
        }

        private static void AssemblyHasNoFoundationReferences()
        {
            string[] forbidden = { "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions" };
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            foreach (string name in forbidden)
                TestAssert.False(references.Contains(name, StringComparer.OrdinalIgnoreCase),
                    "Gameplay assembly still references " + name + ".");
        }

        private static void PackagingIsIndependent()
        {
            string root = FindModuleRoot();
            string project = File.ReadAllText(Path.Combine(root, "RunicAgriculture.csproj"));
            foreach (string name in new[] { "ProjectReference", "RunicPersistence", "RunicTransactions" })
                TestAssert.False(project.Contains(name, StringComparison.Ordinal),
                    "Gameplay project still contains " + name + ".");

            using JsonDocument manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "manifest.json")));
            string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray();
            TestAssert.Equal(1, dependencies.Length, "Manifest must declare only BepInEx.");
            TestAssert.True(dependencies[0].StartsWith(
                    "denikson-BepInExPack_Valheim-", StringComparison.Ordinal),
                "Manifest does not declare BepInEx.");
        }

        private static void DurableArchitectureIsAbsent()
        {
            string[] fragments =
            {
                "DurablePlant", "DedicatedAgriculture", "HarvestAuthorityRuntime",
                "PlantAuthorityRuntime", "AgricultureWorldObjectMutationProvider"
            };
            Type[] types = typeof(Plugin).Assembly.GetTypes();
            foreach (string fragment in fragments)
                TestAssert.False(types.Any(type =>
                        type.FullName?.Contains(fragment, StringComparison.Ordinal) == true),
                    fragment + " is still compiled into Agriculture.");
        }

        private static void VersionIsUnchanged()
        {
            TestAssert.Equal("1.0.3", Plugin.Version, "Plugin version changed.");
            TestAssert.Equal(new System.Version(1, 0, 3, 0), typeof(Plugin).Assembly.GetName().Version,
                "Assembly version changed.");
        }

        private static PlacementValidationResult Valid() =>
            new PlacementValidationResult(true, AgricultureReasonCodes.Valid);

        private static MethodInfo RequiredMethod(Type type, string name) =>
            TestAssert.NotNull(type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic),
                type.FullName + "." + name + " is missing.");

        private static string FindModuleRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string direct = Path.Combine(directory.FullName, "manifest.json");
                if (File.Exists(direct) &&
                    File.Exists(Path.Combine(directory.FullName, "RunicAgriculture.csproj")))
                    return directory.FullName;
                string nested = Path.Combine(directory.FullName, "RunicAgriculture", "manifest.json");
                if (File.Exists(nested)) return Path.GetDirectoryName(nested);
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("RunicAgriculture module root was not found.");
        }
    }
}
