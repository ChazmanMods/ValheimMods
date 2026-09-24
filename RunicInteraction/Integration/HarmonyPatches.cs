using System;
using HarmonyLib;

namespace RunicInteraction.Integration
{
    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class SmelterAwakePatch
    {
        private static void Postfix(Smelter __instance)
        {
            if (!Plugin.RuntimeReady) return;
            try
            {
                HoldRepeatRuntime.Register(__instance.m_addOreSwitch);
                HoldRepeatRuntime.Register(__instance.m_addWoodSwitch);
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_42d8b2177ba1"));
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class CookingStationAwakePatch
    {
        private static void Postfix(CookingStation __instance)
        {
            if (!Plugin.RuntimeReady) return;
            try
            {
                HoldRepeatRuntime.Register(__instance.m_addFoodSwitch);
                HoldRepeatRuntime.Register(__instance.m_addFuelSwitch);
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_d3c1294ddc0b"));
            }
        }
    }

    [HarmonyPatch(typeof(ShieldGenerator), "Start")]
    internal static class ShieldGeneratorStartPatch
    {
        private static void Postfix(ShieldGenerator __instance)
        {
            if (!Plugin.RuntimeReady) return;
            try { HoldRepeatRuntime.Register(__instance.m_addFuelSwitch); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_fb46743df3f5"));
            }
        }
    }

    [HarmonyPatch(typeof(Switch), nameof(Switch.Interact),
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    internal static class SwitchInteractPatch
    {
        private static void Prefix(Switch __instance, bool __1, out HoldRepeatRuntime.IntervalState __state)
        {
            __state = default;
            if (!Plugin.RuntimeReady) return;
            try { __state = HoldRepeatRuntime.Begin(__instance, __1); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_a66398fb3ecb"));
            }
        }

        private static Exception Finalizer(
            Switch __instance,
            HoldRepeatRuntime.IntervalState __state,
            Exception __exception)
        {
            try { HoldRepeatRuntime.End(__instance, __state); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_0f48285fbb18"));
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact),
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    internal static class CookingStationInteractPatch
    {
        private static void Prefix(CookingStation __instance, ref bool __1)
        {
            if (!Plugin.RuntimeReady) return;
            try
            {
                if (HoldRepeatRuntime.ShouldConvertCookingHold(__instance, __1)) __1 = false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_d17138c02b22"));
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Interact),
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    internal static class FermenterInteractPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicProduction")]
        private static void Prefix(Fermenter __instance, ref bool __1)
        {
            if (!Plugin.RuntimeReady) return;
            try
            {
                if (HoldRepeatRuntime.ShouldConvertFermenterHold(__instance, __1)) __1 = false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_8820de8c479e"));
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.Interact),
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    internal static class DoorInteractAutoClosePatch
    {
        private static void Prefix(
            Door __instance,
            Humanoid __0,
            bool __1,
            out DoorAutoCloseRuntime.DoorOpenCapture __state)
        {
            __state = default;
            if (!Plugin.RuntimeReady) return;
            try { __state = DoorAutoCloseRuntime.BeforeInteract(__instance, __0, __1); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_aa888bf56a6f"));
            }
        }

        private static void Postfix(
            bool __result,
            DoorAutoCloseRuntime.DoorOpenCapture __state)
        {
            if (!Plugin.RuntimeReady) return;
            try { DoorAutoCloseRuntime.AfterInteract(__state, __result); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_f4e0e643a03c"));
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddToSector), typeof(ZDO), typeof(ZoneSystem.SectorIndex))]
    internal static class InteractionWardZdoAddedPatch
    {
        private static void Postfix(ZDO zdo)
        {
            if (!Plugin.RuntimeReady) return;
            try { DoorAutoCloseRuntime.ObserveWardTopologyMutation(zdo); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_1730dd88ea22"));
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.RemoveFromSector), typeof(ZDO), typeof(ZoneSystem.SectorIndex))]
    internal static class InteractionWardZdoRemovedPatch
    {
        private static void Prefix(ZDO zdo)
        {
            if (!Plugin.RuntimeReady) return;
            try { DoorAutoCloseRuntime.ObserveWardTopologyMutation(zdo); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_60c31af3a764"));
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "OnLeftClick", new[] { typeof(UIInputHandler) })]
    internal static class InventoryGridLeftClickPatch
    {
        private static bool Prefix(InventoryGrid __instance, UIInputHandler __0)
        {
            if (!Plugin.RuntimeReady) return true;
            try { return !TransferGestureRuntime.TryHandleAltClick(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_6fddbb3ebab5"));
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem",
        new[]
        {
            typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i),
            typeof(InventoryGrid.Modifier)
        })]
    internal static class InventoryGuiSelectedItemPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicInventory")]
        private static bool Prefix(
            InventoryGrid __0,
            ItemDrop.ItemData __1,
            InventoryGrid.Modifier __3)
        {
            if (!Plugin.RuntimeReady) return true;
            try { return TransferGestureRuntime.GuardVanillaMove(__0, __1, __3); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_d9837f81b1dc"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem),
        new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class HumanoidEquipItemPatch
    {
        [HarmonyAfter("chazman.RunicInventory")]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            Humanoid __instance,
            ItemDrop.ItemData __0,
            bool __runOriginal,
            out EquipmentRestoreRuntime.EquipCapture __state)
        {
            __state = default;
            if (!__runOriginal || !Plugin.RuntimeReady) return;
            try { __state = EquipmentRestoreRuntime.BeforeEquip(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_4ab8a58ad577"));
            }
        }

        [HarmonyBefore("chazman.RunicInventory")]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(
            Humanoid __instance,
            ItemDrop.ItemData __0,
            bool __result,
            EquipmentRestoreRuntime.EquipCapture __state)
        {
            if (!Plugin.RuntimeReady) return;
            try { EquipmentRestoreRuntime.AfterEquip(__instance, __0, __result, __state); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_9e9d7ca87ce2"));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem),
        new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class HumanoidUnequipItemPatch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData __0)
        {
            if (!Plugin.RuntimeReady) return;
            try { EquipmentRestoreRuntime.AfterUnequip(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_9ca7a3fcbd23"));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems),
        new[] { typeof(bool), typeof(bool) })]
    internal static class HumanoidHideHandItemsPatch
    {
        private static void Prefix(Humanoid __instance, bool __0)
        {
            if (!Plugin.RuntimeReady) return;
            try { EquipmentRestoreRuntime.BeforeHideHands(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_e6ff5935cebf"));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), "ShowHandItems", new[] { typeof(bool), typeof(bool) })]
    internal static class HumanoidShowHandItemsPatch
    {
        private static void Postfix(Humanoid __instance, bool __0)
        {
            if (!Plugin.RuntimeReady) return;
            try { EquipmentRestoreRuntime.AfterShowHands(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_df2167e667d8"));
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupCrafting")]
    internal static class InventoryGuiSetupCraftingPatch
    {
        private static void Prefix(InventoryGui __instance, out bool __state)
        {
            __state = false;
            if (!Plugin.RuntimeReady) return;
            try
            {
                MenuMemoryRuntime.BeginSetup(__instance);
                __state = true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_430319a022f5"));
            }
        }

        private static void Postfix(InventoryGui __instance, bool __state)
        {
            if (!Plugin.RuntimeReady || !__state) return;
            try { MenuMemoryRuntime.CompleteSetup(__instance); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_259640619909"));
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGuiHidePatch
    {
        private static void Prefix(InventoryGui __instance)
        {
            if (!Plugin.RuntimeReady) return;
            try { MenuMemoryRuntime.BeforeHide(__instance); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_ad92c83b16f9"));
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabCraftPressed))]
    internal static class InventoryGuiCraftTabPatch
    {
        private static void Prefix(InventoryGui __instance, out bool __state)
        {
            __state = false;
            if (!Plugin.RuntimeReady) return;
            try
            {
                MenuMemoryRuntime.BeforeContextChange(__instance);
                __state = true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_ff6fc7caf8d0"));
            }
        }

        private static void Postfix(InventoryGui __instance, bool __state)
        {
            if (!Plugin.RuntimeReady || !__state) return;
            try { MenuMemoryRuntime.AfterContextChange(__instance); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_99dc9c90b600"));
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabUpgradePressed))]
    internal static class InventoryGuiUpgradeTabPatch
    {
        private static void Prefix(InventoryGui __instance, out bool __state)
        {
            __state = false;
            if (!Plugin.RuntimeReady) return;
            try
            {
                MenuMemoryRuntime.BeforeContextChange(__instance);
                __state = true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_c0159cfc3de6"));
            }
        }

        private static void Postfix(InventoryGui __instance, bool __state)
        {
            if (!Plugin.RuntimeReady || !__state) return;
            try { MenuMemoryRuntime.AfterContextChange(__instance); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_f3e01a0bbd55"));
            }
        }
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText),
        new[] { typeof(TextReceiver), typeof(string), typeof(int) })]
    internal static class TextInputRequestPatch
    {
        private static void Prefix(TextReceiver __0, ref int __2)
        {
            if (!Plugin.RuntimeReady) return;
            try { TextEntryRuntime.Begin(__0, ref __2); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_0ddb738f2976"));
            }
        }
    }

    [HarmonyPatch(typeof(TextInput), "setText", new[] { typeof(string) })]
    internal static class TextInputCommitPatch
    {
        private static bool Prefix(TextInput __instance, string __0)
        {
            if (!Plugin.RuntimeReady) return true;
            try { return TextEntryRuntime.ValidateCommit(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_1867a65d1d62"));
                try { ValheimAccess.ClearQueuedTextReceiver(__instance); }
                catch (Exception cleanupException)
                {
                    Diagnostics.Error(cleanupException, global::Runic.Localization.RunicText.Get("text_b0f855d94c95"));
                }
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.Hide))]
    internal static class TextInputHidePatch
    {
        private static void Postfix(TextInput __instance)
        {
            if (!Plugin.RuntimeReady) return;
            try { TextEntryRuntime.AfterHide(__instance); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_17a2cf6465cb"));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup),
        new[] { typeof(UnityEngine.GameObject), typeof(bool), typeof(bool) })]
    internal static class HumanoidPickupPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicInventory")]
        private static bool Prefix(Humanoid __instance, UnityEngine.GameObject __0, ref bool __result)
        {
            if (!Plugin.RuntimeReady) return true;
            try
            {
                if (PickupFilterRuntime.Allow(__instance, __0)) return true;
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_3a8dd69f38c1"));
                return true;
            }
        }
    }
}
