using System;
using System.Linq;
using Runic.Shared;

internal static class EmojiRegressionTests
{
    internal static void Run()
    {
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        string fire = char.ConvertFromUtf32(0x1F525);
        Check(EmojiCatalog.Entries.Length == 64 && EmojiCatalog.Entries.Select(e => e.Unicode).Distinct().Count() == 64, "64 unique atlas entries");
        Check(EmojiCatalog.Entries.GroupBy(e => e.Group).All(g => g.Count() == 16), "four full picker pages");
        foreach (var e in EmojiCatalog.Entries)
            Check(EmojiText.ValidUnicode(e.Text) && e.Text.Length == 2, "complete supplementary Unicode scalar: " + e.Name);
        Check(EmojiText.Insert("Fuel", 0, 0, fire, 96, out var text, out int caret) && text == fire + "Fuel" && caret == 2, "insert at start");
        Check(EmojiText.Insert("Fuel", 4, 4, fire, 96, out text, out caret) && text == "Fuel" + fire && caret == 6, "append");
        Check(EmojiText.Insert("ABCD", 3, 1, fire, 4, out text, out caret) && text == "A" + fire + "D" && caret == 3, "reverse selection and exact limit");
        Check(EmojiText.Insert("A" + fire + "B", 2, 2, fire, 96, out text, out caret) && text == "A" + fire + fire + "B", "caret inside pair snaps before emoji");
        Check(EmojiText.Insert("A" + fire + "B", 2, 3, fire, 96, out text, out caret) && text == "A" + fire + "B", "partial selection expands to whole pair");
        Check(!EmojiText.Insert(new string('a', 95), 95, 95, fire, 96, out text, out caret) && text.Length == 95, "full label remains unchanged");
        Check(EmojiText.Insert(new string('a', 96), 94, 96, fire, 96, out text, out caret) && text.Length == 96, "replace at limit");
        Check(!EmojiText.Insert("a", 0, 0, "\uD800", 96, out text, out caret), "reject malformed insertion");
        Check(!EmojiText.ValidUnicode("a\uD800") && !EmojiText.ValidUnicode("\uDC00a"), "reject lone surrogates");
        Check(EmojiText.Clean("a\uD800b\uDC00c" + fire, 96) == "abc" + fire, "repair truncated paste without losing later text");
        Check(EmojiText.Clean("a" + fire, 2) == "a", "limit never splits pair");
        Check(EmojiText.Clean("a" + fire, 3) == "a" + fire, "exact boundary keeps emoji");
        Check(EmojiText.Clean(fire + "\nFood <color=red>", 256) == fire + "\nFood <color=red>", "text/newlines/markup stay literal");
        Check(EmojiText.ValidUnicode("\U0001F6E1\uFE0F"), "pasted variation selector is preserved");
    }
}
