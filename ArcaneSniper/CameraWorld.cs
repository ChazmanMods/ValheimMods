using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ArcaneSniper
{
    // Add a second interest area. Never replace the player's reference position.
    internal static class CameraWorld
    {
        private const string Request = "ArcaneSniper.CameraRequest.v1", State = "ArcaneSniper.CameraState.v1";
        private sealed class Focus { internal Vector3 Position; internal float Seen; }
        private static readonly Dictionary<long, Focus> cameras = new Dictionary<long, Focus>();
        private static readonly HashSet<long> subscribers = new HashSet<long>();
        private static readonly List<long> expired = new List<long>();
        private static readonly List<ZDO> extra = new List<ZDO>();
        private static readonly HashSet<ZDO> seen = new HashSet<ZDO>();
        private static readonly List<Focus> terrainTargets = new List<Focus>();
        private static int terrainCursor;
        private static readonly SimulationDistance cameraDistance = new SimulationDistance(2, 0, true);
        private static readonly Func<ZoneSystem, Vector2s, bool> poke = AccessTools.MethodDelegate<Func<ZoneSystem, Vector2s, bool>>(AccessTools.Method(typeof(ZoneSystem), "PokeLocalZone"));
        private static readonly Func<SpawnSystem, Vector3, float, bool> inside = AccessTools.MethodDelegate<Func<SpawnSystem, Vector3, float, bool>>(AccessTools.Method(typeof(SpawnSystem), "InsideZone"));
        private delegate bool GoodSpawn(SpawnSystem system, SpawnSystem.SpawnData spawn, ref Vector3 point);
        private static readonly GoodSpawn goodSpawn = AccessTools.MethodDelegate<GoodSpawn>(AccessTools.Method(typeof(SpawnSystem), "IsSpawnPointGood"));
        private static readonly AccessTools.FieldRef<object, ZNetPeer> syncPeer = AccessTools.FieldRefAccess<ZNetPeer>(AccessTools.Inner(typeof(ZDOMan), "ZDOPeer"), "m_peer");
        private static ZRoutedRpc registered;
        private static float nextSend, nextSweep, nextTerrain;
        private static bool announced, supported, requested, adding;
        private static Vector3 lastSent;
        [ThreadStatic] private static Interest interest;
        [ThreadStatic] private static bool eventSpawns;
        internal struct Interest { internal long Peer; internal bool Local, Collect; }

        internal static bool Ready => ZNet.instance && (ZNet.instance.IsServer() || supported);
        internal static bool HasLocal => ZNet.instance && cameras.ContainsKey(ZNet.GetUID());

        internal static void Register()
        {
            cameras.Clear(); subscribers.Clear(); expired.Clear();
            interest = default; announced = supported = requested = false;
            nextSend = nextSweep = nextTerrain = 0;
            registered = ZRoutedRpc.instance;
            registered.Register<ZPackage>(Request, ReceiveRequest);
            registered.Register<ZPackage>(State, ReceiveState);
        }

        internal static void Tick(bool enabled, Vector3 point)
        {
            if (!ZNet.instance || registered == null || registered != ZRoutedRpc.instance) return;
            if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected) return;
            if (!ZNet.instance.IsServer() && ZNet.instance.GetServerPeer()?.IsReady() != true) return;
            // One capability probe per connection. Never spam an unmodded server.
            enabled &= Ready;
            long uid = ZNet.GetUID();
            if (enabled && Ready) Set(uid, true, point);
            else cameras.Remove(uid);
            if (!announced || requested != enabled || (enabled && Time.unscaledTime >= nextSend &&
                ((point - lastSent).sqrMagnitude > 1 || Time.unscaledTime >= nextSend + .75f)))
            {
                // Coalesce movement to at most four packets/sec; idle heartbeat once/sec.
                if (!announced || requested != enabled || Time.unscaledTime >= nextSend)
                {
                    announced = true; requested = enabled; lastSent = point; nextSend = Time.unscaledTime + .25f;
                    if (ZNet.instance.IsServer()) Broadcast(uid, enabled, point);
                    else registered.InvokeRoutedRPC(Request, Pack(uid, enabled, point));
                }
            }
            if (Time.unscaledTime < nextSweep) return;
            nextSweep = Time.unscaledTime + 1;
            expired.Clear();
            foreach (var pair in cameras)
                if (pair.Key != uid && (Time.unscaledTime - pair.Value.Seen > 5 ||
                    (ZNet.instance.IsServer() && ZNet.instance.GetPeer(pair.Key) == null))) expired.Add(pair.Key);
            foreach (long peer in expired)
            {
                cameras.Remove(peer);
                if (ZNet.instance.IsServer()) Broadcast(peer, false, Vector3.zero);
            }
            if (ZNet.instance.IsServer()) subscribers.RemoveWhere(peer => ZNet.instance.GetPeer(peer) == null);
        }

        internal static void Stop()
        {
            if (registered != null && ZNet.instance && requested)
            {
                long uid = ZNet.GetUID();
                cameras.Remove(uid); requested = false;
                if (ZNet.instance.IsServer()) Broadcast(uid, false, Vector3.zero);
                else registered.InvokeRoutedRPC(Request, Pack(uid, false, Vector3.zero));
            }
        }

        private static readonly Action<MonsterAI> wake = AccessTools.MethodDelegate<Action<MonsterAI>>(AccessTools.Method(typeof(MonsterAI), "Wakeup"));
        [HarmonyPatch(typeof(MonsterAI), "UpdateSleep")]
        private static class SleepPresencePatch
        {
            private static bool Prefix(MonsterAI __instance, float dt, ref float ___m_sleepTimer, float ___m_sleepDelay)
            {
                if (cameras.Count == 0) return true;
                float range = __instance.IsSleeping() ? __instance.m_wakeupRange : __instance.m_fallAsleepDistance;
                if (range <= 0) return true;
                bool nearby = false;
                foreach (var pair in cameras) if ((__instance.transform.position - pair.Value.Position).sqrMagnitude < range * range) { nearby = true; break; }
                if (!nearby) return true;
                if (__instance.IsSleeping())
                {
                    ___m_sleepTimer += dt;
                    if (___m_sleepTimer >= ___m_sleepDelay) wake(__instance);
                }
                return false;
            }
        }

        private static ZPackage Pack(long uid, bool active, Vector3 point)
        {
            var package = new ZPackage(); package.Write(uid); package.Write(active); package.Write(point); return package;
        }
        private static void Set(long uid, bool active, Vector3 point)
        {
            if (!active) { cameras.Remove(uid); return; }
            if (!cameras.TryGetValue(uid, out var focus)) cameras.Add(uid, focus = new Focus());
            focus.Position = point; focus.Seen = Time.unscaledTime;
        }
        private static void ReceiveRequest(long sender, ZPackage package)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            var peer = ZNet.instance.GetPeer(sender);
            if (peer == null || !peer.IsReady()) return;
            try
            {
                package.ReadLong(); bool active = package.ReadBool(); Vector3 point = package.ReadVector3();
                if (active && !WorldGeometry.ValidFocus(point.x, point.y, point.z, peer.GetRefPos().x, peer.GetRefPos().z, Plugin.OneMile)) return;
                bool first = subscribers.Add(sender);
                Set(sender, active, point);
                // A disabled state also serves as a capability acknowledgement.
                registered.InvokeRoutedRPC(sender, State, Pack(sender, active, point));
                if (first) foreach (var pair in cameras)
                    if (pair.Key != sender) registered.InvokeRoutedRPC(sender, State, Pack(pair.Key, true, pair.Value.Position));
                Broadcast(sender, active, point, sender);
            }
            catch (Exception error) { Plugin.Instance?.ReportWorldError("Invalid camera packet", error); }
        }
        private static void ReceiveState(long sender, ZPackage package)
        {
            if (!ZNet.instance || ZNet.instance.IsServer() || ZNet.instance.GetServerPeer()?.m_uid != sender) return;
            try
            {
                long uid = package.ReadLong(); bool active = package.ReadBool(); var point = package.ReadVector3();
                supported = true;
                if (uid != ZNet.GetUID()) Set(uid, active, point);
            }
            catch (Exception error) { Plugin.Instance?.ReportWorldError("Invalid server camera packet", error); }
        }
        private static void Broadcast(long uid, bool active, Vector3 point, long exclude = 0)
        {
            foreach (long peer in subscribers)
                if (peer != exclude && ZNet.instance.GetPeer(peer) != null)
                    registered.InvokeRoutedRPC(peer, State, Pack(uid, active, point));
        }

        private static bool InCamera(Vector3 point, Vector3 center, int radius)
        {
            var a = ZoneSystem.GetZone(point); var b = ZoneSystem.GetZone(center);
            return WorldGeometry.InSquare(a.x, a.y, b.x, b.y, radius);
        }
        private static bool Includes(long peer, Vector3 point) => cameras.TryGetValue(peer, out var focus) && InCamera(point, focus.Position, 1);
        private static bool LocalIncludes(Vector3 point)
        {
            if (!ZNet.instance) return false;
            if (!ZNet.instance.IsServer()) return Includes(ZNet.GetUID(), point);
            foreach (var pair in cameras) if (InCamera(point, pair.Value.Position, 1)) return true;
            return false;
        }
        private static bool LoadHere(long uid) => ZNet.instance && (ZNet.instance.IsServer() || uid == ZNet.GetUID());

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class RegisterPatch { private static void Postfix() => Register(); }

        [HarmonyPatch(typeof(EnvZone), "Update")]
        private static class InteriorShellPatch
        {
            private static void Postfix(EnvZone __instance)
            {
                if (!__instance.m_exteriorMesh || !ZNet.instance || !cameras.TryGetValue(ZNet.GetUID(), out var focus)) return;
                var collider = __instance.GetComponent<Collider>();
                if (collider && (collider.ClosestPoint(focus.Position) - focus.Position).sqrMagnitude < .001f)
                    __instance.m_exteriorMesh.forceRenderingOff = false;
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), "CreateLocalZones")]
        private static class TerrainPatch
        {
            private static void Postfix(ZoneSystem __instance)
            {
                if (Time.unscaledTime < nextTerrain || cameras.Count == 0) return;
                nextTerrain = Time.unscaledTime + .1f;
                bool created = false;
                terrainTargets.Clear();
                foreach (var pair in cameras) if (LoadHere(pair.Key)) terrainTargets.Add(pair.Value);
                if (terrainTargets.Count == 0) return;
                int first = terrainCursor++ % terrainTargets.Count;
                for (int index = 0; index < terrainTargets.Count; index++)
                {
                    var zone = ZoneSystem.GetZone(terrainTargets[(first + index) % terrainTargets.Count].Position);
                    // Refresh loaded neighbors even after one new zone was generated.
                    // Spiral rings prioritize the view's center, with one generation per tick.
                    for (int ring = 0; ring <= 2; ring++)
                        for (int y = -ring; y <= ring; y++) for (int x = -ring; x <= ring; x++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring) continue;
                            var candidate = new Vector2s(zone.x + x, zone.y + y);
                            if (!created || __instance.IsZoneLoaded(candidate)) created |= poke(__instance, candidate);
                        }
                }
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        private static class SceneContext
        {
            private static void Prefix(out Interest __state) { __state = interest; interest = new Interest { Local = true, Collect = true }; }
            private static Exception Finalizer(Exception __exception, Interest __state) { interest = __state; return __exception; }
        }
        [HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
        private static class SyncContext
        {
            private static void Prefix(object peer, out Interest __state) { __state = interest; interest = new Interest { Peer = syncPeer(peer).m_uid, Collect = true }; }
            private static Exception Finalizer(Exception __exception, Interest __state) { interest = __state; return __exception; }
        }
        [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
        private static class OwnerContext
        {
            private static void Prefix(long uid, out Interest __state) { __state = interest; interest = new Interest { Peer = uid, Collect = true }; }
            private static Exception Finalizer(Exception __exception, Interest __state) { interest = __state; return __exception; }
        }
        [HarmonyPatch(typeof(ZDOMan), "FindSectorObjects")]
        private static class SectorPatch
        {
            private static void Postfix(ZDOMan __instance, List<ZDO> sectorObjects)
            {
                if (adding || !interest.Collect || cameras.Count == 0) return;
                interest.Collect = false;
                adding = true;
                try
                {
                    seen.Clear(); foreach (var zdo in sectorObjects) seen.Add(zdo);
                    foreach (var pair in cameras)
                    {
                        if (interest.Local ? !LoadHere(pair.Key) : pair.Key != interest.Peer) continue;
                        extra.Clear();
                        __instance.FindSectorObjects(ZoneSystem.GetZone(pair.Value.Position), cameraDistance, extra);
                        foreach (var zdo in extra) if (seen.Add(zdo)) sectorObjects.Add(zdo);
                    }
                }
                finally { extra.Clear(); seen.Clear(); adding = false; }
            }
        }
        [HarmonyPatch(typeof(ZNetScene), "PointInsideActiveArea")]
        private static class ActivePatch
        {
            private static void Postfix(Vector3 point, ref bool __result)
            {
                if (!__result && interest.Peer != 0) __result = Includes(interest.Peer, point);
            }
        }
        [HarmonyPatch(typeof(ZNetScene), "OutsideActiveArea", new Type[] { typeof(Vector3) })]
        private static class OutsidePatch
        {
            private static void Postfix(Vector3 point, ref bool __result) { if (__result && LocalIncludes(point)) __result = false; }
        }
        [HarmonyPatch(typeof(ZDOMan), "IsInPeerActiveArea")]
        private static class PeerActivePatch
        {
            private static void Postfix(Vector3 point, long uid, ref bool __result) { if (!__result) __result = Includes(uid, point); }
        }

        internal static bool PlayerInRange(Vector3 point, float range)
        {
            if (Player.IsPlayerInRange(point, range)) return true;
            foreach (var pair in cameras) if ((pair.Value.Position - point).sqrMagnitude < range * range) return true;
            return false;
        }
        internal static bool PlayerInRange(Vector3 point, float range, float minNoise)
        {
            // Camera travel represents quiet presence, not artificial player noise.
            return Player.IsPlayerInRange(point, range, minNoise);
        }
        [HarmonyPatch]
        private static class SpawnerPresencePatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(CreatureSpawner), "UpdateSpawner");
                yield return AccessTools.Method(typeof(SpawnArea), "UpdateSpawn");
                yield return AccessTools.Method(typeof(SpawnSystem), "IsSpawnPointGood");
            }
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var instruction in instructions)
                {
                    if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Player) && method.Name == "IsPlayerInRange")
                    {
                        var parameters = Array.ConvertAll(method.GetParameters(), p => p.ParameterType);
                        instruction.operand = AccessTools.Method(typeof(CameraWorld), nameof(PlayerInRange), parameters);
                    }
                    yield return instruction;
                }
            }
        }

        private static bool OutdoorCamera(SpawnSystem system, Vector3 point) => point.y < 3000 && inside(system, point, 0);
        internal static int SpawnPresenceCount(List<Player> players, SpawnSystem system)
        {
            if (players.Count != 0) return players.Count;
            foreach (var pair in cameras) if (OutdoorCamera(system, pair.Value.Position)) return 1;
            return 0;
        }
        internal static bool CanSpawnWithoutLocalPlayer() => Player.m_localPlayer != null || cameras.Count != 0;
        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
        private static class DedicatedSpawnPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // Replace only the vanilla local-player-null gate, retaining ownership/validity gates.
                var code = new List<CodeInstruction>(instructions);
                for (int i = 0; i + 2 < code.Count; i++)
                {
                    if (code[i].operand is MethodInfo count && count.Name == "get_Count" && count.DeclaringType == typeof(List<Player>))
                    {
                        // Keep the original instruction's branch labels and exception blocks.
                        code[i].opcode = OpCodes.Ldarg_0; code[i].operand = null;
                        code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraWorld), nameof(SpawnPresenceCount))));
                        i++;
                    }
                    if (code[i].opcode == OpCodes.Ldsfld && code[i].operand is FieldInfo field && field.Name == "m_localPlayer" &&
                        code[i + 1].opcode == OpCodes.Ldnull && code[i + 2].operand is MethodInfo method && method.Name == "op_Equality")
                    {
                        code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(CameraWorld), nameof(CanSpawnWithoutLocalPlayer));
                        code[i + 1].opcode = OpCodes.Ldc_I4_0; code[i + 1].operand = null;
                        code[i + 2].opcode = OpCodes.Ceq; code[i + 2].operand = null;
                    }
                }
                return code;
            }
        }
        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
        private static class EventScope
        {
            private static void Prefix(bool eventSpawners, out bool __state) { __state = eventSpawns; eventSpawns = eventSpawners; }
            private static Exception Finalizer(Exception __exception, bool __state) { eventSpawns = __state; return __exception; }
        }
        private static readonly List<Vector3> spawnOrigins = new List<Vector3>();
        [HarmonyPatch(typeof(SpawnSystem), "FindBaseSpawnPoint")]
        private static class SpawnPointPatch
        {
            private static bool Prefix(SpawnSystem __instance, SpawnSystem.SpawnData spawn, List<Player> allPlayers,
                ref Vector3 spawnCenter, ref Player targetPlayer, ref bool __result)
            {
                if (cameras.Count == 0) return true;
                spawnOrigins.Clear();
                foreach (var player in allPlayers) if (player) spawnOrigins.Add(player.transform.position);
                if (!eventSpawns) foreach (var pair in cameras)
                    if (OutdoorCamera(__instance, pair.Value.Position))
                    {
                        bool nearby = false;
                        foreach (var origin in spawnOrigins) if ((origin - pair.Value.Position).sqrMagnitude < 1) { nearby = true; break; }
                        if (!nearby) spawnOrigins.Add(pair.Value.Position);
                    }
                __result = false; spawnCenter = Vector3.zero; targetPlayer = null;
                float min = spawn.m_spawnRadiusMin > 0 ? spawn.m_spawnRadiusMin : 40;
                float max = spawn.m_spawnRadiusMax > 0 ? spawn.m_spawnRadiusMax : 80;
                for (int i = 0; i < 20 && spawnOrigins.Count > 0; i++)
                {
                    var origin = spawnOrigins[UnityEngine.Random.Range(0, spawnOrigins.Count)];
                    var point = origin + Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0) * Vector3.forward * UnityEngine.Random.Range(min, max);
                    if (!goodSpawn(__instance, spawn, ref point)) continue;
                    spawnCenter = point; __result = true;
                    foreach (var player in allPlayers) if (player && player.transform.position == origin) { targetPlayer = player; break; }
                    break;
                }
                spawnOrigins.Clear(); return false;
            }
        }
    }
}
