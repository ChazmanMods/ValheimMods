// Headless lifecycle harness. These substitute native UI/game objects; they are not shipped.
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class BepInPlugin : Attribute { public BepInPlugin(string guid, string name, string version) { } }
    public class BaseUnityPlugin
    {
        public Configuration.ConfigFile Config = new();
        public FakeLogger Logger = new();
    }
    public class FakeLogger
    {
        public int Errors;
        public void LogInfo(string text) { }
        public void LogError(string text) { Errors++; }
    }
}
namespace BepInEx.Configuration
{
    public class SettingChangedEventArgs : EventArgs { }
    public class ConfigFile
    {
        public readonly Dictionary<string, object> Entries = new();
        public event EventHandler<SettingChangedEventArgs> SettingChanged;
        public ConfigEntry<T> Bind<T>(string section, string key, T value, object description)
        {
            var entry = new ConfigEntry<T>(value, () => SettingChanged?.Invoke(this, new()));
            Entries[section + "/" + key] = entry;
            return entry;
        }
        public ConfigEntry<T> Get<T>(string key) => (ConfigEntry<T>)Entries[key];
    }
    public class ConfigEntry<T>
    {
        private T _value; private readonly Action _changed;
        public ConfigEntry(T value, Action changed) { _value = value; _changed = changed; }
        public T Value { get => _value; set { _value = value; _changed(); } }
    }
    public class ConfigDescription { public ConfigDescription(string text, object range) { } }
    public class AcceptableValueRange<T> { public AcceptableValueRange(T min, T max) { } }
    public struct KeyboardShortcut
    {
        public static bool Pressed;
        public KeyboardShortcut(UnityEngine.KeyCode key, params UnityEngine.KeyCode[] modifiers) { }
        public bool IsDown() => Pressed;
    }
}
namespace UnityEngine
{
    public static class Application { public static bool isBatchMode; }
    public static class Time { public static float unscaledTime; }
    public enum KeyCode { C, LeftAlt }
    public enum EventType { Repaint, Layout, MouseDown }
    public class Event { public static Event current = new(); public EventType type; }
    public class GameObject { public bool activeInHierarchy; }
    public class CanvasGroup { public GameObject gameObject = new(); }
}
public class Player { public static Player m_localPlayer; }
public class ZNet { public static ZNet instance; }
public class EnvMan
{
    public static EnvMan instance;
    public float Fraction = 0.5f;
    public int Day = 142, Reads;
    public bool Throw;
    public float GetDayFraction() { Reads++; if (Throw) throw new Exception("fake failure"); return Fraction; }
    public int GetDay() => Day;
    public static bool IsDay() => instance.Fraction >= 0.25 && instance.Fraction <= 0.75;
}
public class Hud
{
    public static Hud instance;
    public bool m_userHidden, Visible = true;
    public UnityEngine.CanvasGroup m_loadingScreen = new();
    public bool IsVisible() => Visible;
    public static bool IsPieceSelectionVisible() => false;
}
public static class Menu { public static bool Visible; public static bool IsVisible() => Visible; }
public static class Console { public static bool IsVisible() => false; }
public static class TextInput { public static bool Visible; public static bool IsVisible() => Visible; }
public static class InventoryGui { public static bool Visible; public static bool IsVisible() => Visible; }
public static class Minimap { public static bool Visible; public static bool IsOpen() => Visible; }
public static class StoreGui { public static bool IsVisible() => false; }
public static class UnifiedPopup { public static bool IsVisible() => false; }
public static class ConnectPanel { public static bool IsVisible() => false; }
public static class Feedback { public static bool IsVisible() => false; }
public static class PlayerCustomizaton { public static bool IsBarberGuiVisible() => false; }
public static class ZInput { public static bool VirtualKeyboardOpen; }
public static class Game { public static bool Paused; public static bool IsPaused() => Paused; }
public class TextViewer { public static TextViewer instance; public bool IsVisible() => false; }
public class Chat
{
    public static Chat instance;
    public bool Focus;
    public bool HasFocus() => Focus;
    public bool IsChatDialogWindowVisible() => false;
}
namespace RunicClock
{
    internal class ClockView : IDisposable
    {
        internal static ClockView Last;
        internal int Draws, Updates;
        internal bool Disposed, IsDay;
        internal string Time, Day, Local;
        internal ClockView(ClockConfig config) => Last = this;
        internal void SetText(string time, string day, string local, bool isDay)
        { Time = time; Day = day; Local = local; IsDay = isDay; Updates++; }
        internal void Draw() => Draws++;
        public void Dispose() => Disposed = true;
    }
}
