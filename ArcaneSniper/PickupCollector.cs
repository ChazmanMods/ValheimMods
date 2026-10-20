using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArcaneSniper
{
    internal sealed class PickupCollector
    {
        private static readonly FieldInfo DropsField = typeof(ItemDrop).GetField("s_instances", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo ToggleField = typeof(Player).GetField("m_enableAutoPickup", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly List<ItemDrop> batch = new List<ItemDrop>(256);
        private float nextScan;
        private int cursor;
        private Player lastPlayer;

        internal void Tick(Player player, Vector3 origin, float range)
        {
            if (!player || player != Player.m_localPlayer || player.IsDead() || player.IsTeleporting() || player.InCutscene()) return;
            if (player != lastPlayer) { lastPlayer = player; nextScan = 0; cursor = 0; }
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.25f;
            if (!(ToggleField?.GetValue(null) is bool enabled) || !enabled) return;
            var drops = DropsField?.GetValue(null) as List<ItemDrop>;
            if (drops == null) return;
            batch.Clear();
            int count = Mathf.Min(256, drops.Count);
            for (int n = 0; n < count; n++)
            {
                if (cursor >= drops.Count) cursor = 0;
                batch.Add(drops[cursor++]);
            }
            float radiusSquared = range * range;
            var inventory = player.GetInventory();
            foreach (var drop in batch)
            {
                if (!drop || !drop.m_autoPickup || drop.IsPiece() || drop.InTar() ||
                    (drop.transform.position - origin).sqrMagnitude > radiusSquared) continue;
                var view = drop.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                drop.Load();
                if (player.HaveUniqueKey(drop.m_itemData.m_shared.m_name) || !inventory.CanAddItem(drop.m_itemData) ||
                    drop.m_itemData.GetWeight() + inventory.GetTotalWeight() > player.GetMaxCarryWeight()) continue;
                if (!drop.CanPickup()) { drop.RequestOwn(); continue; }
                // Same transfer path as vanilla. Snapshot tolerates registry mutation and other pickup mods.
                player.Pickup(drop.gameObject);
            }
            batch.Clear();
        }
    }
}
