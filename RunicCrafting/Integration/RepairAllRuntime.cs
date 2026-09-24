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
            if (!CraftingRuntime.IsInitialized)
            {
                CraftingDiagnostics.TraceAction("repair-all-bypass", "runtime-unavailable", "vanilla repairs one item");
                return false;
            }
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !player.IsOwner())
            {
                CraftingDiagnostics.TraceAction(
                    "repair-all-bypass",
                    "local-player-owner-required",
                    "vanilla repairs one item");
                return false;
            }

            CraftingStation station = player.GetCurrentCraftingStation();
            if (station == null && !player.NoCostCheat())
            {
                CraftingDiagnostics.TraceAction("repair-all-bypass", "crafting-station-required", "vanilla handles the repair press");
                return false;
            }
            if (station != null)
            {
                if (!CraftingRuntime.CanUseStation(station, player, out string accessReason))
                {
                    player.Message(MessageHud.MessageType.Center, global::Runic.Localization.RunicText.Get("text_e69081d93656") + accessReason);
                    CraftingDiagnostics.TraceAction("repair-all-cancelled", "station-use-denied:" + accessReason);
                    return true;
                }
                if (!station.CheckUsable(player, showMessage: false))
                {
                    CraftingDiagnostics.TraceAction("repair-all-bypass", "station-not-usable", "vanilla handles the repair press");
                    return false;
                }
            }
            if (CanRepairMethod == null)
            {
                CraftingDiagnostics.TraceAction("repair-all-bypass", "valheim-can-repair-method-unavailable", "vanilla repairs one item");
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
                player.Message(MessageHud.MessageType.Center, global::Runic.Localization.RunicText.Get("text_aadc4395f6c2"));
                CraftingDiagnostics.TraceAction("repair-all", "nothing-repairable");
                return true;
            }

            ValheimReflection.NotifyInventoryChanged(inventory);
            if (station != null)
                station.m_repairItemDoneEffects.Create(
                    station.transform.position,
                    Quaternion.identity,
                    null,
                    1f,
                    -1);
            player.Message(MessageHud.MessageType.Center, global::Runic.Localization.RunicText.Format("text_fed7032e1f9c", repaired));
            CraftingDiagnostics.TraceAction("repair-all", "completed", "items=" + repaired);
            return true;
        }
    }
}
