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
                "Runic Portals - setup guide", _titleStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth, content.Status, _statusStyle, gap);
            if (content.Warning.Length != 0)
                DrawLabel(ref currentY, x + inset, innerWidth, content.Warning, _warningStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth, "Controls", _headingStyle, gap * 0.5f);
            DrawLabel(ref currentY, x + inset, innerWidth, content.Controls, _bodyStyle, gap);
            DrawLabel(ref currentY, x + inset, innerWidth,
                "Setup commands and access", _headingStyle, gap * 0.5f);
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
            if (normalized.EndsWith("/lefttrigger", StringComparison.Ordinal)) return "Left Trigger";
            if (normalized.EndsWith("/righttrigger", StringComparison.Ordinal)) return "Right Trigger";
            if (normalized.EndsWith("/leftshoulder", StringComparison.Ordinal)) return "Left Bumper";
            if (normalized.EndsWith("/rightshoulder", StringComparison.Ordinal)) return "Right Bumper";
            if (normalized.EndsWith("/buttonsouth", StringComparison.Ordinal)) return "A / Cross";
            if (normalized.EndsWith("/buttoneast", StringComparison.Ordinal)) return "B / Circle";
            if (normalized.EndsWith("/buttonwest", StringComparison.Ordinal)) return "X / Square";
            if (normalized.EndsWith("/buttonnorth", StringComparison.Ordinal)) return "Y / Triangle";
            if (normalized.EndsWith("/dpad/up", StringComparison.Ordinal)) return "D-pad Up";
            if (normalized.EndsWith("/dpad/down", StringComparison.Ordinal)) return "D-pad Down";
            if (normalized.EndsWith("/dpad/left", StringComparison.Ordinal)) return "D-pad Left";
            if (normalized.EndsWith("/dpad/right", StringComparison.Ordinal)) return "D-pad Right";
            if (normalized.EndsWith("/leftstickpress", StringComparison.Ordinal)) return "Left Stick Click";
            if (normalized.EndsWith("/rightstickpress", StringComparison.Ordinal)) return "Right Stick Click";
            if (normalized.EndsWith("/start", StringComparison.Ordinal)) return "Menu";
            if (normalized.EndsWith("/select", StringComparison.Ordinal)) return "View / Share";
            return BoundedLabel(fallback, "Controller action");
        }

        private float Measure(PortalSetupGuideContent content, float width, int fontSize)
        {
            float inset = Mathf.Max(10f, fontSize * 0.8f);
            float gap = Mathf.Max(4f, fontSize * 0.32f);
            float innerWidth = width - inset * 2f;
            float height = inset * 2f;
            height += LabelHeight("Runic Portals - setup guide", _titleStyle, innerWidth) + gap;
            height += LabelHeight(content.Status, _statusStyle, innerWidth) + gap;
            if (content.Warning.Length != 0)
                height += LabelHeight(content.Warning, _warningStyle, innerWidth) + gap;
            height += LabelHeight("Controls", _headingStyle, innerWidth) + gap * 0.5f;
            height += LabelHeight(content.Controls, _bodyStyle, innerWidth) + gap;
            height += LabelHeight("Setup commands and access", _headingStyle, innerWidth) + gap * 0.5f;
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
                use = KeyboardBinding("Use", "Use");
                string keyboardAlternate = KeyboardBinding("AltPlace", "Alternate Place");
                alternateUse = keyboardAlternate + " + " + use;
                return;
            }

            use = ControllerBinding("JoyUse", "Use");
            string alternateAction = ZInput.InputLayout == InputLayout.Default
                ? "JoyAltPlace"
                : "JoyAltKeys";
            string controllerAlternate = ControllerBinding(alternateAction, "Controller Alt");
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
