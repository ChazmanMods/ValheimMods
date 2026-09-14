using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Splatform;

namespace QuietBuildRotation.Tests
{
    internal static class ReleaseContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("Precision Build runtime and package are 2.0.4", VersionIdentityIsExact);
            TestRunner.Run("startup reports the actual Valheim build instead of a fixed label", StartupVersionIsExact);
            TestRunner.Run("manifest has BepInEx as its only dependency", ManifestDependenciesAreExact);
            TestRunner.Run("module has no Runic runtime assembly references", FoundationReferencesAreExact);
            TestRunner.Run("placement patch ordering composes with Camera, Crafting, and Agriculture", PatchOrderingIsExact);
            TestRunner.Run("module never replaces vanilla place, remove, or repair commits", NativeMutationBoundariesRemainUnpatched);
            TestRunner.Run("placement capture closes when precision mode is toggled off", CaptureScopeTracksExplicitMode);
            TestRunner.Run("precision toggle is frame-polled and routes same-frame pitch", PrecisionToggleAndPitchAreImmediate);
            TestRunner.Run("precision mode is gated to the equipped Hammer build table", PrecisionIsHammerOnly);
            TestRunner.Run("Runic info augments the native panel without breaking the piece menu", BuildInfoAugmentationPreservesMenu);
            TestRunner.Run("repeat and undo history clear at the hammer-session boundary", HistoryScopeIsBounded);
            TestRunner.Run("native-owner mutation code retains capacity, range, durability, and one-shot guards", MutationRuntimeIsConservative);
            TestRunner.Run("area repair binds and invokes the Valheim 1.0 inventory notification exactly", InventoryNotificationBindingIsExact);
            TestRunner.Run("safe utilities contain no remote or durable operation surface", RemoteUndoBoundaryIsDurable);
            TestRunner.Run("catalog localization uses bounded direct Translate only", CatalogLocalizationIsBounded);
            TestRunner.Run("release documentation states native-owner utility behavior truthfully", DocumentationIsTruthful);
            TestRunner.Run("configuration example exposes every 2.0.4 release control", ConfigurationExampleIsComplete);
            TestRunner.Run("published icon is an exact 256 by 256 PNG", IconDimensionsAreExact);
        }

        private static void VersionIdentityIsExact()
        {
            TestAssert.Equal("chazman.RunicPrecisionBuildTool", Plugin.Guid);
            TestAssert.Equal("2.0.4", Plugin.Version);
            Assembly assembly = typeof(Plugin).Assembly;
            TestAssert.Equal(new System.Version(2, 0, 4, 0), assembly.GetName().Version);
            TestAssert.Equal("Runic Precision Build Tool",
                assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
            TestAssert.Equal("2.0.4",
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Module("manifest.json")));
            TestAssert.Equal("RunicPrecisionBuildTool", json.RootElement.GetProperty("name").GetString());
            TestAssert.Equal("2.0.4", json.RootElement.GetProperty("version_number").GetString());
        }

        private static void StartupVersionIsExact()
        {
            TestAssert.Equal("1.0.12", Diagnostics.GetValheimVersion());
            string plugin = File.ReadAllText(Module("Plugin.cs"));
            string diagnostics = File.ReadAllText(Module("Diagnostics.cs"));
            TestAssert.True(plugin.Contains(
                "ready on Valheim", StringComparison.Ordinal));
            TestAssert.True(diagnostics.Contains("global::Version.CurrentVersion.ToString()", StringComparison.Ordinal));
            TestAssert.False(diagnostics.Contains(
                "Application.version", StringComparison.Ordinal));
        }

        private static void ManifestDependenciesAreExact()
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Module("manifest.json")));
            string[] dependencies = json.RootElement.GetProperty("dependencies")
                .EnumerateArray().Select(item => item.GetString()).ToArray();
            TestAssert.Equal(1, dependencies.Length);
            TestAssert.Equal("denikson-BepInExPack_Valheim-5.4.2350", dependencies[0]);
        }

        private static void FoundationReferencesAreExact()
        {
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name.StartsWith("Runic", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            TestAssert.Equal(0, references.Length,
                "Unexpected Runic assembly dependency set: " + string.Join(", ", references));
        }

        private static void PatchOrderingIsExact()
        {
            MethodInfo updatePrefix = typeof(PlayerUpdatePlacementPatch).GetMethod(
                "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo ghostPrefix = typeof(PlayerUpdatePlacementGhostPatch).GetMethod(
                "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo ghostPostfix = typeof(PlayerUpdatePlacementGhostPatch).GetMethod(
                "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            TestAssert.True(updatePrefix != null && ghostPrefix != null && ghostPostfix != null);
            AssertAfter(updatePrefix, "chazman.RunicBuildCamera");
            AssertBefore(updatePrefix, "chazman.RunicCrafting");
            AssertAfter(ghostPrefix, "chazman.RunicBuildCamera");
            AssertBefore(ghostPostfix, "chazman.RunicAgriculture");
            AssertPriority(updatePrefix, Priority.Normal);
            AssertPriority(ghostPrefix, Priority.Normal);
            AssertPriority(ghostPostfix, Priority.Normal);

            HarmonyPatch creatorTarget = typeof(PieceSetCreatorPlacementObserverPatch)
                .GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(Piece), creatorTarget.info.declaringType);
            TestAssert.Equal(nameof(Piece.SetCreator), creatorTarget.info.methodName);
            TestAssert.True(creatorTarget.info.argumentTypes.SequenceEqual(
                new[] { typeof(long), typeof(PlatformUserID) }));
            string adapter = File.ReadAllText(Module(
                Path.Combine("Integration", "PlacementAdapter.cs")));
            TestAssert.True(adapter.Contains(
                "new[] { typeof(long), typeof(PlatformUserID) }",
                StringComparison.Ordinal));
        }

        private static void NativeMutationBoundariesRemainUnpatched()
        {
            string[] forbidden = { "PlacePiece", "TryPlacePiece", "RemovePiece", "Repair" };
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
            {
                foreach (HarmonyPatch patch in type.GetCustomAttributes<HarmonyPatch>())
                {
                    TestAssert.False(
                        patch.info.declaringType == typeof(Player) &&
                        forbidden.Contains(patch.info.methodName, StringComparer.Ordinal),
                        type.Name + " patches native Player." + patch.info.methodName + ".");
                }
            }
        }

        private static void CatalogLocalizationIsBounded()
        {
            string source = File.ReadAllText(Module(Path.Combine("Integration", "BuildCatalogRuntime.cs")));
            TestAssert.True(source.Contains("MaximumLocalizationTokenLength = 128", StringComparison.Ordinal));
            TestAssert.True(source.Contains("_translate(Localization.instance, key)", StringComparison.Ordinal));
            TestAssert.False(source.Contains("Localization.instance.Localize(", StringComparison.Ordinal));
            TestAssert.True(source.Contains("MaximumDisplayLength", StringComparison.Ordinal));
            TestAssert.True(source.Contains("m_gridWidth", StringComparison.Ordinal));
            TestAssert.False(source.Contains("Columns = 15", StringComparison.Ordinal));
        }

        private static void CaptureScopeTracksExplicitMode()
        {
            string source = File.ReadAllText(Module(Path.Combine("Integration", "PlacementRuntime.cs")));
            TestAssert.True(source.Contains(
                "BeginPlacementCapture(player, selectedPiece, inputGate && precisionActive)",
                StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "Utils.GetPrefabName(piece.gameObject)",
                StringComparison.Ordinal));
            TestAssert.False(source.Contains("_capturePieceName", StringComparison.Ordinal));
        }

        private static void PrecisionToggleAndPitchAreImmediate()
        {
            MethodInfo before = typeof(PlacementRuntime).GetMethod(
                "BeforePlacementInput", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo update = typeof(PlacementRuntime).GetMethod(
                "UpdateUtilities", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(before != null && update != null);

            MethodBase[] beforeCalls = IlReader.Calls(before).ToArray();
            int toggle = Array.FindIndex(beforeCalls, call =>
                call.DeclaringType == typeof(PlacementRuntime) &&
                call.Name == "TogglePrecisionMode");
            int route = Array.FindIndex(beforeCalls, call =>
                call.DeclaringType == typeof(InputRouter) &&
                call.Name == "ProcessFrame");
            TestAssert.True(toggle >= 0 && route > toggle,
                "The activation edge must precede wheel routing so immediate pitch is not discarded.");
            TestAssert.True(IlReader.Calls(
                update, typeof(PlacementRuntime), "TryTogglePrecisionModeFromUpdate"),
                "P is still sampled only from Player.UpdatePlacement instead of every plugin frame.");

            string source = File.ReadAllText(Module(
                Path.Combine("Integration", "PlacementRuntime.cs")));
            TestAssert.True(source.Contains(
                "The shared ValheimInputSource consumes the edge once",
                StringComparison.Ordinal));
        }

        private static void PrecisionIsHammerOnly()
        {
            string adapter = File.ReadAllText(Module(
                Path.Combine("Integration", "PlacementAdapter.cs")));
            string runtime = File.ReadAllText(Module(
                Path.Combine("Integration", "PlacementRuntime.cs")));
            TestAssert.True(adapter.Contains("IsHammerBuildMode", StringComparison.Ordinal));
            TestAssert.True(adapter.Contains("$item_hammer", StringComparison.Ordinal));
            TestAssert.True(adapter.Contains(
                "ReferenceEquals(itemTable, _buildPieces(player))",
                StringComparison.Ordinal));
            TestAssert.True(runtime.Contains(
                "PlacementAdapter.IsHammerBuildMode(player)",
                StringComparison.Ordinal));
        }

        private static void BuildInfoAugmentationPreservesMenu()
        {
            Type patch = typeof(Plugin).Assembly.GetType(
                "QuietBuildRotation.HudUpdateBuildPrecisionInfoPatch");
            TestAssert.True(patch != null, "Hud.UpdateBuild augmentation patch is missing.");
            HarmonyPatch harmony = patch.GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(Hud), harmony.info.declaringType);
            TestAssert.Equal("UpdateBuild", harmony.info.methodName);

            MethodInfo postfix = patch.GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                postfix, typeof(PlacementRuntime), "AfterHudUpdateBuild"));

            string runtime = File.ReadAllText(Module(
                Path.Combine("Integration", "PlacementRuntime.cs")));
            string presenter = File.ReadAllText(Module(
                Path.Combine("UI", "OrientationPresenter.cs")));
            TestAssert.True(runtime.Contains(
                "OrientationPresenter.Attach(hud)",
                StringComparison.Ordinal));
            TestAssert.True(runtime.Contains(
                "Hud.IsPieceSelectionVisible()",
                StringComparison.Ordinal));
            TestAssert.False(runtime.Contains("m_buildHud.SetActive", StringComparison.Ordinal));
            TestAssert.False(runtime.Contains("buildHud.SetActive", StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "hud.m_pieceDescription.gameObject",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "rect.SetParent(selectedInfo, false)",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "_selectedInfoRect.sizeDelta = desiredSize",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "_readoutRoot.offsetMin = new Vector2",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "_selectedInfoBackground.raycastTarget = false",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "RestorePanelGeometry()",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains(
                "TextWrappingModes.Normal",
                StringComparison.Ordinal));
            TestAssert.True(presenter.Contains("<b>PRECISION</b>", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("MinimumReadoutWidth", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("MaximumReadoutWidth", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("KeyHints", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("PLACE · ", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("COST ", StringComparison.Ordinal));
            TestAssert.False(presenter.Contains("STATION ", StringComparison.Ordinal));

            Type awakePatch = typeof(Plugin).Assembly.GetType(
                "QuietBuildRotation.HudAwakePrecisionInfoPatch");
            TestAssert.True(awakePatch != null, "Hud.Awake panel attachment patch is missing.");
            HarmonyPatch awakeHarmony = awakePatch.GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(Hud), awakeHarmony.info.declaringType);
            TestAssert.Equal("Awake", awakeHarmony.info.methodName);
        }

        private static void HistoryScopeIsBounded()
        {
            string runtime = File.ReadAllText(Module(Path.Combine("Integration", "PlacementRuntime.cs")));
            int start = runtime.IndexOf("private static void EndPlacementSession()", StringComparison.Ordinal);
            TestAssert.True(start >= 0);
            string scope = runtime.Substring(start, Math.Min(800, runtime.Length - start));
            TestAssert.True(scope.Contains("PlacementHistory.Clear()", StringComparison.Ordinal));
            TestAssert.True(scope.Contains("BuildingMutationRuntime.EndPlacementSession()", StringComparison.Ordinal));
        }

        private static void MutationRuntimeIsConservative()
        {
            string source = File.ReadAllText(Module(
                Path.Combine("Integration", "BuildingMutationRuntime.cs")));
            foreach (string required in new[]
                     {
                         "Physics.OverlapSphereNonAlloc(",
                         "hitCount >= ColliderCapacity",
                         "Mathf.Min(radius, nativeRange)",
                         "_getPlaceDurability(player, tool)",
                         "view && view.IsValid() && view.IsOwner()",
                         "PrivateArea.CheckAccess(position, 0f, false, false)",
                         "!Location.IsInsideNoBuildLocation(position)",
                         "Array.Clear(ColliderBuffer",
                         "Array.Clear(RepairCandidates"
                     })
                TestAssert.True(source.Contains(required, StringComparison.Ordinal),
                    "Mutation runtime lost guard: " + required);

            int clear = source.IndexOf("_undo = default;\n            try", StringComparison.Ordinal);
            int remove = source.IndexOf("wear.Remove(false)", StringComparison.Ordinal);
            TestAssert.True(clear >= 0 && remove > clear,
                "Undo record must clear before native removal starts.");
            TestAssert.False(source.Contains("ZNet.instance.IsServer()", StringComparison.Ordinal));
            TestAssert.False(source.Contains("SetOwner(", StringComparison.Ordinal));
            TestAssert.False(source.Contains("ClaimOwnership(", StringComparison.Ordinal));
        }

        private static void InventoryNotificationBindingIsExact()
        {
            string source = File.ReadAllText(Module(
                Path.Combine("Integration", "BuildingMutationRuntime.cs")));
            TestAssert.True(source.Contains(
                "typeof(Inventory), \"Changed\", new[] { typeof(bool), typeof(bool) }",
                StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "_inventoryChanged?.Invoke(inventory, false, false)",
                StringComparison.Ordinal));
            TestAssert.False(source.Contains(
                "typeof(Inventory), \"Changed\", Type.EmptyTypes",
                StringComparison.Ordinal));
        }

        private static void RemoteUndoBoundaryIsDurable()
        {
            string project = File.ReadAllText(Module("RunicPrecisionBuildTool.csproj"));
            string plugin = File.ReadAllText(Module("Plugin.cs"));
            string runtime = File.ReadAllText(Module(
                Path.Combine("Integration", "BuildingMutationRuntime.cs")));
            TestAssert.False(project.Contains("ProjectReference", StringComparison.Ordinal));
            foreach (string forbidden in new[]
                     {
                         "RunicRegistry", "RunicCapabilityIds", "IRunicRpcService",
                         "RemoteBuildingMutationRuntime", "ZRoutedRpc", "SetOwner(",
                         "ClaimOwnership(", "journal", "quarantine", "custody"
                     })
            {
                TestAssert.False(plugin.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "Plugin retains obsolete runtime coupling: " + forbidden);
                TestAssert.False(runtime.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "Mutation runtime retains obsolete operation state: " + forbidden);
            }
            foreach (string removed in new[]
                     {
                         "RemoteBuildingMutationRuntime.cs", "BuildingMutationRemoteAdapter.cs",
                         "PrecisionDurableClientAdapter.cs", "PrecisionWorldObjectMutationProvider.cs",
                         "RemoteUndoJournal.cs", "PrecisionDurableProtocol.cs"
                     })
                TestAssert.False(File.Exists(Module(Path.Combine("Integration", removed))) ||
                                 File.Exists(Module(Path.Combine("Core", removed))),
                    "Obsolete durable source remains: " + removed);
        }

        private static void DocumentationIsTruthful()
        {
            string combined = string.Join("\n", new[]
            {
                File.ReadAllText(Module("README.md")),
                File.ReadAllText(Module("INSTRUCTIONS.md")),
                File.ReadAllText(Module("TESTING.md"))
            });
            foreach (string required in new[]
                     {
                         "2.0.4", "bounded", "dedicated", "native", "vanilla",
                         "undo", "area repair", "favorites", "recent", "snap",
                         "current owner", "BepInEx", "standalone"
                     })
                TestAssert.True(combined.IndexOf(required, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Release documentation omits: " + required);
        }

        private static void ConfigurationExampleIsComplete()
        {
            string config = File.ReadAllText(Module("RunicPrecisionBuildTool.cfg.example"));
            string source = File.ReadAllText(Module("Configuration.cs"));
            foreach (string key in new[]
                     {
                         "RequirePrecisionMode", "PrecisionModeToggle", "ReferenceFrame",
                         "MatchPositionX", "MatchPositionY", "MatchPositionZ", "MatchPosition",
                         "MatchTransform", "MatchSnapSide", "RepeatTransform",
                         "ResetPitch", "ResetRoll", "ResetYaw", "ResetSway", "ResetHeave",
                         "ResetSurge", "SearchQuery", "ToggleFavorite", "NextFavorite",
                         "NextRecent", "UndoLastPlacement", "AreaRepair"
                     })
                TestAssert.True(config.Contains(key, StringComparison.Ordinal),
                    "Configuration example omits " + key + ".");
            TestAssert.False(source.Contains("Host-only area-repair", StringComparison.Ordinal));
            TestAssert.False(source.Contains("Host/listen-server only", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "native local-owner checks", StringComparison.Ordinal));
        }

        private static void IconDimensionsAreExact()
        {
            byte[] png = File.ReadAllBytes(Module(Path.Combine("media", "icon.png")));
            TestAssert.True(png.Length >= 24);
            TestAssert.Equal(0x89, (int)png[0]);
            int width = ReadBigEndianInt32(png, 16);
            int height = ReadBigEndianInt32(png, 20);
            TestAssert.Equal(256, width);
            TestAssert.Equal(256, height);
        }

        private static void AssertAfter(MethodInfo method, string guid)
        {
            HarmonyAfter attribute = method.GetCustomAttribute<HarmonyAfter>();
            TestAssert.True(attribute?.info.after?.Contains(guid) == true,
                method.Name + " must run after " + guid + ".");
        }

        private static void AssertBefore(MethodInfo method, string guid)
        {
            HarmonyBefore attribute = method.GetCustomAttribute<HarmonyBefore>();
            TestAssert.True(attribute?.info.before?.Contains(guid) == true,
                method.Name + " must run before " + guid + ".");
        }

        private static void AssertPriority(MethodInfo method, int expected)
        {
            HarmonyPriority attribute = method.GetCustomAttribute<HarmonyPriority>();
            TestAssert.Equal(expected, attribute?.info.priority ?? int.MinValue);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 | bytes[offset + 3];

        private static string Module(string relative)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.Combine(root, "RunicPrecisionBuildTool", relative);
        }
    }
}
