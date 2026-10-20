using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArcaneHunter.Core
{
    public sealed class SpawnRule
    {
        public float Chance = 1, Frequency = 1, Population = 1;
        public bool AnyWeather, AnyTime;
        public bool IsDefault => Chance == 1 && Frequency == 1 && Population == 1 && !AnyWeather && !AnyTime;
    }

    public static class HuntingRules
    {
        public static float Clamp(float value, float min, float max, float fallback = 1) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

        public static float Chance(float original, SpawnRule rule) => Clamp(original * Clamp(rule.Chance, 0, 20), 0, 100);
        public static float Interval(float original, SpawnRule rule) => Math.Max(1, original / Clamp(rule.Frequency, 0.1f, 20));
        // Zero means unlimited in Valheim. Preserve that sentinel; disabling uses Chance = 0.
        public static int Population(int original, SpawnRule rule) => original <= 0 ? original :
            Math.Max(1, Math.Min(200, (int)Math.Ceiling(original * Clamp(rule.Population, 0.1f, 10))));

        public static Dictionary<string, SpawnRule> ParseOverrides(string text, Action<string> error)
        {
            var result = new Dictionary<string, SpawnRule>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in (text ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                string[] p = row.Trim().Split(':');
                if (p.Length != 4 || string.IsNullOrWhiteSpace(p[0]) ||
                    !TryNumber(p[1], 0, 20, out float chance) ||
                    !TryNumber(p[2], 0.1f, 20, out float frequency) ||
                    !TryNumber(p[3], 0.1f, 10, out float population))
                {
                    error?.Invoke("Invalid creature override: " + row + ". Expected Prefab:chance:frequency:population.");
                    continue;
                }
                result[p[0].Trim()] = new SpawnRule { Chance = chance, Frequency = frequency, Population = population };
            }
            return result;
        }
        private static bool TryNumber(string s, float min, float max, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

        public static string Bearing(float x, float z)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Floor((Math.Atan2(x, z) * 180 / Math.PI + 360 + 22.5) / 45) % 8;
            return names[index];
        }
        public static string Rank(int xp) => xp >= 5000 ? "Mythic Huntsman" : xp >= 2500 ? "Wildspeaker" :
            xp >= 1000 ? "Arcane Ranger" : xp >= 400 ? "Trailwarden" : xp >= 100 ? "Pathfinder" : "Wayfarer";
        public static string Mastery(int kills) => kills >= 100 ? "Legend" : kills >= 30 ? "Expert" : kills >= 10 ? "Tracker" : kills > 0 ? "Apprentice" : "Unstudied";
        public static int KillXP(int level, bool clean, bool ranged, float distance) =>
            10 + (Math.Max(1, Math.Min(level, 10)) - 1) * 5 + (clean ? 10 : 0) + (ranged && distance >= 40 ? 10 : 0);
    }

    public sealed class Species
    {
        public string Prefab, Name, Region, Hint;
        public Species(string prefab, string name, string region, string hint)
        { Prefab = prefab; Name = name; Region = region; Hint = hint; }
    }

    public static class Bestiary
    {
        public static readonly Species[] All = {
            new Species("Deer", "Deer", "Meadows", "Approach quietly. An unalerted, full-health kill earns the Silent Arrow seal."),
            new Species("Boar", "Boar", "Meadows", "Wild boars count; your tamed livestock never does."),
            new Species("Neck", "Neck", "Meadows", "Search the water's edge, then listen for movement in the reeds."),
            new Species("Greyling", "Greyling", "Meadows", "A first entry for a hunter's field journal."),
            new Species("Greydwarf", "Greydwarf", "Black Forest", "The forest watches back. Track only the prey you intend to pursue."),
            new Species("Greydwarf_Elite", "Greydwarf Brute", "Black Forest", "Heavy prey: leave room for a retreat."),
            new Species("Greydwarf_Shaman", "Greydwarf Shaman", "Black Forest", "Pick your opening before the grove wakes."),
            new Species("Troll", "Troll", "Black Forest", "A distant opening shot is worthy of the Farshot seal."),
            new Species("Skeleton", "Skeleton", "Black Forest", "Field hunts and dungeon encounters both enter the journal."),
            new Species("Draugr", "Draugr", "Swamp", "Watch the ground as carefully as the target."),
            new Species("Draugr_Elite", "Draugr Elite", "Swamp", "Patience is cheaper than a corpse run."),
            new Species("Draugr_Ranged", "Draugr Archer", "Swamp", "An archery duel for the patient hunter."),
            new Species("Blob", "Blob", "Swamp", "Track from a safe distance."),
            new Species("BlobElite", "Ooze", "Swamp", "The hunt may not end with the first kill."),
            new Species("Leech", "Leech", "Swamp", "Shallow water hides moving teeth."),
            new Species("Wraith", "Wraith", "Swamp", "Check the sky as well as the trail."),
            new Species("Surtling", "Surtling", "Swamp", "Location spawners are separate from ambient creature spawns."),
            new Species("Wolf", "Wolf", "Mountain", "One set of tracks can conceal a pack."),
            new Species("Hatchling", "Drake", "Mountain", "Raise your eyes above the snowline."),
            new Species("Fenring", "Fenring", "Mountain", "Prepare before following a nocturnal trail."),
            new Species("StoneGolem", "Stone Golem", "Mountain", "Not every sleeping shape is a boulder."),
            new Species("Goblin", "Fuling", "Plains", "Scout the edges before walking into a camp."),
            new Species("GoblinBrute", "Fuling Berserker", "Plains", "A hunt for experienced rangers."),
            new Species("GoblinShaman", "Fuling Shaman", "Plains", "Choose your first target carefully."),
            new Species("Lox", "Lox", "Plains", "Only wild specimens count toward your hunting record."),
            new Species("Deathsquito", "Deathsquito", "Plains", "The smallest silhouette can be the most urgent."),
            new Species("Serpent", "Serpent", "Ocean", "Chance and frequency are separate. Weather and time restrictions remain unless you lift them."),
            new Species("Hare", "Hare", "Mistlands", "Small quarry rewards a steady aim."),
            new Species("Seeker", "Seeker", "Mistlands", "A tracking pulse can reveal a bearing through the mist."),
            new Species("SeekerBrute", "Seeker Soldier", "Mistlands", "Study the terrain before committing to the hunt."),
            new Species("Gjall", "Gjall", "Mistlands", "Listen overhead. A tracking clue is a last-known position."),
            new Species("Tick", "Tick", "Mistlands", "Check the ground beneath larger prey."),
            new Species("Asksvin", "Asksvin", "Ashlands", "Wild quarry only; tamed mounts are excluded."),
            new Species("Volture", "Volture", "Ashlands", "Scan above the ash."),
            new Species("Morgen", "Morgen", "Ashlands", "A dangerous entry in any hunter's chronicle."),
            new Species("BonemawSerpent", "Bonemaw", "Ashlands", "Sea hunting does not end with the first serpent."),
            new Species("Charred_Melee", "Charred Warrior", "Ashlands", "Keep an escape route through the ash."),
            new Species("Charred_Archer", "Charred Marksman", "Ashlands", "A dangerous rival for a distance record."),
            new Species("Charred_Twitcher", "Charred Twitcher", "Ashlands", "Do not let a trail pull you into a crowd.")
        };
        public static Species Find(string prefab) => All.FirstOrDefault(s => s.Prefab.Equals(prefab, StringComparison.OrdinalIgnoreCase));
        public static string Name(string prefab) => Find(prefab)?.Name ?? prefab;
    }
}
