using System;
using System.IO;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class ControllerBindingPolicyTests
    {
        private static readonly string[] DefaultActions =
        {
            ControllerBindingPolicy.Quick1Action,
            ControllerBindingPolicy.Quick2Action,
            ControllerBindingPolicy.Quick3Action,
            ControllerBindingPolicy.SortAction,
            ControllerBindingPolicy.ToggleLockAction
        };

        private static readonly string[] DefaultPaths =
        {
            "<Gamepad>/select",
            "<Gamepad>/buttonNorth",
            "<Gamepad>/rightShoulder",
            "<Gamepad>/buttonSouth",
            "<Gamepad>/buttonEast"
        };

        internal static void Register()
        {
            TestRunner.Run("exact legacy controller defaults migrate in memory to JoyMap", LegacyDefaultsMapExactly);
            TestRunner.Run("custom controller sets are never silently remapped", CustomBindingsRemainExplicit);
            TestRunner.Run("controller defaults are unique on Classic Alternative1 and Alternative2", NativeLayoutsAreUnique);
            TestRunner.Run("layout-independent Quick 1 is disjoint from other Runic controller defaults", SuiteDefaultsAreDisjoint);
            TestRunner.Run("modifier aliases deny only the affected controller route", ModifierAliasIsRouteLocal);
            TestRunner.Run("primary aliases deny both conflicting routes but preserve unrelated routes", PrimaryAliasesFailClosedLocally);
            TestRunner.Run("malformed custom primary fails closed without disabling valid routes", MalformedPrimaryIsRouteLocal);
        }

        private static void LegacyDefaultsMapExactly()
        {
            string effective = ControllerBindingPolicy.EffectiveQuick1Action(
                ControllerBindingPolicy.ModifierAction,
                ControllerBindingPolicy.LegacyQuick1Action,
                ControllerBindingPolicy.Quick2Action,
                ControllerBindingPolicy.Quick3Action,
                ControllerBindingPolicy.SortAction,
                ControllerBindingPolicy.ToggleLockAction,
                out bool mapped);
            TestAssert.True(mapped);
            TestAssert.Equal(ControllerBindingPolicy.Quick1Action, effective);
        }

        private static void CustomBindingsRemainExplicit()
        {
            string effective = ControllerBindingPolicy.EffectiveQuick1Action(
                ControllerBindingPolicy.ModifierAction,
                ControllerBindingPolicy.LegacyQuick1Action,
                ControllerBindingPolicy.Quick2Action,
                "JoyStart",
                ControllerBindingPolicy.SortAction,
                ControllerBindingPolicy.ToggleLockAction,
                out bool mapped);
            TestAssert.False(mapped);
            TestAssert.Equal(ControllerBindingPolicy.LegacyQuick1Action, effective);
        }

        private static void NativeLayoutsAreUnique()
        {
            ExpectAllRoutes("<Gamepad>/leftTrigger", "Classic");
            ExpectAllRoutes("<Gamepad>/leftShoulder", "Alternative1");
            ExpectAllRoutes("<Gamepad>/leftTrigger", "Alternative2");
        }

        private static void SuiteDefaultsAreDisjoint()
        {
            foreach (string module in new[] { "RunicStorage", "RunicAgriculture", "RunicInteraction" })
            {
                string config = File.ReadAllText(Path.Combine(
                    TestPaths.Repository, module, module + ".cfg.example"));
                TestAssert.False(config.Contains("= " + ControllerBindingPolicy.Quick1Action,
                    StringComparison.Ordinal), module + " must not own Inventory's controller chord primary.");
            }
        }

        private static void ModifierAliasIsRouteLocal()
        {
            string[] paths = (string[])DefaultPaths.Clone();
            paths[0] = "<GAMEPAD>/LEFTSHOULDER";
            int mask = ControllerBindingPolicy.ValidRouteMask(
                ControllerBindingPolicy.ModifierAction,
                "<Gamepad>/leftShoulder",
                DefaultActions,
                paths);
            TestAssert.False(ControllerBindingPolicy.RouteIsValid(mask, 0));
            for (int route = 1; route < ControllerBindingPolicy.RouteCount; route++)
                TestAssert.True(ControllerBindingPolicy.RouteIsValid(mask, route), "route " + route);
        }

        private static void PrimaryAliasesFailClosedLocally()
        {
            string[] paths = (string[])DefaultPaths.Clone();
            paths[2] = paths[1];
            int mask = ControllerBindingPolicy.ValidRouteMask(
                ControllerBindingPolicy.ModifierAction,
                "<Gamepad>/leftTrigger",
                DefaultActions,
                paths);
            TestAssert.True(ControllerBindingPolicy.RouteIsValid(mask, 0));
            TestAssert.False(ControllerBindingPolicy.RouteIsValid(mask, 1));
            TestAssert.False(ControllerBindingPolicy.RouteIsValid(mask, 2));
            TestAssert.True(ControllerBindingPolicy.RouteIsValid(mask, 3));
            TestAssert.True(ControllerBindingPolicy.RouteIsValid(mask, 4));
        }

        private static void MalformedPrimaryIsRouteLocal()
        {
            string[] actions = (string[])DefaultActions.Clone();
            actions[3] = string.Empty;
            int mask = ControllerBindingPolicy.ValidRouteMask(
                ControllerBindingPolicy.ModifierAction,
                "<Gamepad>/leftTrigger",
                actions,
                DefaultPaths);
            TestAssert.False(ControllerBindingPolicy.RouteIsValid(mask, 3));
            TestAssert.Equal(ControllerBindingPolicy.AllRoutesMask & ~(1 << 3), mask);
        }

        private static void ExpectAllRoutes(string modifierPath, string layout)
        {
            int mask = ControllerBindingPolicy.ValidRouteMask(
                ControllerBindingPolicy.ModifierAction,
                modifierPath,
                DefaultActions,
                DefaultPaths);
            TestAssert.Equal(ControllerBindingPolicy.AllRoutesMask, mask, layout);
        }
    }
}
