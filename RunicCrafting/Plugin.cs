using System;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RunicCrafting.Integration;

namespace RunicCrafting
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicCrafting";
        public const string Name = "Runic Crafting";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private bool _configurationSubscribed;
        private string _pendingHudNotice;

        private void Awake()
        {
            Log = Logger;
            Configuration.Bind(Config);
            Config.SettingChanged += OnSettingChanged;
            _configurationSubscribed = true;
            Logger.LogInfo(Name + " configuration: " + Configuration.StateSummary + ".");

            try
            {
                CraftingRuntime.Initialize(Logger);
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                WorkshopAccessCommands.Initialize();
                Logger.LogInfo(
                    Name + " v" + Version + " ready. " + Configuration.StateSummary +
                    ". Nearby crafting and building use locally owned Valheim inventories.");
            }
            catch (Exception exception)
            {
                Logger.LogError(Name + " startup failed; vanilla behavior remains available: " + exception);
                Shutdown();
            }
        }

        private void Update()
        {
            string notice = Interlocked.Exchange(ref _pendingHudNotice, null);
            if (notice == null || !Configuration.ShowStatusMessages.Value) return;
            Player player = Player.m_localPlayer;
            if (player != null) player.Message(MessageHud.MessageType.TopLeft, notice);
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs arguments)
        {
            try
            {
                Configuration.RefreshDerivedSettings();
                CraftingRuntime.OnConfigurationChanged();
                CraftingDiagnostics.ResetRepeatSuppression();
                string setting = arguments?.ChangedSetting == null
                    ? "unknown setting"
                    : arguments.ChangedSetting.Definition.Section + "." +
                      arguments.ChangedSetting.Definition.Key;
                Logger.LogInfo(
                    Name + " configuration changed (" + setting + "): " +
                    Configuration.StateSummary + ". Changes are active now; no restart is required.");
                CraftingDiagnostics.TraceAction("configuration-change", setting, Configuration.StateSummary);
                Interlocked.Exchange(ref _pendingHudNotice, Configuration.HudSummary);
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Crafting configuration refresh failed: " + exception.Message);
            }
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            if (_configurationSubscribed)
            {
                Config.SettingChanged -= OnSettingChanged;
                _configurationSubscribed = false;
            }
            Interlocked.Exchange(ref _pendingHudNotice, null);
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Could not remove every crafting patch: " + exception.Message);
            }
            _harmony = null;
            CraftingRuntime.Shutdown();
            CraftingDiagnostics.ResetRepeatSuppression();
            Log = null;
        }
    }
}
