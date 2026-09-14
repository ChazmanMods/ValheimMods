using BepInEx.Configuration;
using UnityEngine;

namespace RunicClock
{
    internal sealed class ClockConfig
    {
        internal readonly ConfigEntry<bool> Enabled, Visible, Use24Hour, ShowDay, ShowIndicator, ShowRealTime, RealTime24Hour, HideInMenus;
        internal readonly ConfigEntry<float> Scale, OffsetX, OffsetY, BackgroundOpacity;
        internal readonly ConfigEntry<ClockAnchor> Anchor;
        internal readonly ConfigEntry<KeyboardShortcut> ToggleKey;

        internal ClockConfig(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Enable the client-only clock. No world or server settings are changed.");
            Visible = config.Bind("General", "Visible", true, "Show the clock. The toggle shortcut also changes this setting.");
            ToggleKey = config.Bind("General", "ToggleShortcut", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt), "Toggle clock visibility during gameplay. Default: Left Alt+C. Ignored while typing or in menus. Set to None to disable.");
            Use24Hour = config.Bind("Clock", "Use24Hour", true, "Display game time as 18:30 instead of 6:30 PM.");
            ShowDay = config.Bind("Clock", "ShowDay", true, "Show Valheim's current world day number.");
            ShowIndicator = config.Bind("Clock", "ShowSunMoon", true, "Show a sun by day and a crescent moon by night.");
            ShowRealTime = config.Bind("Clock", "ShowRealWorldTime", false, "Add your computer's local time, labeled Local. This is not the server's time zone.");
            RealTime24Hour = config.Bind("Clock", "RealWorldUse24Hour", true, "Use 24-hour format for the optional local clock.");
            Anchor = config.Bind("Display", "Anchor", ClockAnchor.TopCenter, "Choose the screen anchor for the clock.");
            OffsetX = config.Bind("Display", "OffsetX", 0f, new ConfigDescription("Horizontal offset in screen pixels; positive moves right. Kept within the screen safe area.", new AcceptableValueRange<float>(-4096, 4096)));
            OffsetY = config.Bind("Display", "OffsetY", 64f, new ConfigDescription("Vertical offset in screen pixels; positive moves down. Kept within the screen safe area.", new AcceptableValueRange<float>(-4096, 4096)));
            Scale = config.Bind("Display", "Scale", 1f, new ConfigDescription("Clock size multiplier. Also scales with screen height relative to 1080p.", new AcceptableValueRange<float>(0.6f, 2f)));
            BackgroundOpacity = config.Bind("Display", "BackgroundOpacity", 0.65f, new ConfigDescription("Dark panel opacity. Zero hides the panel and its border.", new AcceptableValueRange<float>(0, 1)));
            HideInMenus = config.Bind("Display", "HideInMenus", true, "Hide while menus, inventory, map, chat input, or text input are open. Always hides with the HUD and during loading.");
        }
    }
}
