using System;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    // Optional, server-local adapter to World Engine 1.2.x. No client-selected path, live patching,
    // config-wide reload, policy change, kick, restart, or client-side configuration write.
    internal static class SentinelCapacityBridge
    {
        private static readonly object Gate = new object();
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

        internal static void Populate(SentinelAdminDocument document)
        {
            document.CapacitySupported = true;
            try
            {
                lock (Gate)
                {
                    BaseUnityPlugin engine = Engine(out Type runtime);
                    var saved = SentinelCapacitySettings.Read(engine.Config.ConfigFilePath);
                    bool requested = (bool)runtime.GetField("_requested", Static).GetValue(null);
                    bool validated = (bool)runtime.GetProperty("ValidatedOverride", Static).GetValue(null);
                    int active = (int)runtime.GetProperty("PlayerLimit", Static).GetValue(null);
                    bool fault = requested && !validated;
                    document.CapacityAvailable = true;
                    document.CapacityVersion = engine.Info.Metadata.Version.ToString();
                    document.CapacityRevision = saved.Revision;
                    document.CapacitySavedEnabled = saved.Enabled;
                    document.CapacitySavedPlayers = saved.Players.ToString(CultureInfo.InvariantCulture);
                    document.CapacityActiveEnabled = validated;
                    document.CapacityActivePlayers = fault ? "Blocked by integrity validation" : active.ToString(CultureInfo.InvariantCulture);
                    document.CapacityStatus = (string)runtime.GetProperty(global::Runic.Localization.RunicText.Get("text_920e413c7d41"), Static).GetValue(null);
                    document.CapacityRestartRequired = fault || requested != saved.Enabled || (saved.Enabled && saved.Players != active);
                    document.CapacityCurrentPlayers = ZNet.instance.GetNrOfPlayers().ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception error)
            {
                document.CapacityAvailable = false;
                document.CapacityStatus = global::Runic.Localization.RunicText.Get("text_7097a7e32d9f") + error.Message;
                if (document.CapacityStatus.Length > 512) document.CapacityStatus = document.CapacityStatus.Substring(0, 512);
            }
        }

        internal static string Save(string revision, bool enabled, int players)
        {
            lock (Gate)
            {
                BaseUnityPlugin engine = Engine(out _);
                ConfigFile config = engine.Config;
                ConfigEntry<bool> enabledEntry = config["Player Capacity", "Enabled"] as ConfigEntry<bool>;
                ConfigEntry<int> playerEntry = config["Player Capacity", "MaximumPlayers"] as ConfigEntry<int>;
                if (enabledEntry == null || playerEntry == null) throw new InvalidOperationException("World Engine capacity configuration contract is unavailable.");
                SentinelCapacitySettings.Save(config.ConfigFilePath, revision, enabled, players);
                // Keep BepInEx's in-memory settings aligned so a later normal config save cannot
                // revert the staged values. World Engine's active cap remains frozen at startup.
                bool automaticSave = config.SaveOnConfigSet;
                config.SaveOnConfigSet = false;
                try { enabledEntry.Value = enabled; playerEntry.Value = players; }
                finally { config.SaveOnConfigSet = automaticSave; }
                return global::Runic.Localization.RunicText.Get("text_ce0333ecaef6");
            }
        }

        private static BaseUnityPlugin Engine(out Type runtime)
        {
            runtime = null;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) throw new InvalidOperationException("Only the authoritative host may access these settings.");
            if (!Chainloader.PluginInfos.TryGetValue("chazman.RunicWorldEngine", out PluginInfo info) || info.Instance == null)
                throw new InvalidOperationException("Install RunicWorldEngine 1.2.0 on the host to use this tab.");
            if (info.Metadata.Version.Major != 1 || info.Metadata.Version.Minor != 2)
                throw new InvalidOperationException("This Sentinel adapter requires RunicWorldEngine 1.2.x on the host.");
            BaseUnityPlugin engine = info.Instance;
            if (engine.GetType().GetField("_harmony", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(engine) == null)
                throw new InvalidOperationException("World Engine is disabled or did not initialize. Enable it and restart the host first.");
            runtime = engine.GetType().Assembly.GetType("RunicWorldEngine.Integration.CapacityRuntime");
            if (runtime == null || runtime.GetField("_requested", Static)?.FieldType != typeof(bool) ||
                runtime.GetProperty("ValidatedOverride", Static)?.PropertyType != typeof(bool) ||
                runtime.GetProperty("PlayerLimit", Static)?.PropertyType != typeof(int) || runtime.GetProperty("Status", Static)?.PropertyType != typeof(string))
                throw new InvalidOperationException("Unsupported World Engine capacity contract.");
            return engine;
        }
    }
}
