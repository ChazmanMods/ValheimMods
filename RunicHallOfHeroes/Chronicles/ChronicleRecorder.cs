using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RunicHallOfHeroes.Chronicles
{
    internal static class ChronicleRecorder
    {
        private static readonly FieldInfo LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

        internal static void RecordDefeat(Character defeated)
        {
            if (defeated == null || defeated is Player || Player.m_localPlayer == null) return;
            var hit = LastHitField?.GetValue(defeated) as HitData;
            if (hit?.GetAttacker() != Player.m_localPlayer) return;

            Player player = Player.m_localPlayer;
            string foe = defeated.GetHoverName();
            if (foe.StartsWith("$")) foe = foe.Substring(1).Replace("enemy_", "");
            bool boss = defeated.m_boss;
            string bossPrefab = defeated.gameObject.name.Replace("(Clone)", "");

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon != null)
            {
                var chronicle = ItemChronicle.Read(weapon);
                chronicle.Kills++;
                if (boss) chronicle.BossKills++;
                chronicle.LastExploit = boss
                    ? $"Dealt the final blow to {foe}."
                    : $"Last struck down {foe}.";
                NamingEngine.Reconsider(weapon, chronicle, boss ? bossPrefab : null);
                chronicle.Write(weapon);
            }

            List<ItemDrop.ItemData> equipped = player.GetInventory().GetEquippedItems();
            foreach (ItemDrop.ItemData item in equipped)
            {
                if (item == weapon) continue;
                var chronicle = ItemChronicle.Read(item);
                chronicle.WitnessedKills++;
                if (boss) chronicle.BossKills++;
                chronicle.LastExploit = boss
                    ? $"Was worn at the fall of {foe}."
                    : $"Was worn in victory over {foe}.";
                NamingEngine.Reconsider(item, chronicle, boss ? bossPrefab : null);
                chronicle.Write(item);
            }
        }

        internal static void RecordPlayerDeath(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            Echoes.EchoRecorder.SealDeath(player);
            foreach (ItemDrop.ItemData item in player.GetInventory().GetEquippedItems())
            {
                var chronicle = ItemChronicle.Read(item);
                chronicle.Deaths++;
                chronicle.LastExploit = "Passed through death and returned to the living.";
                NamingEngine.Reconsider(item, chronicle);
                chronicle.Write(item);
            }
        }
    }

    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class CharacterDeathPatch
    {
        private static void Prefix(Character __instance) => ChronicleRecorder.RecordDefeat(__instance);
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerDeathPatch
    {
        private static void Prefix(Player __instance) => ChronicleRecorder.RecordPlayerDeath(__instance);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(int))]
    internal static class ChronicleTooltipPatch
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            var chronicle = ItemChronicle.Read(__instance);
            if (chronicle.Renown == 0) return;

            string title = string.IsNullOrEmpty(chronicle.EarnedName)
                ? "An Unnamed Chronicle"
                : chronicle.EarnedName;
            __result += $"\n\n<color=#D9A441><size=20>{title}</size></color>" +
                        $"\n<color=#BBAA88>Kills: {chronicle.Kills}  Bosses: {chronicle.BossKills}" +
                        $"  Witnessed: {chronicle.WitnessedKills}</color>";
            if (!string.IsNullOrEmpty(chronicle.LastExploit))
                __result += $"\n<i>{chronicle.LastExploit}</i>";
        }
    }
}
