using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicInteraction.Integration;

namespace RunicInteraction.Tests
{
    internal static class HarmonyContractTests
    {
        private const BindingFlags PatchMethods = BindingFlags.Static | BindingFlags.Public |
                                                  BindingFlags.NonPublic;

        internal static void Register()
        {
            TestRunner.Run("Valheim adapter resolves every audited delegate and field", AdapterResolves);
            TestRunner.Run("all 23 Harmony targets resolve exactly in installed Valheim", AllTargetsResolve);
            TestRunner.Run("patch set contains no transpiler or final outcome replacement", NoTranspilers);
            TestRunner.Run("hold-repeat uses a finalizer to restore switch cadence", HoldRepeatRestoresCadence);
            TestRunner.Run("pickup decline sets false before skipping vanilla", PickupDeclineIsNonDestructive);
            TestRunner.Run("text commit validation is the only receiver commit prefix", TextCommitIsNarrow);
            TestRunner.Run("door patch captures the exact local interaction boundary", DoorPatchUsesInteraction);
            TestRunner.Run("keyboard and vanilla controller transfers share one guard", TransferPathsShareGuard);
            TestRunner.Run("Inventory lock denial precedes equipment capture", InventoryDenialPrecedesCapture);
        }

        private static void AdapterResolves()
        {
            TestAssert.True(ValheimAccess.Initialize(out string problem),
                "ValheimAccess.Initialize failed: " + problem);
        }

        private static void AllTargetsResolve()
        {
            Type[] patches = PatchTypes();
            TestAssert.Equal(23, patches.Length);
            foreach (Type patch in patches)
            {
                HarmonyPatch attribute = TestAssert.NotNull(
                    patch.GetCustomAttributes<HarmonyPatch>().SingleOrDefault(),
                    patch.FullName + " must have one exact HarmonyPatch attribute.");
                MethodInfo target = Resolve(attribute);
                TestAssert.NotNull(target, patch.FullName + " did not resolve its installed target.");
                TestAssert.Equal(typeof(Player).Assembly, target.Module.Assembly,
                    patch.FullName + " resolved outside assembly_valheim.");
            }
        }

        private static void NoTranspilers()
        {
            foreach (Type patch in PatchTypes())
            {
                TestAssert.True(patch.GetMethod("Transpiler", PatchMethods) == null,
                    patch.Name + " replaces installed IL.");
                TestAssert.True(patch.GetMethod("Prepare", PatchMethods) == null,
                    patch.Name + " dynamically changes its declared target.");
                TestAssert.True(patch.GetMethod("TargetMethod", PatchMethods) == null,
                    patch.Name + " bypasses exact attribute targeting.");
            }
        }

        private static void HoldRepeatRestoresCadence()
        {
            MethodInfo prefix = Method(typeof(SwitchInteractPatch), "Prefix");
            MethodInfo finalizer = Method(typeof(SwitchInteractPatch), "Finalizer");
            TestAssert.True(IlReader.Calls(prefix, typeof(HoldRepeatRuntime), "Begin"));
            TestAssert.True(IlReader.Calls(finalizer, typeof(HoldRepeatRuntime), "End"));
            TestAssert.Equal(typeof(Exception), finalizer.ReturnType);
            TestAssert.True(typeof(SwitchInteractPatch).GetMethod("Postfix", PatchMethods) == null);
        }

        private static void PickupDeclineIsNonDestructive()
        {
            MethodInfo prefix = Method(typeof(HumanoidPickupPatch), "Prefix");
            TestAssert.True(IlReader.Calls(prefix, typeof(PickupFilterRuntime), "Allow"));
            TestAssert.False(IlReader.Calls(prefix, typeof(ZNetScene), nameof(ZNetScene.Destroy)));
            TestAssert.False(IlReader.Calls(prefix, typeof(Inventory), nameof(Inventory.AddItem)));
        }

