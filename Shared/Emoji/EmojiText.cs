using System;
using System.Text;

namespace Runic.Shared;

internal sealed class EmojiEntry
{
    internal readonly uint Unicode;
    internal readonly string Name, Group;
    internal string Text => char.ConvertFromUtf32((int)Unicode);
    internal EmojiEntry(uint unicode, string name, string group)
    { Unicode = unicode; Name = name; Group = group; }
}

internal static class EmojiText
{
    // The existing wire formats count UTF-16 units. Never store half a surrogate pair.
    internal static bool ValidUnicode(string text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
            if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) ||
                ++i >= text.Length || !char.IsLowSurrogate(text[i]))) return false;
        return true;
    }

    internal static string Clean(string text, int limit)
    {
        var result = new StringBuilder();
        for (int i = 0; i < (text ?? "").Length && result.Length < limit; i++)
        {
            char c = text[i];
            if (!char.IsSurrogate(c)) result.Append(c);
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (result.Length + 2 > limit) break;
                result.Append(c).Append(text[++i]);
            }
        }
        return result.ToString();
    }

    internal static bool Insert(string text, int anchor, int focus, string emoji, int limit,
        out string result, out int caret)
    {
        text ??= "";
        int start = Math.Max(0, Math.Min(text.Length, Math.Min(anchor, focus)));
        int end = Math.Max(0, Math.Min(text.Length, Math.Max(anchor, focus)));
        bool selection = start != end;
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start])) start--;
        if (end > 0 && end < text.Length && char.IsLowSurrogate(text[end])) end += selection ? 1 : -1;
        result = text; caret = start;
        if (!ValidUnicode(text) || !ValidUnicode(emoji) || text.Length - (end - start) + emoji.Length > limit) return false;
        result = text.Substring(0, start) + emoji + text.Substring(end);
        caret = start + emoji.Length;
        return true;
    }
}
