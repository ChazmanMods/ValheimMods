using System.Collections.Generic;
using System.Linq;
using RunicCharacterVault.Shared;
using UnityEngine;

namespace RunicCharacterVault
{
    internal static class CharacterVaultRejection
    {
        internal const string MessageRpc = "RunicCharacterVault_Rejection_v1";
        internal const string AckRpc = "RunicCharacterVault_RejectionAck_v1";
        private const float DisconnectFallbackSeconds = 2f;
        private static readonly Dictionary<ZRpc, float> Deadlines =
            new Dictionary<ZRpc, float>();
        private static readonly HashSet<ZRpc> DisconnectRequested =
            new HashSet<ZRpc>();
        private static readonly HashSet<string> PermittedListRejections =
            new HashSet<string>();

        internal static void RegisterServer(ZRpc rpc)
        {
            rpc.Register(AckRpc, ReceiveAck);
        }

        internal static void RegisterClient(ZRpc rpc)
        {
            ClearClient();
            rpc.Register<ZPackage>(MessageRpc, ReceiveMessage);
        }

        internal static void Reject(ZRpc rpc, string message, string systemMessage = null)
        {
            CharacterVaultPlugin.Log.LogWarning(
                $"CharacterVault rejected {rpc.GetSocket().GetHostName()}: {message}");
            Deadlines[rpc] = Time.unscaledTime + DisconnectFallbackSeconds;
            ZPackage package = new ZPackage();
            package.Write(message);
            package.Write(systemMessage ?? message);
            rpc.Invoke(MessageRpc, package);
        }

        internal static void RecordPermittedListRejection(string hostName)
        {
            PermittedListRejections.Add(hostName);
        }

        internal static void SendPermittedListRejection(ZRpc rpc)
        {
            string hostName = rpc?.GetSocket()?.GetHostName();
            if (hostName != null && PermittedListRejections.Remove(hostName))
            {
                Reject(rpc, CharacterRejectionMessages.PermittedListDenied);
            }
        }

        internal static void SetClientMessage(string userMessage, string systemMessage = null)
        {
            ConnectionFailureMessages.Push(
                "RunicCharacterVault", userMessage, systemMessage);
        }

        internal static void ClearPendingClientMessage()
        {
            ClearClient();
        }

        internal static void Remove(ZRpc rpc)
        {
            Deadlines.Remove(rpc);
            DisconnectRequested.Remove(rpc);
        }

        internal static void Tick()
        {
            DisconnectExpired();
        }

        internal static void Clear()
        {
            Deadlines.Clear();
            DisconnectRequested.Clear();
            PermittedListRejections.Clear();
            ClearClient();
        }

        private static void ReceiveMessage(ZRpc rpc, ZPackage package)
        {
            string message = package.ReadString();
            string systemMessage = package.ReadString();
            ConnectionFailureMessages.Push(
                "RunicCharacterVault", message, systemMessage);
            CharacterVaultPlugin.Log.LogWarning(
                $"Server rejected the character: {message} System detail: {systemMessage}");
            rpc.Invoke(AckRpc);
            CharacterVaultPlugin.Log.LogDebug(
                "Acknowledged the CharacterVault rejection; waiting for the server disconnect.");
        }

        private static void ReceiveAck(ZRpc rpc)
        {
            if (Deadlines.ContainsKey(rpc))
            {
                RequestDisconnect(rpc);
            }
        }

        private static void DisconnectExpired()
        {
            ZRpc[] expired = Deadlines
                .Where(entry => Time.unscaledTime >= entry.Value)
                .Select(entry => entry.Key)
                .ToArray();
            foreach (ZRpc rpc in expired)
            {
                if (DisconnectRequested.Contains(rpc))
                {
                    ForceDisconnect(rpc);
                }
                else
                {
                    RequestDisconnect(rpc);
                }
            }
        }

        private static void RequestDisconnect(ZRpc rpc)
        {
            DisconnectRequested.Add(rpc);
            Deadlines[rpc] = Time.unscaledTime + DisconnectFallbackSeconds;
            CharacterVaultPlugin.Log.LogDebug(
                "Requesting rejected pre-spawn client disconnection.");
            rpc.Invoke("Disconnect");
        }

        private static void ForceDisconnect(ZRpc rpc)
        {
            Deadlines.Remove(rpc);
            DisconnectRequested.Remove(rpc);
            ZNetPeer peer = ZNet.instance?.GetPeers()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.m_rpc, rpc));
            if (peer != null)
            {
                CharacterVaultPlugin.Log.LogWarning(
                    "Rejected client did not disconnect; closing the server connection.");
                ZNet.instance.Disconnect(peer);
            }
        }

        private static void ClearClient()
        {
            ConnectionFailureMessages.Clear("RunicCharacterVault");
        }
    }
}
