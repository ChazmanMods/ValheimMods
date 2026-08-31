using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicExploration.Integration;

namespace RunicExploration.Tests
{
    internal static class PrivacyAndMutationTests
    {
        internal static void Register()
        {
            TestRunner.Run("unknown gate precedes private pin metadata reads", UnknownGatePrecedesMetadata);
            TestRunner.Run("oversize pin gate precedes per-pin traversal", OversizeGatePrecedesLoop);
            TestRunner.Run("no-map gate precedes map and optional reads", NoMapGateIsFirst);
            TestRunner.Run("selection revalidates evidence before centering", SelectionRevalidates);
            TestRunner.Run("draw path uses cached state only", DrawUsesCachedState);
            TestRunner.Run(
                "typing that narrows search clamps rows to the post-input result snapshot",
                SearchNarrowingCannotIndexStaleRows);
            TestRunner.Run("Harmony surface is map-input-only", HarmonySurfaceIsUiOnly);
            TestRunner.Run("text-input postfix uses installed final-postfix order", TextPostfixOrder);
            TestRunner.Run("pointer prefixes fall through outside the panel", PointerPrefixesFallThrough);
            TestRunner.Run("compiled module has no discovery or gameplay mutators", NoForbiddenCalls);
            TestRunner.Run("optional portal adapter exposes readiness only", PortalAdapterIsStatusOnly);
            TestRunner.Run("shared-pin opt-out clears cached records immediately", SharedOptOutClearsCache);
        }

