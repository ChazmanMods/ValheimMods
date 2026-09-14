using RunicClock;
using UnityEngine;

internal static class Program
{
    private static int Main()
    {
        Action[] tests = { OptionalRowsAndIconToggle, LayoutBoundsAndResponsiveScale, GuiColorIsRestored,
            SymbolsAreCachedAndDisposed, TransparentBackgroundDoesNotPaintPanel };
        int passed = 0;
        foreach (var test in tests)
            try { test(); System.Console.WriteLine("PASS " + test.Method.Name); passed++; }
            catch (Exception e) { System.Console.WriteLine("FAIL " + test.Method.Name + ": " + e); }
        System.Console.WriteLine($"{passed}/{tests.Length} production-view checks passed.");
        return passed == tests.Length ? 0 : 1;
    }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static ClockView New(ClockConfig config)
    {
        Screen.width = 1920; Screen.height = 1080; Screen.safeArea = new Rect(0,0,1920,1080);
        GUI.Labels.Clear(); GUI.Textures.Clear(); GUI.Throw = false;
        var view = new ClockView(config); view.SetText("6:30 PM", "Day 142", "Local 10:42 PM", false); return view;
    }
    private static void OptionalRowsAndIconToggle()
    {
        var config = new ClockConfig(); using var view = New(config); view.Draw();
        True(GUI.Labels.Count == 2 && GUI.Textures.Count == 3);
        GUI.Labels.Clear(); GUI.Textures.Clear(); config.ShowRealTime.Value = true; view.Draw(); True(GUI.Labels.Count == 3);
        GUI.Labels.Clear(); GUI.Textures.Clear(); config.ShowDay.Value = false; config.ShowIndicator.Value = false; view.Draw();
        True(GUI.Labels.Count == 2 && GUI.Textures.Count == 2);
    }
    private static void LayoutBoundsAndResponsiveScale()
    {
        var config = new ClockConfig(); using var view = New(config); config.ShowRealTime.Value = true;
        foreach (int height in new[] { 720, 1080, 1440, 2160 })
            foreach (float scale in new[] { 0.6f, 1f, 2f })
                foreach (ClockAnchor anchor in Enum.GetValues<ClockAnchor>())
                {
                    Screen.height = height; Screen.width = height * 16 / 9;
                    Screen.safeArea = new Rect(10, 20, Screen.width - 20, height - 50);
                    config.Scale.Value = scale; config.Anchor.Value = anchor; GUI.Labels.Clear(); GUI.Textures.Clear(); view.Draw();
                    foreach (var item in GUI.Labels.Concat(GUI.Textures))
                    {
                        True(item.Rect.x >= 10 && item.Rect.y >= 30);
                        True(item.Rect.x + item.Rect.width <= Screen.width - 9.9f);
                        True(item.Rect.y + item.Rect.height <= Screen.height - 19.9f);
                    }
                }
    }
    private static void GuiColorIsRestored()
    {
        using var view = New(new ClockConfig()); var color = new Color(0.2f,0.3f,0.4f,0.5f); GUI.color = color;
        view.Draw(); True(GUI.color.Equals(color));
        GUI.Throw = true; try { view.Draw(); } catch (Exception) { }
        True(GUI.color.Equals(color)); GUI.Throw = false;
    }
    private static void SymbolsAreCachedAndDisposed()
    {
        int created = Texture2D.Created, destroyed = UnityEngine.Object.Destroyed;
        var view = New(new ClockConfig()); for (int i = 0; i < 100; i++) view.Draw();
        True(Texture2D.Created - created == 2);
        True(Texture2D.Masks.Count >= 2 && Texture2D.Masks.TakeLast(2).All(mask => mask.Any(c => c.a == 0) && mask.Any(c => c.a == 1)));
        view.Dispose(); True(UnityEngine.Object.Destroyed - destroyed == 2);
    }
    private static void TransparentBackgroundDoesNotPaintPanel()
    {
        var config = new ClockConfig(); config.BackgroundOpacity.Value = 0; config.ShowIndicator.Value = false;
        using var view = New(config); view.Draw(); True(GUI.Textures.Count == 0 && GUI.Labels.Count == 2);
    }
}

namespace RunicClock
{
    internal class Entry<T> { public T Value; public Entry(T value) => Value = value; }
    internal class ClockConfig
    {
        public Entry<float> Scale = new(1), OffsetX = new(0), OffsetY = new(64), BackgroundOpacity = new(0.65f);
        public Entry<ClockAnchor> Anchor = new(ClockAnchor.TopCenter);
        public Entry<bool> ShowIndicator = new(true), ShowDay = new(true), ShowRealTime = new(false);
    }
}
namespace UnityEngine
{
    public class Object { public static int Destroyed; public static void Destroy(Object value) => Destroyed++; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x=x; this.y=y; this.width=width; this.height=height; }
        public float yMax => y + height;
    }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white => new(1,1,1);
    }
    public static class Mathf
    {
        public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static int RoundToInt(float value)=>(int)Math.Round(value);
    }
    public static class Screen { public static int width=1920,height=1080; public static Rect safeArea=new(0,0,1920,1080); }
    public enum FontStyle { Normal, Bold }
    public enum TextAnchor { MiddleCenter }
    public enum TextureFormat { RGBA32 }
    public enum HideFlags { HideAndDontSave }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public class RectOffset { public RectOffset(int a,int b,int c,int d) { } }
    public class GUIContent { public string text; }
    public class GUIStyleState { public Color textColor; }
    public struct Vector2 { public float x,y; }
    public class GUIStyle
    {
        public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public bool richText,wordWrap; public RectOffset padding;
        public GUIStyleState normal = new();
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { fontSize=other.fontSize; }
        // Approximate font metrics; this verifies production geometry, not Unity font rendering.
        public Vector2 CalcSize(GUIContent content)=>new() { x=(content.text?.Length??0)*fontSize*0.6f,y=fontSize };
    }
    public class GUISkin { public GUIStyle label=new(); }
    public static class GUI
    {
        public static GUISkin skin=new(); public static Color color=Color.white; public static bool Throw;
        public static List<(Rect Rect,string Text)> Labels=new(), Textures=new();
        public static void Label(Rect rect,GUIContent content,GUIStyle style) { if(Throw)throw new Exception("fake draw failure"); Labels.Add((rect,content.text)); }
        public static void DrawTexture(Rect rect,Texture2D texture) { if(Throw)throw new Exception("fake draw failure"); Textures.Add((rect,texture.name)); }
    }
    public class Texture2D : Object
    {
        public static Texture2D whiteTexture=new(); public static int Created; public static List<Color[]> Masks=new();
        public string name; public HideFlags hideFlags; public FilterMode filterMode; public TextureWrapMode wrapMode;
        private Texture2D() { }
        public Texture2D(int width,int height,TextureFormat format,bool mip) { Created++; }
        public void SetPixels(Color[] pixels)=>Masks.Add(pixels);
        public void Apply(bool mip,bool unreadable) { }
    }
}