        private static void TextCommitIsNarrow()
        {
            MethodInfo prefix = Method(typeof(TextInputCommitPatch), "Prefix");
            TestAssert.True(IlReader.Calls(prefix, typeof(TextEntryRuntime), "ValidateCommit"));
            TestAssert.False(IlReader.Calls(prefix, typeof(TextReceiver), nameof(TextReceiver.SetText)));
        }

        private static void DoorPatchUsesInteraction()
        {
            HarmonyPatch attribute = typeof(DoorInteractAutoClosePatch).GetCustomAttributes<HarmonyPatch>().Single();
            TestAssert.Equal(typeof(Door), attribute.info.declaringType);
            TestAssert.Equal(nameof(Door.Interact), attribute.info.methodName);
            TestAssert.SequenceEqual(
                new[] { typeof(Humanoid), typeof(bool), typeof(bool) },
                attribute.info.argumentTypes);
            TestAssert.True(IlReader.Calls(
                Method(typeof(DoorInteractAutoClosePatch), "Prefix"),
                typeof(DoorAutoCloseRuntime), "BeforeInteract"));
            TestAssert.True(IlReader.Calls(
                Method(typeof(DoorInteractAutoClosePatch), "Postfix"),
                typeof(DoorAutoCloseRuntime), "AfterInteract"));
        }

        private static void TransferPathsShareGuard()
        {
            TestAssert.True(IlReader.Calls(
                Method(typeof(InventoryGridLeftClickPatch), "Prefix"),
                typeof(TransferGestureRuntime), "TryHandleAltClick"));
            TestAssert.True(IlReader.Calls(
                Method(typeof(InventoryGuiSelectedItemPatch), "Prefix"),
                typeof(TransferGestureRuntime), "GuardVanillaMove"));
        }

        private static void InventoryDenialPrecedesCapture()
        {
            MethodInfo prefix = Method(typeof(HumanoidEquipItemPatch), "Prefix");
            HarmonyAfter order = TestAssert.NotNull(
                prefix.GetCustomAttribute<HarmonyAfter>(), "EquipItem prefix order is missing.");
            TestAssert.True(order.info.after.Contains("chazman.RunicInventory"));
            TestAssert.Equal(Priority.Last,
                TestAssert.NotNull(prefix.GetCustomAttribute<HarmonyPriority>(),
                    "EquipItem prefix priority is missing.").info.priority);
            ParameterInfo runOriginal = TestAssert.NotNull(
                prefix.GetParameters().SingleOrDefault(parameter => parameter.Name == "__runOriginal"),
                "Equip capture must observe prior prefix denial.");
            TestAssert.Equal(typeof(bool), runOriginal.ParameterType);
            TestAssert.True(IlReader.Calls(prefix, typeof(EquipmentRestoreRuntime), "BeforeEquip"));

            MethodInfo postfix = Method(typeof(HumanoidEquipItemPatch), "Postfix");
            HarmonyBefore postfixOrder = TestAssert.NotNull(
                postfix.GetCustomAttribute<HarmonyBefore>(), "EquipItem postfix order is missing.");
            TestAssert.True(postfixOrder.info.before.Contains("chazman.RunicInventory"));
            TestAssert.Equal(Priority.First,
                TestAssert.NotNull(postfix.GetCustomAttribute<HarmonyPriority>(),
                    "EquipItem postfix priority is missing.").info.priority);
        }

        private static Type[] PatchTypes() => typeof(Plugin).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        private static MethodInfo Resolve(HarmonyPatch attribute)
        {
            Type type = attribute.info.declaringType;
            string name = attribute.info.methodName;
            Type[] parameters = attribute.info.argumentTypes ?? Type.EmptyTypes;
            return type?.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, parameters, null);
        }

        private static MethodInfo Method(Type type, string name) => TestAssert.NotNull(
            type.GetMethod(name, PatchMethods), type.FullName + "." + name + " is missing.");
    }
}
