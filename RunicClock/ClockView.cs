using System;
using UnityEngine;

namespace RunicClock
{
    internal sealed class ClockView : IDisposable
    {
        private readonly ClockConfig _settings;
        private readonly GUIContent _time = new GUIContent(), _day = new GUIContent(), _local = new GUIContent();
        private GUIStyle _large, _small;
        private int _fontSize;
        private bool _isDay;
        private Texture2D _sun, _moon;
        private static readonly Color Gold = new Color(1f, 0.80f, 0.35f);
        internal ClockView(ClockConfig settings) => _settings = settings;
        internal void SetText(string time, string day, string local, bool isDay)
        { _time.text = time; _day.text = day; _local.text = local; _isDay = isDay; }

        internal void Draw()
        {
            float scale = (float)ClockModel.Clamp(_settings.Scale.Value, 0.6, 2, 1) * Mathf.Clamp(Screen.height / 1080f, 0.5f, 3f);
            EnsureStyles(Mathf.Max(12, Mathf.RoundToInt(24 * scale)));
            if (_sun == null) _sun = CreateSymbol(false);
            if (_moon == null) _moon = CreateSymbol(true);
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            float safeTop = Screen.height - safe.yMax; // Unity's safe area is bottom-left; IMGUI is top-left.
            float padding = 10 * scale, line = 31 * scale, smallLine = 21 * scale, symbol = 24 * scale;
            bool showIcon = _settings.ShowIndicator.Value, showDay = _settings.ShowDay.Value, showLocal = _settings.ShowRealTime.Value;
            float mainWidth = _large.CalcSize(_time).x + (showIcon ? symbol + 8 * scale : 0);
            float contentWidth = mainWidth;
            if (showDay) contentWidth = Mathf.Max(contentWidth, _small.CalcSize(_day).x);
            if (showLocal) contentWidth = Mathf.Max(contentWidth, _small.CalcSize(_local).x);
            float width = Mathf.Min(safe.width, Mathf.Max(180 * scale, contentWidth + padding * 2));
            float height = Mathf.Min(safe.height, padding * 2 + line + (showDay ? smallLine : 0) + (showLocal ? smallLine : 0));
            var position = ClockModel.Position(_settings.Anchor.Value, safe.x, safeTop, safe.width, safe.height,
                width, height, _settings.OffsetX.Value, _settings.OffsetY.Value);
            Rect panel = new Rect((float)position.X, (float)position.Y, width, height);
            Color previousColor = GUI.color;
            try
            {
                float opacity = (float)ClockModel.Clamp(_settings.BackgroundOpacity.Value, 0, 1, 0.65);
                if (opacity > 0)
                {
                    Paint(panel, new Color(0.025f, 0.035f, 0.06f, opacity));
                    Paint(new Rect(panel.x, panel.y, panel.width, Mathf.Max(1, scale)), new Color(Gold.r, Gold.g, Gold.b, opacity));
                }
                float x = panel.x + (panel.width - mainWidth) / 2, y = panel.y + padding;
                if (showIcon)
                {
                    GUI.color = _isDay ? Gold : new Color(0.77f, 0.86f, 1f);
                    GUI.DrawTexture(new Rect(x, y + (line - symbol) / 2, symbol, symbol), _isDay ? _sun : _moon);
                    x += symbol + 8 * scale;
                }
                GUI.color = Color.white;
                GUI.Label(new Rect(x, y, _large.CalcSize(_time).x, line), _time, _large);
                y += line;
                if (showDay) { GUI.Label(new Rect(panel.x, y, width, smallLine), _day, _small); y += smallLine; }
                if (showLocal) GUI.Label(new Rect(panel.x, y, width, smallLine), _local, _small);
            }
            finally { GUI.color = previousColor; }
        }

        private static void Paint(Rect area, Color color) { GUI.color = color; GUI.DrawTexture(area, Texture2D.whiteTexture); }
        private void EnsureStyles(int fontSize)
        {
            if (_large != null && _fontSize == fontSize) return;
            _fontSize = fontSize;
            _large = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            _large.normal.textColor = Gold;
            _small = new GUIStyle(_large) { fontSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * 0.62f)), fontStyle = FontStyle.Normal };
            _small.normal.textColor = new Color(0.91f, 0.92f, 0.95f);
        }

        private static Texture2D CreateSymbol(bool moon)
        {
            const int size = 64;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float coverage = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            double dx = (x + (sx + 0.5) / 2 - size / 2.0) / (size / 2), dy = (y + (sy + 0.5) / 2 - size / 2.0) / (size / 2);
                            double radius = Math.Sqrt(dx * dx + dy * dy);
                            bool filled = moon ? radius < 0.85 && (dx - 0.4) * (dx - 0.4) + (dy - 0.2) * (dy - 0.2) > 0.72 * 0.72 :
                                radius < 0.48 || (radius > 0.62 && radius < 0.9 && Math.Abs(Math.Sin(Math.Atan2(dy, dx) * 4)) < 0.18);
                            if (filled) coverage += 0.25f;
                        }
                    pixels[y * size + x] = new Color(1, 1, 1, coverage);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = moon ? "RunicClock Moon" : "RunicClock Sun", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels); texture.Apply(false, true);
            return texture;
        }

        public void Dispose()
        {
            if (_sun != null) UnityEngine.Object.Destroy(_sun);
            if (_moon != null) UnityEngine.Object.Destroy(_moon);
            _sun = _moon = null;
        }
    }
}
