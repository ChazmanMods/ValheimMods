using System;
using System.Globalization;
using System.Text;

namespace RunicAwareness.Core
{
    internal readonly struct ItemMetrics
    {
        internal ItemMetrics(
            string name,
            string itemType,
            int quality,
            float damage,
            float armor,
            float block,
            float movementPercent,
            float weight,
            float durability,
            float maximumDurability,
            string skillName,
            float skillLevel)
        {
            Name = name ?? string.Empty;
            ItemType = itemType ?? string.Empty;
            Quality = Math.Max(0, quality);
            Damage = FiniteOrZero(damage);
            Armor = FiniteOrZero(armor);
            Block = FiniteOrZero(block);
            MovementPercent = FiniteOrZero(movementPercent);
            Weight = FiniteOrZero(weight);
            Durability = FiniteOrZero(durability);
            MaximumDurability = FiniteOrZero(maximumDurability);
            SkillName = skillName ?? string.Empty;
            SkillLevel = FiniteOrZero(skillLevel);
        }

        internal string Name { get; }
        internal string ItemType { get; }
        internal int Quality { get; }
        internal float Damage { get; }
        internal float Armor { get; }
        internal float Block { get; }
        internal float MovementPercent { get; }
        internal float Weight { get; }
        internal float Durability { get; }
        internal float MaximumDurability { get; }
        internal string SkillName { get; }
        internal float SkillLevel { get; }

        private static float FiniteOrZero(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    internal static class ItemComparisonFormatter
    {
        internal const int MaximumLines = 8;

        internal static string Format(
            ItemMetrics selected,
            ItemMetrics? equipped,
            bool isEquipped)
        {
            var builder = new StringBuilder(384);
            builder.Append(BoundedText.Label(selected.Name));
            if (isEquipped)
                builder.Append(" (equipped)");
            else if (equipped.HasValue)
                builder.Append(" vs ").Append(BoundedText.Label(equipped.Value.Name));
            else
                builder.Append(" vs empty slot");

            int lines = 1;
            AppendText(builder, ref lines, "Type", BoundedText.Label(selected.ItemType));
            AppendInteger(builder, ref lines, "Quality", selected.Quality,
                equipped?.Quality);
            AppendNumber(builder, ref lines, "Damage (item)", selected.Damage,
                equipped?.Damage, selected.Damage > 0f || (equipped?.Damage ?? 0f) > 0f);
            AppendNumber(builder, ref lines, "Armor", selected.Armor,
                equipped?.Armor, selected.Armor > 0f || (equipped?.Armor ?? 0f) > 0f);
            AppendNumber(builder, ref lines, "Block (skill)", selected.Block,
                equipped?.Block, selected.Block > 0f || (equipped?.Block ?? 0f) > 0f);
            AppendNumber(builder, ref lines, "Move", selected.MovementPercent,
                equipped?.MovementPercent, true, "%");
            if (lines < MaximumLines && !string.IsNullOrEmpty(selected.SkillName))
            {
                builder.Append("\nSkill: ").Append(BoundedText.Label(selected.SkillName))
                    .Append(' ').Append(FormatNumber(selected.SkillLevel));
                lines++;
            }
            AppendNumber(builder, ref lines, "Weight", selected.Weight,
                equipped?.Weight, true);
            if (lines < MaximumLines && selected.MaximumDurability > 0f)
            {
                builder.Append("\nDurability: ")
                    .Append(FormatNumber(selected.Durability)).Append('/')
                    .Append(FormatNumber(selected.MaximumDurability));
                lines++;
            }
            return BoundedText.Sanitize(builder.ToString(), 640, MaximumLines);
        }

        private static void AppendText(
            StringBuilder builder,
            ref int lines,
            string label,
            string value)
        {
            if (lines >= MaximumLines || string.IsNullOrEmpty(value)) return;
            builder.Append('\n').Append(label).Append(": ").Append(value);
            lines++;
        }

        private static void AppendInteger(
            StringBuilder builder,
            ref int lines,
            string label,
            int current,
            int? comparison)
        {
            if (lines >= MaximumLines) return;
            builder.Append('\n').Append(label).Append(": ").Append(current);
            if (comparison.HasValue) AppendDelta(builder, current - comparison.Value, string.Empty);
            lines++;
        }

        private static void AppendNumber(
            StringBuilder builder,
            ref int lines,
            string label,
            float current,
            float? comparison,
            bool include,
            string suffix = "")
        {
            if (!include || lines >= MaximumLines) return;
            builder.Append('\n').Append(label).Append(": ")
                .Append(FormatNumber(current)).Append(suffix);
            if (comparison.HasValue)
                AppendDelta(builder, current - comparison.Value, suffix);
            lines++;
        }

        private static void AppendDelta(StringBuilder builder, float delta, string suffix)
        {
            if (Math.Abs(delta) < 0.05f) return;
            builder.Append(" (");
            if (delta > 0f) builder.Append('+');
            builder.Append(FormatNumber(delta)).Append(suffix).Append(')');
        }

        private static string FormatNumber(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
