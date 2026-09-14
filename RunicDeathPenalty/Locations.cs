using System;
using System.Collections.Generic;
using HarmonyLib;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty
{
    internal static class Locations
    {
        static readonly HashSet<string> Unknown = new HashSet<string>();
        static string mapText;
        static Dictionary<string,int> map = new Dictionary<string,int>();
        internal static string Key(string suffix) => "RunicDeathPenalty." + (ZNet.instance ? ZNet.instance.GetWorldUID().ToString() : "0") + "." + suffix;
        internal static int BiomeTier(Heightmap.Biome biome)
        {
            if (mapText != Plugin.Policy.BiomeOverrides) { map = Rules.ParseMap(Plugin.Policy.BiomeOverrides); mapText = Plugin.Policy.BiomeOverrides; }
            if (map.TryGetValue(biome.ToString(), out int custom)) return custom;
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return 0;
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp: return 2;
                case Heightmap.Biome.Mountain: return 3;
                case Heightmap.Biome.Plains: return 4;
                case Heightmap.Biome.Mistlands: return 5;
                case Heightmap.Biome.AshLands: return 6;
                case Heightmap.Biome.DeepNorth: return 7;
                case Heightmap.Biome.Ocean: return Plugin.Policy.OceanTier;
                default:
                    if (Unknown.Add(biome.ToString())) Plugin.Log.LogWarning("Unmapped biome: " + biome + ". Configure BiomeTierOverrides; default exempt.");
                    return -1;
            }
        }
        internal static int At(Vector3 position) => WorldGenerator.instance != null ? BiomeTier(WorldGenerator.instance.GetBiome(position)) : -1;
        internal static int PlayerTier(Player player)
        {
            if (!player) return -1;
            if (player.InInterior() && player.m_customData.TryGetValue(Key("entrance"), out string saved) && Enum.TryParse(saved, out Heightmap.Biome biome)) return BiomeTier(biome);
            return At(player.transform.position);
        }
        internal static bool Restricted(Player player) => Plugin.Active && Plugin.Policy.Restricted(PlayerTier(player));
        internal static bool Harvest(Component resource)
        {
            if (!Plugin.Active || !Plugin.Policy.Harvest) return true;
            if (Plugin.WaitingForPolicy) return false;
            return !Plugin.Policy.Restricted(At(resource.transform.position)) || Plugin.Deny("Harvesting is locked beyond " + Rules.Name(Plugin.Policy.EffectiveTier) + ".");
        }
    }
    [HarmonyPatch(typeof(Teleport), nameof(Teleport.Interact))]
    static class DungeonEntrance
    {
        static void Prefix(Humanoid character, out string __state) => __state = character is Player p && !p.InInterior() && WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p.transform.position).ToString() : null;
        static void Postfix(Humanoid character, bool __result, string __state)
        {
            if (__result && __state != null && character is Player p) p.m_customData[Locations.Key("entrance")] = __state;
        }
    }
}
