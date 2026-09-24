using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RunicStorage.Engine;

internal sealed class ChestRules
{
    internal const int Limit = 128;
    internal bool Remember;
    internal bool ShowLabel;
    internal int Side;
    internal int Background; // 0 transparent, 1 white, 2 black; older labels default to transparent.
    internal float Size = 1f, Horizontal, Vertical;
    internal float CurveVertical, CurveDepth;
    internal bool WrapAround;
    internal string Color = "#FFFFFFFF", Label = "";
    internal readonly List<string> Items = new();
    internal readonly List<string> Categories = new();
    internal readonly List<string> Memory = new();
    internal readonly List<string> Excluded = new();
    internal readonly List<ChestCustomGroup> CustomGroups = new();
    internal string OnlyBiome = "";
    internal bool Preferred;
    internal static readonly string[] CategoryNames = { "Food", "Health Foods", "Stamina Foods", "Eitr Foods", "Balanced Foods" };

    internal void Learn(IEnumerable<string> items)
    {
        if (!Remember) return;
        foreach (string item in items)
            if (ValidId(item) && Memory.Count < Limit && !Memory.Contains(item)) Memory.Add(item);
    }

    // Lower scores win. An explicit rule set replaces learned/current-content matching.
    internal int Priority(string id, float health, float stamina, float eitr, bool physicallyPresent)
    {
        return Rank(new ChestItemFacts { Id = id, Health = health, Stamina = stamina, Eitr = eitr }, physicallyPresent);
    }

    internal int Rank(ChestItemFacts item, bool physicallyPresent)
    {
        if (Excluded.Contains(item.Id)) return int.MaxValue;
        // Always accept is an exception to the biome filter; Never accept still wins.
        if (Items.Contains(item.Id)) return 0;
        if (OnlyBiome.Length > 0 && !(ChestRuleGroups.Find(OnlyBiome)?.Matches(item) ?? false)) return int.MaxValue;
        int rank = int.MaxValue;
        foreach (string id in Categories)
        {
            var group = ChestRuleGroups.Find(ChestRuleGroups.Canonical(id));
            if (group != null && group.Matches(item)) rank = Math.Min(rank, group.Specificity);
            if (CustomGroups.Any(g => g.Id == id && g.Items.Contains(item.Id))) rank = Math.Min(rank, 2);
        }
        if (rank != int.MaxValue) return rank;
        if (Items.Count > 0 || Categories.Count > 0) return int.MaxValue;
        if (Remember && Memory.Contains(item.Id)) return 3;
        return physicallyPresent ? 4 : int.MaxValue;
    }
    internal int RoutingScore(ChestItemFacts item, bool present)
    { int rank = Rank(item, present); return rank == int.MaxValue ? rank : rank * 2 + (Preferred ? 0 : 1); }

