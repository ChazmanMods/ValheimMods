using System;

namespace Runic.Foundation.Persistence
{
    internal delegate bool RpcRuntimeIdentityResolver(
        ZNetPeer peer,
        string sessionId,
        bool localIsServer,
        out RpcPeerIdentity identity);

    internal delegate bool RpcRuntimeActorResolver(
        ZNetPeer peer,
        long peerId,
        out ZDOID characterId,
        out long claimedPlayerId,
        out string reasonCode);

    /// <summary>
    /// Internal-only Valheim boundary. Production delegates to the live game; tests inject an
    /// independent role, registry, clock, disconnect path, identity, actor, and PeerInfo resume.
    /// </summary>
    internal sealed class RunicRpcRuntimeContext
    {
        private readonly Func<bool> _isServer;
        private readonly Func<long> _clock;
        private readonly Action<ZNetPeer> _disconnect;
        private readonly Action<ZRpc, string> _resumePeerInfo;
        private readonly RpcRuntimeIdentityResolver _identity;
        private readonly RpcRuntimeActorResolver _actor;
        private readonly Func<ZNetPeer, bool> _isAdmin;

        internal RunicRpcRuntimeContext(
            Func<bool> isServer,
            Func<long> clock,
            Action<ZNetPeer> disconnect,
            Action<ZRpc, string> resumePeerInfo,
            RpcRuntimeIdentityResolver identity = null,
            RpcRuntimeActorResolver actor = null,
            Func<ZNetPeer, bool> isAdmin = null)
        {
            _isServer = isServer ?? throw new ArgumentNullException(nameof(isServer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
            _resumePeerInfo = resumePeerInfo ?? throw new ArgumentNullException(nameof(resumePeerInfo));
            _identity = identity;
            _actor = actor;
            _isAdmin = isAdmin;
        }

        internal static RunicRpcRuntimeContext Production { get; } =
            new RunicRpcRuntimeContext(
                () => ZNet.instance != null && ZNet.instance.IsServer(),
                () => DateTime.UtcNow.Ticks,
                peer =>
                {
                    if (ZNet.instance == null) throw new InvalidOperationException("ZNet is unavailable.");
                    ZNet.instance.Disconnect(peer);
                },
                (rpc, password) =>
                {
                    if (ZNet.instance == null)
                        throw new InvalidOperationException("ZNet is unavailable.");
                    var method = HarmonyLib.AccessTools.Method(
                        typeof(ZNet),
                        "SendPeerInfo",
                        new[] { typeof(ZRpc), typeof(string) });
                    if (method == null) throw new MissingMethodException("ZNet.SendPeerInfo");
                    method.Invoke(ZNet.instance, new object[] { rpc, password });
                });

        internal bool IsServer => _isServer();
        internal long UtcNowTicks => _clock();
        internal void Disconnect(ZNetPeer peer) => _disconnect(peer);
        internal void ResumePeerInfo(ZRpc rpc, string password) => _resumePeerInfo(rpc, password);

        internal bool TryResolveIdentity(
            ZNetPeer peer,
            string sessionId,
            bool localIsServer,
            out RpcPeerIdentity identity)
        {
            identity = null;
            return _identity != null && _identity(peer, sessionId, localIsServer, out identity);
        }

        internal bool TryResolveActor(
            ZNetPeer peer,
            long peerId,
            out ZDOID characterId,
            out long claimedPlayerId,
            out string reasonCode)
        {
            characterId = ZDOID.None;
            claimedPlayerId = 0;
            reasonCode = string.Empty;
            return _actor != null &&
                   _actor(peer, peerId, out characterId, out claimedPlayerId, out reasonCode);
        }

        internal bool TryIsAdmin(ZNetPeer peer, out bool isAdmin)
        {
            isAdmin = false;
            if (_isAdmin == null) return false;
            try { isAdmin = _isAdmin(peer); }
            catch { isAdmin = false; }
            return true;
        }
    }
}