        private static void UnknownGatePrecedesMetadata()
        {
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "KnownPinSource.cs")));
            int method = source.IndexOf("KnownPinCaptureStatus Capture(", StringComparison.Ordinal);
            int next = source.IndexOf("internal static bool TryResolve(", method,
                StringComparison.Ordinal);
            string capture = source.Substring(method, next - method);
            int gate = capture.IndexOf("if (!saved || !known) continue;", StringComparison.Ordinal);
            TestAssert.True(gate >= 0);
            foreach (string member in new[] { "pin.m_name", "pin.m_type", "pin.m_checked", "pin.m_ownerID" })
                TestAssert.True(capture.IndexOf(member, StringComparison.Ordinal) > gate,
                    member + " is read before the explored/saved gate.");
            TestAssert.True(capture.IndexOf("Quantize(position.x)", StringComparison.Ordinal) > gate,
                "Unknown coordinates must not enter the retained-state fingerprint.");
            string beforeGate = capture.Substring(0, gate);
            TestAssert.DoesNotContain("Mix(fingerprint, sourceCount)", beforeGate);
            TestAssert.DoesNotContain("Mix(fingerprint, index)", beforeGate);
            TestAssert.True(capture.IndexOf("pin.m_pos", StringComparison.Ordinal) < gate);
            TestAssert.True(capture.IndexOf("pin.m_save", StringComparison.Ordinal) < gate);
        }

        private static void OversizeGatePrecedesLoop()
        {
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "KnownPinSource.cs")));
            int ceiling = source.IndexOf("sourceCount > KnownPinIndexer.HardMaximumPins",
                StringComparison.Ordinal);
            int loop = source.IndexOf("for (int index = 0; index < sourceCount; index++)",
                StringComparison.Ordinal);
            TestAssert.True(ceiling >= 0 && loop > ceiling);
        }

        private static void NoMapGateIsFirst()
        {
            MethodInfo update = typeof(ExplorationRuntime).GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            int noMap = IlReader.FirstFieldOffset(update, typeof(Game), nameof(Game.m_noMap));
            int isOpen = IlReader.FirstCallOffset(update, typeof(Minimap), nameof(Minimap.IsOpen));
            TestAssert.True(noMap >= 0 && isOpen > noMap,
                "Update reads Minimap before the no-map gate.");
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "ExplorationRuntime.cs")));
            int start = source.IndexOf("internal void Update()", StringComparison.Ordinal);
            int end = source.IndexOf("internal void Draw()", start, StringComparison.Ordinal);
            string body = source.Substring(start, end - start);
            int gate = body.IndexOf("Game.m_noMap", StringComparison.Ordinal);
            TestAssert.True(gate >= 0);
            TestAssert.True(body.IndexOf("Minimap.IsOpen", StringComparison.Ordinal) > gate);
            TestAssert.True(body.IndexOf("Minimap.instance", StringComparison.Ordinal) > gate);
            TestAssert.True(body.IndexOf("_portals.Refresh", StringComparison.Ordinal) > gate);
        }

        private static void SelectionRevalidates()
        {
            MethodInfo select = typeof(ExplorationRuntime).GetMethod(
                "SelectAndCenter", BindingFlags.Instance | BindingFlags.NonPublic);
            int validate = IlReader.FirstCallOffset(select, typeof(KnownPinSource),
                nameof(KnownPinSource.TryResolve));
            int center = IlReader.FirstCallOffset(select, typeof(Minimap),
                nameof(Minimap.ShowPointOnMap));
            TestAssert.True(validate >= 0 && center > validate);
        }

        private static void DrawUsesCachedState()
        {
            MethodInfo draw = typeof(ExplorationRuntime).GetMethod(
                "Draw", BindingFlags.Instance | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(draw);
            TestAssert.False(calls.Any(call => call.DeclaringType == typeof(KnownPinSource)));
            TestAssert.False(calls.Any(call => call.DeclaringType == typeof(ValheimContracts)));
            TestAssert.False(calls.Any(call => call.DeclaringType == typeof(Player)));
            TestAssert.False(calls.Any(call => call.DeclaringType == typeof(Ship)));
            TestAssert.False(calls.Any(call => call.DeclaringType == typeof(EnvMan)));
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "ExplorationRuntime.cs")));
            int start = source.IndexOf("internal void Draw()", StringComparison.Ordinal);
            int end = source.IndexOf("internal void OnConfigurationChanged", start,
                StringComparison.Ordinal);
            string drawSource = source.Substring(start, end - start);
            TestAssert.DoesNotContain("Category: \" +", drawSource);
            TestAssert.DoesNotContain("Source: \" +", drawSource);
        }

        private static void SearchNarrowingCannotIndexStaleRows()
        {
            TestAssert.Equal(1, ExplorationRuntime.ClampVisibleRows(24, 1, 1));
            TestAssert.Equal(0, ExplorationRuntime.ClampVisibleRows(24, 0, 0));
            TestAssert.Equal(3, ExplorationRuntime.ClampVisibleRows(12, 8, 3));
            TestAssert.Equal(2, ExplorationRuntime.ClampVisibleRows(2, 8, 8));

            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "ExplorationRuntime.cs")));
            int refresh = source.IndexOf("_query = bounded;", StringComparison.Ordinal);
            int clamp = source.IndexOf("int currentVisibleRows = ClampVisibleRows(",
                StringComparison.Ordinal);
            int loop = source.IndexOf("index < currentVisibleRows", StringComparison.Ordinal);
            TestAssert.True(refresh >= 0 && clamp > refresh && loop > clamp);
        }

        private static void HarmonySurfaceIsUiOnly()
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "InTextInput", "OnMapLeftDown", "OnMapLeftUp", "OnMapLeftClick",
                "OnMapDblClick", "OnMapMiddleClick", "OnMapRightClick"
            };
            Type[] patches = typeof(Plugin).Assembly.GetTypes()
                .Where(type => type.GetCustomAttributes<HarmonyPatch>().Any())
                .ToArray();
            TestAssert.Equal(7, patches.Length);
            foreach (Type type in patches)
            {
                HarmonyPatch patch = type.GetCustomAttributes<HarmonyPatch>().Single();
                TestAssert.Equal(typeof(Minimap), patch.info.declaringType);
                TestAssert.True(allowed.Remove(patch.info.methodName));
                TestAssert.False(type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Any(method => method.GetCustomAttribute<HarmonyTranspiler>() != null ||
                                   method.GetCustomAttribute<HarmonyFinalizer>() != null));
            }
            TestAssert.Equal(0, allowed.Count);
        }

        private static void TextPostfixOrder()
        {
            MethodInfo postfix = typeof(MinimapTextInputPatch).GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(postfix.GetCustomAttribute<HarmonyPostfix>());
            HarmonyPriority priority = TestAssert.NotNull(
                postfix.GetCustomAttribute<HarmonyPriority>());
            TestAssert.Equal(Priority.Last, priority.info.priority);

            Assembly harmony = typeof(Harmony).Assembly;
            Type serialization = TestAssert.NotNull(
                harmony.GetType("HarmonyLib.PatchInfoSerialization", false));
            MethodInfo comparer = TestAssert.NotNull(serialization.GetMethod(
                "PriorityComparer", BindingFlags.Static | BindingFlags.Public |
                                    BindingFlags.NonPublic));
            IReadOnlyList<IlInstruction> il = IlReader.Read(comparer);
            int compare = il.ToList().FindIndex(item => item.Operand is MethodInfo method &&
                method.DeclaringType == typeof(int) && method.Name == nameof(int.CompareTo));
            TestAssert.True(compare >= 0 && compare + 1 < il.Count);
            TestAssert.Equal(OpCodes.Neg, il[compare + 1].OpCode,
                "Installed Harmony no longer sorts high priorities before low priorities.");
            Type manipulator = TestAssert.NotNull(
                harmony.GetType("HarmonyLib.Public.Patching.HarmonyManipulator", false));
            MethodInfo writer = TestAssert.NotNull(manipulator.GetMethod(
                "WritePostfixes", BindingFlags.Instance | BindingFlags.NonPublic));
            TestAssert.False(IlReader.Calls(writer).Any(call => call.DeclaringType ==
                typeof(Enumerable) && call.Name == nameof(Enumerable.Reverse)));
        }

        private static void PointerPrefixesFallThrough()
        {
            foreach (Type patch in typeof(Plugin).Assembly.GetTypes().Where(type =>
                         type.GetCustomAttributes<HarmonyPatch>().Any() &&
                         type != typeof(MinimapTextInputPatch)))
            {
                MethodInfo prefix = patch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .Single(method => method.GetCustomAttribute<HarmonyPrefix>() != null);
                TestAssert.Equal(typeof(bool), prefix.ReturnType);
                TestAssert.Equal(0, prefix.GetParameters().Length);
                HarmonyPriority priority = TestAssert.NotNull(
                    prefix.GetCustomAttribute<HarmonyPriority>());
                TestAssert.Equal(Priority.First, priority.info.priority);
            }
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "HarmonyPatches.cs")));
            TestAssert.Contains("=> !BlocksPointer()", source);
            TestAssert.Contains("=>\n            !(Plugin.Instance?.Runtime?.BlocksMapPointer", source);
        }

        private static void NoForbiddenCalls()
        {
            string[] forbiddenOwners =
            {
                "WorldGenerator", "ZoneSystem", "ZDO", "ZDOMan", "ZNetScene", "Physics",
                "PhysicsScene", "Inventory", "ObjectDB"
            };
            string[] forbiddenNames =
            {
                "AddPin", "RemovePin", "RemovePinByName", "SetMapData", "AddSharedMapData",
                "Explore", "ExploreAll", "ClearPins", "InvokeRPC", "RPC", "ClaimOwnership",
                "RequestOwn", "SetOwner", "FindObjectsOfType", "FindObjectsByType",
                "FindGameObjectsWithTag", "OverlapSphere", "Raycast"
            };
            foreach (MethodInfo method in MethodsWithBodies(typeof(Plugin).Assembly))
            foreach (MethodBase call in IlReader.Calls(method))
            {
                string owner = call.DeclaringType?.Name ?? string.Empty;
                TestAssert.False(forbiddenOwners.Contains(owner, StringComparer.Ordinal),
                    method.DeclaringType?.Name + "." + method.Name + " calls " + owner + "." +
                    call.Name + ".");
                TestAssert.False(forbiddenNames.Contains(call.Name, StringComparer.Ordinal),
                    method.DeclaringType?.Name + "." + method.Name + " calls " + call.Name + ".");
            }
        }

        private static void PortalAdapterIsStatusOnly()
        {
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "OptionalPortalAdapter.cs")));
            TestAssert.Contains("Chainloader.PluginInfos.TryGetValue", source);
            TestAssert.Contains("\"General\", \"Enabled\"", source);
            foreach (string forbidden in new[]
                     {
                         "RunicRegistry", "Capability", "ProtocolVersion",
                         "portals.directory", "IndexedEndpointCount", ".Query(",
                         "Network", "Destination", "Endpoint"
                     })
                TestAssert.DoesNotContain(forbidden, source);
        }

        private static void SharedOptOutClearsCache()
        {
            string source = File.ReadAllText(TestPaths.Plugin(
                Path.Combine("Integration", "ExplorationRuntime.cs")));
            int start = source.IndexOf("internal void OnConfigurationChanged",
                StringComparison.Ordinal);
            int end = source.IndexOf("internal void FailClosed", start,
                StringComparison.Ordinal);
            string method = source.Substring(start, end - start);
            int sharedKey = method.IndexOf("IncludeSharedPins", StringComparison.Ordinal);
            int clear = method.IndexOf("ClearIndex();", sharedKey, StringComparison.Ordinal);
            TestAssert.True(sharedKey >= 0 && clear > sharedKey,
                "Shared-pin opt-out can leave stale shared rows cached until the next refresh.");
        }

        private static IEnumerable<MethodInfo> MethodsWithBodies(Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (method.GetMethodBody() != null) yield return method;
        }
    }
}
