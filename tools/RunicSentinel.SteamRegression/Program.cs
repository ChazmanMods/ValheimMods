using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RunicSentinel.Runtime;
using Steamworks;

internal static class Program
{
    private static int _passed;
    private static void Check(bool success, string name)
    { if (!success) throw new Exception(name); Console.WriteLine("PASS " + name); _passed++; }
    private static void Main()
    {
        var harmony = new Harmony("runic.sentinel.steam.regression");
        harmony.PatchAll(typeof(Program).Assembly);
        foreach (bool dedicated in new[] { false, true })
        foreach (int wrappers in new[] { 0, 1, 2 })
        {
            SentinelSteamSessions.Reset();
            var network = ZNet.instance = new ZNet { Dedicated = dedicated };
            var socket = new ZSteamSocket(42, 7);
            var peer = new ZNetPeer(socket); network.Peers.Add(peer);
            ISocket wrapper = socket;
            if (wrappers >= 1) wrapper = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(wrapper);
            if (wrappers >= 2) wrapper = new ConditionalConfigSync.ConditionalConfigSync.ZNetRpcPeerInfoSyncPatch.BufferingSocket(wrapper);
            // Config synchronization wraps RPC first. The native handshake still sees the raw
            // Steam peer socket; a nested buffering socket can remain on the peer afterward.
            peer.m_rpc = new ZRpc(wrapper);
            Native.Info = new SteamNetConnectionInfo_t {
                m_eState = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected,
                m_identityRemote = new SteamNetworkingIdentity { Id = 42 }, m_nFlags = 1 };
            Native.Accept = true; Native.InfoAvailable = true;
            var matchmaking = new ZSteamMatchmaking();
            matchmaking.VerifySessionTicket(new byte[] { 1 }, new CSteamID(42)); peer.Ready = true;
            peer.m_socket = wrapper;
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _, out string pending) && pending.Contains("pending"), "native acceptance alone waits for Steam callback");
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t { m_SteamID = new CSteamID(42) }, !dedicated);
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "wrong callback backend ignored");
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t { m_SteamID = new CSteamID(42) }, dedicated);
            Check(SentinelTransportIdentity.TryResolvePeer(peer, out string authority, out string subject) && authority == "steam" && subject == "42", "validated ticket works without transport certificate");
            Native.Info.m_identityRemote = new SteamNetworkingIdentity { Id = 43 };
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "ticket cannot override mismatched transport identity");
            Native.Info.m_identityRemote = new SteamNetworkingIdentity { Id = 42 };
            Native.InfoAvailable = false;
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "ticket cannot override missing connection info");
            Native.InfoAvailable = true;
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t {
                m_SteamID = new CSteamID(42), m_eAuthSessionResponse = EAuthSessionResponse.Rejected }, dedicated);
            Native.Info.m_nFlags = 0;
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "revocation denies even a certified transport");
            socket.Close();
            Native.Info.m_nFlags = 1;
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t { m_SteamID = new CSteamID(42) }, dedicated);
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "closed socket cannot regain ticket proof");

            var replacement = new ZSteamSocket(42, 7); peer = new ZNetPeer(replacement);
            network.Peers.Clear(); network.Peers.Add(peer); Native.Accept = false;
            Check(!matchmaking.VerifySessionTicket(new byte[] { 2 }, new CSteamID(42)), "native rejection remains unchanged");
            peer.Ready = true;
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t { m_SteamID = new CSteamID(42) }, dedicated);
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "failed native ticket cannot be approved by a callback");
            replacement.Close(); network.Peers.Clear();
            peer = new ZNetPeer(new ZSteamSocket(42, 9)); network.Peers.Add(peer); Native.Accept = true;
            matchmaking.VerifySessionTicket(new byte[] { 3 }, new CSteamID(42)); peer.Ready = true;
            Callback<ValidateAuthTicketResponse_t>.Raise(new ValidateAuthTicketResponse_t { m_SteamID = new CSteamID(42) }, dedicated);
            Check(SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "fresh ticket after reconnect works");
            if (dedicated) SteamGameServer.EndAuthSession(new CSteamID(42)); else SteamUser.EndAuthSession(new CSteamID(42));
            Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _), "native EndAuthSession revokes proof");
            network.OnDestroy();
            Check(Callback<ValidateAuthTicketResponse_t>.Count == 0, "world shutdown disposes callback");
        }
        WrapperBoundaries();
        SentinelSteamSessions.Reset(); harmony.UnpatchSelf();
        Console.WriteLine(_passed + " HarmonyX integration checks passed (controlled Steam/game seams, not a live server).");
    }

    private static void WrapperBoundaries()
    {
        var native = new ZSteamSocket(42, 3);
        ISocket socket = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(native);
        var outer = new ConditionalConfigSync.ConditionalConfigSync.ZNetRpcPeerInfoSyncPatch.BufferingSocket(socket);
        var peer = new ZNetPeer(outer) { Ready = true, m_rpc = new ZRpc(socket) };
        Check(SentinelSocketTransport.TryResolve(peer, out ISocket result, out _) && ReferenceEquals(native, result), "mixed nested wrappers resolve to the exact native socket");
        Check(ReferenceEquals(peer.m_socket, outer) && ReferenceEquals(peer.m_rpc.GetSocket(), socket) && ReferenceEquals(outer.Original, socket), "resolution leaves peer RPC and queue wrappers untouched");
        peer.m_rpc = new ZRpc(new ZSteamSocket(42, 3));
        Check(!SentinelSocketTransport.TryResolve(peer, out _, out string mismatch) && mismatch == "peer-rpc-transport-mismatch", "matching account and handle cannot substitute another socket object");
        var playfab = new ZPlayFabSocket { m_remotePlayerId = "real-entity" };
        var deceptive = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(native) { m_remotePlayerId = "fake-entity" };
        peer = new ZNetPeer(deceptive) { Ready = true };
        Native.Info.m_nFlags = 0;
        Check(SentinelTransportIdentity.TryResolvePeer(peer, out string backend, out string id) && backend == "steam" && id == "42", "PlayFab-derived wrapper cannot misclassify underlying Steam or inject an entity");
        peer = new ZNetPeer(new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(playfab)) { Ready = true };
        Check(SentinelTransportIdentity.TryResolvePeer(peer, out backend, out id) && backend == "playfab.entity" && id == "real-entity", "wrapped real PlayFab keeps its native entity identity");
        peer = new ZNetPeer(new UnknownWrapper(native)) { Ready = true };
        Check(!SentinelTransportIdentity.TryResolvePeer(peer, out _, out _, out string unknown) && unknown.Contains("UnknownWrapper"), "unknown wrapper denied with its type name");
        Check(!SentinelSocketTransport.TryUnwrap(new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(null), out _, out _), "null original denied");
        var cycle = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(native);
        typeof(ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket).GetField("Original").SetValue(cycle, cycle);
        Check(!SentinelSocketTransport.TryUnwrap(cycle, out _, out string cyclic) && cyclic == "socket-wrapper-cycle", "wrapper cycle rejected");
        socket = native;
        for (int i = 0; i < 32; i++) socket = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(socket);
        Check(SentinelSocketTransport.TryUnwrap(socket, out _, out _), "32 bounded wrapper levels supported");
        socket = new ServerSync.ConfigSync.SendConfigsAfterLogin.BufferingSocket(socket);
        Check(!SentinelSocketTransport.TryUnwrap(socket, out _, out _), "excessive nesting rejected");
        peer = new ZNetPeer(native) { m_rpc = null };
        Check(!SentinelSocketTransport.TryResolve(peer, out _, out _), "missing RPC rejected");
    }
}

