using BepInEx.Configuration;
using RunicDeathPenalty.Core;

namespace RunicDeathPenalty
{
    internal sealed class Configuration
    {
        readonly ConfigEntry<bool> enabled, harvest, use, warnings, recovery, unknown;
        readonly ConfigEntry<ProgressionMode> mode;
        readonly ConfigEntry<int> manual, cap, lead, ocean;
        readonly ConfigEntry<float> multiplier, minutes, budget;
        readonly ConfigEntry<string> items, biomes;
        public Configuration(ConfigFile c)
        {
            enabled = c.Bind("General", "Enabled", true, "Enable policy. Matching plugin remains required on server and clients.");
            mode = c.Bind("Progression", "Mode", ProgressionMode.Automatic, "Automatic uses sequential world boss kills; Manual uses ManualTier.");
            manual = c.Bind("Progression", "ManualTier", 0, Tier("Allowed tier in Manual mode."));
            cap = c.Bind("Progression", "MaximumTier", 7, Tier("Admin ceiling even if later bosses die. Set 2 to hold at Swamp."));
            lead = c.Bind("Progression", "AllowedBiomeLead", 0, Tier("Extra biomes permitted beyond progression. Zero restricts Mountains before Bonemass."));
            ocean = c.Bind("Progression", "OceanRequiredTier", -1, new ConfigDescription("-1 exempts Ocean; otherwise required progression tier 0–7.", new AcceptableValueRange<int>(-1,7)));
            multiplier = c.Bind("Death", "SkillLossMultiplier", 2f, new ConfigDescription("Multiply the world's normal loss rate on restricted deaths; 1–20, final loss capped at 100%.", new AcceptableValueRange<float>(1,20)));
            warnings = c.Bind("General", "EntryWarnings", true, "Explain restrictions when entering a restricted biome.");
            recovery = c.Bind("Recovery", "CapExtraLoss", false, "Optional fixed-window cap on EXTRA skill loss. Normal loss still applies every restricted death.");
            minutes = c.Bind("Recovery", "WindowMinutes", 10f, new ConfigDescription("Fixed recovery window; repeated deaths do not extend it.", new AcceptableValueRange<float>(1,120)));
            budget = c.Bind("Recovery", "ExtraDeathBudget", 1f, new ConfigDescription("Extra-loss allowance measured in initial enhanced-death increments.", new AcceptableValueRange<float>(0,20)));
            harvest = c.Bind("Restrictions", "BlockHarvesting", true, "Block gathering/mining/chopping in restricted biomes and fresh acquisition of above-tier items. Graves and existing storage remain accessible.");
            use = c.Bind("Restrictions", "BlockItemUse", true, "Block above-tier equipment, consumables, ammo, crafting, building and processing everywhere.");
            unknown = c.Bind("Restrictions", "BlockUnmappedItems", false, "Strict mode denies items with no catalog/recipe classification. Unmapped items are logged and can be exported with rdp unmapped.");
            items = c.Bind("Catalog", "ItemTierOverrides", "", "Exact prefab=tier separated by semicolons. -1 always allowed; 0 Meadows,1 BlackForest,2 Swamp,3 Mountains,4 Plains,5 Mistlands,6 Ashlands,7 DeepNorth. Also accepts piece prefabs.");
            biomes = c.Bind("Catalog", "BiomeTierOverrides", "", "Biome enum name=tier; -1 exempt. Mountain is the native enum spelling. DeepNorth requires an explicit decision.");
        }
        static ConfigDescription Tier(string text) => new ConfigDescription(text + " 0 Meadows; 1 BlackForest; 2 Swamp; 3 Mountains; 4 Plains; 5 Mistlands; 6 Ashlands; 7 DeepNorth.", new AcceptableValueRange<int>(0,7));
        public Rules Read(int automatic)
        {
            var r = new Rules {Enabled=enabled.Value, Harvest=harvest.Value, Use=use.Value, Warnings=warnings.Value, RecoveryCap=recovery.Value, BlockUnknown=unknown.Value,
                Mode=mode.Value, ManualTier=manual.Value, CapTier=cap.Value, AllowedLead=lead.Value, OceanTier=ocean.Value, Multiplier=multiplier.Value,
                RecoveryMinutes=minutes.Value, ExtraDeathBudget=budget.Value, ItemOverrides=items.Value, BiomeOverrides=biomes.Value};
            r.EffectiveTier = r.ComputeTier(automatic); r.Validate(); return r;
        }
    }
}
