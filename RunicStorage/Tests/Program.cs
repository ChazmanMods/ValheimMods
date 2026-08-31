using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicStorage.Engine;

namespace RunicStorage.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("PlannerUsesNearestEligibleContainers", PlannerUsesNearestEligibleContainers),
                ("PlannerSkipsProtectedSources", PlannerSkipsProtectedSources),
                ("PlannerReportsRemainder", PlannerReportsRemainder),
                ("CoverageReportsRadiusAndCandidateTruncation", CoverageReportsRadiusAndCandidateTruncation),
                ("SpatialSelectionIsBoundedAndStable", SpatialSelectionIsBoundedAndStable),
                ("SpatialCellsHandleNegativeCoordinates", SpatialCellsHandleNegativeCoordinates),
                ("ActionSelectionKeepsSortPriority", ActionSelectionKeepsSortPriority),
                ("MutationRoutesRequireLocalOwnership", MutationRoutesRequireLocalOwnership),
                ("SearchRemainsReadOnly", SearchRemainsReadOnly),
                ("SearchAndRestockWorkWithInventoryOpen", SearchAndRestockWorkWithInventoryOpen),
                ("SearchPanelKeepsCursorAndKeyboardFocus", SearchPanelKeepsCursorAndKeyboardFocus),
                ("SearchPanelOwnsPrimaryAttackUntilClickRelease", SearchPanelOwnsPrimaryAttackUntilClickRelease),
                ("SearchMenuAppearanceIsBoundedAndComplete", SearchMenuAppearanceIsBoundedAndComplete),
                ("SearchMenuUsesNativeValheimCanvasWithoutAtlasRasterization", SearchMenuUsesNativeValheimCanvasWithoutAtlasRasterization),
                ("SearchHighlightUsesIndependentFailClosedRingVisuals", SearchHighlightUsesIndependentFailClosedRingVisuals),
                ("OpenedSortRequiresContainer", OpenedSortRequiresContainer),
                ("ControllerSessionConsumesOnlyOwnedSequence", ControllerSessionConsumesOnlyOwnedSequence),
                ("QuickStackDiagnosticsAreSpecific", QuickStackDiagnosticsAreSpecific),
				("FirstUseReconcilesLoadedContainers", FirstUseReconcilesLoadedContainers),
                ("HoverRequiresExactDisclosureEvidence", HoverRequiresExactDisclosureEvidence),
                ("HoverRangeIsPhysicallyBounded", HoverRangeIsPhysicallyBounded),
                ("MissingInventoryIsOptional", MissingInventoryIsOptional),
                ("PackageHasNoRuntimeFoundationDependency", PackageHasNoRuntimeFoundationDependency),
                ("RuntimeHasNoDurableOrGlobalMutationLayer", RuntimeHasNoDurableOrGlobalMutationLayer),
                ("MutationsRequireNativeOwnership", MutationsRequireNativeOwnership)
            };
            int failures = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); Console.WriteLine("PASS " + name); }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            Console.WriteLine((tests.Length - failures) + "/" + tests.Length + " tests passed.");
            return failures == 0 ? 0 : 1;
        }

        private static void PlannerUsesNearestEligibleContainers()
        {
            QuickStackPlan plan = QuickStackPlanner.Plan(
                new[] { new QuickStackSource("stack", "Wood", 5) },
                new[]
                {
                    Destination("far", 9f, 5),
                    Destination("near", 1f, 3),
                    new QuickStackDestination("busy", 0f, true, false, true,
                        new Dictionary<string, int> { ["Wood"] = 5 })
                });
            Equal(2, plan.Moves.Count);
            Equal("near", plan.Moves[0].DestinationEndpointId);
            Equal(5, plan.PlannedQuantity);
        }

        private static void PlannerSkipsProtectedSources()
        {
            QuickStackPlan plan = QuickStackPlanner.Plan(
                new[]
                {
                    new QuickStackSource("a", "Wood", 4, protectedSlot: true),
                    new QuickStackSource("b", "Wood", 2)
                },
                new[] { Destination("box", 1f, 10) });
            Equal(2, plan.RequestedQuantity);
            Equal("b", plan.Moves.Single().StackId);
        }

        private static void PlannerReportsRemainder()
        {
            QuickStackPlan plan = QuickStackPlanner.Plan(
                new[] { new QuickStackSource("a", "Wood", 7) },
                new[] { Destination("box", 1f, 3) });
            Equal(3, plan.PlannedQuantity);
            Equal(4, plan.RemainingQuantity);
        }

        private static void CoverageReportsRadiusAndCandidateTruncation()
        {
            True(ContainerQueryCoverage.IsTruncated(30f, 20f, false));
            True(ContainerQueryCoverage.IsTruncated(20f, 20f, true));
            False(ContainerQueryCoverage.IsTruncated(20f, 20f, false));
        }

        private static void SpatialSelectionIsBoundedAndStable()
        {
            SpatialSelectionProfile profile = ContainerSpatialPolicy.ProfileNearest(
                new[]
                {
                    new SpatialCandidateKey(4f, "b", 2),
                    new SpatialCandidateKey(1f, "c", 3),
                    new SpatialCandidateKey(1f, "a", 1)
                }, 2);
            True(profile.Truncated);
            Equal("a", profile.Selected[0].EndpointId);
            Equal("c", profile.Selected[1].EndpointId);
        }

        private static void SpatialCellsHandleNegativeCoordinates()
        {
            Equal(-1, ContainerSpatialPolicy.CellCoordinate(-0.01f));
            Equal(0, ContainerSpatialPolicy.CellCoordinate(9.99f));
            Equal(1, ContainerSpatialPolicy.CellCoordinate(10f));
        }

        private static void ActionSelectionKeepsSortPriority()
        {
            StorageActionRequest request = StorageActionRouter.Select(
                StorageActionEdges.KeyboardQuickStack | StorageActionEdges.KeyboardSort);
            Equal(StorageActionKind.SortOpenedContainer, request.Action);
        }

        private static void MutationRoutesRequireLocalOwnership()
        {
            StorageActionRequest request = new StorageActionRequest(
                StorageActionKind.QuickStack, StorageInputOrigin.Keyboard);
            StorageRouteDecision decision = StorageActionRouter.Route(
                request, Context(mutationOwner: false));
            Equal(StorageRouteOutcome.Blocked, decision.Outcome);
            Equal(StorageRouteReason.HostAuthorityRequired, decision.Reason);
        }

        private static void SearchRemainsReadOnly()
        {
            StorageRouteDecision decision = StorageActionRouter.Route(
                new StorageActionRequest(StorageActionKind.Search, StorageInputOrigin.Keyboard),
                Context(mutationOwner: false));
            Equal(StorageRouteOutcome.Execute, decision.Outcome);
        }

        private static void SearchAndRestockWorkWithInventoryOpen()
        {
            foreach (StorageActionKind action in new[]
                     { StorageActionKind.Search, StorageActionKind.Restock })
            {
                StorageRouteDecision decision = StorageActionRouter.Route(
                    new StorageActionRequest(action, StorageInputOrigin.Keyboard),
                    Context(mutationOwner: true, inventoryOpen: true, openedContainer: true));
                Equal(StorageRouteOutcome.Execute, decision.Outcome);
            }

            string root = Root();
            string actions = File.ReadAllText(Path.Combine(root, "Runtime", "StorageActions.cs"));
            string panel = File.ReadAllText(Path.Combine(root, "Runtime", "StorageSearchPanel.cs"));
            True(actions.Contains("TryGetExactOpenedLocalOwnerInventory", StringComparison.Ordinal));
            True(actions.Contains("StorageSearchEntry", StringComparison.Ordinal));
            True(panel.Contains("StorageSearchHighlight", StringComparison.Ordinal));
            True(panel.Contains("LineRenderer", StringComparison.Ordinal));
        }

        private static void SearchPanelKeepsCursorAndKeyboardFocus()
        {
            string root = Root();
            string panel = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageSearchPanel.cs"));
            string patches = File.ReadAllText(Path.Combine(
                root, "Runtime", "ContainerPatches.cs"));
            True(panel.Contains("RenewCursorLease", StringComparison.Ordinal));
            True(panel.Contains("_filterInput.Select()", StringComparison.Ordinal));
            True(panel.Contains("_filterInput.ActivateInputField()", StringComparison.Ordinal));
            True(panel.Contains("StorageSearchGameplayInputGuard.PollPickerEscape()",
                StringComparison.Ordinal));
            True(panel.Contains("typeof(GraphicRaycaster)", StringComparison.Ordinal));
            True(panel.Contains("RenderMode.ScreenSpaceOverlay", StringComparison.Ordinal));
            True(patches.Contains("GameCamera.UpdateMouseCapture", StringComparison.Ordinal));
            True(patches.Contains("StorageSearchPanel.RenewCursorLease", StringComparison.Ordinal));
            True(patches.Contains("Plugin.SearchPanelOpen", StringComparison.Ordinal));
            True(patches.Contains(
                "[HarmonyAfter(\"chazman.RunicBuildCamera\")]",
                StringComparison.Ordinal));
        }

        private static void SearchPanelOwnsPrimaryAttackUntilClickRelease()
        {
            var suppression = new StorageSearchAttackSuppression();

            False(suppression.ShouldSuppress("Use", true, true, 10));
            True(suppression.ShouldSuppress("Attack", true, true, 10));
            True(suppression.ShouldSuppress("Attack", false, true, 11));
            True(suppression.ShouldSuppress("Attack", false, false, 12));
            True(suppression.ShouldSuppress("Attack", false, false, 12));
            False(suppression.ShouldSuppress("Attack", false, false, 13));

            // Merely closing with Escape must not steal the player's next ordinary attack.
            var noPointerClick = new StorageSearchAttackSuppression();
            True(noPointerClick.ShouldSuppress("Attack", true, false, 20));
            False(noPointerClick.ShouldSuppress("Attack", false, false, 21));

            string root = Root();
            string panel = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageSearchPanel.cs"));
            string patches = File.ReadAllText(Path.Combine(
                root, "Runtime", "ControllerInputPatches.cs"));
            True(patches.Contains("StorageSearchEscapeKeyPatch", StringComparison.Ordinal));
            True(patches.Contains("ShouldSuppressEscape(key)", StringComparison.Ordinal));
            True(panel.Contains(
                "StorageSearchGameplayInputGuard.CaptureGuiPointer(Event.current)",
                StringComparison.Ordinal));
            True(patches.Contains(
                "StorageSearchGameplayInputGuard.ShouldSuppressPrimaryAttack(name)",
                StringComparison.Ordinal));
        }

        private static void SearchMenuAppearanceIsBoundedAndComplete()
        {
            Equal(10, StorageSearchMenuAppearance.ClampFontSize(-100));
            Equal(14, StorageSearchMenuAppearance.ClampFontSize(14));
            Equal(32, StorageSearchMenuAppearance.ClampFontSize(100));

            Equal(StorageSearchMenuFontColor.Turquoise,
                StorageSearchMenuAppearance.NormalizeFontColorConfigValue("turquoise"));
            Equal(StorageSearchMenuFontColor.LightGray,
                StorageSearchMenuAppearance.NormalizeFontColorConfigValue("#E6E6E6"));
            Equal(StorageSearchMenuFontColor.Cyan,
                StorageSearchMenuAppearance.NormalizeFontColorConfigValue("00FFFF80"));
            Equal(StorageSearchMenuFontColor.LightGray,
                StorageSearchMenuAppearance.NormalizeFontColorConfigValue("not-a-color"));
            StorageSearchMenuColor turquoise = StorageSearchMenuAppearance.ResolveFontColor(
                StorageSearchMenuFontColor.Turquoise);
            Equal((byte)0x40, turquoise.Red);
            Equal((byte)0xE0, turquoise.Green);
            Equal((byte)0xD0, turquoise.Blue);
            Equal((byte)0xFF, turquoise.Alpha);
            Equal(13, Enum.GetValues(typeof(StorageSearchMenuFontColor)).Length);

            StorageSearchMenuLayout normal = StorageSearchMenuAppearance.LayoutFor(14);
            StorageSearchMenuLayout large = StorageSearchMenuAppearance.LayoutFor(32);
            True(large.ControlHeight > normal.ControlHeight);
            True(large.ItemHeight > normal.ItemHeight);
            True(large.PreferredWindowHeight > normal.PreferredWindowHeight);

            string root = Root();
            string config = File.ReadAllText(Path.Combine(root, "PluginConfig.cs"));
            string panel = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageSearchPanel.cs"));
            True(config.Contains("SearchMenuFontSize", StringComparison.Ordinal));
            True(config.Contains(
                "ConfigEntry<StorageSearchMenuFontColor> SearchMenuFontColor",
                StringComparison.Ordinal));
            True(config.Contains("TryReadAndRemoveLegacySearchMenuFontColor(",
                StringComparison.Ordinal));
            True(config.Contains("config.Remove(definition)", StringComparison.Ordinal));
            True(config.Contains(
                "StorageSearchMenuAppearance.DefaultFontColor,",
                StringComparison.Ordinal));
            True(config.Contains(
                "if (hadPersistedSearchMenuFontColor)",
                StringComparison.Ordinal));
            True(config.Contains(
                "SearchMenuFontColor.Value = persistedSearchMenuFontColor;",
                StringComparison.Ordinal));
            True(config.Contains("HasOrphanedValue(config, definition)",
                StringComparison.Ordinal));
            False(config.Contains(
                    "SearchMenuFontColor.Value = StorageSearchMenuAppearance.DefaultFontColor",
                    StringComparison.Ordinal),
                "Native menu migration must not overwrite a persisted named color.");
            Reject(config, "Use RRGGBB", "optional leading #");
            True(config.Contains("AcceptableValueRange<int>(10, 32)", StringComparison.Ordinal));
            True(panel.Contains("CreateText(panel.transform", StringComparison.Ordinal));
            True(panel.Contains("CreateInput(filterRow, layout, textColor)", StringComparison.Ordinal));
            True(panel.Contains("CreateButton(_entryContent, label, layout, textColor",
                StringComparison.Ordinal));
            True(panel.Contains("PluginConfig.SearchMenuFontSize?.Value", StringComparison.Ordinal));
            True(panel.Contains("PluginConfig.SearchMenuFontColor?.Value", StringComparison.Ordinal));
            True(panel.Contains(
                "StorageSearchGameplayInputGuard.CaptureGuiPointer(Event.current)",
                StringComparison.Ordinal));
        }

        private static void SearchHighlightUsesIndependentFailClosedRingVisuals()
        {
            string panel = File.ReadAllText(Path.Combine(
                Root(), "Runtime", "StorageSearchPanel.cs"));
            True(panel.Contains(
                "new GameObject(\"RunicStorageSearchRing\" + index)",
                StringComparison.Ordinal));
            True(panel.Contains("ringObject.transform.SetParent(_visualRoot.transform, false)",
                StringComparison.Ordinal));
            True(panel.Contains("_visualRoot.transform.position = _center",
                StringComparison.Ordinal));
            Reject(panel, "_visualRoot.AddComponent<LineRenderer>()",
                "_rings[ringIndex].SetPosition");
            foreach (string guard in new[]
                     {
                         "if (!ring)", "VisualsAreValid()", "TryUpdateVisuals()",
                         "ApplyColor(", "catch (Exception)", "if (_stopping) return",
                         "enabled = false", "StopHighlight()"
                     })
                True(panel.Contains(guard, StringComparison.Ordinal),
                    "Missing highlight lifecycle guard: " + guard);
        }

        private static void SearchMenuUsesNativeValheimCanvasWithoutAtlasRasterization()
        {
            string root = Root();
            string theme = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageSearchVanillaTheme.cs"));
            string panel = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageSearchPanel.cs"));
            string project = File.ReadAllText(Path.Combine(root, "RunicStorage.csproj"));

            foreach (string vanillaContract in new[]
                     {
                         "InventoryGui.instance", "inventory.m_splitPanel", "m_takeAllButton",
                         "m_playerName", "TMP_FontAsset", "FindBestPanelImage",
                         "PanelSprite = panel.sprite", "ButtonSprite = source.image.sprite",
                         "ButtonSprites = source.spriteState", "ButtonColors = source.colors"
                     })
                True(theme.Contains(vanillaContract, StringComparison.Ordinal),
                    "Missing vanilla UI source: " + vanillaContract);
            foreach (string nativeCanvasContract in new[]
                     {
                         "RunicStorageNativeSearchCanvas", "typeof(Canvas)",
                         "typeof(CanvasScaler)", "typeof(GraphicRaycaster)",
                         "_theme.PanelSprite", "TMP_InputField", "ScrollRect", "Scrollbar",
                         "Button", "TextMeshProUGUI"
                     })
                True(panel.Contains(nativeCanvasContract, StringComparison.Ordinal),
                    "Missing native Canvas contract: " + nativeCanvasContract);
            foreach (string prohibitedRasterPath in new[]
                     { "Texture2D", "RenderTexture", "Graphics.Blit", "ReadPixels", "CopySpriteTexture" })
                False(theme.Contains(prohibitedRasterPath, StringComparison.Ordinal),
                    "Packed Valheim atlas sprites must not be rasterized: " + prohibitedRasterPath);
            foreach (string lifecycleContract in new[]
                     {
                         "StorageSearchVanillaTheme.Create()", "EnsureNativeView(false)",
                         "DestroyNativeView()", "StorageSearchVanillaTheme.CurrentSourceToken()"
                     })
                True(panel.Contains(lifecycleContract, StringComparison.Ordinal),
                    "Missing native themed panel lifecycle: " + lifecycleContract);
            foreach (string readableFallback in new[]
                     {
                         "new Color32(0x3C, 0x2A, 0x1D, 0xFF)",
                         "new Color32(0x18, 0x10, 0x0B, 0xF2)",
                         "new Color(0f, 0f, 0f, 0.68f)"
                     })
                True(panel.Contains(readableFallback, StringComparison.Ordinal),
                    "Missing readable native-control fallback: " + readableFallback);
            foreach (string inputContract in new[]
                     {
                         "input.onValueChanged.AddListener(OnFilterChanged)",
                         "clear.onClick.AddListener(ClearFilter)",
                         "close.onClick.AddListener(Close)",
                         "row.onClick.AddListener(() => Select(selected))"
                     })
                True(panel.Contains(inputContract, StringComparison.Ordinal),
                    "Missing native input contract: " + inputContract);
            True(panel.Contains("StorageSearchGameplayInputGuard.CaptureGuiPointer(Event.current)",
                StringComparison.Ordinal));
            True(project.Contains("UnityEngine.UI.dll", StringComparison.Ordinal));
            True(project.Contains("Unity.TextMeshPro.dll", StringComparison.Ordinal));
            True(project.Contains("UnityEngine.TextRenderingModule.dll", StringComparison.Ordinal));
            True(project.Contains("UnityEngine.UIModule.dll", StringComparison.Ordinal));
            True(project.Contains("gui_framework.dll", StringComparison.Ordinal));
        }

        private static void OpenedSortRequiresContainer()
        {
            StorageRouteDecision decision = StorageActionRouter.Route(
                new StorageActionRequest(
                    StorageActionKind.SortOpenedContainer, StorageInputOrigin.Keyboard),
                Context(mutationOwner: true, inventoryOpen: true, openedContainer: false));
            Equal(StorageRouteReason.SortRequiresOpenedContainer, decision.Reason);
        }

        private static void ControllerSessionConsumesOnlyOwnedSequence()
        {
            Equal(ControllerChordDisposition.ReportWithoutConsume,
                ControllerChordSessionPolicy.Decide(false, StorageRouteOutcome.Blocked));
            Equal(ControllerChordDisposition.ExecuteAndConsume,
                ControllerChordSessionPolicy.Decide(false, StorageRouteOutcome.Execute));
            Equal(ControllerChordDisposition.ReportAndConsume,
                ControllerChordSessionPolicy.Decide(true, StorageRouteOutcome.Blocked));
        }

        private static void QuickStackDiagnosticsAreSpecific()
        {
            Equal(QuickStackNoOpReason.InventoryEmpty,
                QuickStackDiagnostics.Classify(new QuickStackObservation(0, 0, 0, 0, 0, 0)));
            Equal(QuickStackNoOpReason.NoMatchingResources,
                QuickStackDiagnostics.Classify(new QuickStackObservation(2, 2, 0, 1, 0, 0)));
        }

		private static void FirstUseReconcilesLoadedContainers()
		{
			string root = Root();
			string index = File.ReadAllText(Path.Combine(
				root, "Runtime", "ContainerIndex.cs"));
			string actions = File.ReadAllText(Path.Combine(
				root, "Runtime", "StorageActions.cs"));
			True(index.Contains("RefreshLoadedContainers()", StringComparison.Ordinal));
			True(index.Contains("FindObjectsByType<Container>", StringComparison.Ordinal));
			True(actions.Contains(
				"_index.RefreshLoadedContainers()", StringComparison.Ordinal));
			True(actions.IndexOf("_index.RefreshLoadedContainers()", StringComparison.Ordinal) <
				actions.IndexOf("_index.Nearest(", StringComparison.Ordinal),
				"Loaded containers must be reconciled before the first spatial query.");
		}

        private static void HoverRequiresExactDisclosureEvidence()
        {
            var facts = new HoverDisclosureFacts(
                true, true, true, true, true, true, true, true, true, true, true, false);
            True(HoverDisclosurePolicy.AllowsBeforeSynchronization(facts));
            False(HoverDisclosurePolicy.Allows(facts));
        }

        private static void HoverRangeIsPhysicallyBounded()
        {
            True(HoverRangePolicy.IsWithinPhysicalReach(9f, 4f));
            False(HoverRangePolicy.IsWithinPhysicalReach(121f, 100f));
        }

        private static void MissingInventoryIsOptional()
        {
            True(StorageItemProtection.TryCapture(
                new object[] { new object(), new object() },
                out StorageProtectionSnapshot snapshot,
                out string reason));
            Equal(2, snapshot.Count);
            Equal(StorageProtectionState.NotApplicable, snapshot.StateAt(0));
            True(reason.StartsWith("ok", StringComparison.Ordinal));
        }

        private static void PackageHasNoRuntimeFoundationDependency()
        {
            string root = Root();
            string project = File.ReadAllText(Path.Combine(root, "RunicStorage.csproj"));
            string manifest = File.ReadAllText(Path.Combine(root, "manifest.json"));
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
            Reject(project, "ProjectReference", "RunicPersistence.csproj",
                "RunicTransactions.csproj", "RunicInventory.csproj");
            Reject(manifest, "RunicCore", "RunicPersistence", "RunicTransactions", "RunicInventory");
            Reject(plugin, "BepInDependency", "RunicRegistry", "RunicCoreApi");
        }

        private static void RuntimeHasNoDurableOrGlobalMutationLayer()
        {
            string root = Root();
            string source = string.Join("\n", Directory.GetFiles(
                Path.Combine(root, "Runtime"), "*.cs").Select(File.ReadAllText));
            Reject(source, "RemoteStorage", "RunicMutationGate", "Quarantine", "Journal");
        }

        private static void MutationsRequireNativeOwnership()
        {
            string root = Root();
            string authority = File.ReadAllText(Path.Combine(
                root, "Runtime", "StorageContainerAuthority.cs"));
            string service = File.ReadAllText(Path.Combine(
                root, "Runtime", "ValheimContainerService.cs"));
            True(authority.Contains("container.IsOwner()", StringComparison.Ordinal));
            True(service.Contains("requireWritable && !container.IsOwner()", StringComparison.Ordinal));
            True(service.Contains("StorageMutationLease", StringComparison.Ordinal));
        }

        private static QuickStackDestination Destination(string id, float distance, int capacity) =>
            new QuickStackDestination(id, distance, true, false, false,
                new Dictionary<string, int> { ["Wood"] = capacity });

        private static StorageRouteContext Context(
            bool mutationOwner,
            bool inventoryOpen = false,
            bool openedContainer = false) =>
            new StorageRouteContext(true, false, false, inventoryOpen, openedContainer,
                false, false, mutationOwner, mutationOwner);

        private static string Root() => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));

        private static void Reject(string source, params string[] values)
        {
            foreach (string value in values)
                False(source.Contains(value, StringComparison.Ordinal), "Unexpected token: " + value);
        }

        private static void True(bool value, string message = "Expected true.")
        { if (!value) throw new InvalidOperationException(message); }
        private static void False(bool value, string message = "Expected false.")
        { if (value) throw new InvalidOperationException(message); }
        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }
    }
}
