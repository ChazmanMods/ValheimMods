using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RunicSigns.Core;

internal sealed class TextStyle
{
    internal int Bold = -1, Italic = -1; // -1 inherits the whole-caption setting
    internal float Size; // zero inherits, otherwise a relative multiplier
    internal string Ink = "";
    internal TextStyle Clone() => (TextStyle)MemberwiseClone();
    internal bool Same(TextStyle other) => Bold == other.Bold && Italic == other.Italic && Size == other.Size && Ink == other.Ink;
    internal bool Empty => Bold == -1 && Italic == -1 && Size == 0 && Ink == "";
    internal bool Valid => Bold >= -1 && Bold <= 1 && Italic >= -1 && Italic <= 1 &&
        (Size == 0 || SignSettings.InRange(Size, .1f, 20)) && (Ink == "" || SignFormatting.ValidHex(Ink));
}
internal sealed class StyleRun
{
    internal int Start, Length;
    internal TextStyle Style = new();
}

internal static class SignFormatting
{
    internal static bool ValidHex(string s) => s != null && s.Length == 9 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
    internal static bool TryColor(string value, out string hex)
    {
        hex = (value ?? "").Trim().ToUpperInvariant();
        string name = hex;
        int i = Array.FindIndex(LabelColors.Names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) hex = LabelColors.Hex[i];
        if (hex.Length == 7 && hex[0] == '#') hex += "FF";
        return ValidHex(hex);
    }
    internal static bool ValidRuns(List<StyleRun> runs)
    {
        if (runs == null || runs.Count > 128) return false;
        int end = 0;
        foreach (var r in runs) {
            if (r == null || r.Style == null || !r.Style.Valid || r.Start < end || r.Length <= 0 ||
                r.Start > SignSettings.TextLimit || r.Length > SignSettings.TextLimit - r.Start) return false;
            end = r.Start + r.Length;
        }
        return true;
    }
    internal static string EncodeRuns(List<StyleRun> runs)
    {
        if (runs.Count == 0) return "";
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(runs.Count);
        foreach (var r in runs) { writer.Write(r.Start); writer.Write(r.Length); writer.Write(r.Style.Bold); writer.Write(r.Style.Italic); writer.Write(r.Style.Size); writer.Write(r.Style.Ink); }
        return Convert.ToBase64String(stream.ToArray());
    }
    internal static bool TryDecodeRuns(string value, out List<StyleRun> runs)
    {
        runs = new(); if (value == "") return true;
        try {
            using var stream = new MemoryStream(Convert.FromBase64String(value)); using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32(); if (count < 0 || count > 128) return false;
            for (int i = 0; i < count; i++) runs.Add(new StyleRun { Start = reader.ReadInt32(), Length = reader.ReadInt32(),
                Style = new TextStyle { Bold = reader.ReadInt32(), Italic = reader.ReadInt32(), Size = reader.ReadSingle(), Ink = reader.ReadString() } });
            return stream.Position == stream.Length && ValidRuns(runs);
        } catch { return false; }
    }
    internal static void Selection(string text, ref int start, ref int end)
    {
        int a = Math.Max(0, Math.Min(text.Length, Math.Min(start, end)));
        int b = Math.Max(0, Math.Min(text.Length, Math.Max(start, end)));
        if (a == b) {
            if (a > 0 && a < text.Length && char.IsLowSurrogate(text[a])) a--;
            start = end = a; return;
        }
        if (a > 0 && a < text.Length && char.IsLowSurrogate(text[a])) a--;
        if (b > 0 && b < text.Length && char.IsLowSurrogate(text[b])) b++;
        start = a; end = b;
    }
    private static TextStyle[] Expand(int length, List<StyleRun> runs)
    {
        var styles = Enumerable.Range(0, length).Select(_ => new TextStyle()).ToArray();
        foreach (var r in runs) for (int i = r.Start; i < Math.Min(length, r.Start + r.Length); i++) styles[i] = r.Style.Clone();
        return styles;
    }
    private static List<StyleRun> Compact(TextStyle[] styles)
    {
        var result = new List<StyleRun>();
        for (int i = 0; i < styles.Length;) {
            int end = i + 1; while (end < styles.Length && styles[i].Same(styles[end])) end++;
            if (!styles[i].Empty) result.Add(new StyleRun { Start = i, Length = end - i, Style = styles[i].Clone() });
            i = end;
        }
        if (result.Count > 128) throw new ArgumentException("Too many independently styled sections (maximum 128). Clear some formatting first.");
        return result;
    }
    internal static List<StyleRun> Apply(string text, List<StyleRun> runs, int start, int end, Action<TextStyle> change)
    {
        Selection(text, ref start, ref end);
        var styles = Expand(text.Length, runs);
        for (int i = start; i < end; i++) change(styles[i]);
        return Compact(styles);
    }
    internal static bool ApplySelection(SignSettings settings, string text, int start, int end, Action<TextStyle> change)
    {
        Selection(text, ref start, ref end);
        if (start == end) return false;
        settings.Runs = Apply(text, settings.Runs, start, end, change);
        return true;
    }
    internal static void ApplyEditor(SignSettings settings, string text, int start, int end, Action<TextStyle> change)
    {
        Selection(text, ref start, ref end);
        if (start != end) { ApplySelection(settings, text, start, end, change); return; }
        // No selection edits the caption defaults. Explicit word overrides remain until Reset Text.
        var style = new TextStyle { Bold = settings.Bold ? 1 : 0, Italic = settings.Italic ? 1 : 0,
            Size = settings.TextSize, Ink = settings.Ink != "" ? settings.Ink : LabelColors.Hex[settings.Color] };
        change(style);
        settings.Bold = style.Bold == 1; settings.Italic = style.Italic == 1;
        settings.TextSize = style.Size == 0 ? 1 : style.Size; settings.Ink = style.Ink;
    }
    internal static void ResetText(SignSettings settings)
    {
        settings.Runs.Clear();
        settings.Bold = settings.Italic = false;
        settings.Ink = ""; settings.Color = 9; settings.Opacity = 1; settings.TextSize = 1;
    }
    internal static void NormalizeEditorOpacity(SignSettings settings)
    {
        if (settings.Opacity == 1) return;
        string WithOpacity(string ink) => ink.Substring(0, 7) +
            ((int)Math.Round(Convert.ToInt32(ink.Substring(7, 2), 16) * settings.Opacity)).ToString("X2");
        settings.Ink = WithOpacity(settings.Ink != "" ? settings.Ink : LabelColors.Hex[settings.Color]);
        foreach (var run in settings.Runs)
            if (run.Style.Ink != "") run.Style.Ink = WithOpacity(run.Style.Ink);
        settings.Opacity = 1;
    }
    internal static List<StyleRun> Edit(string before, string after, List<StyleRun> runs)
    {
        int prefix = 0;
        while (prefix < Math.Min(before.Length, after.Length) && before[prefix] == after[prefix]) prefix++;
        if (prefix > 0 && prefix < before.Length && char.IsLowSurrogate(before[prefix])) prefix--;
        int suffix = 0;
        while (suffix < Math.Min(before.Length, after.Length) - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
        if (suffix > 0 && char.IsLowSurrogate(before[before.Length - suffix])) suffix--;
        var old = Expand(before.Length, runs); var next = Expand(after.Length, new());
        for (int i = 0; i < prefix; i++) next[i] = old[i];
        for (int i = prefix; i < after.Length - suffix; i++) next[i] = old.Length == 0 ? new() : old[Math.Min(prefix, old.Length - 1)].Clone();
        for (int i = 0; i < suffix; i++) next[after.Length - 1 - i] = old[before.Length - 1 - i];
        return Compact(next);
    }
    internal static string Render(string text, SignSettings settings)
    {
        text ??= "";
        // Use only the string supplied by vanilla's permission/filter pipeline, never raw ZDO text.
        var styles = Expand(text.Length, settings.Runs);
        // Filtering can change indices. Never insert formatting between a Unicode pair.
        for (int i = 1; i < text.Length; i++)
            if (char.IsHighSurrogate(text[i - 1]) && char.IsLowSurrogate(text[i])) styles[i] = styles[i - 1];
        var output = new StringBuilder();
        for (int i = 0; i < text.Length;) {
            int end = i + 1; while (end < text.Length && styles[i].Same(styles[end])) end++;
            var s = styles[i];
            bool bold = s.Bold == -1 ? settings.Bold : s.Bold == 1, italic = s.Italic == -1 ? settings.Italic : s.Italic == 1;
            string ink = s.Ink != "" ? s.Ink : settings.Ink != "" ? settings.Ink : LabelColors.Hex[settings.Color];
            int alpha = (int)Math.Round(Convert.ToInt32(ink.Substring(7, 2), 16) * settings.Opacity);
            output.Append("<color=").Append(ink.Substring(0, 7)).Append(alpha.ToString("X2")).Append('>');
            if (s.Size != 0) output.Append("<size=").Append((s.Size * 100).ToString("0.###", CultureInfo.InvariantCulture)).Append("%>");
            if (bold) output.Append("<b>"); if (italic) output.Append("<i>");
            output.Append(text.Substring(i, end - i).Replace("<", "<noparse><</noparse>"));
            if (italic) output.Append("</i>"); if (bold) output.Append("</b>");
            if (s.Size != 0) output.Append("</size>"); output.Append("</color>");
            i = end;
        }
        return output.ToString();
    }
}
