using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicInteraction.Core;
using RunicInteraction.Integration;
using UnityEngine;

namespace RunicInteraction.Tests
{
    internal static class InstalledValheimContractTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance;

        internal static void Register()
        {
            TestRunner.Run("installed Valheim version is exactly 1.0.12", InstalledVersionIsExact);
            TestRunner.Run("bounded controller modifiers are installed and distinct from JoyUse in every layout", ControllerModifierPathIsDistinct);
            TestRunner.Run("Player hold interactions retain vanilla 0.2 second cadence", PlayerHoldCadenceIsVanilla);
            TestRunner.Run("Switch retains its own positive-interval validation", SwitchRetainsIntervalGate);
            TestRunner.Run("smelter ore and fuel callbacks validate then remove one then RPC", SmelterCallbacksAreVanillaTransactions);
            TestRunner.Run("cooking food and fuel callbacks validate then remove one then RPC", CookingCallbacksAreVanillaTransactions);
            TestRunner.Run("fermenter base callback validates then removes one then RPC", FermenterCallbackIsVanillaTransaction);
            TestRunner.Run("shield fuel callback validates then removes one then RPC", ShieldCallbackIsVanillaTransaction);
            TestRunner.Run("pickup mutates inventory before destroying its world drop", PickupOrderingIsInstalled);
            TestRunner.Run("door close delegates to installed owner state transition", DoorRpcRetainsInstalledChecks);
            TestRunner.Run("container permission check has the exact installed signature", ContainerPermissionBoundaryIsExact);
            TestRunner.Run("portal and tame text remain RPC outcomes", PortalAndTameRemainRpcOutcomes);
            TestRunner.Run("sign text retains vanilla ward and ownership outcome", SignRetainsVanillaOutcome);
            TestRunner.Run("vanilla crafting menu already stores PieceTable category selection", VanillaPieceTableMemoryRemainsAvailable);
        }

        private static void InstalledVersionIsExact()
        {
            Type versionType = typeof(Player).Assembly.GetType("Version", true);
            PropertyInfo property = versionType.GetProperty("CurrentVersion",
                BindingFlags.Public | BindingFlags.Static);
            TestAssert.NotNull(property, "Version.CurrentVersion is missing.");
            TestAssert.Equal(ValheimAccess.AuditedGameVersion,
                property.GetValue(null)?.ToString());
            ValheimAccess.ValidateGameVersion(versionType);
        }

        private static void ControllerModifierPathIsDistinct()
        {
            MethodInfo generic = ExactAny(typeof(ZInput), "AddGenericGamepadButtons");
            var usePaths = new Dictionary<string, GamepadInput>(StringComparer.Ordinal);
            foreach (string layout in new[]
                     {
                         "AddGamepadClassicButtons", "AddGamepadAlt1Buttons", "AddGamepadAlt2Buttons"
                     })
                usePaths.Add(layout, GamepadActionPath(ExactAny(typeof(ZInput), layout), "JoyUse"));
            foreach (string option in InteractionInputBindings.CreateControllerModifierOptions())
            {
                GamepadInput modifier = GamepadActionPath(generic, option);
                foreach (KeyValuePair<string, GamepadInput> layout in usePaths)
                    TestAssert.False(layout.Value == modifier,
                        layout.Key + " maps JoyUse onto allowed modifier " + option + ".");
            }
            TestAssert.Equal(
                GamepadInput.StickRButton,
                GamepadActionPath(
                    generic,
                    InteractionInputBindings.DefaultPickupBypassControllerModifier));
        }

        private static void PlayerHoldCadenceIsVanilla()
        {
            MethodInfo interact = Exact(typeof(Player), "Interact",
                typeof(GameObject), typeof(bool), typeof(bool));
            TestAssert.True(IlReader.LoadsFloat(interact, 0.2f),
                "Installed Player.Interact hold throttle is no longer 0.2 seconds.");
            TestAssert.True(IlReader.AccessesField(interact, typeof(Player), "m_lastHoverInteractTime"));
            TestAssert.True(IlReader.Calls(interact, typeof(Interactable), nameof(Interactable.Interact)));
        }

