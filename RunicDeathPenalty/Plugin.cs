using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty
{
    [BepInPlugin(Guid, "Runic Death Penalty", Version)]
    [BepInDependency("chazman.RunicProduction", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("chazman.RunicCrafting", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicDeathPenalty", Version = "0.1.0";
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static Rules Policy = new Rules();
        internal static bool WaitingForPolicy => ZNet.instance && !ZNet.instance.IsServer() && !Network.Synchronized;
        Configuration settings;
        Harmony harmony;
        float nextTick, lastNotice = -100;
        string entry = "";
        internal static bool Active => Instance && Policy.Enabled && ZNet.instance;
        void Awake()
        {
            Instance = this; Log = Logger;
            try
            {
                settings = new Configuration(Config); Policy = settings.Read(0);
                harmony = new Harmony(Guid); harmony.PatchAll();
                new Terminal.ConsoleCommand("rdp", "RunicDeathPenalty: status | reload (server console) | item <prefab> | unmapped", Command);
                Logger.LogInfo("RunicDeathPenalty " + Version + " loaded. Server/client version agreement required.");
            }
            catch (Exception e) { Logger.LogError(e); harmony?.UnpatchSelf(); Instance = null; throw; }
        }
        void Update()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1;
            if (!ZNet.instance) { Network.Reset(); entry = ""; return; }
            if (ZNet.instance.IsServer()) RefreshPolicy();
            Network.Tick();
            var p = Player.m_localPlayer;
            if (!p || p.IsDead() || WaitingForPolicy) return;
            DeathRuntime.UpdateProtections(p);
            if (Policy.Enabled && Policy.Use)
                foreach (var item in p.GetInventory().GetAllItems().ToArray())
                    if (item.m_equipped && !ItemCatalog.Use(item, false)) { p.UnequipItem(item, false); Deny("Equipment locked by group progression: " + ItemCatalog.Prefab(item)); }
            int tier = Locations.PlayerTier(p);
            string key = tier + ":" + Policy.Encode();
            if (entry != key && Policy.Warnings && Policy.Restricted(tier))
                Deny("Restricted biome: " + Rules.Name(tier) + ". Group: " + Rules.Name(Policy.EffectiveTier) + ". Death: " + Policy.Multiplier + "x skill loss; no recovery protections." +
                    (Policy.Harvest ? " Harvesting locked." : "") + (Policy.Use ? " Advanced items locked." : ""));
            entry = key;
        }
        internal void RefreshPolicy()
        {
            try
            {
                var next = settings.Read(ZoneSystem.instance ? Rules.AutomaticTier(k => ZoneSystem.instance.GetGlobalKey(k)) : 0);
                string wire = next.Encode();
                if (wire == Policy.Encode()) return;
                Policy = next; Network.Broadcast();
                Log.LogInfo("Policy updated. Allowed through " + Rules.Name(Policy.EffectiveTier));
            }
            catch (Exception e) { Log.LogError("Rejected invalid configuration; keeping last valid policy: " + e.Message); }
        }
        internal static bool Deny(string reason)
        {
            if (Instance && Time.unscaledTime - Instance.lastNotice >= 3)
            {
                Instance.lastNotice = Time.unscaledTime;
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Runic: " + reason);
                Log.LogInfo(reason);
            }
            return false;
        }
        void Command(Terminal.ConsoleEventArgs args)
        {
            string op = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
            if (op == "reload")
            {
                if (!ZNet.instance || !ZNet.instance.IsServer()) { args.Context.AddString("Reload the config in the server console. Clients cannot change server policy."); return; }
                Config.Reload(); RefreshPolicy(); args.Context.AddString("Server configuration reloaded.");
            }
            else if (op == "item" && args.Length > 2) args.Context.AddString(args[2] + ": " + Rules.Name(ItemCatalog.Tier(args[2])) + "; " + (ItemCatalog.Allowed(args[2], false) ? "allowed" : "locked"));
            else if (op == "unmapped")
            {
                ItemCatalog.Rebuild(); string path = Path.Combine(Paths.ConfigPath, "RunicDeathPenalty.unmapped.txt");
                File.WriteAllLines(path, ItemCatalog.Unknown.OrderBy(x => x)); args.Context.AddString("Unmapped prefab report: " + path);
            }
            else args.Context.AddString("RunicDeathPenalty " + Version + ": allowed through " + Rules.Name(Policy.EffectiveTier) + "; loss " + Policy.Multiplier + "x; harvest=" + Policy.Harvest + "; use=" + Policy.Use + "; recovery cap=" + Policy.RecoveryCap + "; synced=" + !WaitingForPolicy);
        }
        void OnDestroy() { harmony?.UnpatchSelf(); Network.Reset(); Instance = null; }
    }

    // Other gameplay mods can check this before any direct resource mutation.
    public static class ProgressionApi
    {
        public static bool CanUseItem(string prefab) => !Plugin.Active || !Plugin.Policy.Use || ItemCatalog.Allowed(prefab, false);
        public static int AllowedTier => Plugin.Policy.EffectiveTier;
    }
}
