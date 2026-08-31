using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RunicBuildCamera
{
    internal static class BuildCameraConfig
    {
        internal const float DefaultCameraRange = 60f;
        internal const float CameraRangeHardMaximum = 100f;
        internal const float DefaultRemoteActionDistance = 100f;
        internal const float RemoteActionDistanceHardMaximum = 100f;

        private const float MinimumDistance = 1f;
        private static readonly List<Action> UnsubscribeActions = new List<Action>();

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ToggleShortcut { get; private set; }
        internal static ConfigEntry<float> CameraRange { get; private set; }
        internal static ConfigEntry<float> MoveSpeed { get; private set; }
        internal static ConfigEntry<float> FastMoveMultiplier { get; private set; }
        internal static ConfigEntry<bool> WorldRelativeMovement { get; private set; }
        internal static ConfigEntry<float> RemoteActionDistance { get; private set; }
        internal static ConfigEntry<bool> PickupEnabled { get; private set; }
        internal static ConfigEntry<float> PickupRange { get; private set; }
        internal static ConfigEntry<float> PickupIntervalSeconds { get; private set; }
        internal static ConfigEntry<bool> DemisterFollowCamera { get; private set; }
        internal static ConfigEntry<float> DemisterRangeMultiplier { get; private set; }
        internal static ConfigEntry<bool> InvertMouseHorizontal { get; private set; }
        internal static ConfigEntry<bool> InvertMouseVertical { get; private set; }
        internal static ConfigEntry<bool> InvertControllerHorizontal { get; private set; }
        internal static ConfigEntry<bool> InvertControllerVertical { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static event Action Changed;

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            UnhookChanges();

            Enabled = BindEntry(
                config,
                "General",
                "Enabled",
                true,
                "Enable Runic Build Camera. Disabling it leaves Valheim's normal build controls unchanged.");
            ToggleShortcut = BindEntry(
                config,
                "Controls",
                "ToggleShortcut",
                new KeyboardShortcut(KeyCode.B),
                "Keyboard shortcut that enters or exits the detached build camera.");
            CameraRange = BindRange(
                config,
                "Camera",
                "CameraRange",
                DefaultCameraRange,
                MinimumDistance,
                CameraRangeHardMaximum,
                "Maximum distance, in metres, that the detached camera may travel from the player.");
            MoveSpeed = BindRange(
                config,
                "Camera",
                "MoveSpeed",
                10f,
                0.5f,
                50f,
                "Base detached-camera movement speed in metres per second.");
            FastMoveMultiplier = BindRange(
                config,
                "Camera",
                "FastMoveMultiplier",
                3f,
                1f,
                10f,
                "Multiplier applied while the fast-move input is held.");
            WorldRelativeMovement = BindEntry(
                config,
                "Camera",
                "WorldRelativeMovement",
                false,
                "Move on fixed world axes instead of axes derived from the camera view.");
            RemoteActionDistance = BindRange(
                config,
                "Remote Actions",
                "RemoteActionDistance",
                DefaultRemoteActionDistance,
                MinimumDistance,
                RemoteActionDistanceHardMaximum,
                "Maximum avatar-to-target distance, in metres, while detached placement, repair, " +
                "or removal runs. Effective crafting-station build range is raised only inside the " +
                "scoped call; station data is not changed. Valheim's camera ray remains limited to 50 metres.");
            PickupEnabled = BindEntry(
                config,
                "Pickup",
                "PickupEnabled",
                true,
                "Allow nearby loose world-item drops to be collected while the detached camera is active. Chests and other containers are excluded.");
            PickupRange = BindRange(
                config,
                "Pickup",
                "PickupRange",
                10f,
                1f,
                50f,
                "Collection radius, in metres, around the detached camera for eligible loose world items.");
            PickupIntervalSeconds = BindRange(
                config,
                "Pickup",
                "PickupIntervalSeconds",
                0.25f,
                0.05f,
                2f,
                "Minimum time, in seconds, between detached-camera pickup scans.");
            DemisterFollowCamera = BindEntry(
                config,
                "Mist",
                "DemisterFollowCamera",
                true,
                "Move the player's active Wisplight mist-clearing effect with the detached camera. This does nothing without an active Wisplight demister.");
            DemisterRangeMultiplier = BindRange(
                config,
                "Mist",
                "DemisterRangeMultiplier",
                2f,
                0.25f,
                5f,
                "Multiplier applied to mist-clearing range while it follows the detached camera.");
            InvertMouseHorizontal = BindEntry(
                config,
                "Controls",
                "InvertMouseHorizontal",
                false,
                "Invert horizontal mouse look while the detached camera is active.");
            InvertMouseVertical = BindEntry(
                config,
                "Controls",
                "InvertMouseVertical",
                false,
                "Invert vertical mouse look while the detached camera is active.");
            InvertControllerHorizontal = BindEntry(
                config,
                "Controls",
                "InvertControllerHorizontal",
                false,
                "Invert horizontal controller look while the detached camera is active.");
            InvertControllerVertical = BindEntry(
                config,
                "Controls",
                "InvertControllerVertical",
                false,
                "Invert vertical controller look while the detached camera is active.");
            VerboseLogging = BindEntry(
                config,
                "Diagnostics",
                "VerboseLogging",
                false,
                "Write additional state-transition diagnostics. Per-frame logging remains disabled.");

        }

        private static ConfigEntry<T> BindEntry<T>(
            ConfigFile config,
            string section,
            string key,
            T defaultValue,
            string description)
        {
            ConfigEntry<T> entry = config.Bind(section, key, defaultValue, description);
            HookChange(entry);
            return entry;
        }

        private static ConfigEntry<float> BindRange(
            ConfigFile config,
            string section,
            string key,
            float defaultValue,
            float minimum,
            float maximum,
            string description)
        {
            ConfigEntry<float> entry = config.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(
                    description,
                    new AcceptableValueRange<float>(minimum, maximum)));
            HookChange(entry);
            return entry;
        }

        private static void HookChange<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += OnSettingChanged;
            UnsubscribeActions.Add(() => entry.SettingChanged -= OnSettingChanged);
        }

        private static void UnhookChanges()
        {
            foreach (Action unsubscribe in UnsubscribeActions)
                unsubscribe();
            UnsubscribeActions.Clear();
        }

        private static void OnSettingChanged(object sender, EventArgs args) => Changed?.Invoke();
    }
}
