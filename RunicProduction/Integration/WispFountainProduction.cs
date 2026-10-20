using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        private static readonly AccessTools.FieldRef<LuredWisp, float> WispDespawnTimer =
            AccessTools.FieldRefAccess<LuredWisp, float>("m_despawnTimer");
        private static readonly MethodInfo PickWisp = AccessTools.Method(
            typeof(Pickable), "RPC_Pick", new[] { typeof(long), typeof(int) });

        // Reuse the wisp's native two-second target update. No scene scans, new
        // timers, ownership requests, or changes to fountain spawning are needed.
        internal static void TryCollectWisp(LuredWisp wisp)
        {
            if (!_initialized || !(ProductionConfig.Enabled?.Value ?? false) ||
                wisp == null || !ValheimAccess.IsNativeOwner(wisp) ||
                WispDespawnTimer(wisp) > 0f ||
                (wisp.m_despawnInDaylight && EnvMan.IsDaylight())) return;
            WispSpawner fountain = WispSpawner.GetBestSpawner(
                wisp.transform.position, wisp.m_maxLureDistance);
            if (fountain == null || !fountain.isActiveAndEnabled ||
                SafetyPausedStations.Contains(fountain.GetInstanceID()) ||
                !NativeProductionOutput.Collects(fountain) || NativeProductionOutput.CollectorWaiting(fountain) ||
                fountain.m_spawnPoint == null || fountain.m_nearbyTreshold <= 0f ||
                (wisp.transform.position - fountain.m_spawnPoint.position).sqrMagnitude >
                    fountain.m_nearbyTreshold * fountain.m_nearbyTreshold) return;
            Pickable pickable = wisp.GetComponent<Pickable>();
            if (pickable == null || pickable.GetPicked() || pickable.GetEnabled == 0 ||
                pickable.m_itemPrefab == null) return;
            Component previous = NativeProductionOutput.Current;
            try
            {
                // The native pick method creates the real, modded/scaled drops and
                // consumes this exact wisp. Existing output capture routes those
                // drops through the same buffer/chest path as other producers.
                NativeProductionOutput.Current = fountain;
                PickWisp.Invoke(pickable, new object[] { ZDOMan.GetSessionID(), 0 });
            }
            finally { NativeProductionOutput.Current = previous; }
        }
    }

    [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
    internal static class ProductionWispPickEffectPatch
    {
        private static ZDOID EffectPlayer(Character player)
        {
            if (player != null) return player.GetZDOID();
            // Vanilla references the local player only to tag its pickup effect.
            // A dedicated server has no player; only our automatic wisp scope
            // supplies an empty effect origin. Ordinary pickup stays native.
            if (NativeProductionOutput.Current is WispSpawner) return default;
            return player.GetZDOID();
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(Character), nameof(Character.GetZDOID));
            MethodInfo replacement = AccessTools.Method(typeof(ProductionWispPickEffectPatch), nameof(EffectPlayer));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Native pickable effect-player boundary changed");
        }
    }

}