        private static void SwitchRetainsIntervalGate()
        {
            MethodInfo interact = Exact(typeof(Switch), nameof(Switch.Interact),
                typeof(Humanoid), typeof(bool), typeof(bool));
            TestAssert.True(IlReader.AccessesField(interact, typeof(Switch), "m_holdRepeatInterval"));
            TestAssert.True(IlReader.AccessesField(interact, typeof(Switch), "m_lastUseTime"));
            TestAssert.True(IlReader.AccessesField(interact, typeof(Switch), "m_onUse"));
        }

        private static void SmelterCallbacksAreVanillaTransactions()
        {
            AssertRemoveBeforeRpc(Exact(typeof(Smelter), "OnAddOre",
                typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)));
            AssertRemoveBeforeRpc(Exact(typeof(Smelter), "OnAddFuel",
                typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)));
        }

        private static void CookingCallbacksAreVanillaTransactions()
        {
            AssertRemoveBeforeRpc(Exact(typeof(CookingStation), "CookItem",
                typeof(Humanoid), typeof(ItemDrop.ItemData)));
            AssertRemoveBeforeRpc(Exact(typeof(CookingStation), "OnAddFuelSwitch",
                typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)));
        }

        private static void FermenterCallbackIsVanillaTransaction() =>
            AssertRemoveBeforeRpc(Exact(typeof(Fermenter), "AddItem",
                typeof(Humanoid), typeof(ItemDrop.ItemData)));

        private static void ShieldCallbackIsVanillaTransaction() =>
            AssertRemoveBeforeRpc(Exact(typeof(ShieldGenerator), "OnAddFuel",
                typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)));

        private static void PickupOrderingIsInstalled()
        {
            MethodInfo pickup = Exact(typeof(Humanoid), nameof(Humanoid.Pickup),
                typeof(GameObject), typeof(bool), typeof(bool));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(pickup);
            int load = Index(calls, typeof(ItemDrop), nameof(ItemDrop.Load));
            int add = Index(calls, typeof(Inventory), nameof(Inventory.AddItem));
            int destroy = Index(calls, typeof(ZNetScene), nameof(ZNetScene.Destroy));
            TestAssert.True(load >= 0 && add > load && destroy > add,
                "Installed pickup ordering changed; filter prefix audit must be revisited.");
        }

        private static void DoorRpcRetainsInstalledChecks()
        {
            MethodInfo rpc = Exact(typeof(Door), "RPC_UseDoor", typeof(long), typeof(bool));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(rpc);
            int can = Index(calls, typeof(Door), "CanInteract");
            int set = Index(calls, typeof(ZDO), nameof(ZDO.Set));
            int update = Index(calls, typeof(Door), "UpdateState");
            TestAssert.True(can >= 0 && set > can && update > set,
                "Installed door owner transition no longer validates and updates atomically.");
        }

        private static void ContainerPermissionBoundaryIsExact()
        {
            MethodInfo check = Exact(typeof(Container), "CheckAccess", typeof(long));
            TestAssert.Equal(typeof(bool), check.ReturnType);
            TestAssert.True(IlReader.AccessesField(check, typeof(Container), "m_privacy"),
                "Container access no longer checks its installed privacy mode.");
            TestAssert.True(IlReader.AccessesField(check, typeof(Container), "m_piece"),
                "Container private access no longer resolves the creator through its Piece.");
        }

        private static void PortalAndTameRemainRpcOutcomes()
        {
            foreach (Type receiver in new[] { typeof(TeleportWorld), typeof(Tameable) })
            {
                MethodInfo set = Exact(receiver, nameof(TextReceiver.SetText), typeof(string));
                TestAssert.True(IlReader.Calls(set, typeof(ZNetView), nameof(ZNetView.InvokeRPC)),
                    receiver.Name + ".SetText no longer delegates its outcome by RPC.");
            }
        }

        private static void SignRetainsVanillaOutcome()
        {
            MethodInfo set = Exact(typeof(Sign), nameof(Sign.SetText), typeof(string));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(set);
            int ward = Index(calls, typeof(PrivateArea), nameof(PrivateArea.CheckAccess));
            int claim = Index(calls, typeof(ZNetView), nameof(ZNetView.ClaimOwnership));
            int write = Index(calls, typeof(ZDO), nameof(ZDO.Set));
            TestAssert.True(ward >= 0 && claim > ward && write > claim,
                "Sign.SetText vanilla ward/ownership/write sequence changed.");
        }

        private static void VanillaPieceTableMemoryRemainsAvailable()
        {
            FieldInfo selected = typeof(PieceTable).GetField("m_selectedPiece", Instance);
            FieldInfo last = typeof(PieceTable).GetField("m_lastSelectedPiece", Instance);
            TestAssert.NotNull(selected, "PieceTable.m_selectedPiece is missing.");
            TestAssert.NotNull(last, "PieceTable.m_lastSelectedPiece is missing.");
            TestAssert.Equal(typeof(Vector2Int[]), selected.FieldType);
            TestAssert.Equal(typeof(Vector2Int[]), last.FieldType);
        }

        private static void AssertRemoveBeforeRpc(MethodInfo method)
        {
            IReadOnlyList<MethodBase> calls = IlReader.Calls(method);
            int remove = -1;
            for (int index = 0; index < calls.Count; index++)
            {
                MethodBase call = calls[index];
                if (call.DeclaringType != typeof(Inventory)) continue;
                if (call.Name == nameof(Inventory.RemoveItem) || call.Name == nameof(Inventory.RemoveOneItem))
                {
                    remove = index;
                    break;
                }
            }
            int rpc = Index(calls, typeof(ZNetView), nameof(ZNetView.InvokeRPC));
            TestAssert.True(remove >= 0 && rpc > remove,
                method.DeclaringType?.Name + "." + method.Name +
                " no longer consumes one validated item before its vanilla RPC.");
        }

        private static int Index(IReadOnlyList<MethodBase> calls, Type owner, string name)
        {
            for (int index = 0; index < calls.Count; index++)
                if (calls[index].DeclaringType == owner && calls[index].Name == name) return index;
            return -1;
        }

        private static GamepadInput GamepadActionPath(MethodInfo method, string action)
        {
            IReadOnlyList<IlInstruction> instructions = IlReader.Read(method);
            for (int index = 0; index < instructions.Count; index++)
            {
                IlInstruction instruction = instructions[index];
                if (instruction.OpCode != System.Reflection.Emit.OpCodes.Ldstr ||
                    !string.Equals(instruction.Operand as string, action, StringComparison.Ordinal)) continue;
                int? value = null;
                for (int cursor = index + 1; cursor < instructions.Count; cursor++)
                {
                    IlInstruction candidate = instructions[cursor];
                    int? constant = Int32Constant(candidate);
                    if (constant.HasValue) value = constant;
                    if (candidate.OpCode != System.Reflection.Emit.OpCodes.Call &&
                        candidate.OpCode != System.Reflection.Emit.OpCodes.Callvirt) continue;
                    MethodBase call = candidate.Operand as MethodBase;
                    if (call?.Name == "get_Item" && value.HasValue)
                        return (GamepadInput)value.Value;
                    break;
                }
            }
            throw new InvalidOperationException(method.Name + " does not bind " + action + ".");
        }

        private static int? Int32Constant(IlInstruction instruction)
        {
            System.Reflection.Emit.OpCode code = instruction.OpCode;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_0) return 0;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_1) return 1;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_2) return 2;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_3) return 3;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_4) return 4;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_5) return 5;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_6) return 6;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_7) return 7;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_8) return 8;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4_M1) return -1;
            if (code == System.Reflection.Emit.OpCodes.Ldc_I4 ||
                code == System.Reflection.Emit.OpCodes.Ldc_I4_S)
                return Convert.ToInt32(instruction.Operand);
            return null;
        }

        private static MethodInfo Exact(Type type, string name, params Type[] parameters) =>
            TestAssert.NotNull(type.GetMethod(name, Instance, null, parameters, null),
                type.FullName + "." + name + "(" +
                string.Join(",", parameters.Select(item => item.Name)) + ") is missing.");

        private static MethodInfo ExactAny(Type type, string name) =>
            TestAssert.NotNull(type.GetMethod(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null),
                type.FullName + "." + name + "() is missing.");
    }
}
