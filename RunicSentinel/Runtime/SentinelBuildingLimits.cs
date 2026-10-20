using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal static class SentinelBuildingLimits
    {
        private const string SettingsRpc = "runic.sentinel.building.settings.v1";
        private const string AckRpc = "runic.sentinel.building.ack.v1";
        private static BuildingLimits _local = BuildingLimits.Vanilla;
        private static BuildingLimits _remote = BuildingLimits.Vanilla;
        private static ZRpc _serverRpc;
        private static readonly HashSet<ZRpc> Acknowledged = new HashSet<ZRpc>();
        internal static BuildingLimits Active => ZNet.instance != null && ZNet.instance.IsServer() ? _local : _remote;

        // Full Sentinel loads first through the client's soft dependency. Only one assembly
        // installs these patches; otherwise vertical support loss would be divided twice.
        internal static bool ShouldInstall()
        {
#if RUNIC_SENTINEL_CLIENT_ONLY
            var property = Type.GetType("RunicSentinel.Plugin, RunicSentinel", false)?.GetProperty(
                "BuildingLimitsAvailable", BindingFlags.Public | BindingFlags.Static);
            return property == null || !(bool)property.GetValue(null);
#else
            return true;
#endif
        }

#if !RUNIC_SENTINEL_CLIENT_ONLY
        internal static ConfigFile HostConfig { get; private set; }
        internal static void Configure(ConfigFile config)
        {
            HostConfig = config;
            const string section = "Building and Terrain";
            var enabled = config.Bind(section, "Enabled", false, "Enable custom limits on this host. Players need RunicSentinelClient 1.0.5 or the matching full RunicSentinel build. Restart to apply. Back up the world before changing limits.");
            var support = config.Bind(section, "VerticalSupportMultiplier", 1f, new ConfigDescription("Reduces vertical support loss for taller structures. 1 = vanilla; 2 = half the vertical loss, not an exact height in metres. Horizontal support loss is unchanged. Restart required.", new AcceptableValueRange<float>(1f, 10f)));
            var raise = config.Bind(section, "MaximumTerrainRaise", 8f, new ConfigDescription("Maximum metres above original terrain, independently of dig depth. Vanilla = 8. Restart required.", new AcceptableValueRange<float>(1f, 64f)));
            var dig = config.Bind(section, "MaximumDigDepth", 8f, new ConfigDescription("Maximum metres below original terrain. Vanilla = 8. Restart required. Does not lower water or permit tunnels.", new AcceptableValueRange<float>(1f, 64f)));
            _local = enabled.Value ? new BuildingLimits(support.Value, raise.Value, dig.Value) : BuildingLimits.Vanilla;
        }
#endif

        internal static void Reset()
        {
            _remote = BuildingLimits.Vanilla;
            _serverRpc = null;
            Acknowledged.Clear();
        }

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class ConnectionPatch
        {
            private static bool Prepare() => ShouldInstall();
            private static void Prefix(ZNet __instance, ZNetPeer peer)
            {
                if (__instance.IsServer())
                {
#if !RUNIC_SENTINEL_CLIENT_ONLY
                    peer.m_rpc.Register<ZPackage>(AckRpc, (rpc, package) =>
                    {
                        // The acknowledgement is capability/consistency evidence, not authentication.
                        // Never accepts client-selected settings or writes config/world state.
                        if (package != null && package.Size() == 16 && __instance.GetPeers().Any(p => ReferenceEquals(p.m_rpc, rpc)) &&
                            package.GetArray().SequenceEqual(_local.Encode())) Acknowledged.Add(rpc);
                    });
#endif
                }
                else
                {
                    Reset();
                    _serverRpc = peer.m_rpc;
                    peer.m_rpc.Register<ZPackage>(SettingsRpc, (rpc, package) =>
                    {
                        if (!ReferenceEquals(rpc, _serverRpc) || !ReferenceEquals(ZNet.instance, __instance) || __instance.IsServer()) return;
                        try
                        {
                            if (package == null || package.Size() != 16) throw new InvalidOperationException("Invalid building settings packet.");
                            _remote = BuildingLimits.Decode(package.GetArray());
                            rpc.Invoke(AckRpc, new ZPackage(_remote.Encode()));
                        }
                        catch (Exception)
                        {
                            Reset();
                            __instance.Disconnect(peer);
                        }
                    });
                }
            }
        }

        // Send before vanilla ClientHandshake, so the ordered connection delivers settings
        // and its acknowledgement before native PeerInfo admits the client and loads the world.
#if !RUNIC_SENTINEL_CLIENT_ONLY
        [HarmonyPatch(typeof(ZNet), "RPC_ServerHandshake")]
        private static class HandshakePatch
        {
            private static bool Prepare() => ShouldInstall();
            private static void Prefix(ZNet __instance, ZRpc rpc)
            {
                if (__instance.IsServer() && !_local.IsVanilla) rpc.Invoke(SettingsRpc, new ZPackage(_local.Encode()));
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class AdmissionPatch
        {
            private static bool Prepare() => ShouldInstall();
            private static bool Prefix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer() || _local.IsVanilla || Acknowledged.Contains(rpc)) return true;
                var peer = __instance.GetPeers().FirstOrDefault(p => ReferenceEquals(p.m_rpc, rpc));
                if (peer != null)
                {
                    UnityEngine.Debug.LogWarning("Runic Sentinel: custom building limits require RunicSentinelClient 1.0.5 or a compatible full RunicSentinel plugin on the client.");
                    rpc.Invoke("Error", 3);
                    __instance.Disconnect(peer);
                }
                return false;
            }
        }
#endif

        [HarmonyPatch(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) })]
        private static class DisconnectPatch
        {
            private static bool Prepare() => ShouldInstall();
            private static void Prefix(ZNetPeer peer)
            {
                if (peer?.m_rpc == null) return;
                Acknowledged.Remove(peer.m_rpc);
                if (ReferenceEquals(peer.m_rpc, _serverRpc)) Reset();
            }
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ResetPatch { private static bool Prepare() => ShouldInstall(); private static void Prefix() => Reset(); }
        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class DestroyPatch { private static bool Prepare() => ShouldInstall(); private static void Postfix() => Reset(); }

        internal static float Raise() => Active.RaiseHeight;
        internal static float Dig() => Active.DigDepth;
        internal static float NegativeDig() => -Active.DigDepth;
        internal static float DigThreshold() => Active.DigDepth - 0.05f;

        [HarmonyPatch]
        internal static class TerrainPatch
        {
            private static bool Prepare() => ShouldInstall();
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(TerrainComp), "LevelTerrain");
                yield return AccessTools.Method(typeof(TerrainComp), "RaiseTerrain");
                yield return AccessTools.Method(typeof(TerrainComp), "ApplyToHeightmap");
                yield return AccessTools.Method(typeof(Heightmap), "LevelTerrain");
                yield return AccessTools.Method(typeof(Heightmap), "AtMaxWorldLevelDepth");
            }

            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            {
                var code = instructions.ToList();
                bool threshold = __originalMethod.Name == "AtMaxWorldLevelDepth";
                int dig = 0, raise = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    bool single = code[i].opcode == OpCodes.Ldc_R4;
                    bool twice = code[i].opcode == OpCodes.Ldc_R8;
                    if (!single && !twice) continue;
                    double value = Convert.ToDouble(code[i].operand);
                    string getter;
                    if (threshold && single && (float)value == 7.95f) { getter = nameof(DigThreshold); dig++; }
                    else if (!threshold && value == -8) { getter = nameof(NegativeDig); dig++; }
                    else if (!threshold && value == 8)
                    {
                        bool subtraction = i + 1 < code.Count && code[i + 1].opcode == OpCodes.Sub;
                        getter = subtraction ? nameof(Dig) : nameof(Raise);
                        if (subtraction) dig++; else raise++;
                    }
                    else continue;
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = typeof(SentinelBuildingLimits).GetMethod(getter, BindingFlags.Static | BindingFlags.NonPublic);
                    if (twice) code.Insert(++i, new CodeInstruction(OpCodes.Conv_R8));
                }
                if (dig != 1 || raise != (threshold ? 0 : 1))
                    throw new InvalidOperationException("Building limit patch does not match installed Valheim: " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name);
                return code;
            }
        }

        [HarmonyPatch(typeof(WearNTear), "GetMaterialProperties")]
        private static class SupportPatch
        {
            private static bool Prepare() => ShouldInstall();
            private static void Postfix(ref float verticalLoss) => verticalLoss = Active.VerticalLoss(verticalLoss);
        }
    }
}
