using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty
{
    internal static class Network
    {
        const string Hello = "RDP_Hello_v1", Settings = "RDP_Policy_v1";
        static readonly HashSet<ZRpc> Compatible = new HashSet<ZRpc>();
        static readonly Dictionary<ZRpc, float> Pending = new Dictionary<ZRpc, float>();
        internal static bool Synchronized;
        internal static void Reset() { Compatible.Clear(); Pending.Clear(); Synchronized = false; }
        internal static void Register(ZNetPeer peer)
        {
            Pending[peer.m_rpc] = Time.unscaledTime + 30;
            peer.m_rpc.Register<string>(Hello, (rpc, version) =>
            {
                if (version == Plugin.Version) { Compatible.Add(rpc); Pending.Remove(rpc); }
                else Reject(rpc);
            });
            peer.m_rpc.Register<string>(Settings, (rpc, wire) =>
            {
                // The direct server connection is the only policy authority; never accept routed peer settings.
                if (!ZNet.instance || ZNet.instance.IsServer() || !peer.m_server || !Compatible.Contains(rpc)) return;
                try { Plugin.Policy = Rules.Decode(wire); Synchronized = true; }
                catch (Exception e) { Plugin.Log.LogError("Rejected server policy: " + e.Message); Reject(rpc); }
            });
        }
        internal static void Send(ZRpc rpc)
        {
            rpc.Invoke(Hello, Plugin.Version);
            if (ZNet.instance.IsServer()) rpc.Invoke(Settings, Plugin.Policy.Encode());
        }
        internal static bool Admit(ZRpc rpc)
        {
            if (Compatible.Contains(rpc)) return true;
            Reject(rpc); return false;
        }
        static void Reject(ZRpc rpc)
        {
            Plugin.Log.LogError("Connection rejected: server and client require RunicDeathPenalty " + Plugin.Version + ".");
            if (ZNet.instance.IsServer()) rpc.Invoke("Error", 3);
            else AccessTools.Field(typeof(ZNet), "m_connectionStatus").SetValue(null, ZNet.ConnectionStatus.ErrorVersion);
            Pending[rpc] = Time.unscaledTime + 2;
            Compatible.Remove(rpc);
        }
        internal static void Broadcast()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            foreach (var peer in ZNet.instance.GetPeers()) if (Compatible.Contains(peer.m_rpc)) peer.m_rpc.Invoke(Settings, Plugin.Policy.Encode());
        }
        internal static void Tick()
        {
            var live = new HashSet<ZRpc>(ZNet.instance.GetPeers().Select(p => p.m_rpc));
            Compatible.RemoveWhere(r => !live.Contains(r));
            foreach (var pair in Pending.ToArray())
            {
                if (!live.Contains(pair.Key)) { Pending.Remove(pair.Key); continue; }
                if (Time.unscaledTime < pair.Value) continue;
                var peer = ZNet.instance.GetPeers().FirstOrDefault(p => p.m_rpc == pair.Key);
                peer?.m_socket.Close(); Pending.Remove(pair.Key);
            }
        }
    }
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    static class RegisterConnection { static void Prefix(ZNetPeer peer) => Network.Register(peer); }
    [HarmonyPatch(typeof(ZNet), "SendPeerInfo")]
    static class SendHandshake { static void Prefix(ZRpc rpc) => Network.Send(rpc); }
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    static class AdmitConnection { [HarmonyPriority(Priority.First)] static bool Prefix(ZRpc rpc) => Network.Admit(rpc); }
}
