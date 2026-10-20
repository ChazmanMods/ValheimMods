using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterSigns
{
    [HarmonyPatch]
    internal static class SignPatches
    {
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText))]
        [HarmonyPrefix]
        private static bool ReplaceVanillaEditor(TextReceiver sign, string topic, int charLimit)
        {
            if (!(sign is Sign target)) return true;
            SignEditor.Open(target);
            return false;
        }

        [HarmonyPatch(typeof(Sign), "UpdateText")]
        [HarmonyPostfix]
        private static void ApplyFormatting(Sign __instance) => Format(__instance);

        internal static void Format(Sign __instance)
        {
            if (__instance == null || __instance.m_textWidget == null) return;
            if (!SignStyle.HasStoredStyle(__instance)) return;
            SignStyle style = SignStyle.Read(__instance);
            TMP_Text widget = __instance.m_textWidget;
            widget.text = style.Text;
            widget.color = style.Color;
            widget.alignment = style.Alignment;
            widget.fontStyle = (style.Bold ? FontStyles.Bold : FontStyles.Normal) |
                               (style.Italic ? FontStyles.Italic : FontStyles.Normal);
            if (style.Size > 0f) widget.fontSize = style.Size;
            if (!string.IsNullOrEmpty(style.Font))
            {
                TMP_FontAsset font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .FirstOrDefault(f => string.Equals(f.name, style.Font, StringComparison.OrdinalIgnoreCase));
                if (font != null && FontCatalog.CanRenderBasicText(font)) widget.font = font;
            }
        }
    }
}
