using System;
using System.Globalization;

namespace RunicHallOfHeroes.Chronicles
{
    internal sealed class ItemChronicle
    {
        internal const string CustomDataKey = "chazman.hallofheroes.chronicle.v1";

        internal string Id = Guid.NewGuid().ToString("N");
        internal string EarnedName = "";
        internal string LastExploit = "";
        internal int Kills;
        internal int BossKills;
        internal int WitnessedKills;
        internal int Deaths;

        internal int Renown => Kills + WitnessedKills + BossKills * 100 + Deaths * 20;

        internal static ItemChronicle Read(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null ||
                !item.m_customData.TryGetValue(CustomDataKey, out string encoded) ||
                string.IsNullOrEmpty(encoded))
                return new ItemChronicle();

            try
            {
                string[] p = encoded.Split('|');
                return new ItemChronicle
                {
                    Id = Decode(p, 0, Guid.NewGuid().ToString("N")),
                    EarnedName = Decode(p, 1, ""),
                    LastExploit = Decode(p, 2, ""),
                    Kills = Parse(p, 3),
                    BossKills = Parse(p, 4),
                    WitnessedKills = Parse(p, 5),
                    Deaths = Parse(p, 6)
                };
            }
            catch
            {
                return new ItemChronicle();
            }
        }

        internal void Write(ItemDrop.ItemData item)
        {
            if (item.m_customData == null)
                item.m_customData = new System.Collections.Generic.Dictionary<string, string>();

            item.m_customData[CustomDataKey] = string.Join("|",
                Encode(Id),
                Encode(EarnedName),
                Encode(LastExploit),
                Kills.ToString(CultureInfo.InvariantCulture),
                BossKills.ToString(CultureInfo.InvariantCulture),
                WitnessedKills.ToString(CultureInfo.InvariantCulture),
                Deaths.ToString(CultureInfo.InvariantCulture));
        }

        private static int Parse(string[] values, int index) =>
            index < values.Length &&
            int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;

        private static string Encode(string value) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? ""));

        private static string Decode(string[] values, int index, string fallback)
        {
            if (index >= values.Length) return fallback;
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(values[index])); }
            catch { return fallback; }
        }
    }
}
