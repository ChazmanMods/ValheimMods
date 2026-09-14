using System;
using BepInEx;
using HarmonyLib;
using RunicSentinelClient.Runtime;
using UnityEngine;

namespace RunicSentinelClient
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicSentinelClient";
        public const string Name = "Runic Sentinel Client";
        public const string Version = "1.0.1";

        private Harmony _harmony;
        internal static ClientAdmissionRuntime ActiveRuntime { get; private set; }

        private void Awake()
        {
            ClientConfiguration.Bind(Config);
            if (!(ClientConfiguration.Enabled?.Value ?? true))
            {
                Logger.LogInfo(Name + " is disabled; no worker or RPC handler was created.");
                return;
            }
            if (Application.isBatchMode)
            {
                Logger.LogInfo(Name + " is client-only and remains inert in batch mode.");
                return;
            }
            try
            {
                ActiveRuntime = new ClientAdmissionRuntime(
                    message => Logger.LogInfo(message),
                    message => Logger.LogWarning(message));
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                if (!ClientTransportPatches.IsInstalled(Guid))
                    throw new InvalidOperationException(
                        "The pre-admission connection patch was not installed.");
                Logger.LogInfo(
                    Name + " v" + Version + " initialized. It activates only for a client " +
                    "connection and reports self-observed compatibility evidence to the server.");
            }
            catch (Exception exception)
            {
                Shutdown();
                Logger.LogError(Name + " failed safely: " + exception.GetType().Name + ".");
            }
        }

        private void Update()
        {
            try { ActiveRuntime?.Tick(); }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Sentinel Client admission tick failed safely (" +
                    exception.GetType().Name + ").");
            }
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            _harmony = null;
            try { ActiveRuntime?.Dispose(); } catch { }
            ActiveRuntime = null;
        }
    }
}
