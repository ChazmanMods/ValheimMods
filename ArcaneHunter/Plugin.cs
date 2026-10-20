using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using ArcaneHunter.Core;
using UnityEngine;

namespace ArcaneHunter
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    [SynchronizationMode(AdminOnlyStrictness.Always)]
    [HarmonyPatch]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.ArcaneHunter", Name = "ArcaneHunter", Version = "0.2.0";
        internal const string JournalStorage = Guid + ".journal.v1";
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static HunterConfig Settings;
        internal JournalData Journal = new JournalData();
        internal HunterInterface Interface;
        internal HunterSense Sense;
        private Harmony harmony;
        private Player loadedPlayer;
        private bool journalWritable = true;

        private void Awake()
        {
            Instance = this; Log = Logger;
            Settings = new HunterConfig(Config);
            Interface = new HunterInterface(this);
            Sense = new HunterSense(this);
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            new Terminal.ConsoleCommand("arcanehunter_prefabs", "List creature prefab names for ArcaneHunter custom spawn overrides.", args =>
            {
                if (!ZNetScene.instance) { args.Context.AddString("Enter a world first."); return; }
                var names = ZNetScene.instance.m_prefabs.Where(p => p && p.GetComponent<Character>() && !p.GetComponent<Player>())
                    .Select(p => p.name).OrderBy(n => n).ToArray();
                args.Context.AddString(string.Join(", ", names));
                Logger.LogInfo("Creature prefabs (not all have ambient spawn entries): " + string.Join(", ", names));
            });
            Logger.LogInfo($"{Name} {Version}: the trail awaits. F6 journal / F7 sense / F8 focus. Game {(global::Version.CurrentVersion)}");
        }
        internal void EnsureJournal(Player player)
        {
            if (ReferenceEquals(loadedPlayer, player)) return;
            Interface.Close(); Sense.Clear();
            loadedPlayer = player;
            Journal = new JournalData(); journalWritable = true;
            if (!player) return;
            if (player.m_customData.TryGetValue(JournalStorage, out string json))
            {
                try { Journal = JournalData.Deserialize(json); }
                catch (Exception ex)
                {
                    // Preserve the original bytes. Never replace unsupported or corrupt data.
                    journalWritable = false;
                    Logger.LogWarning("Hunter journal could not be loaded; original data preserved. " + ex.Message);
                    player.Message(MessageHud.MessageType.Center, "ArcaneHunter: journal could not load. Original data preserved; see log.");
                }
            }
        }
        internal void SaveJournal()
        {
            if (loadedPlayer && journalWritable) loadedPlayer.m_customData[JournalStorage] = Journal.Serialize();
        }
        internal void PrepareBoard()
        {
            var player = Player.m_localPlayer;
            if (!player || !ZNet.instance || !Settings.Commissions.Value) return;
            // Use the game's day length rather than wall time. Only one board per world/day.
            int day = EnvMan.instance ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            string key = ZNet.instance.GetWorldUID() + ":" + day;
            if (Journal.BoardKey == key) return;
            string biome = player.GetCurrentBiome().ToString();
            string region = biome == "BlackForest" ? "Black Forest" : biome == "AshLands" ? "Ashlands" : biome;
            var candidates = Bestiary.All.Where(s => s.Region == region && ZNetScene.instance.GetPrefab(s.Prefab)).ToArray();
            // Unknown/new biomes draw from actually observed species instead of impossible targets.
            string target = candidates.Length > 0 ? candidates[(int)((uint)key.GetStableHashCode() % candidates.Length)].Prefab :
                Journal.Records.FirstOrDefault(r => r.Observed)?.Prefab;
            if (target != null) { Journal.PrepareBoard(key, target); SaveJournal(); }
        }
        private void Update()
        {
            Player player = Player.m_localPlayer;
            EnsureJournal(player);
            if (!player || !Settings.Enabled.Value || player.IsDead() || player.IsTeleporting() || player.InCutscene())
            { Interface.Close(); Sense.ClearClues(); return; }
            if (Interface.IsOpen)
            {
                if (Settings.JournalKey.Value.IsDown() || Input.GetKeyDown(KeyCode.Escape)) Interface.Close();
                return;
            }
            if (InputBusy()) return;
            if (Settings.JournalKey.Value.IsDown()) { PrepareBoard(); Interface.Open(); }
            else if (Settings.PulseKey.Value.IsDown()) Sense.Pulse(player);
            else if (Settings.FocusKey.Value.IsDown()) Sense.Focus(player);
        }
        internal static bool InputBusy() =>
            (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            StoreGui.IsVisible() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() ||
            (TextViewer.instance && TextViewer.instance.IsVisible()) || GameCamera.InFreeFly();
        private void OnGUI()
        {
            if (!Player.m_localPlayer || !Settings.Enabled.Value || Player.m_localPlayer.IsDead()) return;
            Interface.Draw();
        }
        internal void Notify(string message)
        {
            if (Settings.Toasts.Value && Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message);
        }
        private void OnDestroy()
        {
            SaveJournal(); Interface?.Dispose(); Sense?.Clear();
            harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save)), HarmonyPrefix]
        private static void SavePlayer(Player __instance)
        {
            if (Instance && __instance == Player.m_localPlayer) { Instance.EnsureJournal(__instance); Instance.SaveJournal(); }
        }
        [HarmonyPatch(typeof(Player), "OnDestroy"), HarmonyPrefix]
        private static void DestroyPlayer(Player __instance)
        {
            if (Instance && ReferenceEquals(Instance.loadedPlayer, __instance))
            { Instance.SaveJournal(); Instance.Interface.Close(); Instance.Sense.Clear(); Instance.loadedPlayer = null; }
        }
    }
}
