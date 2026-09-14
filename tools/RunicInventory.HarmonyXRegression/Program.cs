using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class HarmonyXResizeTests
    {
        private static int _height;
        private static bool _dropped;
        private static bool _originalRan;
        private static bool _runicOwned;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Resize() { _originalRan = true; }
        private static bool Runic() { if (!_runicOwned) return true; _height = 7; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Archery() { _height = 6; _dropped = true; return false; }
        private static bool Guard(ref bool __result) { if (!_runicOwned) return true; __result = false; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int FindSlot() { return -77; }
        private static bool RunicFind(ref int __result) { __result = -1; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ArcheryFind(ref int __result) { __result = 4; return false; }
        private static bool GuardFind(ref int __0, ref bool __result) { __0 = -1; __result = false; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasCapacity() { return true; }
        private static bool RunicCapacity(ref bool __result) { __result = false; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ArcheryCapacity(ref bool __result) { __result = true; return false; }
        private static bool GuardCapacity(ref bool __0, ref bool __result) { __0 = false; __result = false; return false; }
        private static MethodInfo Method(string name) => typeof(HarmonyXResizeTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        private static int Main()
        {
            try
            {
                Reproduction();
                SlotResults();
                Cleanup();
                Console.WriteLine("PASS HarmonyX resize/ejection and slot-result regressions; cleanup retention policy.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        private static void Reproduction()
        {
            var harmony = new Harmony("runic.tests.resize-sequence");
            try
            {
                harmony.Patch(Method(nameof(Resize)), prefix: new HarmonyMethod(Method(nameof(Runic))) { priority = 800 });
                harmony.Patch(Method(nameof(Resize)), prefix: new HarmonyMethod(Method(nameof(Archery))) { priority = 400 });
                _runicOwned = true;
                _height = 4; _dropped = _originalRan = false;
                Resize();
                TestAssert.False(_originalRan);
                TestAssert.True(_dropped, "HarmonyX must reproduce the 1.1.3 late-prefix failure.");
                TestAssert.Equal(6, _height);
                harmony.Patch(Method(nameof(Archery)), prefix: new HarmonyMethod(Method(nameof(Guard))));
                _height = 4; _dropped = _originalRan = false;
                Resize();
                TestAssert.False(_originalRan);
                TestAssert.False(_dropped);
                TestAssert.Equal(7, _height);
                _runicOwned = false;
                _height = 4; _dropped = false;
                Resize();
                TestAssert.True(_dropped, "Non-Runic inventory must retain BA's own resize behavior.");
                TestAssert.Equal(6, _height);
            }
            finally { harmony.UnpatchSelf(); }
        }

        private static void SlotResults()
        {
            var harmony = new Harmony("runic.tests.slot-results");
            try
            {
                harmony.Patch(Method(nameof(FindSlot)), prefix: new HarmonyMethod(Method(nameof(RunicFind))) { priority = 900 });
                harmony.Patch(Method(nameof(FindSlot)), prefix: new HarmonyMethod(Method(nameof(ArcheryFind))) { priority = 800 });
                TestAssert.Equal(4, FindSlot()); // BA overwrites Runic's full-backpack result under HarmonyX.
                harmony.Patch(Method(nameof(ArcheryFind)), prefix: new HarmonyMethod(Method(nameof(GuardFind))));
                TestAssert.Equal(-1, FindSlot());
                harmony.Patch(Method(nameof(HasCapacity)), prefix: new HarmonyMethod(Method(nameof(RunicCapacity))) { priority = 900 });
                harmony.Patch(Method(nameof(HasCapacity)), prefix: new HarmonyMethod(Method(nameof(ArcheryCapacity))) { priority = 800 });
                TestAssert.True(HasCapacity());
                harmony.Patch(Method(nameof(ArcheryCapacity)), prefix: new HarmonyMethod(Method(nameof(GuardCapacity))));
                TestAssert.False(HasCapacity());
            }
            finally { harmony.UnpatchSelf(); }
        }

        private static void Cleanup()
        {
            for (int nativeRows = 4; nativeRows <= 9; nativeRows++)
            {
                var positions = new CleanupCoordinate[5];
                for (int i = 0; i < 5; i++) positions[i] = new CleanupCoordinate(i, nativeRows + 2);
                var decision = InventoryCleanupPolicy.Evaluate(8, nativeRows + 2, positions, out int height);
                TestAssert.True(decision == InventoryCleanupDecision.RetainAndExpand);
                TestAssert.Equal(nativeRows + 3, height);
                TestAssert.True(InventoryCleanupPolicy.Evaluate(8, height, positions, out _) == InventoryCleanupDecision.NativeAllowed);
            }
            foreach (var position in new[] { new CleanupCoordinate(-1, 0), new CleanupCoordinate(8, 0), new CleanupCoordinate(0, -1), new CleanupCoordinate(0, int.MaxValue) })
            {
                TestAssert.True(InventoryCleanupPolicy.Evaluate(8, 6, new[] { position }, out int height) == InventoryCleanupDecision.RetainWithoutResize);
                TestAssert.Equal(6, height);
            }
        }
    }
    internal static class TestAssert
    {
        internal static void True(bool value, string message = "Expected true") { if (!value) throw new Exception(message); }
        internal static void False(bool value) { True(!value, "Expected false"); }
        internal static void Equal(int expected, int actual) { True(expected == actual, "Expected " + expected + ", actual " + actual); }
    }
}
