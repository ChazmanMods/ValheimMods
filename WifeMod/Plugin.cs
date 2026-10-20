using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace WifeMod
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("com.profmags.companions")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.WifeMod";
        public const string Name = "Wife Companion";
        public const string Version = "0.6.0";
        internal const string PrefabName = "Chazman_WifeCompanion";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> SpawnShortcut;
        internal static ConfigEntry<float> PregnancyChance;
        internal static ConfigEntry<int> GestationDays;
        internal static ConfigEntry<float> SequenceSeconds;
        internal static ConfigEntry<KeyboardShortcut> PregnancyStatusShortcut;
        internal static ConfigEntry<float> WifeScale;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            SpawnShortcut = Config.Bind("General", "Spawn shortcut", new KeyboardShortcut(KeyCode.W, KeyCode.LeftShift), "Spawns a companion beside the local player.");
            PregnancyChance = Config.Bind("Family", "Pregnancy chance percent", 65f, new ConfigDescription("Chance that a completed bed sequence begins pregnancy.", new AcceptableValueRange<float>(0f, 100f)));
            GestationDays = Config.Bind("Family", "Gestation days", 3, new ConfigDescription("Number of in-game days before the child is born.", new AcceptableValueRange<int>(1, 30)));
            SequenceSeconds = Config.Bind("Family", "Black screen hold seconds", 5f, new ConfigDescription("How long the Time passes message remains on the black screen.", new AcceptableValueRange<float>(3f, 20f)));
            PregnancyStatusShortcut = Config.Bind("Family", "Pregnancy status shortcut", new KeyboardShortcut(KeyCode.P, KeyCode.LeftShift), "Shows the nearest wife's pregnancy status and due date.");
            WifeScale = Config.Bind("Appearance", "Wife scale", 0.82f, new ConfigDescription("Root scale applied to the wife relative to the Offline Companions prefab.", new AcceptableValueRange<float>(0.65f, 1.1f)));
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo($"{Name} v{Version} loaded. Press Left Shift+W to spawn a companion.");
        }

        private void Update()
        {
            if (SpawnShortcut.Value.IsDown() && Player.m_localPlayer != null)
                WifePrefab.SpawnFor(Player.m_localPlayer);
            if (PregnancyStatusShortcut.Value.IsDown() && Player.m_localPlayer != null)
                WifeCompanion.ShowNearestPregnancyStatus(Player.m_localPlayer);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
