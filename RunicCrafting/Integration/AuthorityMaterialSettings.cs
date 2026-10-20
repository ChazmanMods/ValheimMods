using System;
using System.Globalization;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicCrafting.Integration
{
    // Session-only server limits: never overwrite a player's saved local configuration.
    internal static class AuthorityMaterialSettings
    {
        private const string RequestRpc = "RunicCrafting_MaterialLimits_Request_v1";
        private const string ReplyRpc = "RunicCrafting_MaterialLimits_Reply_v1";
        internal sealed class Limits
        {
            internal float Range, Interaction, Stationless;
            internal int Candidates, Returned;
        }
        private static ZRoutedRpc registered;
        private static Limits remote;
        private static string world;
        private static long server;
        private static float nextRequest;
        internal static Limits Current => remote != null && ZNet.instance != null && !ZNet.instance.IsServer() &&
            InventoryOperationNetwork.Ready && TryWorld(out string currentWorld) && currentWorld == world &&
            ZNet.instance.GetServerPeer()?.m_uid == server ? remote : null;
        private static bool TryWorld(out string value)
        {
            value = null;
            if (ZNet.instance == null) return false;
            // Native GetWorldUID dereferences m_world without a null check. ZNet.Update
            // already runs while connecting, before PeerInfo supplies the client's world.
            var context = ZNet.instance.GetWorld();
            if (context == null || context.m_uid == 0) return false;
            value = context.m_uid.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        internal static void Clear()
        {
            bool changed = remote != null;
            remote = null; world = null; server = 0; nextRequest = 0;
            if (changed) CraftingRuntime.OnConfigurationChanged();
        }
        private static Limits Local()
        {
            float cap = Mathf.Clamp(Configuration.RangeCapMeters.Value, 1f, 50f);
            int candidates = Math.Max(1, Math.Min(256, Configuration.MaximumCandidateContainers.Value));
            return new Limits { Range = cap, Interaction = Mathf.Clamp(Configuration.InteractionRangeMeters.Value, 1f, cap),
                Stationless = Mathf.Clamp(Configuration.StationlessBuildRangeMeters.Value, 1f, cap), Candidates = candidates,
                Returned = Math.Max(1, Math.Min(Math.Min(128, candidates), Configuration.MaximumReturnedContainers.Value)) };
        }
        internal static void Tick()
        {
            if (ZRoutedRpc.instance == null || !TryWorld(out string currentWorld)) { Clear(); return; }
            if (!ReferenceEquals(registered, ZRoutedRpc.instance))
            {
                registered = ZRoutedRpc.instance; Clear();
                registered.Register<ZPackage>(RequestRpc, Request);
                registered.Register<ZPackage>(ReplyRpc, Reply);
            }
            if (ZNet.instance.IsServer()) return;
            if (!InventoryOperationNetwork.Ready) { Clear(); return; }
            long peer = ZNet.instance.GetServerPeer()?.m_uid ?? 0;
            if (peer == 0) { Clear(); return; }
            if (world != currentWorld || server != peer) { Clear(); world = currentWorld; server = peer; }
            if (Time.realtimeSinceStartup < nextRequest) return;
            nextRequest = Time.realtimeSinceStartup + (remote == null ? 1f : 30f);
            var packet = new ZPackage(); packet.Write(currentWorld);
            registered.InvokeRoutedRPC(peer, RequestRpc, packet);
        }
        private static void Request(long sender, ZPackage packet)
        {
            try
            {
                if (!TryWorld(out string currentWorld) || !ZNet.instance.IsServer() || !InventoryOperationNetwork.Compatible(sender) ||
                    ZNet.instance.GetPeer(sender)?.IsReady() != true || packet == null || packet.Size() > 64) return;
                string requestedWorld = packet.ReadString(); OperationPacket.End(packet);
                if (requestedWorld != currentWorld) return;
                var limits = Local(); var reply = new ZPackage(); reply.Write(currentWorld); reply.Write(1);
                reply.Write(limits.Range); reply.Write(limits.Interaction); reply.Write(limits.Stationless);
                reply.Write(limits.Candidates); reply.Write(limits.Returned);
                registered.InvokeRoutedRPC(sender, ReplyRpc, reply);
            }
            catch (Exception error) { Plugin.Log?.LogWarning("Material-limit request rejected: " + error.Message); }
        }
        internal static Limits Decode(ZPackage packet, string expectedWorld)
        {
            if (packet == null || packet.Size() > 128 || packet.ReadString() != expectedWorld || packet.ReadInt() != 1)
                throw new InvalidOperationException("Invalid material-limit envelope");
            var result = new Limits { Range = packet.ReadSingle(), Interaction = packet.ReadSingle(), Stationless = packet.ReadSingle(),
                Candidates = packet.ReadInt(), Returned = packet.ReadInt() };
            OperationPacket.End(packet);
            bool Radius(float value, float cap) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 1f && value <= cap;
            if (!Radius(result.Range, 50f) || !Radius(result.Interaction, result.Range) || !Radius(result.Stationless, result.Range) ||
                result.Candidates < 1 || result.Candidates > 256 || result.Returned < 1 || result.Returned > Math.Min(128, result.Candidates))
                throw new InvalidOperationException("Invalid material-limit values");
            return result;
        }
        private static void Reply(long sender, ZPackage packet)
        {
            try
            {
                if (!TryWorld(out string currentWorld) || ZNet.instance.IsServer() || !InventoryOperationNetwork.Ready ||
                    ZNet.instance.GetServerPeer()?.m_uid != sender) return;
                var limits = Decode(packet, currentWorld);
                bool changed = remote == null || remote.Range != limits.Range || remote.Interaction != limits.Interaction ||
                    remote.Stationless != limits.Stationless || remote.Candidates != limits.Candidates || remote.Returned != limits.Returned;
                remote = limits; world = currentWorld; server = sender;
                if (changed)
                {
                    CraftingRuntime.OnConfigurationChanged();
                    Plugin.Log?.LogInfo($"Server material limits received: range cap={limits.Range}m; candidates={limits.Candidates}; sources={limits.Returned}; stationless={limits.Stationless}m; interaction={limits.Interaction}m.");
                }
            }
            catch (Exception error) { Plugin.Log?.LogWarning("Material limits were not accepted: " + error.Message); }
        }
    }
    [HarmonyPatch(typeof(ZNet), "Update")]
    internal static class AuthorityMaterialSettingsTick
    { private static void Postfix() => AuthorityMaterialSettings.Tick(); }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class AuthorityMaterialSettingsClear
    { private static void Postfix() => AuthorityMaterialSettings.Clear(); }
}
