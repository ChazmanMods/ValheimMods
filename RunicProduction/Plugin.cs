using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RunicProduction.Integration;

namespace RunicProduction
{
    [BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicProduction";
        public const string Name = "Runic Production";
        public const string Version = "2.3.4";
        public const string ModuleId = "runic.production";

        private Harmony _harmony;

        private void Awake()
        {
            RunicAutomation.MutationGate.Diagnostic = message => { if (message.Contains("Indeterminate")) Logger.LogError(message); else Logger.LogDebug(message); };
            ProductionDiagnostics.Initialize(Logger);
            ProductionConfig.Bind(Config);
            Config.SettingChanged += OnSettingChanged;
            ProductionDiagnostics.Configuration("startup");
            try
            {
                ValheimAccess.VerifySignatures();
                ProductionRuntime.Initialize();
                ProductionRuntime.RegisterAuthorityLinks();
                ProductionRuntime.RegisterAuthorityRecipes();
                ProductionRuntime.RegisterAuthorityStationClock();
                AuthorityStationInteraction.Register();
                AuthorityStationRetirement.Register();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                RunicAutomation.Operations.InventoryOperationNetwork.RegisterModule("production");
                ProductionRuntime.SeedLoadedStations();
                NearbyIngredientContainerIndex.SeedLoadedContainers();
                ProductionDiagnostics.MarkAvailable();
                Logger.LogInfo(
                    $"{Name} v{Version} ready: server-authoritative inventory and production transactions, " +
                    "persistent explicit links, and bounded exemplar replenishment.");
            }
            catch (Exception exception)
            {
                Cleanup();
                ProductionDiagnostics.Disable("startup validation failed", exception);
            }
        }

        private void Update()
        {
            if (ProductionDiagnostics.RuntimeAvailable)
            {
                NativeProductionOutput.Tick();
                ProductionRuntime.Update();
            }
        }

        private void OnDestroy() => Cleanup();

        private void OnSettingChanged(object sender, SettingChangedEventArgs eventArgs)
        {
            try
            {
                ProductionRuntime.RefreshConfiguration();
                ProductionDiagnostics.Configuration(
                    eventArgs?.ChangedSetting?.Definition.ToString() ?? "setting-change");
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Runic Production rejected an updated setting: " + exception.Message);
            }
        }

        private void Cleanup()
        {
            Config.SettingChanged -= OnSettingChanged;
            NearbyIngredientContainerIndex.Clear();
            ProductionRuntime.Shutdown();
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Runic Production could not remove every Harmony patch: " +
                    exception.Message);
            }
            _harmony = null;
        }
    }
}