    internal static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        !value.Any(char.IsControl);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal string Encode() => EncodeVersion(5);
    private string EncodeVersion(byte version)
    {
        if (!Runic.Shared.CaptionCurve.Valid(CurveVertical) || !Runic.Shared.CaptionCurve.Valid(CurveDepth) || Background < 0 || Background > 2 || Side < 0 || Side > 4 || !Finite(Size) || Size < .3f || Size > 2f ||
            !Finite(Horizontal) || !Finite(Vertical) || Math.Abs(Horizontal) > 1 || Math.Abs(Vertical) > 1 ||
            Label == null || Label.Length > 96 || Label.Any(char.IsControl) || !Runic.Shared.EmojiText.ValidUnicode(Label) ||
            Color == null || Color.Length != 9 || Color[0] != '#' || !Color.Skip(1).All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid label settings.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(version); writer.Write(Remember); writer.Write(ShowLabel); writer.Write((byte)Side);
            writer.Write(Size); writer.Write(Horizontal); writer.Write(Vertical); writer.Write(Color); writer.Write(Label);
            foreach (var list in new[] { Items, version >= 2 ? Categories.Select(ChestRuleGroups.Canonical).ToList() : Categories, Memory })
            {
                if (list.Count > Limit || list.Distinct().Count() != list.Count || list.Any(s => !ValidId(s)))
                    throw new InvalidDataException("Invalid or excessive chest rules.");
                writer.Write((byte)list.Count);
                foreach (string entry in list) writer.Write(entry);
            }
            if (version >= 2)
            {
                if (OnlyBiome.Length > 0 && !ChestRuleGroups.Biomes.Any(g => g.Id == OnlyBiome)) throw new InvalidDataException("Unknown biome.");
                writer.Write(Preferred); writer.Write(OnlyBiome);
                WriteList(writer, Excluded);
                if (CustomGroups.Count > 16 || CustomGroups.Select(g => g.Id).Distinct().Count() != CustomGroups.Count)
                    throw new InvalidDataException("At most 16 unique custom groups are supported.");
                writer.Write((byte)CustomGroups.Count);
                foreach (var group in CustomGroups)
                {
                    if (!group.Id.StartsWith("custom:", StringComparison.Ordinal) || !Guid.TryParseExact(group.Id.Substring(7), "N", out _) ||
                        !ValidId(group.Name) || group.Name.Length > 48) throw new InvalidDataException("Invalid custom group identity or name.");
                    writer.Write(group.Id); writer.Write(group.Name); WriteList(writer, group.Items);
                }
            }
            if (version >= 3) writer.Write((byte)Background);
            if (version >= 4) { writer.Write(CurveVertical); writer.Write(CurveDepth); }
            if (version >= 5) writer.Write(WrapAround);
        }
        if (Categories.Any(c => version == 1 ? !CategoryNames.Contains(c) :
            ChestRuleGroups.Find(ChestRuleGroups.Canonical(c)) == null && !CustomGroups.Any(g => g.Id == c))) throw new InvalidDataException("Unknown group.");
        string result = Convert.ToBase64String(stream.ToArray());
        if (result.Length > 65536) throw new InvalidDataException("Chest rules are too large.");
        return result;
    }
    private static void WriteList(BinaryWriter writer, List<string> list)
    {
        if (list.Count > Limit || list.Distinct().Count() != list.Count || list.Any(s => !ValidId(s))) throw new InvalidDataException("Invalid item list.");
        writer.Write((byte)list.Count); foreach (string value in list) writer.Write(value);
    }
    private static void ReadList(BinaryReader reader, List<string> list)
    { int count = reader.ReadByte(); if (count > Limit) throw new InvalidDataException(); for (int i = 0; i < count; i++) list.Add(reader.ReadString()); }

    internal static bool TryDecode(string text, out ChestRules rules)
    {
        rules = new ChestRules();
        if (string.IsNullOrEmpty(text)) return true;
        if (text.Length > 65536) return false;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(text));
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            byte version = reader.ReadByte();
            if (version < 1 || version > 5) return false;
            rules.Remember = reader.ReadBoolean(); rules.ShowLabel = reader.ReadBoolean(); rules.Side = reader.ReadByte();
            rules.Size = reader.ReadSingle(); rules.Horizontal = reader.ReadSingle(); rules.Vertical = reader.ReadSingle();
            rules.Color = reader.ReadString(); rules.Label = reader.ReadString();
            foreach (var list in new[] { rules.Items, rules.Categories, rules.Memory })
            {
                int count = reader.ReadByte();
                if (count > Limit) return false;
                for (int i = 0; i < count; i++) list.Add(reader.ReadString());
            }
            if (version >= 2)
            {
                rules.Preferred = reader.ReadBoolean(); rules.OnlyBiome = reader.ReadString();
                ReadList(reader, rules.Excluded);
                int groups = reader.ReadByte(); if (groups > 16) return false;
                for (int i = 0; i < groups; i++)
                {
                    var group = new ChestCustomGroup { Id = reader.ReadString(), Name = reader.ReadString() };
                    ReadList(reader, group.Items); rules.CustomGroups.Add(group);
                }
            }
            if (version >= 3) rules.Background = reader.ReadByte();
            if (version >= 4) { rules.CurveVertical = reader.ReadSingle(); rules.CurveDepth = reader.ReadSingle(); }
            if (version >= 5) rules.WrapAround = reader.ReadBoolean();
            if (stream.Position != stream.Length || rules.EncodeVersion(version) != text) return false;
            for (int i = 0; i < rules.Categories.Count; i++) rules.Categories[i] = ChestRuleGroups.Canonical(rules.Categories[i]);
            return true;
        }
        catch { rules = new ChestRules(); return false; }
    }
}
