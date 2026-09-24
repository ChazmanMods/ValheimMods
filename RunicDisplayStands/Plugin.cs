using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace RunicDisplayStands
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "chazman.RunicDisplayStands";
        public const string PluginName = "RunicDisplayStands";
        public const string PluginVersion = "1.3.9";

        public static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        // ---- Config ----
        public static ConfigEntry<string> CfgStandPrefabs;
        public static ConfigEntry<bool> CfgGamepadSupport;
        public static ConfigEntry<KeyboardShortcut> CfgTakeOneKey;

        // Parsed, cached set rebuilt whenever the config changes (locally or via server sync)
        public static HashSet<string> StandPrefabSet = new HashSet<string>();

        private readonly Harmony _harmony = new Harmony(PluginGUID);

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgStandPrefabs = Config.Bind(
                "Admin",
                "Stand Prefabs",
                "itemstand,itemstandh,ArmorStand",
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_a5f39c09729f"),
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            CfgGamepadSupport = Config.Bind(
                "General",
                "Gamepad Support",
                true,
                global::Runic.Localization.RunicText.Get("text_58fec321de23"));

            CfgTakeOneKey = Config.Bind(
                "General",
                "Take One Item Key",
                new KeyboardShortcut(UnityEngine.KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_01ab013c2d3d"));

            RebuildStandPrefabSet();
            CfgStandPrefabs.SettingChanged += (_, _) => RebuildStandPrefabSet();

            // Registers this config with the lightweight server-sync system so that,
            // if the host has the mod installed, every connecting client is forced to
            // match the admin-controlled settings (mirrors the "server enforces config"
            // behavior of the original mod) -- see Network/ConfigSync.cs.
            ConfigSync.RegisterSyncedConfig(CfgStandPrefabs);

            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        internal static void RebuildStandPrefabSet()
        {
            StandPrefabSet = CfgStandPrefabs.Value
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        }

        internal static bool IsManagedStand(UnityEngine.GameObject stand)
        {
            if (stand == null) return false;
            // Location-scripted ItemStands (the Forsaken stones, boss offering
            // holders, quest props, etc.) deliberately reuse the vanilla
            // "itemstand" name.  They are not build pieces and their Interact
            // methods carry gameplay logic that must remain untouched.
            if (stand.GetComponent<Piece>() == null) return false;

            string prefabName = stand.name.Replace("(Clone)", "");
            return StandPrefabSet.Contains(prefabName) ||
                   prefabName.StartsWith(
                       "piece_runic_hero_display",
                       System.StringComparison.OrdinalIgnoreCase);
        }
    }

    // Minimal stand-in so the config attribute compiles even if you don't also install
    // a "Configuration Manager" mod. Safe to leave as-is.
    public class ConfigurationManagerAttributes
    {
        public bool IsAdminOnly;
    }
}
