using TMPro;

namespace BetterSigns
{
    internal static class FontCatalog
    {
        private const string RequiredGlyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        internal static bool CanRenderBasicText(TMP_FontAsset font)
        {
            if (font == null) return false;
            foreach (char c in RequiredGlyphs)
                if (!font.HasCharacter(c, true, false)) return false;
            return true;
        }

        internal static bool CanRender(TMP_FontAsset font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                int codePoint = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i])) i++;
                if (codePoint > char.MaxValue || !font.HasCharacter((char)codePoint, true, false)) return false;
            }
            return true;
        }
    }
}
