using System;
using System.Collections.Generic;
using RunicSentinel.Runtime;

// Execute the production administrator resolver against controlled game/config seams.
// Native socket authentication is covered separately; subjects here represent its output.
internal static class Program
{
    private const string Account = "76561198797291314";
    private static int Main()
    {
        int passed = 0;
        void Check(string name, bool actual, bool expected)
        {
            if (actual != expected) throw new Exception(name);
            System.Console.WriteLine("PASS " + name);
            passed++;
        }
        var network = new ZNet();
        var peer = new ZNetPeer();
        bool Access(string authority = "steam", string subject = Account) =>
            SentinelServerAdministrator.IsAdministrator(network, peer, authority, subject);
        Check("unknown account denied", Access(), false);
        foreach (string prefix in new[] { "", "Steam_", "V_" })
        {
            network.m_adminList.Entries.Add(prefix + Account);
            Check(prefix + "authenticated Steam administrator accepted", Access(), true);
            Check(prefix + "different authenticated account denied", Access("steam", "76561198797291315"), false);
            Check(prefix + "wrong backend cannot borrow Steam grant", Access("playfab.entity"), false);
            network.m_adminList.Entries.Clear();
            Check(prefix + "revocation takes effect at next list read", Access(), false);
        }
        network.NativeAdministrator = true;
        Check("native backend administrator accepted", Access("playfab.entity", "authenticated-entity"), true);
        Check("native lookup uses socket hostname", network.LastHost == peer.m_socket.GetHostName(), true);
        peer.Ready = false;
        Check("not-ready peer denied", Access(), false);
        peer.Ready = true;
        peer.m_rpc = null;
        Check("missing RPC denied", Access(), false);
        peer.m_rpc = new object();
        var socket = peer.m_socket;
        peer.m_socket = null;
        Check("missing socket denied", Access(), false);
        peer.m_socket = socket;
        network.Server = false;
        Check("client process cannot confer server rights", Access(), false);
        network.Server = true;
        RunicSentinel.SentinelConfig.UseServerAdminList.Value = false;
        Check("inheritance opt-out denies native administrator", Access(), false);
        Check("inheritance opt-out denies local-host inheritance",
            SentinelServerAdministrator.IsAdministrator(network, null, "steam", Account), false);
        RunicSentinel.SentinelConfig.UseServerAdminList.Value = true;
        network.ThrowNative = true;
        Check("native resolver exception denies access", Access(), false);
        network.ThrowNative = false;
        network.NativeAdministrator = false;
        Check("dedicated process does not get local-host rights",
            SentinelServerAdministrator.IsAdministrator(network, null, "steam", Account), false);
        network.Dedicated = false;
        Check("local listen host inherits rights",
            SentinelServerAdministrator.IsAdministrator(network, null, "steam", Account), true);
        UnityEngine.Application.isBatchMode = true;
        Check("batch-mode process cannot use local-host shortcut",
            SentinelServerAdministrator.IsAdministrator(network, null, "steam", Account), false);
        Check("missing network denied",
            SentinelServerAdministrator.IsAdministrator(null, peer, "steam", Account), false);
        System.Console.WriteLine(passed + " administrator resolver checks passed.");
        return 0;
    }
}

// Minimal seams, not copies of Valheim authorization logic.
internal sealed class SyncedList
{
    internal readonly HashSet<string> Entries = new HashSet<string>(StringComparer.Ordinal);
    public bool Contains(string id) => Entries.Contains(id);
}
internal sealed class ZNet
{
    internal SyncedList m_adminList = new SyncedList();
    internal bool Server = true, Dedicated = true, NativeAdministrator, ThrowNative;
    internal string LastHost;
    public bool IsServer() => Server;
    public bool IsDedicated() => Dedicated;
    public bool IsAdmin(string host)
    {
        if (ThrowNative) throw new InvalidOperationException();
        LastHost = host;
        return NativeAdministrator;
    }
}
internal sealed class ZNetPeer
{
    internal bool Ready = true;
    internal object m_rpc = new object();
    internal TestSocket m_socket = new TestSocket();
    public bool IsReady() => Ready;
}
internal sealed class TestSocket
{
    public string GetHostName() => "server-authenticated-socket-host";
}
namespace RunicSentinel
{
    internal static class SentinelConfig
    {
        internal static BooleanSetting UseServerAdminList = new BooleanSetting();
    }
    internal sealed class BooleanSetting { internal bool Value = true; }
}
namespace UnityEngine
{
    internal static class Application { internal static bool isBatchMode; }
}
