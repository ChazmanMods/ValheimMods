using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunicDisplayStands
{
    /// <summary>
    /// A minimal, self-contained replacement for what a shared "server sync" library
    /// (like Zen_ModLib) normally provides: if the host has this mod installed, connecting
    /// clients receive the host's admin config values over ZRoutedRpc and apply them locally,
    /// so the "Stand Prefabs" list stays consistent for everyone -- without requiring a
    /// separate shared-library mod at all.
    ///
    /// This mod does NOT reject connections from clients who lack it (kept client-side /
    /// optional like the original, per its own Thunderstore listing), it only pushes config
    /// when both sides happen to have it.
    /// </summary>
    internal static class ConfigSync
    {
        private const string RpcName = "RunicDisplayStands_ConfigSync";
        private const int MaximumPayloadBytes = 4096;
        private const int MaximumEntries = 16;
        private const int MaximumKeyBytes = 128;
        private const int MaximumValueBytes = 2048;
        private static readonly List<ConfigEntryBase> _synced = new List<ConfigEntryBase>();
        private static ZRoutedRpc _registeredRpc;

        public static void RegisterSyncedConfig(ConfigEntryBase entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            string key = entry.Definition.Key ?? string.Empty;
            if (!IsBoundedField(key, MaximumKeyBytes) || key.IndexOfAny(new[] { '=', '|' }) >= 0)
                throw new ArgumentException("Synced config keys must be bounded and delimiter-free.", nameof(entry));
            if (_synced.Exists(candidate => candidate.Definition == entry.Definition)) return;
            if (_synced.Count >= MaximumEntries)
                throw new InvalidOperationException("The synced config entry limit has been reached.");
            _synced.Add(entry);
            EnsureRpcRegistered();
        }

        internal static void EnsureRpcRegistered()
        {
            var routedRpc = ZRoutedRpc.instance;
            if (routedRpc == null || ReferenceEquals(_registeredRpc, routedRpc)) return;

            // Register once ZNet exists. ZRoutedRpc.instance is the vanilla RPC bus used
            // by every Valheim mod that needs client<->server messaging -- no external
            // networking library needed.
            routedRpc.Register<string>(RpcName, OnConfigReceived);
            _registeredRpc = routedRpc;
        }

        /// <summary>Called after the routed-RPC layer has accepted a ready peer.</summary>
        public static void NotifyNewConnection(ZNetPeer peer)
        {
            EnsureRpcRegistered();
            if (!ReferenceEquals(_registeredRpc, ZRoutedRpc.instance)) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || peer == null) return;

            // Push our current values to the newly connected client.
            if (!TrySerialize(out string payload)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcName, payload);
        }

        private static void OnConfigReceived(long sender, string payload)
        {
            ZNet network = ZNet.instance;
            if (network == null || network.IsServer()) return; // servers never take client values
            ZNetPeer server = network.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_uid != sender) return;
            if (!TryDeserialize(payload)) return;
            Plugin.RebuildStandPrefabSet();
            Plugin.Log.LogInfo("Received admin config from server; local Stand Prefabs list updated.");
        }

        private static bool TrySerialize(out string payload)
        {
            // Very small format: "key=value|key=value"
            payload = string.Empty;
            if (_synced.Count == 0 || _synced.Count > MaximumEntries) return false;
            var parts = new List<string>(_synced.Count);
            foreach (var entry in _synced)
            {
                string key = entry.Definition.Key ?? string.Empty;
                string value = entry.BoxedValue?.ToString() ?? string.Empty;
                if (!IsBoundedField(key, MaximumKeyBytes) ||
                    !IsBoundedField(value, MaximumValueBytes) ||
                    key.IndexOfAny(new[] { '=', '|' }) >= 0 || value.IndexOf('|') >= 0)
                    return false;
                parts.Add(key + "=" + value);
            }
            payload = string.Join("|", parts);
            if (!IsBoundedField(payload, MaximumPayloadBytes))
            {
                payload = string.Empty;
                return false;
            }
            return true;
        }

        private static bool TryDeserialize(string payload)
        {
            if (!IsBoundedField(payload, MaximumPayloadBytes)) return false;
            string[] parts = payload.Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length == 0 || parts.Length > MaximumEntries) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in parts)
            {
                int separator = part.IndexOf('=');
                if (separator <= 0) return false;
                string key = part.Substring(0, separator);
                string value = part.Substring(separator + 1);
                if (!IsBoundedField(key, MaximumKeyBytes) ||
                    !IsBoundedField(value, MaximumValueBytes) ||
                    key.IndexOf('|') >= 0 || !values.TryAdd(key, value))
                    return false;
            }

            foreach (var entry in _synced)
            {
                if (values.TryGetValue(entry.Definition.Key, out string value))
                {
                    entry.BoxedValue = value;
                }
            }
            return true;
        }

        private static bool IsBoundedField(string value, int maximumBytes) =>
            value != null && Encoding.UTF8.GetByteCount(value) <= maximumBytes;
    }

    [HarmonyLib.HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class ZNet_Awake_ConfigSync_Patch
    {
        private static void Postfix()
        {
            ConfigSync.EnsureRpcRegistered();
        }
    }

    /// <summary>
    /// Fires ConfigSync's connection hook only after Valheim has assigned the peer UID and
    /// added it to ZRoutedRpc. ZNet.OnNewConnection is too early: its peer is not routeable yet.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.AddPeer))]
    internal static class ZRoutedRpc_AddPeer_ConfigSync_Patch
    {
        private static void Postfix(ZNetPeer peer)
        {
            ConfigSync.NotifyNewConnection(peer);
        }
    }
}
