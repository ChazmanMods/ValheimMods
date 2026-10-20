using System;
using System.Collections.Generic;

namespace RunicHallOfHeroes.Chronicles
{
    internal static class NamingEngine
    {
        private static readonly Dictionary<string, string> BossTitles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Eikthyr"] = "Storm-Horn's Bane",
                ["gd_king"] = "Root-Breaker",
                ["Bonemass"] = "Mire-Bane",
                ["Dragonqueen"] = "Frost-Wake",
                ["GoblinKing"] = "Ashen Oath",
                ["SeekerQueen"] = "Hive-Silencer",
                ["Fader"] = "Flame's End"
            };

        internal static void Reconsider(ItemDrop.ItemData item, ItemChronicle chronicle, string bossPrefab = null)
        {
            if (!string.IsNullOrEmpty(bossPrefab) &&
                BossTitles.TryGetValue(bossPrefab.Replace("(Clone)", ""), out string bossTitle))
            {
                chronicle.EarnedName = bossTitle;
                return;
            }

            bool weapon = IsWeapon(item);
            if (weapon)
            {
                if (chronicle.Kills >= 500) chronicle.EarnedName = "The Red Chronicle";
                else if (chronicle.Kills >= 100) chronicle.EarnedName = "Oathcleaver";
                else if (chronicle.Kills >= 25) chronicle.EarnedName = "Foe-Biter";
                else if (chronicle.Kills >= 10) chronicle.EarnedName = "First-Blooded";
            }
            else
            {
                if (chronicle.BossKills >= 3) chronicle.EarnedName = "The Kingsworn Raiment";
                else if (chronicle.BossKills >= 1) chronicle.EarnedName = "Boss-Worn";
                else if (chronicle.WitnessedKills >= 100) chronicle.EarnedName = "The Battle-Witness";
                else if (chronicle.Deaths >= 1) chronicle.EarnedName = "Returned from Darkness";
            }
        }

        internal static bool IsWeapon(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            var type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                   type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                   type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
                   type == ItemDrop.ItemData.ItemType.Bow ||
                   type == ItemDrop.ItemData.ItemType.Shield ||
                   type == ItemDrop.ItemData.ItemType.Torch;
        }
    }
}
