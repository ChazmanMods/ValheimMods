using System.Collections.Generic;
using HarmonyLib;
using ArcaneHunter.Core;

namespace ArcaneHunter
{
    [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
    internal static class SpawnPatches
    {
        // Substitute a call-local copy. Never mutate shared prefab data or remove entries:
        // vanilla's cooldown key incorporates the original entry index.
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(ref List<SpawnSystem.SpawnData> spawners, bool eventSpawners)
        {
            if (!Plugin.Settings.Enabled.Value || eventSpawners || spawners == null) return;
            List<SpawnSystem.SpawnData> adjusted = null;
            for (int i = 0; i < spawners.Count; i++)
            {
                var original = spawners[i];
                if (original?.m_prefab == null) continue;
                var rule = Plugin.Settings.Rule(original.m_prefab.name);
                if (rule == null || rule.IsDefault) continue;
                if (adjusted == null) adjusted = new List<SpawnSystem.SpawnData>(spawners);
                var copy = original.Clone();
                if (rule.Chance <= 0) copy.m_enabled = false;
                if (rule.Chance != 1) copy.m_spawnChance = HuntingRules.Chance(original.m_spawnChance, rule);
                if (rule.Frequency != 1) copy.m_spawnInterval = HuntingRules.Interval(original.m_spawnInterval, rule);
                if (rule.Population != 1) copy.m_maxSpawned = HuntingRules.Population(original.m_maxSpawned, rule);
                if (rule.AnyTime) copy.m_spawnAtDay = copy.m_spawnAtNight = true;
                if (rule.AnyWeather) copy.m_requiredEnvironments.Clear();
                adjusted[i] = copy;
            }
            if (adjusted != null) spawners = adjusted;
        }
    }
}
