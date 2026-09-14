using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace RunicWorldEngine.Integration
{
    internal static class SaveSmoothingRuntime
    {
        private static readonly object Gate = new object();
        private static readonly FieldInfo SaveThreadField =
            AccessTools.Field(typeof(ZNet), "m_saveThread");
        private static readonly MethodInfo SaveWorldMethod =
            AccessTools.Method(typeof(ZNet), "SaveWorld", new[] { typeof(bool) });
        private static bool _pending;
        private static bool _dispatching;
        private static float _requestedAt;
        private static float _eligibleAt;

        internal static bool BeforeSave(ZNet network, bool sync, Thread saveThread)
        {
            if (!(WorldEngineConfig.SmoothWorldSaves?.Value ?? true) || sync) return true;
            lock (Gate)
            {
                if (_dispatching) return true;
                float now = Time.unscaledTime;
                bool busySave = saveThread != null && saveThread.IsAlive;
                bool busyFrame = Time.unscaledDeltaTime * 1000f > FrameBudget();
                if (!busySave && !busyFrame) return true;
                if (!_pending) _requestedAt = now;
                _pending = true;
                _eligibleAt = Math.Max(_eligibleAt, now + (busySave ? 0.25f : 0.05f));
                return false;
            }
        }

        internal static void Tick()
        {
            if (!(WorldEngineConfig.Enabled?.Value ?? false) ||
                !(WorldEngineConfig.SmoothWorldSaves?.Value ?? true)) return;
            ZNet network = ZNet.instance;
            if (network == null || !network.IsServer()) return;
            bool dispatch;
            lock (Gate)
            {
                if (!_pending || _dispatching) return;
                float now = Time.unscaledTime;
                if (now < _eligibleAt) return;
                Thread thread = SaveThreadField?.GetValue(network) as Thread;
                if (thread != null && thread.IsAlive)
                {
                    _eligibleAt = now + 0.25f;
                    return;
                }
                bool stableFrame = Time.unscaledDeltaTime * 1000f <= FrameBudget();
                float maximum = Math.Max(
                    0f,
                    Math.Min(30f, WorldEngineConfig.MaximumSaveDeferralSeconds?.Value ?? 5f));
                if (!stableFrame && now - _requestedAt < maximum)
                {
                    _eligibleAt = now + 0.05f;
                    return;
                }
                _pending = false;
                _dispatching = true;
                dispatch = true;
            }
            if (!dispatch) return;
            try
            {
                if (SaveWorldMethod == null)
                    throw new MissingMethodException("ZNet.SaveWorld(bool)");
                SaveWorldMethod.Invoke(network, new object[] { false });
            }
            finally
            {
                lock (Gate) _dispatching = false;
            }
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                _pending = false;
                _dispatching = false;
                _requestedAt = 0f;
                _eligibleAt = 0f;
            }
        }

        private static float FrameBudget() => Math.Max(
            8f,
            Math.Min(100f, WorldEngineConfig.SaveFrameBudgetMilliseconds?.Value ?? 24f));
    }

    [HarmonyPatch(typeof(ZNet), "SaveWorld", typeof(bool))]
    internal static class SmoothWorldSavePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ZNet __instance, bool sync, Thread ___m_saveThread) =>
            SaveSmoothingRuntime.BeforeSave(__instance, sync, ___m_saveThread);
    }
}
