using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Utils;
using ArcaneHunter.Core;
using UnityEngine;

namespace ArcaneHunter
{
    internal sealed class CreatureConfig
    {
        public ConfigEntry<float> Chance, Frequency, Population;
        public ConfigEntry<bool> AnyTime, AnyWeather;
        public SpawnRule Read() => new SpawnRule { Chance = Chance.Value, Frequency = Frequency.Value,
            Population = Population.Value, AnyTime = AnyTime.Value, AnyWeather = AnyWeather.Value };
    }
    internal sealed class HunterConfig
    {
        public readonly ConfigEntry<bool> Enabled, Tracking, Commissions, Toasts, AutoPickup;
        public readonly ConfigEntry<float> AutoPickupRange;
        public readonly ConfigEntry<float> Radius, Cooldown, Stamina, TrailLifetime, UiScale;
        public readonly ConfigEntry<KeyboardShortcut> JournalKey, PulseKey, FocusKey;
        public readonly ConfigEntry<string> Overrides;
        private readonly Dictionary<string, CreatureConfig> creatures = new Dictionary<string, CreatureConfig>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, SpawnRule> overrides = new Dictionary<string, SpawnRule>(StringComparer.OrdinalIgnoreCase);
        public HunterConfig(ConfigFile file)
        {
            file.SaveOnConfigSet = false;
            Enabled = Sync(file, "General", "Enabled", true, "Enable ArcaneHunter gameplay and spawn changes.");
            Tracking = Sync(file, "Tracking", "Enabled", true, "Allow the Hunter's Sense pulse.");
            AutoPickup = Sync(file, "Auto Pickup", "Enabled", true, "Collect eligible loose loot automatically at long range. Respects the game's auto-pickup toggle, carry weight and inventory capacity.");
            AutoPickupRange = Number(file, "Auto Pickup", "Range", 500, 2, 2000, "Pickup radius in meters from your body. Only loaded drops can be collected; does not load distant areas. Works while using a free camera. Items go directly to inventory.");
            Commissions = Sync(file, "Hunting", "Commissions", true, "Enable daily personal commissions; rewards are journal renown only.");
            Radius = Number(file, "Tracking", "Radius", 100, 20, 200, "Loaded-creature tracking radius in metres. Does not load distant areas.");
            Cooldown = Number(file, "Tracking", "Cooldown", 20, 2, 300, "Seconds between tracking pulses.");
            Stamina = Number(file, "Tracking", "StaminaCost", 15, 0, 100, "Stamina consumed by one pulse, including unsuccessful searches.");
            TrailLifetime = Number(file, "Tracking", "ClueLifetime", 30, 5, 120, "Seconds a last-known-position clue remains visible.");
            Overrides = Sync(file, "Spawns", "CustomOverrides", "", "Any ambient prefab, including modded creatures: Prefab:chanceMultiplier:frequencyMultiplier:populationMultiplier;OtherPrefab:2:1:1. Overrides named sections. Ranges 0-20, 0.1-20, 0.1-10. Restart after file edits.");
            foreach (Species s in Bestiary.All)
            {
                string section = "Creature " + s.Prefab;
                creatures[s.Prefab] = new CreatureConfig {
                    Chance = Number(file, section, "ChanceMultiplier", 1, 0, 20, "Multiplier for each eligible spawn roll. 0 disables ambient spawning; 1 is unchanged; final chance caps at 100 percent."),
                    Frequency = Number(file, section, "FrequencyMultiplier", 1, 0.1f, 20, "Spawn attempts occur this many times as often. 2 halves the interval. Minimum interval 1 second."),
                    Population = Number(file, section, "PopulationMultiplier", 1, 0.1f, 10, "Scale the existing local population cap; rounds up. Unlimited vanilla caps stay unlimited; finite adjusted caps max 200."),
                    AnyTime = Sync(file, section, "IgnoreTimeRestrictions", false, "Allow this ambient entry during both day and night. May also change vanilla daytime despawn behavior."),
                    AnyWeather = Sync(file, section, "IgnoreWeatherRestrictions", false, "Remove weather restrictions from this ambient entry. Biome, depth and progression requirements remain.")
                };
            }
            JournalKey = file.Bind("Controls", "Journal", new KeyboardShortcut(KeyCode.F6), "Open the field journal.");
            PulseKey = file.Bind("Controls", "HuntersSense", new KeyboardShortcut(KeyCode.F7), "Read nearby wildlife signs.");
            FocusKey = file.Bind("Controls", "Focus", new KeyboardShortcut(KeyCode.F8), "Focus the creature nearest your crosshair, or clear focus if none is in view.");
            Toasts = file.Bind("Interface", "HuntNotifications", true, "Show discovery and kill messages.");
            UiScale = file.Bind("Interface", "Scale", 1f, new ConfigDescription("Journal and clue scale.", new AcceptableValueRange<float>(0.7f, 1.6f)));
            Overrides.SettingChanged += (_, __) => Parse();
            Parse();
            file.SaveOnConfigSet = true;
            file.Save();
        }
        private void Parse() => overrides = HuntingRules.ParseOverrides(Overrides.Value, message => Plugin.Log.LogWarning(message));
        public SpawnRule Rule(string prefab)
        {
            if (overrides.TryGetValue(prefab, out var custom)) return custom;
            return creatures.TryGetValue(prefab, out var config) ? config.Read() : null;
        }
        private static ConfigEntry<T> Sync<T>(ConfigFile f, string section, string key, T value, string description) =>
            f.Bind(section, key, value, new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        private static ConfigEntry<float> Number(ConfigFile f, string section, string key, float value, float min, float max, string description) =>
            f.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdminOnly = true }));
    }
}
