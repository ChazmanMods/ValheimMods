using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicSafety.Integration;

namespace RunicSafety.Tests
{
    internal static class HarmonyContractTests
    {
        private const BindingFlags Methods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Register()
        {
            TestRunner.Run("all 12 Harmony targets resolve exactly", AllTargetsResolve);
            TestRunner.Run("Harmony patch set contains no transpiler", NoTranspilers);
            TestRunner.Run("Harmony patch set contains no dynamic target resolver", NoDynamicTargets);
            TestRunner.Run("high-impact prefixes only decline or allow original", PrefixesAreNarrow);
            TestRunner.Run("tombstone patch is observational void prefix and postfix", TombstoneObservational);
            TestRunner.Run("incinerator owner boundary is the exact installed RPC", IncineratorOwnerExact);
            TestRunner.Run("portal confirmation runs after Interaction validation", PortalOrdersAfterInteraction);
            TestRunner.Run("station protected-item patches run first", StationPatchesRunFirst);
            TestRunner.Run("localization patch only reinstalls words", LocalizationPatchNarrow);
            TestRunner.Run("patch assembly has no finalizer outcome substitution", NoFinalizers);
        }

        private static Type[] PatchTypes() => typeof(Plugin).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        private static void AllTargetsResolve()
        {
            Type[] patches = PatchTypes();
            TestAssert.Equal(12, patches.Length);
            foreach (Type patch in patches)
            {
                HarmonyPatch attribute = TestAssert.NotNull(
                    patch.GetCustomAttributes<HarmonyPatch>().SingleOrDefault(), patch.FullName);
                MethodInfo target = Resolve(attribute);
                TestAssert.NotNull(target, patch.FullName + " target failed.");
                TestAssert.True(target.DeclaringType == typeof(Localization) ||
                                target.Module.Assembly == typeof(Player).Assembly,
                    patch.FullName + " targets an unexpected assembly.");
            }
        }

        private static void NoTranspilers()
        {
            foreach (Type patch in PatchTypes())
                TestAssert.Equal(null, patch.GetMethod("Transpiler", Methods));
        }

        private static void NoDynamicTargets()
        {
            foreach (Type patch in PatchTypes())
            {
                TestAssert.Equal(null, patch.GetMethod("TargetMethod", Methods));
                TestAssert.Equal(null, patch.GetMethod("TargetMethods", Methods));
                TestAssert.Equal(null, patch.GetMethod("Prepare", Methods));
            }
        }

        private static void PrefixesAreNarrow()
        {
            foreach (Type patch in PatchTypes().Where(type => type != typeof(LocalizationSetupLanguagePatch) &&
                                                              type != typeof(PlayerCreateTombstonePatch)))
            {
                MethodInfo prefix = TestAssert.NotNull(patch.GetMethod("Prefix", Methods), patch.FullName);
                TestAssert.Equal(typeof(bool), prefix.ReturnType, patch.FullName);
                TestAssert.False(IlReader.Calls(prefix, typeof(ZDO), nameof(ZDO.Set)));
                TestAssert.False(IlReader.Calls(prefix, typeof(Inventory), nameof(Inventory.RemoveItem)));
                TestAssert.False(IlReader.Calls(prefix, typeof(ZNetScene), nameof(ZNetScene.Destroy)));
            }
        }

        private static void TombstoneObservational()
        {
            MethodInfo prefix = TestAssert.NotNull(typeof(PlayerCreateTombstonePatch).GetMethod("Prefix", Methods));
            MethodInfo postfix = TestAssert.NotNull(typeof(PlayerCreateTombstonePatch).GetMethod("Postfix", Methods));
            TestAssert.Equal(typeof(void), prefix.ReturnType);
            TestAssert.Equal(typeof(void), postfix.ReturnType);
            TestAssert.True(IlReader.Calls(prefix, typeof(SafetyRuntime), "BeginTombstoneAudit"));
            TestAssert.True(IlReader.Calls(postfix, typeof(SafetyRuntime), "CompleteTombstoneAudit"));
        }

        private static void IncineratorOwnerExact()
        {
            HarmonyPatch patch = typeof(IncineratorRequestPatch).GetCustomAttribute<HarmonyPatch>();
            TestAssert.Equal(typeof(Incinerator), patch.info.declaringType);
            TestAssert.Equal("RPC_RequestIncinerate", patch.info.methodName);
            TestAssert.SequenceEqual(new[] { typeof(long), typeof(long) }, patch.info.argumentTypes);
        }

        private static void PortalOrdersAfterInteraction()
        {
            HarmonyAfter after = TestAssert.NotNull(typeof(TeleportWorldSetTextPatch).GetCustomAttribute<HarmonyAfter>());
            TestAssert.True(after.info.after.Contains("chazman.RunicInteraction"));
        }

        private static void StationPatchesRunFirst()
        {
            foreach (Type type in new[]
                     {
                         typeof(SmelterAddOrePatch), typeof(SmelterAddFuelPatch),
                         typeof(CookingStationAddFuelPatch), typeof(CookingStationUseItemPatch),
                         typeof(FermenterAddItemPatch), typeof(ItemStandUseItemPatch)
                     })
            {
                HarmonyPriority priority = TestAssert.NotNull(
                    type.GetMethod("Prefix", Methods).GetCustomAttribute<HarmonyPriority>(), type.FullName);
                TestAssert.Equal(Priority.First, priority.info.priority);
            }
        }

        private static void LocalizationPatchNarrow()
        {
            MethodInfo postfix = typeof(LocalizationSetupLanguagePatch).GetMethod("Postfix", Methods);
            TestAssert.True(IlReader.Calls(postfix, typeof(LocalizationBridge), "Install"));
            TestAssert.False(IlReader.Calls(postfix, typeof(ZDO), nameof(ZDO.Set)));
        }

        private static void NoFinalizers()
        {
            foreach (Type patch in PatchTypes())
                TestAssert.Equal(null, patch.GetMethod("Finalizer", Methods));
        }

        private static MethodInfo Resolve(HarmonyPatch attribute)
        {
            Type type = attribute.info.declaringType;
            return type?.GetMethod(attribute.info.methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, attribute.info.argumentTypes ?? Type.EmptyTypes, null);
        }
    }
}
