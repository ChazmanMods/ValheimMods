using System;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class InventoryAdapterTests
    {
        internal static void Register()
        {
            TestRunner.Run("optional Inventory API can resolve after an earlier unavailable lookup", LateResolution);
            TestRunner.Run("optional Inventory API rejects malformed return and parameter contracts", MalformedApi);
            TestRunner.Run("optional native-use API remains distinct from legacy transfer API", UseApi);
            TestRunner.Run("only native consumable destinations use the slot-use view", UseDestinations);
        }
        private static void LateResolution()
        {
            TestAssert.True(InventoryProtectionAdapter.ResolveMethod(null) == null);
            var method = InventoryProtectionAdapter.ResolveMethod(typeof(ReadyApi));
            TestAssert.True(method != null);
            object[] args = { new object(), 0 };
            TestAssert.True((bool)method.Invoke(null, args));
            TestAssert.Equal(2, (int)args[1]);
        }
        private static void MalformedApi()
        {
            TestAssert.True(InventoryProtectionAdapter.ResolveMethod(typeof(WrongResult)) == null);
            TestAssert.True(InventoryProtectionAdapter.ResolveMethod(typeof(string)) == null);
        }
        private static class ReadyApi
        {
            public static bool TryGetProtection(object item, out int state) { state = 2; return true; }
        }
        private static void UseApi()
        {
            TestAssert.True(InventoryProtectionAdapter.ResolveUseMethod(typeof(ReadyApi)) == null);
            TestAssert.True(InventoryProtectionAdapter.ResolveUseMethod(typeof(WrongUseResult)) == null);
            object[] args = { new object(), 0 };
            var transfer = InventoryProtectionAdapter.ResolveMethod(typeof(UseReadyApi));
            var use = InventoryProtectionAdapter.ResolveUseMethod(typeof(UseReadyApi));
            TestAssert.True((bool)transfer.Invoke(null, args));
            TestAssert.Equal(2, (int)args[1]);
            TestAssert.True((bool)use.Invoke(null, args));
            TestAssert.Equal(1, (int)args[1]);
        }
        private static void UseDestinations()
        {
            foreach (ProtectionDestination destination in Enum.GetValues(typeof(ProtectionDestination)))
            {
                bool expected = destination == ProtectionDestination.SmelterInput || destination == ProtectionDestination.SmelterFuel ||
                    destination == ProtectionDestination.CookingStation || destination == ProtectionDestination.CookingFuel ||
                    destination == ProtectionDestination.Fermenter;
                TestAssert.Equal(expected, InventoryProtectionAdapter.AllowsNativeUse(destination));
            }
            TestAssert.False(InventoryProtectionAdapter.AllowsNativeUse((ProtectionDestination)999));
        }
        private static class UseReadyApi
        {
            public static bool TryGetProtection(object item, out int state) { state = 2; return true; }
            public static bool TryGetUseProtection(object item, out int state) { state = 1; return true; }
        }
        private static class WrongUseResult
        {
            public static int TryGetUseProtection(object item, out int state) { state = 1; return 1; }
        }
        private static class WrongResult
        {
            public static int TryGetProtection(object item, out int state) { state = 1; return 1; }
        }
    }
}
