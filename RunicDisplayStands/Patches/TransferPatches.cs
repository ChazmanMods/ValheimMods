using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicDisplayStands
{
    [HarmonyPatch]
    internal static class StandGuiTransferPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InventoryGui), "OnSelectedItem",
                new[] { typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier) });
            yield return AccessTools.Method(typeof(InventoryGui), "OnTakeAll", Type.EmptyTypes);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(out ContainerBridge.Transfer __state)
        {
            __state = null;
            var bridge = ContainerBridge.Opened;
            if (bridge == null || bridge.InTransfer) return true;
            try { __state = bridge.BeginTransfer(); return true; }
            catch (Exception error) { bridge.Warn(error.Message); return false; }
        }

        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception, ContainerBridge.Transfer __state)
        {
            __state?.Finish(__exception == null);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
    internal static class StandRightClickPatch
    {
        private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1)
        {
            var bridge = ContainerBridge.Opened;
            if (bridge == null || !ContainerBridge.IsStandInventory(__0.GetInventory())) return true;
            bridge.TakeItem(__1, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class StandWorldDropPatch
    {
        private static bool Prefix(Humanoid __instance, Inventory __0, ref bool __result)
        {
            var bridge = ContainerBridge.Opened;
            if (bridge == null || __instance != Player.m_localPlayer ||
                (!bridge.InTransfer && !ContainerBridge.IsStandInventory(__0))) return true;
            // Spawning a world item cannot be rolled back as an inventory-only operation.
            bridge.Warn("Move the item into your inventory before dropping it on the ground.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class StandUnscopedMutationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(Inventory).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => new[] { "AddItem", "RemoveItem", "RemoveOneItem", "RemoveAll", "MoveAll",
                    "MoveItemToThis", "MoveInventoryToGrave", "StackAll" }.Contains(method.Name));

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Inventory __instance) =>
            !ContainerBridge.IsStandInventory(__instance) || ContainerBridge.Opened.InTransfer;
    }
}
