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
            Enabled = config.Bind("General", "Enabled", true, global::Runic.Localization.RunicText.Get("text_78cf8eb401b2"));
            Visible = config.Bind("General", "Visible", true, global::Runic.Localization.RunicText.Get("text_51fb57908b0f"));
            ToggleKey = config.Bind("General", "ToggleShortcut", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt), global::Runic.Localization.RunicText.Get("text_4f83e880ad86"));
            Use24Hour = config.Bind("Clock", "Use24Hour", true, global::Runic.Localization.RunicText.Get("text_cb603c524349"));
            ShowDay = config.Bind("Clock", "ShowDay", true, global::Runic.Localization.RunicText.Get("text_7249b2c66cc5"));
            ShowIndicator = config.Bind("Clock", "ShowSunMoon", true, global::Runic.Localization.RunicText.Get("text_be831133ec1a"));
            ShowRealTime = config.Bind("Clock", "ShowRealWorldTime", false, global::Runic.Localization.RunicText.Get("text_be4fa73da1f2"));
            RealTime24Hour = config.Bind("Clock", "RealWorldUse24Hour", true, global::Runic.Localization.RunicText.Get("text_30e89d7617d0"));
            Anchor = config.Bind("Display", "Anchor", ClockAnchor.TopCenter, global::Runic.Localization.RunicText.Get("text_45f207c536dc"));
            OffsetX = config.Bind("Display", "OffsetX", 0f, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_e8f31dcb1a5f"), new AcceptableValueRange<float>(-4096, 4096)));
            OffsetY = config.Bind("Display", "OffsetY", 64f, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_86214a9260e0"), new AcceptableValueRange<float>(-4096, 4096)));
            Scale = config.Bind("Display", "Scale", 1f, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_7d0b625e1025"), new AcceptableValueRange<float>(0.6f, 2f)));
            BackgroundOpacity = config.Bind("Display", "BackgroundOpacity", 0.65f, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_9409bab3d014"), new AcceptableValueRange<float>(0, 1)));
            HideInMenus = config.Bind("Display", "HideInMenus", true, global::Runic.Localization.RunicText.Get("text_1f7ab3406e2f"));
        }
    }
}
