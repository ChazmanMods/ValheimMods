using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class ServerDisconnectSaveCoordinator : IDisposable
    {
        private const float ConfirmationTimeoutSeconds = 30f;
        private readonly Dictionary<string, PendingServerSave> _pending =
            new Dictionary<string, PendingServerSave>(StringComparer.Ordinal);
        private readonly HashSet<ZRpc> _authorizedDisconnects = new HashSet<ZRpc>();

        internal bool AllowKick(ZNet network, ZNetPeer peer)
        {
            bool validPeer = network?.IsServer() == true && peer?.m_rpc != null;
            bool authorized = validPeer && _authorizedDisconnects.Remove(peer.m_rpc);
            bool pending = validPeer && HasPendingRequest(peer.m_rpc);
            KickSaveEligibility eligibility = validPeer
                ? CharacterVaultPlugin.Transfers?.GetKickSaveEligibility(peer) ??
                    KickSaveEligibility.Unmanaged
                : KickSaveEligibility.Unmanaged;
            KickAction action = KickSavePolicy.Decide(validPeer, authorized, pending, eligibility);
            if (TryResolveWithoutSave(action, authorized, peer, out bool allow))
            {
                return allow;
            }

            IKickSaveRequest request = new KickSaveRequestOperation(() =>
            {
                bool started = TryRequest(peer, "server kick",
                    (requestId, saved) => CompleteKick(network, peer, requestId, saved),
                    out string requestId);
                return new KickSaveRequestResult(started, requestId);
            });
            KickSaveRequestResult result = KickSaveRequestExecutor.Execute(action, request);
            if (!result.Started)
            {
                CharacterVaultPlugin.Log.LogWarning(
                    $"A final save could not be requested for {peer.m_playerName}; allowing the kick without it.");
                return true;
            }

            CharacterVaultPlugin.Log.LogMessage(
                $"Delayed kick for {peer.m_playerName} until final save {result.RequestId} is committed.");
            return false;
        }

        private static bool TryResolveWithoutSave(KickAction action, bool authorized,
            ZNetPeer peer, out bool allow)
        {
            allow = action == KickAction.Allow || action == KickAction.AllowWithoutSave;
            if (action == KickAction.Allow && authorized)
            {
                CharacterVaultPlugin.Log.LogInfo(
                    $"Allowing kick for {peer.m_playerName} after its confirmed final save.");
            }
            else if (action == KickAction.AllowWithoutSave)
            {
                CharacterVaultPlugin.Log.LogInfo(
                    $"Allowing kick for {peer.m_playerName} without a character save.");
            }
            else if (action == KickAction.WaitForPendingSave)
            {
                CharacterVaultPlugin.Log.LogWarning(
                    $"Ignored another kick for {peer.m_playerName} while its final save is pending.");
            }
            return action != KickAction.RequestSave;
        }

        internal bool TryRequest(ZNetPeer peer, string reason,
            Action<string, bool> completed, out string requestId)
        {
            requestId = null;
            if (peer?.m_rpc == null || completed == null ||
                ZNet.instance?.IsServer() != true || !peer.IsReady() ||
                CharacterVaultPlugin.Transfers?.CanRequestSave(peer) != true)
            {
                return false;
            }

            requestId = "server-disconnect-" + Guid.NewGuid().ToString("N");
            _pending[requestId] = new PendingServerSave(peer.m_rpc, peer.m_playerName,
                reason, completed);
            CharacterVaultPlugin.Log.LogMessage(
                $"Requesting final save {requestId} for {peer.m_playerName} before {reason}.");
            CharacterVaultPlugin.Transfers.RequestSave(peer, requestId);
            CharacterVaultPlugin.Instance.Run(WaitForConfirmation(requestId));
            return true;
        }

        internal void RecordCommitted(ZRpc rpc, string requestId)
        {
            if (!_pending.TryGetValue(requestId, out PendingServerSave save) || save.Rpc != rpc)
            {
                return;
            }

            _pending.Remove(requestId);
            _authorizedDisconnects.Add(rpc);
            CharacterVaultPlugin.Log.LogMessage(
                $"Final save {requestId} for {save.PlayerName} committed; " +
                $"authorizing {save.Reason}.");
            save.Completed(requestId, true);
        }

        internal void RecordDisconnected(ZRpc rpc)
        {
            _authorizedDisconnects.Remove(rpc);
            foreach (string requestId in RequestsFor(rpc))
            {
                CompleteFailed(requestId, "the connection closed before confirmation");
            }
        }

        internal bool HasPendingSave(ZRpc rpc)
        {
            return rpc != null && HasPendingRequest(rpc);
        }

        public void Dispose()
        {
            _authorizedDisconnects.Clear();
            foreach (string requestId in new List<string>(_pending.Keys))
            {
                CompleteFailed(requestId, "CharacterVault unloaded before confirmation");
            }
        }

        private void CompleteKick(ZNet network, ZNetPeer peer, string requestId, bool saved)
        {
            if (!saved)
            {
                CharacterVaultPlugin.Log.LogWarning(
                    $"Final save {requestId} for {peer.m_playerName} was not confirmed; proceeding with the kick.");
            }
            else
            {
                CharacterVaultPlugin.Log.LogMessage(
                    $"Replaying kick for {peer.m_playerName} after save {requestId}.");
            }
            ZRpc rpc = peer?.m_rpc;
            if (rpc == null || peer.m_socket == null) return;
            _authorizedDisconnects.Add(rpc);
            try
            {
                network.Kick(peer.m_socket.GetHostName());
            }
            finally
            {
                _authorizedDisconnects.Remove(rpc);
            }
        }

        private bool HasPendingRequest(ZRpc rpc)
        {
            foreach (PendingServerSave save in _pending.Values)
            {
                if (save.Rpc == rpc)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator WaitForConfirmation(string requestId)
        {
            float deadline = Time.realtimeSinceStartup + ConfirmationTimeoutSeconds;
            while (_pending.ContainsKey(requestId) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (_pending.ContainsKey(requestId))
            {
                CompleteFailed(requestId,
                    $"no commit acknowledgement arrived within {ConfirmationTimeoutSeconds:0} seconds");
            }
        }

        private void CompleteFailed(string requestId, string reason)
        {
            if (!_pending.TryGetValue(requestId, out PendingServerSave save))
            {
                return;
            }

            _pending.Remove(requestId);
            CharacterVaultPlugin.Log.LogWarning(
                $"Final save {requestId} for {save.PlayerName} was abandoned: {reason}; {save.Reason} will continue.");
            save.Completed(requestId, false);
        }

        private List<string> RequestsFor(ZRpc rpc)
        {
            List<string> requests = new List<string>();
            foreach (KeyValuePair<string, PendingServerSave> pair in _pending)
            {
                if (pair.Value.Rpc == rpc)
                {
                    requests.Add(pair.Key);
                }
            }

            return requests;
        }
    }

    internal sealed class KickSaveRequestOperation : IKickSaveRequest
    {
        private readonly Func<KickSaveRequestResult> _request;

        internal KickSaveRequestOperation(Func<KickSaveRequestResult> request)
        {
            _request = request;
        }

        public KickSaveRequestResult Request()
        {
            return _request();
        }
    }

    internal sealed class PendingServerSave
    {
        internal PendingServerSave(ZRpc rpc, string playerName, string reason,
            Action<string, bool> completed)
        {
            Rpc = rpc;
            PlayerName = playerName;
            Reason = reason;
            Completed = completed;
        }

        internal Action<string, bool> Completed { get; }
        internal string PlayerName { get; }
        internal string Reason { get; }
        internal ZRpc Rpc { get; }
    }
}
