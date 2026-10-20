using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ArcaneHunter
{
    [HarmonyPatch(typeof(Player), "AutoPickup")]
    internal static class AutoPickupPatch
    {
        private static readonly FieldInfo DropsField = typeof(ItemDrop).GetField("s_instances", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly List<ItemDrop> Batch = new List<ItemDrop>(256);
        private static float nextScan;
        private static int cursor;
        private static Player lastPlayer;

        private static bool Prefix(Player __instance, bool ___m_enableAutoPickup)
        {
            if (__instance != Player.m_localPlayer || !Plugin.Settings.Enabled.Value || !Plugin.Settings.AutoPickup.Value)
                return true;
            if (__instance != lastPlayer) { lastPlayer = __instance; nextScan = 0; cursor = 0; }
            if (!___m_enableAutoPickup || __instance.IsDead() || __instance.IsTeleporting() || __instance.InCutscene()) return false;
            if (Time.time < nextScan) return false;
            nextScan = Time.time + 0.25f;
            var drops = DropsField?.GetValue(null) as List<ItemDrop>;
            if (drops == null) return true;
            // Bounded batches avoid a giant physics query and keep dense loot fields responsive.
            // Copy before pickup: vanilla removes entries by swapping the last item into the removed slot.
            Batch.Clear();
            int count = Mathf.Min(256, drops.Count);
            for (int n = 0; n < count; n++)
            {
                if (cursor >= drops.Count) cursor = 0;
                Batch.Add(drops[cursor++]);
            }
            float radiusSquared = Plugin.Settings.AutoPickupRange.Value * Plugin.Settings.AutoPickupRange.Value;
            Vector3 origin = __instance.transform.position + Vector3.up;
            var inventory = __instance.GetInventory();
            foreach (var drop in Batch)
            {
                if (!drop || !drop.m_autoPickup || drop.IsPiece() || drop.InTar() ||
                    (drop.transform.position - origin).sqrMagnitude > radiusSquared) continue;
                var view = drop.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                drop.Load();
                if (__instance.HaveUniqueKey(drop.m_itemData.m_shared.m_name) || !inventory.CanAddItem(drop.m_itemData) ||
                    drop.m_itemData.GetWeight() + inventory.GetTotalWeight() > __instance.GetMaxCarryWeight()) continue;
                if (!drop.CanPickup()) { drop.RequestOwn(); continue; }
                // Vanilla pickup rechecks ownership, handles full stacks, destroys the network drop,
                // and issues the normal inventory notifications. Never edit a remote inventory/ZDO directly.
                __instance.Pickup(drop.gameObject);
            }
            Batch.Clear();
            return false;
        }
    }
}
