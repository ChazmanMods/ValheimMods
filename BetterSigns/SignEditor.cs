using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace BetterSigns
{
    internal sealed class SignEditor : MonoBehaviour
    {
        private static SignEditor _instance;
        private Sign _target;
        private SignStyle _style;
        private Rect _window;
        private string[] _fonts = Array.Empty<string>();
        private TMP_FontAsset[] _fontAssets = Array.Empty<TMP_FontAsset>();
        private int _fontIndex;
        private Vector2 _symbolScroll;
        private bool _stylesReady;
        private GUIStyle _windowStyle, _titleStyle, _labelStyle, _mutedStyle, _textAreaStyle;
        private GUIStyle _buttonStyle, _selectedButtonStyle, _previewStyle, _swatchStyle;
        private Texture2D _panelTexture, _fieldTexture, _buttonTexture, _selectedTexture, _previewTexture;
        private string _saveError = "";

        internal static bool IsOpen => _instance != null && _instance._target != null;

        private static readonly string[] Symbols =
        {
            "⚔", "🛡", "☠", "♥", "★", "✦", "⚒", "⌂", "→", "←", "↑", "↓",
            "✓", "✕", "!", "?", "♜", "♞", "♠", "♣", "♦", "🔥", "❄", "☀",
            "☾", "⚡", "⚓", "⛏", "🐟", "🐗", "🐺", "🦌", "🍖", "🍺", "📫"
        };

        private static readonly Color[] Colors =
        {
            new Color(1f,1f,1f), new Color(.08f,.08f,.08f), new Color(.42f,.22f,.08f),
            new Color(.92f,.2f,.18f), new Color(1f,.52f,.08f), new Color(1f,.84f,.15f),
            new Color(.25f,.78f,.3f), new Color(.18f,.78f,.82f), new Color(.2f,.48f,.95f),
            new Color(.64f,.32f,.95f), new Color(.9f,.2f,.72f)
        };

        private void Awake() => _instance = this;

        internal static void Open(Sign sign)
        {
            if (_instance == null || sign == null) return;
            _instance._target = sign;
            _instance._style = SignStyle.Read(sign);
            _instance._saveError = "";
            _instance.LoadFonts();
            GUIManager.BlockInput(true);
            float width = Mathf.Min(760f, Screen.width - 40f);
            float height = Mathf.Min(720f, Screen.height - 40f);
            _instance._window = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            GUI.FocusControl(null);
        }

        private void LoadFonts()
        {
            _fontAssets = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .Where(f => f != null && !string.IsNullOrEmpty(f.name) && FontCatalog.CanRenderBasicText(f))
                .GroupBy(f => f.name).Select(g => g.First()).OrderBy(f => f.name).ToArray();
            _fonts = _fontAssets.Select(f => f.name).ToArray();
            _fontIndex = Math.Max(0, Array.FindIndex(_fonts,
                n => string.Equals(n, _style.Font, StringComparison.OrdinalIgnoreCase)));
        }

        private void Update()
        {
            if (_target == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnDestroy()
        {
            if (_target != null) GUIManager.BlockInput(false);
        }

        private void OnGUI()
        {
            if (_target == null || _style == null) return;
            EnsureStyles();
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, .52f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            _window = GUI.Window(942731, _window, DrawWindow, GUIContent.none, _windowStyle);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label("BETTER SIGNS", _titleStyle);
            GUILayout.Label("Design the text, then preview it before saving.", _mutedStyle);
            GUILayout.Space(10);

            GUILayout.Label("SIGN TEXT", _labelStyle);
            _style.Text = GUILayout.TextArea(_style.Text ?? "", _textAreaStyle, GUILayout.Height(108));
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("FONT", _labelStyle, GUILayout.Width(65));
            if (GUILayout.Button("◀", _buttonStyle, GUILayout.Width(44), GUILayout.Height(34))) ChangeFont(-1);
            GUILayout.Label(_fonts.Length == 0 ? "Default Valheim font" : FriendlyFontName(_fonts[_fontIndex]),
                _previewStyle, GUILayout.ExpandWidth(true), GUILayout.Height(34));
            if (GUILayout.Button("▶", _buttonStyle, GUILayout.Width(44), GUILayout.Height(34))) ChangeFont(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("SIZE", _labelStyle, GUILayout.Width(65));
            float size = _style.Size <= 0 ? 24 : _style.Size;
            size = GUILayout.HorizontalSlider(size, 8, 96, GUILayout.ExpandWidth(true));
            _style.Size = Mathf.Round(size);
            GUILayout.Label(_style.Size.ToString("0"), _previewStyle, GUILayout.Width(48), GUILayout.Height(32));
            if (GUILayout.Button("B", _style.Bold ? _selectedButtonStyle : _buttonStyle, GUILayout.Width(45), GUILayout.Height(34))) _style.Bold = !_style.Bold;
            if (GUILayout.Button("I", _style.Italic ? _selectedButtonStyle : _buttonStyle, GUILayout.Width(45), GUILayout.Height(34))) _style.Italic = !_style.Italic;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ALIGN", _labelStyle, GUILayout.Width(65));
            AlignmentButton("LEFT", TextAlignmentOptions.Left);
            AlignmentButton("CENTER", TextAlignmentOptions.Center);
            AlignmentButton("RIGHT", TextAlignmentOptions.Right);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("COLOR", _labelStyle);
            GUILayout.BeginHorizontal();
            foreach (Color color in Colors)
            {
                Color oldBackground = GUI.backgroundColor;
                GUI.backgroundColor = color;
                if (GUILayout.Button(ColorDistance(_style.Color, color) < .02f ? "✓" : "", _swatchStyle,
                    GUILayout.Width(48), GUILayout.Height(32))) _style.Color = color;
                GUI.backgroundColor = oldBackground;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawSymbols();
            GUILayout.Space(8);
            DrawPreview();
            if (!string.IsNullOrEmpty(_saveError))
                GUILayout.Label(_saveError, new GUIStyle(_mutedStyle) { normal = { textColor = new Color(1f,.3f,.25f) } });
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE SIGN", _selectedButtonStyle, GUILayout.Height(46))) Save();
            if (GUILayout.Button("CANCEL", _buttonStyle, GUILayout.Height(46))) Close();
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, _window.width, 42));
        }

        private void DrawSymbols()
        {
            List<string> supported = Symbols.Where(CurrentFontSupports).ToList();
            GUILayout.Label("SYMBOLS", _labelStyle);
            if (supported.Count == 0)
            {
                GUILayout.Label("This font has no supported symbols. Choose another font.", _mutedStyle);
                return;
            }
            _symbolScroll = GUILayout.BeginScrollView(_symbolScroll, GUILayout.Height(82));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < supported.Count; i++)
            {
                if (i > 0 && i % 12 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Button(supported[i], _buttonStyle, GUILayout.Width(48), GUILayout.Height(34))) _style.Text += supported[i];
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private void DrawPreview()
        {
            GUILayout.Label("PREVIEW", _labelStyle);
            GUIStyle preview = new GUIStyle(_previewStyle)
            {
                alignment = ToGuiAlignment(_style.Alignment),
                fontSize = Mathf.Clamp(Mathf.RoundToInt(_style.Size * .72f), 13, 42),
                fontStyle = _style.Bold && _style.Italic ? FontStyle.BoldAndItalic :
                    _style.Bold ? FontStyle.Bold : _style.Italic ? FontStyle.Italic : FontStyle.Normal,
                normal = { textColor = _style.Color }
            };
            GUILayout.Label(string.IsNullOrEmpty(_style.Text) ? "Your sign will appear here" : _style.Text,
                preview, GUILayout.Height(72));
        }

        private void AlignmentButton(string text, TextAlignmentOptions alignment)
        {
            if (GUILayout.Button(text, _style.Alignment == alignment ? _selectedButtonStyle : _buttonStyle,
                GUILayout.Height(34))) _style.Alignment = alignment;
        }

        private void ChangeFont(int direction)
        {
            if (_fonts.Length == 0) return;
            _fontIndex = (_fontIndex + _fonts.Length + direction) % _fonts.Length;
            _symbolScroll = Vector2.zero;
        }

        private bool CurrentFontSupports(string symbol)
        {
            return _fontAssets.Length > 0 && FontCatalog.CanRender(_fontAssets[_fontIndex], symbol);
        }

        private void Save()
        {
            if (_fonts.Length > 0) _style.Font = _fonts[_fontIndex];
            if (!_style.Store(_target, out _saveError))
            {
                Plugin.Log.LogError("Better Signs save failed: " + _saveError);
                return;
            }
            SignPatches.Format(_target);
            SignStyle verified = SignStyle.Read(_target);
            if (!string.Equals(verified.Text, _style.Text, StringComparison.Ordinal) ||
                !string.Equals(_target.m_textWidget.text, _style.Text, StringComparison.Ordinal))
            {
                _saveError = "The sign failed post-save verification. Nothing was released.";
                Plugin.Log.LogError($"Better Signs verification failed. expected='{_style.Text}' zdo='{verified.Text}' rendered='{_target.m_textWidget.text}'");
                return;
            }
            Plugin.Log.LogInfo($"Better Signs save verified: {_style.Text.Length} characters, font '{_style.Font}'.");
            Close();
        }

        private void Close()
        {
            _target = null;
            _style = null;
            GUIManager.BlockInput(false);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            _panelTexture = SolidTexture(new Color(.055f, .07f, .085f, .99f));
            _fieldTexture = SolidTexture(new Color(.92f, .94f, .94f, 1f));
            _buttonTexture = SolidTexture(new Color(.16f, .19f, .22f, 1f));
            _selectedTexture = SolidTexture(new Color(.76f, .48f, .16f, 1f));
            _previewTexture = SolidTexture(new Color(.025f, .032f, .04f, 1f));
            _windowStyle = new GUIStyle(GUI.skin.window) { normal = { background = _panelTexture }, padding = new RectOffset(20,20,14,16) };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f,.72f,.3f) } };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _mutedStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.68f,.72f,.75f) } };
            _textAreaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 20, wordWrap = true, padding = new RectOffset(10,10,8,8), normal = { background = _fieldTexture, textColor = new Color(.04f,.05f,.06f) }, focused = { background = _fieldTexture, textColor = new Color(.04f,.05f,.06f) } };
            _buttonStyle = ButtonStyle(_buttonTexture, Color.white);
            _selectedButtonStyle = ButtonStyle(_selectedTexture, Color.white);
            _previewStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(10,10,5,5), normal = { background = _previewTexture, textColor = Color.white } };
            _swatchStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { background = Texture2D.whiteTexture, textColor = Color.white }, hover = { background = Texture2D.whiteTexture, textColor = Color.white }, active = { background = Texture2D.whiteTexture, textColor = Color.white } };
        }

        private static GUIStyle ButtonStyle(Texture2D background, Color textColor) => new GUIStyle(GUI.skin.button)
        {
            fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8,8,6,6), normal = { background = background, textColor = textColor },
            hover = { background = background, textColor = new Color(1f,.78f,.36f) },
            active = { background = background, textColor = Color.white }
        };

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }

        private static string FriendlyFontName(string name) => name.Replace(" SDF", "").Replace("-", " ");
        private static float ColorDistance(Color a, Color b) => Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);
        private static TextAnchor ToGuiAlignment(TextAlignmentOptions alignment) =>
            alignment == TextAlignmentOptions.Left ? TextAnchor.MiddleLeft :
            alignment == TextAlignmentOptions.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
    }
}
