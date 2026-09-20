using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class RepairAllRuntime
    {
        private static readonly MethodInfo CanRepairMethod = AccessTools.Method(typeof(InventoryGui), "CanRepair");
        private static readonly FieldInfo InventoryGuiInstanceField = AccessTools.Field(typeof(InventoryGui), "instance");

        internal static bool TryHandle(InventoryGui gui)
        {
            Player player = Player.m_localPlayer;
            if (!Configuration.Enabled.Value)
            {
                CraftingDiagnostics.TraceAction("repair-all-bypass", "mod-disabled", "vanilla repairs one item");
                return false;
            }
            if (!Configuration.RepairAll.Value)
            {
                CraftingDiagnostics.TraceAction("repair-all-bypass", "repair-all-disabled", "vanilla repairs one item");
                return false;
            }
            return TryRepair(player, gui, player?.GetCurrentCraftingStation(), true, true);
        }

        // Called only after CraftingStation.Interact returned success. This is intentionally
        // separate from RepairAll so opening a station can opt in without changing the repair
        // button's existing configuration or vanilla fallback behavior.
        internal static void TryHandleStationOpen(CraftingStation station, Player player)
        {
            if (!Configuration.Enabled.Value || !Configuration.AutoRepairOnStationOpen.Value ||
                !CraftingRuntime.IsInitialized || player == null || station == null ||
                !ValheimReflection.CanMutateLocalPlayer(player) ||
                !ReferenceEquals(player.GetCurrentCraftingStation(), station)) return;

            InventoryGui gui = InventoryGuiInstanceField != null && InventoryGuiInstanceField.IsStatic
                ? InventoryGuiInstanceField.GetValue(null) as InventoryGui
                : null;
            gui = gui ?? UnityEngine.Object.FindObjectOfType<InventoryGui>();
            if (gui == null)
            {
                CraftingDiagnostics.TraceAction("station-open-repair", "inventory-gui-unavailable");
                return;
            }
            TryRepair(player, gui, station, false, false);
        }

        private static bool TryRepair(
            Player player,
            InventoryGui gui,
            CraftingStation station,
            bool requireRepairAllSetting,
            bool notifyWhenNothing)
        {
            string action = requireRepairAllSetting ? "repair-all" : "station-open-repair";
            if (!CraftingRuntime.IsInitialized)
            {
                CraftingDiagnostics.TraceAction(action + "-bypass", "runtime-unavailable", "vanilla repairs one item");
                return false;
            }
            if (requireRepairAllSetting && !Configuration.RepairAll.Value)
            {
                CraftingDiagnostics.TraceAction(action + "-bypass", "repair-all-disabled", "vanilla repairs one item");
                return false;
            }
            if (player == null || !ValheimReflection.CanMutateLocalPlayer(player))
            {
                CraftingDiagnostics.TraceAction(action + "-bypass", "local-player-owner-required", "vanilla repairs one item");
                return false;
            }
            if (station == null && !player.NoCostCheat())
            {
                CraftingDiagnostics.TraceAction(action + "-bypass", "crafting-station-required", "vanilla handles the repair press");
                return false;
            }
            if (station != null)
            {
                if (!CraftingRuntime.CanUseStation(station, player, out string accessReason))
                {
                    if (requireRepairAllSetting)
                        player.Message(MessageHud.MessageType.Center, "Station use denied: " + accessReason);
                    CraftingDiagnostics.TraceAction(action + "-cancelled", "station-use-denied:" + accessReason);
                    return requireRepairAllSetting;
                }
                if (!station.CheckUsable(player, showMessage: false))
                {
                    CraftingDiagnostics.TraceAction(action + "-bypass", "station-not-usable", "vanilla handles the repair press");
                    return false;
                }
            }
            if (gui == null || CanRepairMethod == null)
            {
                CraftingDiagnostics.TraceAction(action + "-bypass", "valheim-can-repair-unavailable", "vanilla handles the repair press");
                return false;
            }

            var worn = new List<ItemDrop.ItemData>();
            Inventory inventory = player.GetInventory();
            inventory.GetWornItems(worn);
            int repaired = 0;
            foreach (ItemDrop.ItemData item in worn)
            {
                if (!(bool)CanRepairMethod.Invoke(gui, new object[] { item })) continue;
                float maximum = item.GetMaxDurability();
                if (maximum <= 0f || item.m_durability >= maximum) continue;
                player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / maximum);
                item.m_durability = maximum;
                repaired++;
            }

            if (repaired == 0)
            {
                if (notifyWhenNothing) player.Message(MessageHud.MessageType.Center, "No more item to repair");
                CraftingDiagnostics.TraceAction(action, "nothing-repairable");
                return true;
            }

            ValheimReflection.NotifyInventoryChanged(inventory);
            if (station != null)
                station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity, null, 1f, -1);
            player.Message(MessageHud.MessageType.Center, $"Repaired {repaired} items");
            CraftingDiagnostics.TraceAction(action, "completed", "items=" + repaired);
            return true;
        }
    }
}
