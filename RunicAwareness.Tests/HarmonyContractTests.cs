using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicAwareness.Integration;

namespace RunicAwareness.Tests
{
    internal static class HarmonyContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("Harmony surface is postfix-only", HarmonySurfaceIsPostfixOnly);
            TestRunner.Run("capture postfixes never receive result by reference", ResultsAreNeverWritable);
            TestRunner.Run("item hook targets exact vanilla tooltip builder", ItemHookIsExact);
            TestRunner.Run("comfort hook targets exact local-player overload", ComfortHookIsExact);
            TestRunner.Run("context hooks target only visible hover getters", ContextHooksAreVisibleOnly);
            TestRunner.Run("production switch capture includes fermenters", ProductionSwitchIncludesFermenters);
            TestRunner.Run("all postfixes observe final composed UI results", PostfixesObserveFinalResults);
            TestRunner.Run("installed Harmony emits low-priority postfixes last", InstalledPostfixOrderingIsExact);
        }

        private static void HarmonySurfaceIsPostfixOnly()
        {
            foreach (Type patch in PatchTypes())
            {
                TestAssert.True(patch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .Any(method => method.GetCustomAttribute<HarmonyPostfix>() != null),
                    patch.Name + " has no postfix.");
                TestAssert.False(patch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .Any(method => method.GetCustomAttribute<HarmonyPrefix>() != null ||
                                   method.GetCustomAttribute<HarmonyTranspiler>() != null ||
                                   method.GetCustomAttribute<HarmonyFinalizer>() != null),
                    patch.Name + " carries a gameplay-capable Harmony hook.");
            }
        }

        private static void ResultsAreNeverWritable()
        {
            foreach (Type patch in PatchTypes())
            foreach (MethodInfo method in patch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (method.GetCustomAttribute<HarmonyPostfix>() == null) continue;
                ParameterInfo result = method.GetParameters().FirstOrDefault(
                    parameter => parameter.Name == "__result");
                if (result != null)
                    TestAssert.False(result.ParameterType.IsByRef,
                        patch.Name + " can rewrite the original hover result.");
                TestAssert.Equal(typeof(void), method.ReturnType);
            }
        }

        private static void ItemHookIsExact()
        {
            HarmonyPatch patch = typeof(InventoryGridCreateItemTooltipPatch)
                .GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(InventoryGrid), patch.info.declaringType);
            TestAssert.Equal("CreateItemTooltip", patch.info.methodName);
            TestAssert.Equal(typeof(ItemDrop.ItemData), patch.info.argumentTypes[0]);
            TestAssert.Equal(typeof(UITooltip), patch.info.argumentTypes[1]);
        }

        private static void ComfortHookIsExact()
        {
            HarmonyPatch patch = typeof(RestedCalculateComfortPatch)
                .GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(SE_Rested), patch.info.declaringType);
            TestAssert.Equal(nameof(SE_Rested.CalculateComfortLevel), patch.info.methodName);
            TestAssert.Equal(1, patch.info.argumentTypes.Length);
            TestAssert.Equal(typeof(Player), patch.info.argumentTypes[0]);
        }

        private static void ContextHooksAreVisibleOnly()
        {
            var allowed = new HashSet<Type>
            {
                typeof(CraftingStation), typeof(CookingStation), typeof(Fermenter), typeof(Plant),
                typeof(Beehive), typeof(Tameable), typeof(Switch)
            };
            foreach (Type patchType in PatchTypes().Where(type =>
                         type != typeof(InventoryGridCreateItemTooltipPatch) &&
                         type != typeof(RestedCalculateComfortPatch)))
            {
                HarmonyPatch patch = patchType.GetCustomAttributes<HarmonyPatch>().Single();
                TestAssert.True(allowed.Contains(patch.info.declaringType));
                TestAssert.Equal("GetHoverText", patch.info.methodName);
            }
        }

        private static void PostfixesObserveFinalResults()
        {
            foreach (Type patch in PatchTypes())
            foreach (MethodInfo method in patch.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (method.GetCustomAttribute<HarmonyPostfix>() == null) continue;
                HarmonyPriority priority = TestAssert.NotNull(
                    method.GetCustomAttribute<HarmonyPriority>(),
                    method.DeclaringType?.Name + "." + method.Name + " lacks priority.");
                TestAssert.Equal(Priority.Last, priority.info.priority);
            }
        }

        private static void ProductionSwitchIncludesFermenters()
        {
            MethodInfo postfix = typeof(ProductionSwitchHoverPatch).GetMethod(
                "Postfix",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(postfix).Any(call =>
                    call is MethodInfo method && method.IsGenericMethod &&
                    method.Name == nameof(UnityEngine.Component.GetComponentInParent) &&
                    method.GetGenericArguments().Single() == typeof(Fermenter)),
                "Fermenter controls must retain their already-visible hover context.");
        }

        private static void InstalledPostfixOrderingIsExact()
        {
            Assembly harmony = typeof(Harmony).Assembly;
            Type wrapper = TestAssert.NotNull(
                harmony.GetType("HarmonyLib.PatchSorter+PatchSortingWrapper", false),
                "Installed Harmony patch sorter wrapper is missing.");
            MethodInfo compare = TestAssert.NotNull(
                wrapper.GetMethod("CompareTo", BindingFlags.Instance | BindingFlags.Public),
                "Installed Harmony patch priority comparer is missing.");
            Type serialization = TestAssert.NotNull(
                harmony.GetType("HarmonyLib.PatchInfoSerialization", false),
                "Installed Harmony priority policy is missing.");
            MethodInfo priorityComparer = TestAssert.NotNull(
                serialization.GetMethod("PriorityComparer",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                "Installed Harmony priority policy method is missing.");
            TestAssert.True(IlReader.Calls(compare).Any(call => call == priorityComparer),
                "Installed Harmony patch sorter no longer delegates to its priority policy.");
            IReadOnlyList<IlInstruction> priorityIl = IlReader.Read(priorityComparer);
            int priorityCompare = priorityIl.ToList().FindIndex(instruction =>
                instruction.Operand is MethodInfo method &&
                method.DeclaringType == typeof(int) &&
                method.Name == nameof(int.CompareTo));
            TestAssert.True(priorityCompare >= 0 && priorityCompare + 1 < priorityIl.Count,
                "Installed Harmony comparer no longer exposes priority order.");
            TestAssert.Equal(OpCodes.Neg, priorityIl[priorityCompare + 1].OpCode,
                "Installed Harmony no longer sorts high priorities before low priorities.");

            Type manipulator = TestAssert.NotNull(
                harmony.GetType("HarmonyLib.Public.Patching.HarmonyManipulator", false),
                "Installed Harmony postfix emitter is missing.");
            MethodInfo writePostfixes = TestAssert.NotNull(
                manipulator.GetMethod("WritePostfixes", BindingFlags.Instance | BindingFlags.NonPublic),
                "Installed Harmony postfix emitter method is missing.");
            TestAssert.False(IlReader.Calls(writePostfixes).Any(call =>
                    call.DeclaringType == typeof(Enumerable) && call.Name == nameof(Enumerable.Reverse)),
                "Installed Harmony reverses the sorted postfix list during emission.");
            TestAssert.True(IlReader.Calls(writePostfixes).Any(call =>
                    call.Name == nameof(IEnumerable<object>.GetEnumerator)),
                "Installed Harmony no longer emits postfixes by forward enumeration.");
        }

        private static IEnumerable<Type> PatchTypes() =>
            typeof(Plugin).Assembly.GetTypes().Where(type =>
                type.GetCustomAttributes<HarmonyPatch>().Any());
    }
}
