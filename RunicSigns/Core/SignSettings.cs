using System;
using System.Globalization;
using System.Linq;

namespace RunicSigns.Core;

// One versioned, bounded ZDO string. Text and its author remain owned by vanilla Sign.
internal sealed class SignSettings
{
    internal const int TextLimit = 1024;
    internal const int RecordLimit = 16384;
    internal float Scale = 1, TextSize = 1, Horizontal, Vertical, Depth;
    internal int Color = 9, Background, Alignment = 1;
    internal bool Bold, Italic;
    internal bool Fit = true;
    internal int OffsetUnit, Effect; // bit 1: outline, bit 2: shadow
    internal float CurveVertical, CurveDepth;
    internal string Ink = "";
    internal float Opacity = 1;
    internal System.Collections.Generic.List<StyleRun> Runs = new();
    internal SignSettings Clone() { TryDecode(Encode(), out var copy); return copy; }

    internal bool Valid => InRange(Scale, .25f, 4) && InRange(TextSize, .1f, 20) &&
        InRange(Horizontal, -OffsetLimit, OffsetLimit) && InRange(Vertical, -OffsetLimit, OffsetLimit) && InRange(Depth, -OffsetLimit, OffsetLimit) &&
        Color >= 0 && Color < LabelColors.Names.Length && Background >= 0 && Background <= 2 &&
        Alignment >= 0 && Alignment <= 2 && OffsetUnit >= 0 && OffsetUnit <= 1 &&
        Effect >= 0 && Effect <= 3 && InRange(CurveVertical, -1, 1) && InRange(CurveDepth, -1, 1) && InRange(Opacity, 0, 1) &&
        (Ink == "" || SignFormatting.ValidHex(Ink)) && SignFormatting.ValidRuns(Runs);
    internal float OffsetLimit => OffsetUnit == 1 ? 2000 : 20;

    internal static bool InRange(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    internal string Encode()
    {
        if (!Valid) throw new ArgumentException("Invalid sign settings.");
        return string.Join("|", "4", F(Scale), F(TextSize), F(Horizontal), F(Vertical),
            Color, Background, Alignment, Bold ? 1 : 0, Italic ? 1 : 0, Fit ? 1 : 0, OffsetUnit, Effect, Ink, F(Opacity), SignFormatting.EncodeRuns(Runs), F(CurveVertical), F(CurveDepth), F(Depth));
    }
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    internal static bool TryDecode(string raw, out SignSettings settings)
    {
        settings = new SignSettings();
        if (string.IsNullOrEmpty(raw)) return true;
        if (raw.Length > RecordLimit) return false;
        string[] p = raw.Split('|');
        bool legacy = p.Length == 10 && p[0] == "1";
        bool v2 = p.Length == 16 && p[0] == "2";
        bool v3 = p.Length == 18 && p[0] == "3";
        bool v4 = p.Length == 19 && p[0] == "4";
        if (!legacy && !v2 && !v3 && !v4) return false;
        if (!Float(p[1], out settings.Scale) || !Float(p[2], out settings.TextSize) ||
            !Float(p[3], out settings.Horizontal) || !Float(p[4], out settings.Vertical) ||
            !int.TryParse(p[5], out settings.Color) || !int.TryParse(p[6], out settings.Background) ||
            !int.TryParse(p[7], out settings.Alignment) || (p[8] != "0" && p[8] != "1") ||
            (p[9] != "0" && p[9] != "1")) return false;
        settings.Bold = p[8] == "1"; settings.Italic = p[9] == "1";
        if (!legacy) {
            if ((p[10] != "0" && p[10] != "1") || !int.TryParse(p[11], out settings.OffsetUnit) ||
                !int.TryParse(p[12], out settings.Effect) || !Float(p[14], out settings.Opacity) ||
                !SignFormatting.TryDecodeRuns(p[15], out settings.Runs)) return false;
            settings.Fit = p[10] == "1"; settings.Ink = p[13];
        }
        if ((v3 || v4) && (!Float(p[16], out settings.CurveVertical) || !Float(p[17], out settings.CurveDepth))) return false;
        if (v4 && !Float(p[18], out settings.Depth)) return false;
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
