using System;
using HarmonyLib;
using UnityEngine;

namespace RunicInventory.Integration
{
    internal static class HarmonyOrderIds
    {
        internal const string Interaction = "chazman.RunicInteraction";
        internal const string Storage = "chazman.RunicStorage";
        internal const string Agriculture = "chazman.RunicAgriculture";
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer), new Type[0])]
    internal static class LocalPlayerPatch
    {
        private static void Postfix(Player __instance)
        {
            try { Plugin.Instance?.Runtime?.OnLocalPlayerChanged(__instance); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_5e2af652f514")); }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize), typeof(int))]
    internal static class NativeInventorySizePatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(BetterArcheryCompatibility.Guid)]
        private static bool Prefix(Player __instance, int __0)
        {
            InventoryRuntime runtime = Plugin.Instance?.Runtime;
            if (!(runtime?.HandlesNativeInventorySize(__instance) ?? false)) return true;
            try { runtime.SetNativeInventorySize(__instance, __0); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_f57245ae7d85"));
                runtime.FailClosed("topology.native-resize-faulted");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropInvalidItems))]
    internal static class InvalidInventoryCleanupPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Humanoid __instance)
        {
            try { return Plugin.Instance?.Runtime?.AllowInvalidItemCleanup(__instance) ?? true; }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_52e7295e93f5"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    internal static class PositionedInventoryAddPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData __0, int __2, int __3, bool __4, ref bool __result)
        {
            if (Plugin.Instance?.Runtime?.AllowPositionedAddition(__instance, __0, __2, __3, __4) ?? true) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
    internal static class PositionedInventoryPublicAddPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData __0, Vector2i __1, ref bool __result)
        {
            if (Plugin.Instance?.Runtime?.AllowPositionedAddition(__instance, __0, __1.x, __1.y, false) ?? true) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load), new[] { typeof(ZPackage) })]
    internal static class PlayerLoadPatch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            try { Plugin.Instance?.Runtime?.OnPlayerLoadStarted(__instance); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_e0f06451ec8e")); }
        }

        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            try { Plugin.Instance?.Runtime?.OnPlayerLoadCompleted(__instance); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_ee6b51b127eb")); }
        }

        private static Exception Finalizer(Player __instance, Exception __exception)
        {
            if (__exception == null || __instance != Player.m_localPlayer) return __exception;
            try { Plugin.Instance?.Runtime?.OnPlayerLoadFaulted(__instance); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_a01583f91988")); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), "FindEmptySlot", new[] { typeof(bool) })]
    internal static class FindEmptySlotPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        [HarmonyBefore(BetterArcheryCompatibility.Guid)]
        private static bool Prefix(Inventory __instance, bool __0, ref Vector2i __result)
        {
            InventoryRuntime runtime = Plugin.Instance?.Runtime;
            if (runtime == null) return true;
            try
            {
                if (!runtime.TryFindEmptySlot(__instance, __0, out Vector2i result)) return true;
                __result = result;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_a7d775bf70dd"));
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), "HaveEmptySlot")]
    internal static class ReservedRowEmptySlotPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        [HarmonyBefore(BetterArcheryCompatibility.Guid)]
        private static bool Prefix(Inventory __instance, ref bool __result)
        {
            var runtime = Plugin.Instance?.Runtime;
            if (!BetterArcheryCompatibility.Active || runtime == null ||
                !runtime.TryFindEmptySlot(__instance, true, out Vector2i slot)) return true;
            __result = slot.x >= 0 && slot.y >= 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem),
        new[] { typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class CanAddItemPatch
    {
        private static void Postfix(
            Inventory __instance,
            ItemDrop.ItemData __0,
            int __1,
            ref bool __result)
        {
            try { Plugin.Instance?.Runtime?.AdjustCanAddItem(__instance, __0, __1, ref __result); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_53761e20f3aa"));
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(Vector2i) })]
    internal static class InventoryGridDropPatch
    {
        private static bool Prefix(
            InventoryGrid __instance,
            Inventory __0,
            ItemDrop.ItemData __1,
            int __2,
            Vector2i __3,
            ref bool __result)
        {
            InventoryRuntime runtime = Plugin.Instance?.Runtime;
            if (runtime == null) return true;
            try
            {
                if (runtime.AllowGridDrop(__instance.GetInventory(), __0, __1, __2, __3)) return true;
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_7a37cc353ed8"));
                __result = false;
                return false;
            }
        }

        private static void Postfix(InventoryGrid __instance, Vector2i __3, bool __result)
        {
            try { Plugin.Instance?.Runtime?.AfterGridDrop(__instance.GetInventory(), __3, __result); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_ddb31309f28b")); }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem",
        new[] { typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier) })]
    internal static class InventorySelectedActionPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(HarmonyOrderIds.Interaction)]
        private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1, InventoryGrid.Modifier __3)
        {
            try { return Plugin.Instance?.Runtime?.AllowSelectedAction(__0, __1, __3) ?? true; }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_3e88cfe8d8ea"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "OnRightDown", new[] { typeof(UIInputHandler) })]
    internal static class InventoryGridRightClickLockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(InventoryGrid __instance, UIInputHandler __0)
        {
            try { return !(Plugin.Instance?.Runtime?.TryTogglePointerLock(__instance, __0) ?? false); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_8c84b47b888a"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class HumanoidDropItemPatch
    {
        private static bool Prefix(
            Humanoid __instance,
            Inventory __0,
            ItemDrop.ItemData __1,
            ref bool __result)
        {
            try
            {
                if (Plugin.Instance?.Runtime?.AllowItemAction(__instance, __0, __1, "dropping it") ?? true) return true;
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_bd906f82d5a1"));
                __result = false;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class HumanoidUseItemPatch
    {
        private static bool Prefix(Humanoid __instance, Inventory __0, ItemDrop.ItemData __1)
        {
            try { return Plugin.Instance?.Runtime?.AllowItemAction(__instance, __0, __1, "using it") ?? true; }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_e5a67ea3dfef"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem),
        new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class HumanoidEquipItemPatch
    {
        // A locked destination must stop the call before Interaction captures hand state.
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(HarmonyOrderIds.Interaction)]
        private static bool Prefix(
            Humanoid __instance,
            ItemDrop.ItemData __0,
            ref bool __result,
            ref bool __state)
        {
            try
            {
                InventoryRuntime runtime = Plugin.Instance?.Runtime;
                if (runtime?.AllowEquip(__instance, __0) ?? true)
                {
                    __state = runtime?.BeginEquipmentTransition(__instance, __0) ?? false;
                    return true;
                }
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_93f1585631fb"));
                __result = false;
                return false;
            }
        }

        // Interaction observes the vanilla result before Inventory moves armor/utility to its role.
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Interaction)]
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData __0, bool __result)
        {
            try { Plugin.Instance?.Runtime?.OnEquipped(__instance, __0, __result); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_6f0fac2b7d96")); }
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            try { Plugin.Instance?.Runtime?.EndEquipmentTransition(__state, __exception); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_8ffe7868b2d8"));
                try { Plugin.Instance?.Runtime?.FailClosed("equipment.transition-cleanup-faulted"); }
                catch (Exception nested) { Diagnostics.Error(nested, global::Runic.Localization.RunicText.Get("text_6eb22f316c66")); }
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup),
        new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
    internal static class PickupFilterPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(HarmonyOrderIds.Interaction)]
        private static bool Prefix(Humanoid __instance, GameObject __0, ref bool __result, ref EquipmentAdditionState __state)
        {
            try
            {
                InventoryRuntime runtime = Plugin.Instance?.Runtime;
                if (runtime?.AllowPickup(__instance, __0) ?? true)
                {
                    __state = runtime?.BeginEquipmentAddition(__instance, __0 ? __0.GetComponent<ItemDrop>()?.m_itemData : null);
                    return true;
                }
                __result = false;
                return false; // before Load, ownership request, AddItem, or world-drop destruction
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_3a8dd69f38c1"));
                return true;
            }
        }

        private static void Postfix(bool __result, EquipmentAdditionState __state)
        {
            try { Plugin.Instance?.Runtime?.CompleteEquipmentAddition(__state, __result); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_4242334d5891")); }
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverText), new Type[0])]
    internal static class ItemDropHoverPatch
    {
        private static void Postfix(ItemDrop __instance, ref string __result)
        {
            try { Plugin.Instance?.Runtime?.AppendPickupPreview(__instance, ref __result); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_badc41bb45b3")); }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) })]
    internal static class InventoryCraftingPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicCrafting")]
        private static bool Prefix(InventoryGui __instance, ref EquipmentAdditionState __state)
        {
            try
            {
                InventoryRuntime runtime = Plugin.Instance?.Runtime;
                bool allowed = runtime?.AllowCrafting(__instance) ?? true;
                if (allowed) __state = runtime?.BeginEquipmentAddition(Player.m_localPlayer);
                return allowed;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_9dcb98dbeaf2"));
                return false;
            }
        }

        private static void Postfix(EquipmentAdditionState __state)
        {
            try { Plugin.Instance?.Runtime?.CompleteEquipmentAddition(__state, true); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_8ba112f9b4a1")); }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
    internal static class InventoryRepairAllowancePatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicCrafting")]
        private static void Prefix(ref bool __state)
        {
            try { __state = Plugin.Instance?.Runtime?.BeginRepairAllowance() ?? false; }
            catch (Exception exception)
            {
                __state = false;
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_308dd06e3440"));
            }
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            try { Plugin.Instance?.Runtime?.EndRepairAllowance(__state); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_0e0b093f6911"));
            }
            return __exception;
        }
    }

    internal static class StationLockGuard
    {
        internal static bool Allow(
            Humanoid actor,
            ItemDrop.ItemData item,
            string action,
            ref bool result,
            string nativeBoundary = null)
        {
            try
            {
                if (Plugin.Instance?.Runtime?.AllowStationItem(actor, item, action) ?? true) return true;
                result = false;
                return false;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_89b92dc8251d"));
                result = false;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnAddOre",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class SmelterOreLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicSafety")]
        private static bool Prefix(Humanoid __1, ItemDrop.ItemData __2, ref bool __result) =>
            StationLockGuard.Allow(
                __1, __2, "processing it", ref __result, "Smelter.OnAddOre");
    }

    [HarmonyPatch(typeof(Smelter), "OnAddFuel",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class SmelterFuelLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicSafety")]
        private static bool Prefix(Humanoid __1, ItemDrop.ItemData __2, ref bool __result) =>
            StationLockGuard.Allow(
                __1, __2, "processing it", ref __result, "Smelter.OnAddFuel");
    }

    [HarmonyPatch(typeof(CookingStation), "CookItem",
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class CookingItemLockPatch
    {
        private static bool Prefix(Humanoid __0, ItemDrop.ItemData __1, ref bool __result) =>
            StationLockGuard.Allow(
                __0, __1, "cooking it", ref __result, "CookingStation.CookItem");
    }

    [HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class CookingFuelLockPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicSafety")]
        [HarmonyBefore("chazman.RunicProduction")]
        private static bool Prefix(Humanoid __1, ItemDrop.ItemData __2, ref bool __result) =>
            StationLockGuard.Allow(
                __1, __2, "processing it", ref __result, "CookingStation.OnAddFuelSwitch");
    }

    [HarmonyPatch(typeof(Fermenter), "AddItem", new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class FermenterLockPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicSafety")]
        [HarmonyBefore("chazman.RunicProduction")]
        private static bool Prefix(Humanoid __0, ItemDrop.ItemData __1, ref bool __result) =>
            StationLockGuard.Allow(__0, __1, "fermenting it", ref __result);
    }

    [HarmonyPatch(typeof(Incinerator), "OnIncinerate",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class IncineratorLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicSafety")]
        private static bool Prefix(Humanoid __1, ItemDrop.ItemData __2, ref bool __result) =>
            StationLockGuard.Allow(__1, __2, "incinerating it", ref __result);
    }

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem),
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class ItemStandLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicSafety")]
        private static bool Prefix(Humanoid __0, ItemDrop.ItemData __1, ref bool __result) =>
            StationLockGuard.Allow(
                __0, __1, "displaying it", ref __result, "ItemStand.UseItem");
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem),
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class OfferingLockPatch
    {
        private static bool Prefix(
            Humanoid __0,
            ItemDrop.ItemData __1,
            ref bool __result) =>
            StationLockGuard.Allow(
                __0, __1, "sacrificing it", ref __result,
                "OfferingBowl.UseItem");
    }

    [HarmonyPatch(typeof(ShieldGenerator), "OnAddFuel",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class ShieldFuelLockPatch
    {
        private static bool Prefix(Humanoid __1, ItemDrop.ItemData __2, ref bool __result) =>
            StationLockGuard.Allow(
                __1, __2, "processing it", ref __result, "ShieldGenerator.OnAddFuel");
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class ControllerGetButtonPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Storage, HarmonyOrderIds.Agriculture)]
        private static bool Prefix(string name, ref bool __result) => Suppress(name, ref __result);
        private static bool Suppress(string name, ref bool result)
        {
            if (!InputReservation.ShouldSuppress(name)) return true;
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class ControllerGetButtonDownPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Storage, HarmonyOrderIds.Agriculture)]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!InputReservation.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class ControllerGetButtonUpPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Storage, HarmonyOrderIds.Agriculture)]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!InputReservation.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonPressedTimer), typeof(string))]
    internal static class ControllerPressedTimerPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Storage, HarmonyOrderIds.Agriculture)]
        private static bool Prefix(string name, ref float __result)
        {
            if (!InputReservation.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonLastPressedTimer), typeof(string))]
    internal static class ControllerLastPressedTimerPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(HarmonyOrderIds.Storage, HarmonyOrderIds.Agriculture)]
        private static bool Prefix(string name, ref float __result)
        {
            if (!InputReservation.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }
}
