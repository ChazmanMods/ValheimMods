using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class ServerFinalSaveMonitor
    {
        private const float TimeoutSeconds = 10f;
        private readonly Dictionary<ZRpc, PendingDisconnect> _pending =
            new Dictionary<ZRpc, PendingDisconnect>();
        private readonly HashSet<ZRpc> _receivedFinalSaves = new HashSet<ZRpc>();
        private readonly HashSet<ZRpc> _warned = new HashSet<ZRpc>();

        internal void Observe(ZRpc rpc, string playerName, bool connected)
        {
            if (rpc == null)
            {
                return;
            }
            if (connected)
            {
                _pending.Remove(rpc);
                _warned.Remove(rpc);
                return;
            }
            if (!_receivedFinalSaves.Contains(rpc) && !_pending.ContainsKey(rpc) &&
                !_warned.Contains(rpc))
            {
                _pending[rpc] = new PendingDisconnect(playerName,
                    Time.realtimeSinceStartup + TimeoutSeconds);
            }
        }

        internal void RecordSaveReceived(ZRpc rpc, string requestId)
        {
            if (!IsFinalDisconnectRequest(requestId))
            {
                return;
            }
            _receivedFinalSaves.Add(rpc);
            _pending.Remove(rpc);
            _warned.Remove(rpc);
        }

        private static bool IsFinalDisconnectRequest(string requestId) =>
            requestId?.StartsWith("disconnect-", StringComparison.Ordinal) == true ||
            requestId?.StartsWith("server-disconnect-",
                StringComparison.Ordinal) == true;

        internal void Update()
        {
            foreach (ZRpc rpc in new List<ZRpc>(_pending.Keys))
            {
                PendingDisconnect pending = _pending[rpc];
                if (Time.realtimeSinceStartup < pending.Deadline)
                {
                    continue;
                }
                _pending.Remove(rpc);
                _warned.Add(rpc);
                CharacterVaultPlugin.Log.LogWarning(
                    $"No final character save was received from {pending.PlayerName} " +
                    $"within {TimeoutSeconds:0} seconds after the connection was lost.");
            }
        }

        internal void RecordRemoved(ZRpc rpc, string playerName)
        {
            Observe(rpc, playerName, false);
            _receivedFinalSaves.Remove(rpc);
        }

        internal void Clear()
        {
            _pending.Clear();
            _receivedFinalSaves.Clear();
            _warned.Clear();
        }

        private sealed class PendingDisconnect
        {
            internal PendingDisconnect(string playerName, float deadline)
            {
                PlayerName = playerName;
                Deadline = deadline;
            }

            internal float Deadline { get; }
            internal string PlayerName { get; }
        }
    }
}
