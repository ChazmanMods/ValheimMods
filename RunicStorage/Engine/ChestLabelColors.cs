using System;
using System.Linq;

namespace RunicStorage.Engine;

internal static class ChestLabelColors
{
    // ColorUtility.TryParseHtmlString's named palette. Persist opaque RGBA, never user markup.
    internal static readonly string[] Names = { "Red", "Cyan", "Blue", "Darkblue", "Lightblue", "Purple", "Yellow", "Lime", "Fuchsia", "White", "Silver", "Grey", "Black", "Orange", "Brown", "Maroon", "Green", "Olive", "Navy", "Teal", "Aqua", "Magenta" };
    internal static readonly string[] Hex = { "#FF0000FF", "#00FFFFFF", "#0000FFFF", "#0000A0FF", "#ADD8E6FF", "#800080FF", "#FFFF00FF", "#00FF00FF", "#FF00FFFF", "#FFFFFFFF", "#C0C0C0FF", "#808080FF", "#000000FF", "#FFA500FF", "#A52A2AFF", "#800000FF", "#008000FF", "#808000FF", "#000080FF", "#008080FF", "#00FFFFFF", "#FF00FFFF" };

    internal static int Resolve(string input)
    {
        string name = (input ?? "").Trim().Replace(" ", "").Replace("-", "").ToLowerInvariant();
        if (name.Length == 0) return 9;
        int exact = Array.FindIndex(Names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact;
        if (name == "gray") return 11;
        // Familiar non-Unity names have a known hue; approximate in RGB, not by spelling.
        string rgb = name switch { "pink" => "FFC0CB", "hotpink" => "FF69B4", "gold" => "FFD700",
            "beige" => "F5F5DC", "ivory" => "FFFFF0", "violet" => "EE82EE", "indigo" => "4B0082",
            "turquoise" => "40E0D0", "coral" => "FF7F50", "salmon" => "FA8072", "crimson" => "DC143C",
            "lavender" => "E6E6FA", "tan" => "D2B48C", "chartreuse" => "7FFF00", _ => name.TrimStart('#') };
        if (rgb.Length == 3 || rgb.Length == 4) rgb = string.Concat(rgb.Take(3).Select(c => new string(c, 2)));
        if ((rgb.Length == 6 || rgb.Length == 8) && rgb.All(Uri.IsHexDigit))
        {
            int r = Convert.ToInt32(rgb.Substring(0, 2), 16), g = Convert.ToInt32(rgb.Substring(2, 2), 16), b = Convert.ToInt32(rgb.Substring(4, 2), 16);
            return Enumerable.Range(0, Names.Length).OrderBy(i =>
                Square(r - Convert.ToInt32(Hex[i].Substring(1, 2), 16)) +
                Square(g - Convert.ToInt32(Hex[i].Substring(3, 2), 16)) +
                Square(b - Convert.ToInt32(Hex[i].Substring(5, 2), 16))).First();
        }
        // Unknown words have no measurable hue: use nearest spelling and show the choice.
        return Enumerable.Range(0, Names.Length).OrderBy(i => Distance(name, Names[i].ToLowerInvariant())).First();
    }
    private static int Square(int x) => x * x;
    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++) {
            var next = new int[b.Length + 1]; next[0] = i;
            for (int j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = next;
        }
        return previous[b.Length];
    }
    internal static string Markup(string caption, string hex) => "<color=" +
        (hex != null && hex.Length == 9 && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : Hex[Resolve(hex)]) + ">" +
        (caption ?? "").Replace("<", "‹").Replace(">", "›") + "</color>";
}
