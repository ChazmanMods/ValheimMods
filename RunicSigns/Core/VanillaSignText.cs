using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RunicSigns.Core;

// Only built-in TMP tags go into vanilla's synchronized caption. No sprite assets or
// custom renderer are required by readers. Keep the bound shared with save requests.
internal static class VanillaSignText
{
    internal const int Limit = 4096;
    private static readonly Regex Tags = new(@"<noparse><</noparse>|<line-height=0>\n|</?(?:color|size|b|i|align|voffset|indent|nobr|line-height)(?:=[^<>]*)?>", RegexOptions.CultureInvariant);
    internal static string Caption(string permittedText) => Tags.Replace(permittedText ?? "",
        match => match.Value == "<noparse><</noparse>" ? "<" : "");

    internal static string Encode(string caption, SignSettings settings, float fontSize,
        float width, float height, bool orthographic)
    {
        if (!settings.Valid || !SignSettings.ValidText(caption)) throw new ArgumentException("Invalid sign design.");
        var style = settings.Clone();
        // Absolute size tags avoid multiplying nested percentage sizes differently
        // on clients whose native text component uses auto sizing.
        string body = SignFormatting.Render(caption, style, fontSize * style.TextSize);
        float unit = orthographic ? 1 : .1f;
        float x = style.Horizontal * (style.OffsetUnit == 0 ? width / unit : 1);
        float y = style.Vertical * (style.OffsetUnit == 0 ? height / unit : 1);
        // TMP includes voffset in its automatic ascender/descender line metrics.
        // Without a driven line height, each wrap adds the offset to line spacing,
        // cancelling movement on lower lines. Set spacing before applying voffset,
        // using the tallest selected size so enlarged words do not overlap rows.
        float largestSize = 1;
        foreach (var run in style.Runs)
            if (run.Start < caption.Length) largestSize = Math.Max(largestSize, run.Style.Size);
        string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        // A zero-height leading line establishes TMP's driven-line-spacing state
        // before it saves an automatic-wrap checkpoint. A line-height tag alone
        // is insufficient: restoring that checkpoint otherwise restores automatic
        // spacing. Caption() removes this synthetic newline when editing again.
        return "<align=" + new[] { "left", "center", "right" }[style.Alignment] + "><nobr><indent=" + F(x) +
            "><size=" + F(fontSize * style.TextSize * largestSize) + "><line-height=0>\n<line-height=100%><voffset=" + F(y) +
            ">" + body + "</voffset></line-height></size></indent></nobr></align>";
    }
}
