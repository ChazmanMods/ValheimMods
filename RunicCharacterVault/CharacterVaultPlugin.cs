using System.Collections;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using RunicCharacterVault.Shared;
using UnityEngine;

namespace RunicCharacterVault
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class CharacterVaultPlugin : RunicPluginBase
    {
        internal const string PluginGuid = "chazman.RunicCharacterVault";
        internal const string PluginName = "Runic Character Vault";
        internal const string PluginVersion = "1.0.2";
        internal static ModLog Log { get; private set; }
        internal static GracefulShutdownCoordinator Coordinator { get; private set; }
        internal static VoluntaryDisconnectCoordinator DisconnectCoordinator { get; private set; }
        internal static ServerDisconnectSaveCoordinator ServerDisconnects { get; private set; }
        internal static CharacterSaveStatusDisplay SaveStatus { get; private set; }
        internal static CharacterVaultPlugin Instance { get; private set; }
        internal static CharacterVaultSettings Settings { get; private set; }
        internal static ProfileTransferService Transfers { get; private set; }
        internal static bool PlayFabVerboseLogging { get; private set; }

        private void Awake()
        {
            Instance = this;
            PlayFabVerboseLogging = Config.Bind(
                "Diagnostics",
                "PlayFabVerboseLogging",
                false,
                "Enables verbose PlayFab Party logging for local diagnostics.").Value;
            Log = InitializePlugin(PluginGuid);
            Settings = new CharacterVaultSettings(Config);
            Transfers = new ProfileTransferService(SynchronizationContext.Current);
            Coordinator = new GracefulShutdownCoordinator(SynchronizationContext.Current);
            DisconnectCoordinator = new VoluntaryDisconnectCoordinator();
            ServerDisconnects = new ServerDisconnectSaveCoordinator();
            SaveStatus = new CharacterSaveStatusDisplay();
            PlayFabVerboseDiagnostics.Enable();
            CharacterVaultLobbyLeftDiagnostics.Register();
            Log.LogInfo($"{PluginName} {PluginVersion} is loaded.");
        }

        internal void Run(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        internal void QuitNextFrame()
        {
            StartCoroutine(QuitAfterCurrentFrame());
        }

        private void Update()
        {
            CharacterVaultRejection.Tick();
            Transfers.MonitorFinalSaves();
        }

        private static IEnumerator QuitAfterCurrentFrame()
        {
            yield return null;
            Application.Quit();
        }

        private void OnDestroy()
        {
            CharacterVaultLobbyLeftDiagnostics.Unregister();
            DisconnectCoordinator?.Dispose();
            ServerDisconnects?.Dispose();
            Coordinator?.Dispose();
            Transfers?.Dispose();
            SaveStatus?.Dispose();
            CharacterVaultRejection.Clear();
            DisconnectCoordinator = null;
            ServerDisconnects = null;
            Coordinator = null;
            Transfers = null;
            SaveStatus = null;
            Settings = null;
            PlayFabVerboseLogging = false;
            Instance = null;
            Log?.LogInfo($"{PluginName} {PluginVersion} is unloaded.");
            ShutdownPlugin();
            Log = null;
        }
    }
}
