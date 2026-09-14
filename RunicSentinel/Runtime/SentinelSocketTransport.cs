using System;
using System.Collections.Generic;
using System.Reflection;

namespace RunicSentinel.Runtime
{
    internal static class SentinelSocketTransport
    {
        private const int MaximumWrappers = 32;

        internal static bool TryResolve(ZNetPeer peer, out ISocket transport, out string reason)
        {
            transport = null;
            reason = "missing-peer-or-rpc-socket";
            if (peer?.m_socket == null || peer.m_rpc == null) return false;
            try
            {
                if (!TryUnwrap(peer.m_socket, out ISocket fromPeer, out reason) ||
                    !TryUnwrap(peer.m_rpc.GetSocket(), out ISocket fromRpc, out reason)) return false;
                if (!ReferenceEquals(fromPeer, fromRpc))
                { reason = "peer-rpc-transport-mismatch"; return false; }
                transport = fromPeer;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            { reason = "socket-resolution-error:" + exception.GetType().Name; return false; }
        }

        internal static bool TryUnwrap(ISocket socket, out ISocket transport, out string reason)
        {
            transport = null;
            var seen = new List<ISocket>();
            for (int depth = 0; depth <= MaximumWrappers; depth++)
            {
                reason = "missing-inner-socket";
                if (socket == null) return false;
                foreach (ISocket previous in seen)
                    if (ReferenceEquals(previous, socket))
                    { reason = "socket-wrapper-cycle"; return false; }
                seen.Add(socket);
                Type type = socket.GetType();
                // Check exact native types, not 'is ZPlayFabSocket': the synchronization wrappers
                // inherit ZPlayFabSocket but may carry a Steam connection in Original.
                if (type == typeof(ZSteamSocket) || type == typeof(ZPlayFabSocket))
                { transport = socket; reason = string.Empty; return true; }
                if (depth == MaximumWrappers) break;
                if (type.FullName != "ServerSync.ConfigSync+SendConfigsAfterLogin+BufferingSocket" &&
                    type.FullName != "ConditionalConfigSync.ConditionalConfigSync+ZNetRpcPeerInfoSyncPatch+BufferingSocket")
                {
                    string name = type.FullName ?? type.Name;
                    reason = "unsupported-socket:" + (name.Length <= 180 ? name : name.Substring(0, 180));
                    return false;
                }
                FieldInfo original = type.GetField("Original", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (original == null || !original.IsInitOnly || original.FieldType != typeof(ISocket))
                { reason = "unsupported-buffering-socket-layout"; return false; }
                // Read the audited backing field only. Do not invoke arbitrary wrapper getters,
                // rewrite either socket, flush queues, or use the wrapper's identity as evidence.
                socket = original.GetValue(socket) as ISocket;
            }
            reason = "socket-wrapper-depth-exceeded";
            return false;
        }
    }
}
