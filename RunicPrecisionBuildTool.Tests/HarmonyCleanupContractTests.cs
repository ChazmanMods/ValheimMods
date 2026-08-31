using System;
using System.IO;

namespace QuietBuildRotation.Tests
{
    internal static class HarmonyCleanupContractTests
    {
        internal static void Register()
        {
            TestRunner.Run(
                "Placement Harmony patches have exception finalizers",
                PlacementPatchesHaveFinalizers);
            TestRunner.Run(
                "Interrupted placement cleanup rejects partial frame state",
                CleanupContractsAreExact);
        }

        private static void PlacementPatchesHaveFinalizers()
        {
            string source = ReadSource("Plugin.cs");
            string inputPatch = Slice(
                source,
                "internal static class PlayerUpdatePlacementPatch",
                "[HarmonyPatch(typeof(Player), \"SetupPlacementGhost\")]"
            );
            string ghostPatch = Slice(
                source,
                "internal static class PlayerUpdatePlacementGhostPatch",
                "[HarmonyPatch(typeof(Hud), \"UpdateBuild\""
            );

            Contains(inputPatch, "private static Exception Finalizer(");
            Contains(inputPatch, "PlacementRuntime.AbortPlacementInput(__instance)");
            Contains(inputPatch, "return __exception;");
            Contains(ghostPatch, "private static Exception Finalizer(Exception __exception)");
            Contains(ghostPatch, "PlacementRuntime.AbortPlacementGhost()");
            Contains(ghostPatch, "return __exception;");
        }

        private static void CleanupContractsAreExact()
        {
            string runtime = ReadSource(Path.Combine("Integration", "PlacementRuntime.cs"));
            string inspector = ReadSource(Path.Combine("Integration", "TargetInspector.cs"));
            string inputAbort = Slice(
                runtime,
                "internal static void AbortPlacementInput",
                "internal static void OnPlacementGhostSetup");
            string ghostAbort = Slice(
                runtime,
                "internal static void AbortPlacementGhost",
                "internal static Quaternion ComposeCandidateRotation");
            string captureAbort = Slice(
                inspector,
                "internal void AbortPlacementUpdate",
                "internal bool Refresh");

            Contains(inputAbort, "SetPlaceRotation(player, _yawBeforeInput)");
            Contains(inputAbort, "SetScrollAmount(player, _scrollBeforeInput)");
            Contains(inputAbort, "_framePlayer = null;");
            Contains(inputAbort, "_consumeWheel = false;");
            Contains(ghostAbort, "_targetInspector.AbortPlacementUpdate()");
            Contains(ghostAbort, "AxisGuidePresenter.Hide()");
            Contains(captureAbort, "_captureOpen = false;");
            Contains(captureAbort, "ClearCapture()");
        }

        private static string ReadSource(string relative)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string path = Path.Combine(root, "RunicPrecisionBuildTool", relative);
            return File.ReadAllText(path);
        }

        private static string Slice(string source, string start, string end)
        {
            int startIndex = source.IndexOf(start, StringComparison.Ordinal);
            TestAssert.True(startIndex >= 0, "Missing source anchor: " + start);
            int endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
            TestAssert.True(endIndex > startIndex, "Missing source end anchor: " + end);
            return source.Substring(startIndex, endIndex - startIndex);
        }

        private static void Contains(string source, string expected) =>
            TestAssert.True(
                source.IndexOf(expected, StringComparison.Ordinal) >= 0,
                "Missing required cleanup contract: " + expected);
    }
}
