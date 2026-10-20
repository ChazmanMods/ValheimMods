using System;
using TMPro;
using UnityEngine;

namespace BetterSigns
{
    internal sealed class SignStyle
    {
        private const string EnabledKey = "BetterSigns_enabled";
        private const string FontKey = "BetterSigns_font";
        private const string ColorKey = "BetterSigns_color";
        private const string SizeKey = "BetterSigns_size";
        private const string AlignmentKey = "BetterSigns_alignment";
        private const string BoldKey = "BetterSigns_bold";
        private const string ItalicKey = "BetterSigns_italic";

        internal string Text = "";
        internal string Font = "";
        internal Color Color = Color.white;
        internal float Size = 0f;
        internal TextAlignmentOptions Alignment = TextAlignmentOptions.Center;
        internal bool Bold;
        internal bool Italic;
        internal bool Enabled;

        internal static SignStyle Read(Sign sign)
        {
            var style = new SignStyle { Text = sign?.GetText() ?? "" };
            ZNetView view = sign != null ? sign.GetComponent<ZNetView>() : null;
            ZDO zdo = view?.GetZDO();
            if (zdo == null || !zdo.GetBool(EnabledKey, false))
            {
                if (sign?.m_textWidget != null)
                {
                    style.Font = sign.m_textWidget.font?.name ?? "";
                    style.Color = sign.m_textWidget.color;
                    style.Size = sign.m_textWidget.fontSize;
                    style.Alignment = sign.m_textWidget.alignment;
                    style.Bold = (sign.m_textWidget.fontStyle & FontStyles.Bold) != 0;
                    style.Italic = (sign.m_textWidget.fontStyle & FontStyles.Italic) != 0;
                }
                return style;
            }

            style.Enabled = true;
            style.Font = zdo.GetString(FontKey, "");
            style.Color = UnpackColor(zdo.GetInt(ColorKey, -1));
            style.Size = zdo.GetFloat(SizeKey, 0f);
            style.Alignment = (TextAlignmentOptions)zdo.GetInt(AlignmentKey, (int)TextAlignmentOptions.Center);
            style.Bold = zdo.GetBool(BoldKey, false);
            style.Italic = zdo.GetBool(ItalicKey, false);
            return style;
        }

        internal bool Store(Sign sign, out string error)
        {
            error = "";
            ZNetView view = sign != null ? sign.GetComponent<ZNetView>() : null;
            if (view?.GetZDO() == null)
            {
                error = "The sign is not connected to the world.";
                return false;
            }

            sign.SetText(Text ?? "");
            ZDO zdo = view.GetZDO();
            string writtenText = zdo.GetString(ZDOVars.s_text, "");
            if (!string.Equals(writtenText, Text ?? "", StringComparison.Ordinal))
            {
                error = "Valheim rejected the text update. Move closer or check ward access.";
                return false;
            }

            view.ClaimOwnership();
            zdo.Set(EnabledKey, true);
            zdo.Set(FontKey, Font ?? "");
            zdo.Set(ColorKey, PackColor(Color));
            zdo.Set(SizeKey, Size);
            zdo.Set(AlignmentKey, (int)Alignment);
            zdo.Set(BoldKey, Bold);
            zdo.Set(ItalicKey, Italic);
            Enabled = true;
            return true;
        }

        internal static bool HasStoredStyle(Sign sign) =>
            sign != null && sign.GetComponent<ZNetView>()?.GetZDO()?.GetBool(EnabledKey, false) == true;

        private static int PackColor(Color value)
        {
            Color32 c = value;
            return c.r | (c.g << 8) | (c.b << 16) | (c.a << 24);
        }

        private static Color UnpackColor(int value)
        {
            if (value == -1) return Color.white;
            return new Color32((byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24));
        }
    }
}