// Controlled API seams. The linked production observer/resolver/registry and real installed
// HarmonyX execute unchanged. No Steam login, game launch, server, saves or permissions are touched.
public interface ISocket { string GetHostName(); }
public class ZRpc { private readonly ISocket _socket; public ZRpc(ISocket socket) { _socket = socket; } public ISocket GetSocket() => _socket; }
public class ZNetPeer
{
    public ISocket m_socket; public ZRpc m_rpc; public bool Ready;
    public ZNetPeer(ISocket socket) { m_socket = socket; m_rpc = new ZRpc(socket); }
    public bool IsReady() => Ready;
}
public class ZNet
{
    public static ZNet instance; public bool Dedicated; public List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsServer() => true; public bool IsDedicated() => Dedicated; public List<ZNetPeer> GetPeers() => Peers;
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { }
}
public class ZSteamSocket : ISocket
{
    private HSteamNetConnection m_con; private readonly ulong _id;
    public ZSteamSocket(ulong id, uint handle) { _id = id; m_con = new HSteamNetConnection { m_HSteamNetConnection = handle }; }
    public string GetHostName() => _id.ToString(); public CSteamID GetPeerID() => new CSteamID(_id);
    [MethodImpl(MethodImplOptions.NoInlining)] public void Close() { m_con = HSteamNetConnection.Invalid; }
}
public class ZPlayFabSocket : ISocket { public string m_remotePlayerId; public string GetHostName() => "playfab"; }
public class UnknownWrapper : ZPlayFabSocket { public readonly ISocket Original; public UnknownWrapper(ISocket socket) { Original = socket; } }
public class ZSteamMatchmaking
{
    [MethodImpl(MethodImplOptions.NoInlining)] public bool VerifySessionTicket(byte[] ticket, CSteamID steamID) => Native.Accept;
}
internal static class Native { internal static bool Accept, InfoAvailable; internal static SteamNetConnectionInfo_t Info; }
namespace ServerSync
{
    public class ConfigSync
    {
        public class SendConfigsAfterLogin
        {
            public class BufferingSocket : ZPlayFabSocket, ISocket
            {
                public readonly ISocket Original;
                public BufferingSocket(ISocket socket) { Original = socket; }
                public new string GetHostName() => Original.GetHostName();
            }
        }
    }
}
namespace ConditionalConfigSync
{
    public class ConditionalConfigSync
    {
        public class ZNetRpcPeerInfoSyncPatch
        {
            public class BufferingSocket : ZPlayFabSocket, ISocket
            {
                public readonly ISocket Original;
                public BufferingSocket(ISocket socket) { Original = socket; }
                public new string GetHostName() => Original.GetHostName();
            }
        }
    }
}
namespace Steamworks
{
    public struct CSteamID { public ulong m_SteamID; public CSteamID(ulong id) { m_SteamID = id; } public override string ToString() => m_SteamID.ToString(); }
    public struct HSteamNetConnection
    {
        public uint m_HSteamNetConnection; public static HSteamNetConnection Invalid => default;
        public static bool operator ==(HSteamNetConnection a, HSteamNetConnection b) => a.m_HSteamNetConnection == b.m_HSteamNetConnection;
        public static bool operator !=(HSteamNetConnection a, HSteamNetConnection b) => !(a == b);
        public override bool Equals(object obj) => obj is HSteamNetConnection value && this == value;
        public override int GetHashCode() => (int)m_HSteamNetConnection;
    }
    public struct SteamNetworkingIdentity { public ulong Id; public CSteamID GetSteamID() => new CSteamID(Id); }
    public enum ESteamNetworkingConnectionState { k_ESteamNetworkingConnectionState_Connected }
    public enum EAuthSessionResponse { k_EAuthSessionResponseOK, Rejected }
    public struct SteamNetConnectionInfo_t { public SteamNetworkingIdentity m_identityRemote; public int m_nFlags; public ESteamNetworkingConnectionState m_eState; }
    public struct ValidateAuthTicketResponse_t { public CSteamID m_SteamID; public EAuthSessionResponse m_eAuthSessionResponse; }
    public class Callback<T> : IDisposable
    {
        private static readonly List<Callback<T>> All = new List<Callback<T>>();
        private Action<T> _action; private bool _dedicated;
        public static int Count => All.Count;
        public static Callback<T> Create(Action<T> action) => Add(action, false);
        public static Callback<T> CreateGameServer(Action<T> action) => Add(action, true);
        private static Callback<T> Add(Action<T> action, bool dedicated) { var callback = new Callback<T> { _action = action, _dedicated = dedicated }; All.Add(callback); return callback; }
        public static void Raise(T data, bool dedicated) { foreach (var callback in All.ToArray()) if (callback._dedicated == dedicated) callback._action(data); }
        public void Dispose() => All.Remove(this);
    }
    public static class SteamNetworkingSockets { public static bool GetConnectionInfo(HSteamNetConnection handle, out SteamNetConnectionInfo_t info) { info = Native.Info; return Native.InfoAvailable; } }
    public static class SteamGameServerNetworkingSockets { public static bool GetConnectionInfo(HSteamNetConnection handle, out SteamNetConnectionInfo_t info) { info = Native.Info; return Native.InfoAvailable; } }
    public static class SteamGameServer { [MethodImpl(MethodImplOptions.NoInlining)] public static void EndAuthSession(CSteamID subject) { } }
    public static class SteamUser
    {
        public static CSteamID GetSteamID() => new CSteamID(42);
        [MethodImpl(MethodImplOptions.NoInlining)] public static void EndAuthSession(CSteamID subject) { }
    }
}
