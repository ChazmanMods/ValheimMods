using System;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Client-only, screen-space help for the portal currently under the crosshair. The Runic
    /// edit session pins its source so the command guide remains visible behind TextInput.
    /// </summary>
    internal sealed class PortalHoverPanel : IDisposable
    {
        private const int MaximumBindingCharacters = 64;
        private readonly PortalRuntime _runtime;
        private TeleportWorld _observedPortal;
        private int _observedFrame = -10;
        private bool _disabled;
        private int _styledFontSize;
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _bodyStyle;

        internal PortalHoverPanel(PortalRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        internal void Observe(TeleportWorld portal)
        {
            if (_disabled || portal == null) return;
            _observedPortal = portal;
            _observedFrame = Time.frameCount;
        }

        internal void ResetStyles() => _styledFontSize = 0;

        internal void Disable(Exception exception)
        {
            if (_disabled) return;
            _disabled = true;
            _observedPortal = null;
            Diagnostics.Warning(
                "Runic Portals disabled only its setup panel after a presentation fault; portal routing remains active. " +
                (exception == null ? "Unknown failure." : exception.GetType().Name + ": " + exception.Message));
        }

        internal void Draw()
        {
            if (_disabled || Application.isBatchMode || Event.current == null ||
                !(PortalConfig.ShowSetupPanel?.Value ?? true) || !_runtime.FeatureEnabled)
                return;

            TeleportWorld portal = _runtime.ActiveEditPortalForPanel;
            if (portal == null)
            {
                int age = Time.frameCount - _observedFrame;
                if (_observedPortal == null || age < 0 || age > 1) return;
                portal = _observedPortal;
            }
            if (portal == null || Player.m_localPlayer == null || Hud.instance == null ||
                Hud.instance.m_userHidden ||
                !_runtime.TryGetHoverPanelState(portal, out PortalHoverPanelState state))
                return;

            ResolveBindings(out string use, out string alternateUse);
            PortalSetupGuideContent content = PortalSetupGuide.Compose(state, use, alternateUse);
            Rect safe = Screen.safeArea;
            float safeTop = Screen.height - safe.yMax;
            float margin = 12f;
            float availableWidth = safe.width - margin * 2f;
            float availableHeight = safe.height - margin * 2f;
            if (!Finite(availableWidth) || !Finite(availableHeight) ||
                availableWidth < 340f || availableHeight < 300f)
                return;

            float configuredScale = FiniteClamp(
                PortalConfig.PanelScale?.Value ?? 1f, 0.75f, 1.5f, 1f);
            float width = Mathf.Min(700f * configuredScale, availableWidth);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(14f * configuredScale), 10, 22);
            float height;
            do
            {
                EnsureStyles(fontSize);
                height = Measure(content, width, fontSize);
                if (height <= availableHeight || fontSize <= 10) break;
                fontSize--;
            } while (true);
            if (height > availableHeight) return;

            float x = safe.xMin + margin;
            float y = safeTop + Mathf.Max(margin, (safe.height - height) * 0.5f);
            Rect panel = new Rect(x, y, width, height);
            GUI.Box(panel, GUIContent.none, _boxStyle);

            float inset = Mathf.Max(10f, fontSize * 0.8f);
            float gap = Mathf.Max(4f, fontSize * 0.32f);
            float innerWidth = width - inset * 2f;
            float currentY = y + inset;
            DrawLabel(ref currentY, x + inset, innerWidth,
                global::Runic.Localization.RunicText.Get("text_d07337e0623b"), _titleStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth, content.Status, _statusStyle, gap);
            if (content.Warning.Length != 0)
                DrawLabel(ref currentY, x + inset, innerWidth, content.Warning, _warningStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth, global::Runic.Localization.RunicText.Get("text_799c26913574"), _headingStyle, gap * 0.5f);
            DrawLabel(ref currentY, x + inset, innerWidth, content.Controls, _bodyStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth,
                global::Runic.Localization.RunicText.Get("text_957d354098df"), _headingStyle, gap * 0.5f);
            DrawLabel(ref currentY, x + inset, innerWidth, content.Instructions, _bodyStyle, 0f);
        }

        public void Dispose()
        {
            _observedPortal = null;
            _boxStyle = _titleStyle = _statusStyle = _warningStyle = _headingStyle =
                _bodyStyle = null;
        }

        internal static string FriendlyControllerPath(string path, string fallback)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();
            if (normalized.EndsWith("/lefttrigger", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_3ba65231b54a");
            if (normalized.EndsWith("/righttrigger", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_eb5168025c81");
            if (normalized.EndsWith("/leftshoulder", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_2bbd2670e952");
            if (normalized.EndsWith("/rightshoulder", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_b238567f2f82");
            if (normalized.EndsWith("/buttonsouth", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_b81cc6f28763");
            if (normalized.EndsWith("/buttoneast", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_7df3fba69cfe");
            if (normalized.EndsWith("/buttonwest", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_9affb90082db");
            if (normalized.EndsWith("/buttonnorth", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_7c3f8373297f");
            if (normalized.EndsWith("/dpad/up", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_1f85615c359b");
            if (normalized.EndsWith("/dpad/down", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_6e47237e9353");
            if (normalized.EndsWith("/dpad/left", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_321cd19f454a");
            if (normalized.EndsWith("/dpad/right", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_89cbe5b197bb");
            if (normalized.EndsWith("/leftstickpress", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_c3a92a975036");
            if (normalized.EndsWith("/rightstickpress", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_50b1892d38b2");
            if (normalized.EndsWith("/start", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_99af6606ff9d");
            if (normalized.EndsWith("/select", StringComparison.Ordinal)) return global::Runic.Localization.RunicText.Get("text_0b2e04edf664");
            return BoundedLabel(fallback, global::Runic.Localization.RunicText.Get("text_0b896c1c823b"));
        }

        private float Measure(PortalSetupGuideContent content, float width, int fontSize)
        {
            float inset = Mathf.Max(10f, fontSize * 0.8f);
            float gap = Mathf.Max(4f, fontSize * 0.32f);
            float innerWidth = width - inset * 2f;
            float height = inset * 2f;
            height += LabelHeight(global::Runic.Localization.RunicText.Get("text_d07337e0623b"), _titleStyle, innerWidth) + gap;
            height += LabelHeight(content.Status, _statusStyle, innerWidth) + gap;
            if (content.Warning.Length != 0)
                height += LabelHeight(content.Warning, _warningStyle, innerWidth) + gap;
            height += LabelHeight(global::Runic.Localization.RunicText.Get("text_799c26913574"), _headingStyle, innerWidth) + gap * 0.5f;
            height += LabelHeight(content.Controls, _bodyStyle, innerWidth) + gap;
            height += LabelHeight(global::Runic.Localization.RunicText.Get("text_957d354098df"), _headingStyle, innerWidth) + gap * 0.5f;
            height += LabelHeight(content.Instructions, _bodyStyle, innerWidth);
            return Mathf.Ceil(height);
        }

        private static float LabelHeight(string text, GUIStyle style, float width) =>
            Mathf.Ceil(style.CalcHeight(new GUIContent(text ?? string.Empty), width));

        private static void DrawLabel(
            ref float y,
            float x,
            float width,
            string text,
            GUIStyle style,
            float gap)
        {
            float height = LabelHeight(text, style, width);
            GUI.Label(new Rect(x, y, width, height), text, style);
            y += height + gap;
        }

        private void EnsureStyles(int fontSize)
        {
            if (_boxStyle != null && _styledFontSize == fontSize) return;
            _styledFontSize = fontSize;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = Label(fontSize + 3, FontStyle.Bold, new Color(0.96f, 0.82f, 0.4f, 1f));
            _statusStyle = Label(fontSize, FontStyle.Bold, new Color(0.75f, 0.9f, 1f, 1f));
            _warningStyle = Label(fontSize, FontStyle.Bold, new Color(1f, 0.68f, 0.3f, 1f));
            _headingStyle = Label(fontSize, FontStyle.Bold, new Color(0.88f, 0.88f, 0.88f, 1f));
            _bodyStyle = Label(Math.Max(10, fontSize - 1), FontStyle.Normal,
                new Color(0.9f, 0.92f, 0.94f, 1f));
        }

        private static GUIStyle Label(int fontSize, FontStyle fontStyle, Color color)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = false
            };
            style.normal.textColor = color;
            return style;
        }

        private static void ResolveBindings(out string use, out string alternateUse)
        {
            bool controller = false;
            try { controller = ZInput.IsGamepadActive(); }
            catch (Exception) { }
            if (!controller)
            {
                use = KeyboardBinding(global::Runic.Localization.RunicText.Get("text_c36d819e7bc6"), global::Runic.Localization.RunicText.Get("text_c36d819e7bc6"));
                string keyboardAlternate = KeyboardBinding("AltPlace", global::Runic.Localization.RunicText.Get("text_c24799272339"));
                alternateUse = keyboardAlternate + " + " + use;
                return;
            }

            use = ControllerBinding("JoyUse", global::Runic.Localization.RunicText.Get("text_c36d819e7bc6"));
            string alternateAction = ZInput.InputLayout == InputLayout.Default
                ? "JoyAltPlace"
                : "JoyAltKeys";
            string controllerAlternate = ControllerBinding(alternateAction, global::Runic.Localization.RunicText.Get("text_8f425f2e1d7b"));
            alternateUse = controllerAlternate + " + " + use;
        }

        private static string KeyboardBinding(string action, string fallback)
        {
            try
            {
                string value = ZInput.instance?.GetBoundKeyString(action, true);
                if (!string.IsNullOrWhiteSpace(value) && value.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string localized = Localization.instance == null
                        ? value
                        : Localization.instance.Localize(value);
                    return BoundedLabel(localized, fallback);
                }
            }
            catch (Exception) { }
            return fallback;
        }

        private static string ControllerBinding(string action, string fallback)
        {
            try
            {
                ZInput.ButtonDef definition = ZInput.instance?.GetButtonDef(action);
                return FriendlyControllerPath(definition?.GetActionPath(true), fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static string BoundedLabel(string value, string fallback)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return fallback;
            if (text.Length > MaximumBindingCharacters)
                text = text.Substring(0, MaximumBindingCharacters);
            for (int index = 0; index < text.Length; index++)
                if (char.IsControl(text[index])) return fallback;
            return text;
        }

        private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
            Finite(value) ? Mathf.Clamp(value, minimum, maximum) : fallback;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
