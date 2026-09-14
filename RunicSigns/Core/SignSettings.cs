using System;
using System.Globalization;
using System.Linq;

namespace RunicSigns.Core;

// One versioned, bounded ZDO string. Text and its author remain owned by vanilla Sign.
internal sealed class SignSettings
{
    internal const int TextLimit = 256;
    internal const int RecordLimit = 160;
    internal float Scale = 1, TextSize = 1, Horizontal, Vertical;
    internal int Color = 9, Background, Alignment = 1;
    internal bool Bold, Italic;

    internal bool Valid => InRange(Scale, .25f, 4) && InRange(TextSize, .3f, 3) &&
        InRange(Horizontal, -.4f, .4f) && InRange(Vertical, -.4f, .4f) &&
        Color >= 0 && Color < LabelColors.Names.Length && Background >= 0 && Background <= 2 &&
        Alignment >= 0 && Alignment <= 2;

    internal static bool InRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    internal string Encode()
    {
        if (!Valid) throw new ArgumentException("Invalid sign settings.");
        return string.Join("|", "1", F(Scale), F(TextSize), F(Horizontal), F(Vertical),
            Color, Background, Alignment, Bold ? 1 : 0, Italic ? 1 : 0);
    }
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    internal static bool TryDecode(string raw, out SignSettings settings)
    {
        settings = new SignSettings();
        if (string.IsNullOrEmpty(raw)) return true;
        if (raw.Length > RecordLimit) return false;
        string[] p = raw.Split('|');
        if (p.Length != 10 || p[0] != "1") return false;
        if (!Float(p[1], out settings.Scale) || !Float(p[2], out settings.TextSize) ||
            !Float(p[3], out settings.Horizontal) || !Float(p[4], out settings.Vertical) ||
            !int.TryParse(p[5], out settings.Color) || !int.TryParse(p[6], out settings.Background) ||
            !int.TryParse(p[7], out settings.Alignment) || (p[8] != "0" && p[8] != "1") ||
            (p[9] != "0" && p[9] != "1")) return false;
        settings.Bold = p[8] == "1"; settings.Italic = p[9] == "1";
        return settings.Valid;
    }
    private static bool Float(string s, out float f) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

    internal static bool ValidText(string text) => text != null && text.Length <= TextLimit &&
        !text.Any(c => char.IsControl(c) && c != '\n') && ValidUnicode(text);
    private static bool ValidUnicode(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i]))) return false;
        return true;
    }
}
