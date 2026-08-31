using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicAwareness.Integration;

namespace RunicAwareness.Tests
{
    internal static class SafetyAndPerformanceTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin contains no gameplay mutator calls", NoGameplayMutatorCalls);
            TestRunner.Run("plugin performs no inventory or world comfort scans", NoInventoryOrWorldScans);
            TestRunner.Run("portal panel detection uses only BepInEx metadata", PortalDetectionUsesBepInEx);
            TestRunner.Run("retired Production service bridge is absent", RetiredProductionBridgeIsAbsent);
            TestRunner.Run("draw path carries no state scans or arrays", DrawPathIsCached);
            TestRunner.Run("safe-area height changes invalidate cached layout", SafeAreaHeightInvalidatesLayout);
            TestRunner.Run("hard scan and output caps are release constants", HardCapsAreExact);
            TestRunner.Run("capture windows expire bounded stale state", CaptureWindowsAreBounded);
            TestRunner.Run("inventory captures cannot cross local players", InventoryCaptureIsPlayerBound);
            TestRunner.Run("all labels bypass expanding localization parser", AllLocalizationBypassesExpandingParser);
            TestRunner.Run("optional ward disclosure is bounded and never flashes", WardDisclosureIsBoundedAndObservational);
            TestRunner.Run("unsheltered comfort never reads stale scratch pieces", UnshelteredComfortSkipsScratchPieces);
            TestRunner.Run("unchanged vanilla comfort passes refresh capture age", UnchangedComfortRefreshesAge);
            TestRunner.Run("equipment comparison never crosses unmatched slots", EquipmentComparisonDoesNotCrossSlots);
            TestRunner.Run("player loss invalidates cached signatures", PlayerLossCannotReuseStalePanels);
            TestRunner.Run("building details require avatar reach and strict ward access", BuildingDetailsRequireDisclosureGate);
        }

        private static void NoGameplayMutatorCalls()
        {
            var forbidden = new HashSet<string>(StringComparer.Ordinal)
            {
                "Player.ClearFood", "Player.EatFood", "Player.RemoveOneFood",
                "SEMan.AddStatusEffect", "SEMan.RemoveStatusEffect", "SEMan.RemoveAllStatusEffects",
                "Humanoid.EquipItem", "Humanoid.UnequipItem", "Humanoid.UnequipAllItems",
                "Humanoid.ConsumeItem", "Humanoid.UseItem",
                "Inventory.AddItem", "Inventory.RemoveItem", "Inventory.RemoveAll",
                "Inventory.MoveItemToThis", "Inventory.Changed",
                "ZDO.Set", "ZDO.Reset", "ZNetView.InvokeRPC", "ZNetView.ClaimOwnership",
                "Character.Damage", "Character.Heal", "WearNTear.Repair", "WearNTear.Remove"
            };
            foreach (MethodInfo method in PluginMethods())
            foreach (MethodBase call in IlReader.Calls(method))
            {
                string key = call.DeclaringType?.Name + "." + call.Name;
                TestAssert.False(forbidden.Contains(key),
                    method.DeclaringType?.FullName + "." + method.Name + " calls " + key + ".");
            }
        }

        private static void NoInventoryOrWorldScans()
        {
            foreach (MethodInfo method in PluginMethods())
            foreach (MethodBase call in IlReader.Calls(method))
            {
                bool inventoryScan = call.DeclaringType == typeof(Inventory) &&
                                     (call.Name == nameof(Inventory.GetAllItems) ||
                                      call.Name == "GetAllItemsInGridOrder");
                bool comfortScan = call.DeclaringType == typeof(Piece) &&
                                   call.Name == nameof(Piece.GetAllComfortPiecesInRadius);
                TestAssert.False(inventoryScan || comfortScan,
                    method.DeclaringType?.FullName + "." + method.Name +
                    " introduced an unbounded scan.");
            }
        }

        private static void PortalDetectionUsesBepInEx()
        {
            string portals = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\OptionalPortalPanelAdapter.cs"));
            TestAssert.Contains("Chainloader.PluginInfos.TryGetValue", portals);
            TestAssert.Contains("\"Display\", \"ShowSetupPanel\"", portals);
            TestAssert.Contains("GetComponentInParent<TeleportWorld>()", portals);
            TestAssert.Contains("age >= 0 && age <= 1", portals,
                "Portal setup suppression must cover the guide's one-frame hover retention only.");
            TestAssert.DoesNotContain("using RunicPortals", portals,
                "Optional portal-panel detection must not create a peer assembly dependency.");
            TestAssert.DoesNotContain("RunicRegistry", portals);
            TestAssert.DoesNotContain("Capability", portals);
        }

        private static void RetiredProductionBridgeIsAbsent()
        {
            TestAssert.False(System.IO.File.Exists(
                TestPaths.PluginFile("Integration\\OptionalProductionAdapter.cs")));
            string runtime = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\AwarenessRuntime.cs"));
            foreach (string forbidden in new[]
                     {
                         "IProductionLinkService", "ProductionIntegrationApi",
                         "production.explicit-links", "_production.TryGetStatus"
                     })
                TestAssert.DoesNotContain(forbidden, runtime);
        }

        private static void DrawPathIsCached()
        {
            MethodInfo draw = typeof(AwarenessRuntime).GetMethod(
                "Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            TestAssert.False(IlReader.HasNewArray(draw));
            TestAssert.False(IlReader.Calls(draw, typeof(Inventory), nameof(Inventory.GetAllItems)));
            TestAssert.False(IlReader.Calls(draw, typeof(SEMan), nameof(SEMan.GetStatusEffects)));
            TestAssert.False(IlReader.Calls(draw, typeof(Player), nameof(Player.GetFoods)));
            TestAssert.False(IlReader.Calls(draw, typeof(Localization), nameof(Localization.Localize)));
        }

        private static void HardCapsAreExact()
        {
            TestAssert.Equal(8, AwarenessConfig.HardMaximumFoodScan);
            TestAssert.Equal(32, AwarenessConfig.HardMaximumEffectScan);
            TestAssert.Equal(64, AwarenessConfig.HardMaximumComfortPieces);
            TestAssert.Equal(40, AwarenessConfig.HardMaximumPanelLines);
            TestAssert.Equal(4096, AwarenessConfig.HardMaximumPanelCharacters);
            TestAssert.Equal(8192, ContextCapture.MaximumVisibleTextCharacters);
            TestAssert.Equal(128, BoundedLocalization.MaximumTokenCharacters);
            TestAssert.Equal(4096, StrictWardDisclosure.MaximumWardAreasExamined);
        }

        private static void SafeAreaHeightInvalidatesLayout()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\AwarenessRuntime.cs"));
            TestAssert.Contains("maximumHeight - _lastMaximumHeight", source);
            int cache = source.IndexOf("_cachedHeight = Mathf.Min(", StringComparison.Ordinal);
            int limit = source.IndexOf("maximumHeight,", cache, StringComparison.Ordinal);
            TestAssert.True(cache >= 0 && limit > cache);
        }

        private static void CaptureWindowsAreBounded()
        {
            TestAssert.True(ContextCapture.MaximumAgeSeconds <= 1f);
            TestAssert.True(HoverItemCapture.MaximumAgeSeconds <= 1f);
            TestAssert.True(ComfortCapture.MaximumAgeSeconds <= 5f);
        }

        private static void AllLocalizationBypassesExpandingParser()
        {
            foreach (MethodInfo method in PluginMethods())
                TestAssert.False(IlReader.Calls(
                    method,
                    typeof(Localization),
                    nameof(Localization.Localize)),
                    method.DeclaringType?.FullName + "." + method.Name +
                    " calls Localization.Localize.");
        }

        private static void InventoryCaptureIsPlayerBound()
        {
            MethodInfo tryGet = typeof(HoverItemCapture).GetMethod(
                nameof(HoverItemCapture.TryGet),
                BindingFlags.Static | BindingFlags.NonPublic);
            ParameterInfo[] parameters = TestAssert.NotNull(tryGet).GetParameters();
            TestAssert.Equal(typeof(Player), parameters[0].ParameterType);
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\ContextCapture.cs"));
            TestAssert.Contains("_sourcePlayerInstanceId == player.GetInstanceID()", source);
        }

        private static void UnshelteredComfortSkipsScratchPieces()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\ComfortCapture.cs"));
            TestAssert.Contains(
                "var pieces = sheltered ? field.GetValue(null) as List<Piece> : null;",
                source,
                "Unsheltered vanilla calculations leave s_tempPieces stale and must not read it.");
            TestAssert.Contains("captured.SourceInstanceId == player.GetInstanceID()", source,
                "Comfort explanations must be tied to the current local player instance.");
        }

        private static void WardDisclosureIsBoundedAndObservational()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\StrictWardDisclosure.cs"));
            TestAssert.Contains("if (count > MaximumWardAreasExamined) return false;", source);
            TestAssert.DoesNotContain("PrivateArea.CheckAccess", source,
                "Disclosure must not invoke the ward-flash-capable convenience path.");
            TestAssert.DoesNotContain("Trigger", source,
                "Observational disclosure must never emit a ward effect.");
        }

        private static void PlayerLossCannotReuseStalePanels()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\AwarenessRuntime.cs"));
            int clear = source.IndexOf("private void ClearAll()", StringComparison.Ordinal);
            int reset = source.IndexOf("ResetSignatures();", clear, StringComparison.Ordinal);
            TestAssert.True(clear >= 0 && reset > clear,
                "A cleared panel cache must not suppress the next player's identical signature.");
        }

        private static void UnchangedComfortRefreshesAge()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\ComfortCapture.cs"));
            int unchanged = source.IndexOf(
                "_current.Version > 0 && _current.Signature == signature",
                StringComparison.Ordinal);
            int refreshed = source.IndexOf(
                "UnityEngine.Time.unscaledTime",
                unchanged,
                StringComparison.Ordinal);
            int returned = source.IndexOf("return;", unchanged, StringComparison.Ordinal);
            TestAssert.True(unchanged >= 0 && refreshed > unchanged && returned > refreshed,
                "An unchanged completed vanilla pass must refresh capture age before returning.");
        }

        private static void EquipmentComparisonDoesNotCrossSlots()
        {
            string source = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\ValheimContracts.cs"));
            int start = source.IndexOf("internal static ItemDrop.ItemData EquippedFor", StringComparison.Ordinal);
            int end = source.IndexOf("private static ItemDrop.ItemData ReadItem", start, StringComparison.Ordinal);
            TestAssert.True(start >= 0 && end > start);
            string method = source.Substring(start, end - start);
            TestAssert.Contains("? left", method);
            TestAssert.Contains(": null;", method,
                "An unmatched selected type must compare against an empty slot, not an unrelated weapon.");
            TestAssert.DoesNotContain(": right;", method);
        }

        private static void BuildingDetailsRequireDisclosureGate()
        {
            string runtime = System.IO.File.ReadAllText(
                TestPaths.PluginFile("Integration\\AwarenessRuntime.cs"));
            int piece = runtime.IndexOf("if (buildingPiece != null)", StringComparison.Ordinal);
            int gate = runtime.IndexOf(
                "AllowsDetailedDisclosure(player, buildingPiece)", piece, StringComparison.Ordinal);
            int details = runtime.IndexOf("UpdateBuilding(buildingPiece, now)", piece,
                StringComparison.Ordinal);
            int unavailable = runtime.IndexOf("UpdateBuildingUnavailable()", piece,
                StringComparison.Ordinal);
            TestAssert.True(piece >= 0 && gate > piece && details > gate && unavailable > details,
                "Building health/transform must be gated before it is read or formatted.");
            TestAssert.Contains(
                "Building details unavailable outside avatar interaction reach or without current ward access.",
                runtime);
        }

        private static IEnumerable<MethodInfo> PluginMethods() =>
            typeof(Plugin).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.GetMethodBody() != null);
    }
}
