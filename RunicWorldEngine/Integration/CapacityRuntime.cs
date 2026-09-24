using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using RunicWorldEngine.Core;

namespace RunicWorldEngine.Integration
{
    internal static class CapacityRuntime
    {
        private const string Owner = Plugin.Guid + ".capacity";
        private static readonly Dictionary<MethodBase, CapacityAudit.Site> Targets = new Dictionary<MethodBase, CapacityAudit.Site>();
        private static Harmony _harmony;
        private static bool _requested, _valid, _dedicated;
        internal static int PlayerLimit { get; private set; } = 10;
        internal static bool ValidatedOverride => _requested && _valid;
        internal static string Status { get; private set; } = "vanilla capacity (override disabled)";

        internal static void Initialize()
        {
            _requested = WorldEngineConfig.OverridePlayerCap.Value;
            if (!_requested) return;
            try
            {
                int configured = WorldEngineConfig.MaximumPlayers.Value;
                CapacityAudit.Limit(configured, _dedicated, true);
                using (var assembly = AssemblyDefinition.ReadAssembly(typeof(ZNet).Assembly.Location))
                {
                    _dedicated = CapacityAudit.IsDedicated(assembly);
                    byte[] image = File.ReadAllBytes(typeof(ZNet).Assembly.Location);
                    foreach (var site in CapacityAudit.Validate(assembly, _dedicated))
                    {
                        var method = typeof(ZNet).Module.ResolveMethod(site.Token);
                        byte[] loaded = method.GetMethodBody()?.GetILAsByteArray();
                        if (loaded == null || !loaded.SequenceEqual(CapacityAudit.MethodCode(image, site.Rva)))
                            throw new InvalidOperationException("Loaded/preloader-modified capacity body differs from audit: " + method.Name);
                        RejectForeignPatches(method);
                        Targets.Add(method, site);
                    }
                }
                PlayerLimit = configured; // Frozen until process restart.
                _harmony = new Harmony(Owner);
                foreach (var target in Targets.Keys)
                    _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CapacityRuntime), nameof(Transpile)) { priority = Priority.Last });
                _valid = true;
                if (!CheckIntegrity()) throw new InvalidOperationException(Status);
                Status = global::Runic.Localization.RunicText.Format("text_ac591cf388f4", Targets.Count, Targets.Count, PlayerLimit, CapacityAudit.Limit(PlayerLimit, _dedicated, true));
                Plugin.Log.LogInfo("Capacity: " + Status);
            }
            catch (Exception error)
            {
                _harmony?.UnpatchSelf();
                _harmony = null;
                _valid = false;
                PlayerLimit = 10;
                Status = global::Runic.Localization.RunicText.Get("text_b80e6b9c2dd3") + error.Message;
                Plugin.Log.LogError(Status);
            }
        }

        // Public hosting must not start with a partly patched set. Existing connections are not kicked
        // if another mod later changes the patch set; subsequent admissions are blocked instead.
        internal static bool CheckIntegrity()
        {
            if (!_requested) return true;
            if (!_valid) return false;
            try
            {
                foreach (var target in Targets.Keys)
                {
                    RejectForeignPatches(target);
                    var info = Harmony.GetPatchInfo(target);
                    if (info == null || info.Transpilers.Count(p => p.owner == Owner) != 1)
                        throw new InvalidOperationException("Capacity patch missing: " + target.Name);
                }
                return true;
            }
            catch (Exception error)
            {
                _valid = false; // Sticky fault. Do not silently shrink an already advertised lobby.
                Status = global::Runic.Localization.RunicText.Get("text_c175aa0f5106") + error.Message;
                Plugin.Log.LogError(Status);
                return false;
            }
        }

        private static void RejectForeignPatches(MethodBase target)
        {
            var info = Harmony.GetPatchInfo(target);
            // Authentication/character-vault prefixes and diagnostic postfixes remain independent.
            // Reject competing body rewrites; never disable another mod's admission checks.
            if (info != null && info.Transpilers.Any(p => p.owner != Owner))
                throw new InvalidOperationException("Conflicting capacity/body transpiler on " + target.DeclaringType.Name + "." + target.Name);
        }

        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return CapacityRewrite.Apply(instructions, Targets[original], PlayerLimit, _dedicated);
        }

        internal static void Reset()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Targets.Clear();
            _requested = _valid = false;
            PlayerLimit = 10;
            Status = global::Runic.Localization.RunicText.Get("text_29e297337e69");
        }
    }

    [HarmonyPatch(typeof(ZNet), "OpenServer")]
    internal static class CapacityHostGuard
    {
        private static bool Prefix() => CapacityRuntime.CheckIntegrity();
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class CapacityAdmissionGuard
    {
        private static bool Prefix(ZNet __instance, ZRpc __0)
        {
            if (!__instance.IsServer() || CapacityRuntime.CheckIntegrity()) return true;
            __0.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorFull);
            return false;
        }
    }
}
