using System;
using BepInEx.Configuration;

namespace Runic.Foundation.Core
{
    internal static class PluginConfig
    {
        internal static ConfigEntry<float> NotificationMinimumIntervalSeconds { get; private set; }
        internal static ConfigEntry<bool> LogPublishedNotifications { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static event Action Changed;

        internal static void Bind(ConfigFile config)
        {
            NotificationMinimumIntervalSeconds = config.Bind(
                "Notifications",
                "MinimumIntervalSeconds",
                10f,
                new ConfigDescription(
                    "Default per-module/code/context notification cooldown. Publishers may request a longer interval.",
                    new AcceptableValueRange<float>(0f, 300f)));
            LogPublishedNotifications = config.Bind(
                "Notifications",
                "LogPublishedNotifications",
                true,
                "Write published suite notifications to the BepInEx log.");
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Log module, capability, service, and keybinding registry changes.");

            NotificationMinimumIntervalSeconds.SettingChanged += OnSettingChanged;
            LogPublishedNotifications.SettingChanged += OnSettingChanged;
            VerboseLogging.SettingChanged += OnSettingChanged;
        }

        internal static void Unbind()
        {
            if (NotificationMinimumIntervalSeconds != null)
                NotificationMinimumIntervalSeconds.SettingChanged -= OnSettingChanged;
            if (LogPublishedNotifications != null)
                LogPublishedNotifications.SettingChanged -= OnSettingChanged;
            if (VerboseLogging != null)
                VerboseLogging.SettingChanged -= OnSettingChanged;
            Changed = null;
        }

        private static void OnSettingChanged(object sender, EventArgs arguments) => Changed?.Invoke();
    }
}
