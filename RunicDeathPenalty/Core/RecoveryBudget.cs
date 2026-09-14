using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RunicDeathPenalty.Core
{
    // One fixed window per world/character. Budgets are skill levels, not percentages of shrinking levels.
    public sealed class RecoveryBudget
    {
        public double Expires;
        public readonly Dictionary<int, float> Remaining = new Dictionary<int, float>();
        public void Begin(double now, Rules rules, float baseRate, IEnumerable<KeyValuePair<int, float>> levels)
        {
            if (now < Expires) return;
            Expires = now + rules.RecoveryMinutes * 60;
            Remaining.Clear();
            float extra = Math.Max(0, Math.Min(1, baseRate * rules.Multiplier) - baseRate);
            foreach (var pair in levels) Remaining[pair.Key] = pair.Value * extra * rules.ExtraDeathBudget;
        }
        public float Loss(int skill, float level, float baseRate, Rules rules)
        {
            float normal = level * Rules.Clamp(baseRate, 0, 1);
            float extra = Math.Max(0, level * Rules.Clamp(baseRate * rules.Multiplier, 0, 1) - normal);
            if (rules.RecoveryCap)
            {
                Remaining.TryGetValue(skill, out float left);
                extra = Math.Min(extra, left);
                Remaining[skill] = Math.Max(0, left - extra);
            }
            return Math.Min(level, normal + extra);
        }
        public string Encode() => Expires.ToString("R", CultureInfo.InvariantCulture) + "|" + string.Join(";", Remaining.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value.ToString("R", CultureInfo.InvariantCulture)));
        public static RecoveryBudget Decode(string text)
        {
            var r = new RecoveryBudget();
            if (string.IsNullOrEmpty(text)) return r;
            if (text.Length > 32000) throw new FormatException("Recovery state too large.");
            var parts = text.Split('|'); if (parts.Length != 2) throw new FormatException("Invalid recovery state.");
            r.Expires = double.Parse(parts[0], CultureInfo.InvariantCulture);
            if (double.IsNaN(r.Expires) || double.IsInfinity(r.Expires) || r.Expires < 0) throw new FormatException("Invalid expiry.");
            foreach (var part in parts[1].Split(new[]{';'}, StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=');
                if (pair.Length != 2) throw new FormatException("Invalid skill budget.");
                float value = float.Parse(pair[1], CultureInfo.InvariantCulture);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 2000) throw new FormatException("Invalid skill budget.");
                r.Remaining[int.Parse(pair[0])] = value;
            }
            return r;
        }
    }
}
