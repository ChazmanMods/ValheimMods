using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RunicWorldEngine.Contracts;
using RunicWorldEngine.Core;
using RunicWorldEngine.Integration;
using UnityEngine;

namespace RunicWorldEngine
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicWorldEngine";
        public const string Name = "Runic World Engine";
        public const string Version = "1.0.0";
        private Harmony _harmony;
        private float _nextSummaryAt;

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            WorldEngineConfig.Bind(Config);
            if (!(WorldEngineConfig.Enabled?.Value ?? true))
            {
                Logger.LogInfo(Name + " is disabled; no Harmony patches or observatory state were created.");
                return;
            }

            try
            {
                _harmony = new Harmony(Guid);
                ObservatoryRuntime.Verify();
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo(
                    $"{Name} v{Version} ready: bounded aggregate ZDO observability. " +
                    "Unknown data is preserved; compaction and sync changes are off.");
            }
            catch (Exception exception)
            {
                Shutdown();
                Logger.LogError(Name + " failed closed; Valheim world handling remains unchanged. " + exception);
            }
        }

        private void Update()
        {
            if (!(WorldEngineConfig.Enabled?.Value ?? false) ||
                !(WorldEngineConfig.LogPeriodicSummary?.Value ?? false)) return;
            float now = Time.unscaledTime;
            if (now < _nextSummaryAt) return;
            _nextSummaryAt = now + Mathf.Clamp(
                WorldEngineConfig.SummaryIntervalSeconds.Value,
                5f,
                600f);
            ZdoObservatorySnapshot value = ObservatoryRuntime.Current;
            Logger.LogInfo(
                $"World sample #{value.Sequence}: objects={value.TotalObjects}, peers={value.ConnectedPeers}, " +
                $"created={value.CreatedSincePreviousSample}, destroyed={value.DestroyedSincePreviousSample}, " +
                $"sent/s={value.SentLastSecond}, received/s={value.ReceivedLastSecond}, " +
                $"save={value.LastSaveMilliseconds:F1}ms, load={value.LastLoadMilliseconds:F1}ms.");
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            _harmony = null;
            ObservatoryRuntime.Reset();
        }
    }
}
