using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RunicDeathPenalty.Core
{
    public enum ProgressionMode { Automatic, Manual }
    public sealed class Rules
    {
        public bool Enabled = true, Harvest = true, Use = true, Warnings = true, RecoveryCap = false, BlockUnknown = false;
        public ProgressionMode Mode = ProgressionMode.Automatic;
        public int ManualTier = 0, CapTier = 7, AllowedLead = 0, OceanTier = -1, EffectiveTier = 0;
        public float Multiplier = 2, RecoveryMinutes = 10, ExtraDeathBudget = 1;
        public string ItemOverrides = "", BiomeOverrides = "";
        public static readonly string[] Names = { "Meadows", "BlackForest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands", "DeepNorth" };
        public static readonly string[] Bosses = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };
        public static int AutomaticTier(Func<string, bool> defeated)
        {
            int tier = 0;
            foreach (string boss in Bosses) { if (!defeated(boss)) break; tier++; }
            return tier;
        }
        public int ComputeTier(int automatic) => Math.Min(CapTier, Math.Min(7, (Mode == ProgressionMode.Manual ? ManualTier : automatic) + AllowedLead));
        public bool Restricted(int tier) => Enabled && tier >= 0 && tier > EffectiveTier;
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static string Name(int tier) => tier >= 0 && tier < Names.Length ? Names[tier] : "unmapped";
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ProgressionMode), Mode) || ManualTier < 0 || ManualTier > 7 || CapTier < 0 || CapTier > 7 ||
                EffectiveTier < 0 || EffectiveTier > 7 || AllowedLead < 0 || AllowedLead > 7 || OceanTier < -1 || OceanTier > 7 ||
                !Finite(Multiplier, 1, 20) || !Finite(RecoveryMinutes, 1, 120) || !Finite(ExtraDeathBudget, 0, 20))
                throw new FormatException("Invalid progression or death settings.");
            ParseMap(ItemOverrides); ParseMap(BiomeOverrides);
        }
        static bool Finite(float f, float min, float max) => !float.IsNaN(f) && !float.IsInfinity(f) && f >= min && f <= max;
        public static Dictionary<string, int> ParseMap(string input)
        {
            if (input == null || input.Length > 24000) throw new FormatException("Tier map exceeds 24000 characters.");
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string token in input.Split(new[] {'\n', '\r'}, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.TrimStart().StartsWith("#")).SelectMany(line => line.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)))
            {
                string line = token.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] pair = line.Split('=');
                if (pair.Length != 2 || pair[0].Trim().Length == 0 || !int.TryParse(pair[1].Trim(), out int tier) || tier < -1 || tier > 7)
                    throw new FormatException("Use exactPrefab=tier entries separated by semicolons; tiers -1 through 7.");
                result[pair[0].Trim()] = tier;
            }
            return result;
        }
        public string Encode()
        {
            Validate();
            return string.Join("|", new[] {"RDP1", Enabled.ToString(), Harvest.ToString(), Use.ToString(), Warnings.ToString(), RecoveryCap.ToString(), BlockUnknown.ToString(),
                ((int)Mode).ToString(), ManualTier.ToString(), CapTier.ToString(), AllowedLead.ToString(), OceanTier.ToString(), EffectiveTier.ToString(),
                Multiplier.ToString("R", CultureInfo.InvariantCulture), RecoveryMinutes.ToString("R", CultureInfo.InvariantCulture), ExtraDeathBudget.ToString("R", CultureInfo.InvariantCulture),
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ItemOverrides)), Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(BiomeOverrides))});
        }
        public static Rules Decode(string input)
        {
            if (input == null || input.Length > 66000) throw new FormatException("Policy too large.");
            var p = input.Split('|'); if (p.Length != 18 || p[0] != "RDP1") throw new FormatException("Invalid policy version.");
            var r = new Rules { Enabled = bool.Parse(p[1]), Harvest = bool.Parse(p[2]), Use = bool.Parse(p[3]), Warnings = bool.Parse(p[4]), RecoveryCap = bool.Parse(p[5]), BlockUnknown = bool.Parse(p[6]),
                Mode = (ProgressionMode)int.Parse(p[7]), ManualTier = int.Parse(p[8]), CapTier = int.Parse(p[9]), AllowedLead = int.Parse(p[10]), OceanTier = int.Parse(p[11]), EffectiveTier = int.Parse(p[12]),
                Multiplier = float.Parse(p[13], CultureInfo.InvariantCulture), RecoveryMinutes = float.Parse(p[14], CultureInfo.InvariantCulture), ExtraDeathBudget = float.Parse(p[15], CultureInfo.InvariantCulture),
                ItemOverrides = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p[16])), BiomeOverrides = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p[17])) };
            r.Validate(); return r;
        }
    }
}
