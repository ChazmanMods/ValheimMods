using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RunicAutomation.Operations
{
    internal static class InventoryOperationNetwork
    {
        private const string HelloRpc = "RunicOperations_Hello_v2";
        private const string ReadyRpc = "RunicOperations_Ready_v2";
        private const string RequestRpc = "RunicOperations_Request_v2";
        private const string ResultRpc = "RunicOperations_Result_v2";
        private const string QueryKind = "storage.query.v2";
        private const string ViewerKind = "storage.viewer.v2";
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.network.v2", () => new Network().Routes());
        // Registers the caller's build identity (module version id) and its embedded core hash.
        // Both run in the calling plugin's assembly even when another plugin owns the routes.
        internal static void RegisterModule(string module)
        {
            EmbeddedCoreAgreement.Register(module);
            ((Action<string, string>)Routes["module"])(module, Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToString("N"));
        }
        internal static void Tick() => ((Action)Routes["tick"])();
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static bool Ready => ((Func<bool>)Routes["ready"])();
        internal static bool Installed => ((Func<bool>)Routes["installed"])();
        internal static bool Compatible(long peer) => ((Func<long, bool>)Routes["compatible"])(peer);
        internal static bool Send(string world, string id, string kind, byte[] payload, Action<int, string, byte[]> completed) =>
            ((Func<string, string, string, byte[], Action<int, string, byte[]>, bool>)Routes["send"])(world, id, kind, payload, completed);
        internal static bool CancelQuery(string id) => ((Func<string, bool>)Routes["cancel-query"])(id);
        private static bool ReadOnlyKind(string kind) => kind == QueryKind || kind == ViewerKind;
        private static bool BackgroundKind(string kind) => kind == "production.clock.v2" || kind == "production.recipe.v2" ||
            kind == "drawer.retrieve.v1" || kind == "inventory.animal-consume.v1";
        private static int Priority(string kind) => kind == QueryKind ? 0 : kind == ViewerKind ? 3 : BackgroundKind(kind) ? 2 : 1;

        private sealed class Pending
        {
            internal string World, Id, Kind;
            internal byte[] Payload;
            internal float Retry, Created, NextBusyWarning;
            internal Action<int, string, byte[]> Completed;
        }
        private sealed class Network
        {
            private readonly Dictionary<string, string> modules = new Dictionary<string, string>();
            private readonly HashSet<long> compatible = new HashSet<long>();

            private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
            private readonly Dictionary<long, Tuple<float, int>> rates = new Dictionary<long, Tuple<float, int>>();
            private readonly Dictionary<long, Tuple<float, int>> queryRates = new Dictionary<long, Tuple<float, int>>();
            private readonly Dictionary<long, float> actorWarnings = new Dictionary<long, float>();
            private ZRoutedRpc registered;
            private bool ready;
            private float nextHello, nextCleanup;
            private int frame = -1;
            // Every module embeds the authority; none needs another Runic plugin to start it.
            private bool Complete => modules.Count > 0;
            private string BuildStamp => OperationRequest.Hash(string.Join(";", modules.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value)));
            private string mismatch = "";
            private string World => ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);

            internal Dictionary<string, Delegate> Routes() => new Dictionary<string, Delegate>
            {
                ["module"] = new Action<string, string>((module, build) => modules[module] = build),
                ["tick"] = new Action(Tick),
                ["clear"] = new Action(Clear),
                ["ready"] = new Func<bool>(() => ready && Complete),
                ["installed"] = new Func<bool>(() => modules.Count > 0),
                ["compatible"] = new Func<long, bool>(peer => peer == ZDOMan.GetSessionID() || compatible.Contains(peer)),
                ["send"] = new Func<string, string, string, byte[], Action<int, string, byte[]>, bool>(Send),
                ["cancel-query"] = new Func<string, bool>(CancelQuery)
            };

            private void Clear()
            {
                // Clear can run several times while the same native RPC instance is alive.
                // Native Register uses Dictionary.Add; keep the instance identity until replaced.
                ready = false; frame = -1; nextHello = 0; nextCleanup = 0; mismatch = "";
                compatible.Clear(); rates.Clear(); queryRates.Clear(); actorWarnings.Clear(); pending.Clear(); SharedOperationHost.Stop();
                // Module registrations survive a world switch; request and authority state do not.
            }
            private void Tick()
            {
                if (!Complete || ZNet.instance == null || ZRoutedRpc.instance == null) { ready = false; return; }
                // Mixed embedded cores never negotiate; every module stays on native handling.
                if (!EmbeddedCoreAgreement.Verify()) { ready = false; return; }
                // Connecting clients have a ZNet before PeerInfo creates its World. The
                // native GetWorldUID method dereferences that world, so it is not a guard.
                var context = ZNet.instance.GetWorld();
                if (context == null || context.m_uid == 0) { ready = false; return; }
                if (Time.frameCount == frame) return;
                frame = Time.frameCount;
                if (!ReferenceEquals(registered, ZRoutedRpc.instance))
                {
                    registered = ZRoutedRpc.instance; ready = false;
                    registered.Register<ZPackage>(HelloRpc, Hello);
                    registered.Register<ZPackage>(ReadyRpc, ReadyReply);
                    registered.Register<ZPackage>(RequestRpc, Request);
                    registered.Register<ZPackage>(ResultRpc, Result);
                }
                if (ZNet.instance.IsServer())
                {
                    if (!SharedOperationHost.Active)
                        SharedOperationHost.Start(World, Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "RunicOperations", World));
                    ready = true;
                    SharedOperationHost.Tick(Time.frameCount);
                    if (Time.realtimeSinceStartup > nextCleanup)
                    {
                        nextCleanup = Time.realtimeSinceStartup + 10;
                        var live = new HashSet<long>(ZNet.instance.GetPeers().Select(x => x.m_uid));
                        compatible.RemoveWhere(x => !live.Contains(x));
                        foreach (long peer in rates.Keys.Where(x => !live.Contains(x)).ToArray()) rates.Remove(peer);
                        foreach (long peer in queryRates.Keys.Where(x => !live.Contains(x)).ToArray()) queryRates.Remove(peer);
                        foreach (long peer in actorWarnings.Keys.Where(x => !live.Contains(x)).ToArray()) actorWarnings.Remove(peer);
                    }
                }
                else if (!ready && Time.realtimeSinceStartup >= nextHello && ZNet.instance.GetServerPeer() != null)
                {
                    nextHello = Time.realtimeSinceStartup + 2;
                    var hello = new ZPackage(); hello.Write(SharedOperationHost.Protocol); hello.Write(World); hello.Write(BuildStamp);
                    registered.InvokeRoutedRPC(ZNet.instance.GetServerPeer().m_uid, HelloRpc, hello);
                }
                if (!ready) return;
                foreach (Pending request in pending.Values.OrderBy(x => Priority(x.Kind)).ToArray())
                {
                    // Host replies can synchronously complete a request or invoke a UI callback
                    // that cancels another query while this snapshot is being enumerated.
                    if (!pending.TryGetValue(request.Id, out var current) || !ReferenceEquals(current, request)) continue;
                    if (request.World != World || Time.realtimeSinceStartup < request.Retry) continue;
                    request.Retry = Time.realtimeSinceStartup + (request.Kind == QueryKind ? 0.25f : 2f);
                    var packet = new ZPackage(); packet.Write(SharedOperationHost.Protocol); packet.Write(request.World);
                    packet.Write(request.Id); packet.Write(request.Kind); packet.Write(request.Payload);
                    long server = ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0;
                    if (server != 0) registered.InvokeRoutedRPC(server, RequestRpc, packet);
                }
            }
            private bool Send(string world, string id, string kind, byte[] payload, Action<int, string, byte[]> completed)
            {
                if (!Guid.TryParseExact(id, "N", out _) || kind == null || kind.Length > 128 || payload == null || payload.Length > 65536) return false;
                if (pending.TryGetValue(id, out var existing))
                {
                    if (existing.World != world || existing.Kind != kind || !OwnerOperationQueue.Same(existing.Payload, payload)) return false;
                    existing.Completed = completed; return true;
                }
                // Human chest queries and lid presence cannot consume or be starved by
                // the slots holding durable character/automation mutation requests.
                if (kind == QueryKind || kind == ViewerKind)
                {
                    if (pending.Values.Count(x => x.Kind == kind) >= (kind == QueryKind ? 4 : 2)) return false;
                }
                else
                {
                    int ordinary = pending.Values.Count(x => !ReadOnlyKind(x.Kind));
                    if (ordinary >= 16 || BackgroundKind(kind) && ordinary >= 12) return false;
                }
                pending.Add(id, new Pending { World = world, Id = id, Kind = kind, Payload = (byte[])payload.Clone(),
                    Completed = completed, Created = Time.realtimeSinceStartup });
                return true;
            }
            private bool CancelQuery(string id)
            {
                if (id == null || !pending.TryGetValue(id, out var request) || !ReadOnlyKind(request.Kind)) return false;
                // Cancellation applies only to disposable views/presence. Never discard a
                // mutation request whose committed result or character escrow is unresolved.
                return pending.Remove(id);
            }
            private void Hello(long sender, ZPackage packet)
            {
                try
                {
                    if (!Complete || ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.GetPeer(sender) == null || packet.Size() > 256) return;
                    int version = packet.ReadInt(); string world = packet.ReadString(); string stamp = packet.ReadString(); OperationPacket.End(packet);
                    bool accepted = version == SharedOperationHost.Protocol && world == World && stamp == BuildStamp && EmbeddedCoreAgreement.Agree;
                    if (accepted) compatible.Add(sender); else compatible.Remove(sender);
                    var reply = new ZPackage(); reply.Write(SharedOperationHost.Protocol); reply.Write(World); reply.Write(accepted); reply.Write(BuildStamp);
                    registered.InvokeRoutedRPC(sender, ReadyRpc, reply);
                    if (!accepted) UnityEngine.Debug.Log("Runic peer uses native inventory handling; optional Runic server transactions were not negotiated.");
                }
                catch (Exception error) { MutationGate.Report("operation hello rejected: " + error.GetType().Name); }
            }
            private void ReadyReply(long sender, ZPackage packet)
            {
                try
                {
                    if (ZNet.instance?.GetServerPeer()?.m_uid != sender || packet.Size() > 256) return;
                    int protocol = packet.ReadInt(); string world = packet.ReadString(); bool accepted = packet.ReadBool(); string stamp = packet.ReadString(); OperationPacket.End(packet);
                    ready = protocol == SharedOperationHost.Protocol && world == World && accepted && stamp == BuildStamp;
                    if (!ready && mismatch != stamp)
                    {
                        mismatch = stamp;
                        UnityEngine.Debug.Log("Runic uses native inventory handling for this server; optional Runic server transactions were not negotiated.");
                    }
                }
                catch { ready = false; }
            }
            private void Request(long sender, ZPackage packet)
            {
                try
                {
                    if (!ready || ZNet.instance == null || !ZNet.instance.IsServer() ||
                        (sender != ZDOMan.GetSessionID() && !compatible.Contains(sender)) || packet == null || packet.Size() > 66048) return;
                    if (packet.ReadInt() != SharedOperationHost.Protocol) return;
                    string world = packet.ReadString(), id = packet.ReadString(), kind = packet.ReadString();
                    byte[] payload = OperationPacket.Bytes(packet); OperationPacket.End(packet);
                    if (world != World || !Guid.TryParseExact(id, "N", out _) || kind.Length > 128 || payload.Length > 65536) return;
                    // Decode and validate the envelope before classifying. The registered
                    // query handler still validates the action; a client cannot put a
                    // mutation into this lane merely by changing its kind string.
                    float now = Time.realtimeSinceStartup;
                    var bucket = ReadOnlyKind(kind) ? queryRates : rates;
                    if (!bucket.TryGetValue(sender, out var rate) || now - rate.Item1 >= 1) rate = Tuple.Create(now, 0);
                    if (rate.Item2 >= 32) return;
                    bucket[sender] = Tuple.Create(rate.Item1, rate.Item2 + 1);
                    if (!NetworkInventoryAccess.TryActor(sender, out string actor))
                    {
                        // The peer and envelope are valid, but its replicated character may not
                        // be available yet. Keep the same request pending; never journal a denial
                        // or submit an operation without an authenticated character principal.
                        Reply(sender, world, id, (int)OperationStatus.Busy, "actor-unavailable", Array.Empty<byte>());
                        if (!actorWarnings.TryGetValue(sender, out float nextWarning) || now >= nextWarning)
                        {
                            actorWarnings[sender] = now + 30f;
                            UnityEngine.Debug.LogWarning($"Runic inventory request deferred: character identity unavailable for peer {sender}; request={id}. The request will retry.");
                        }
                        return;
                    }
                    var result = SharedOperationHost.Submit(world, actor, id, kind, payload,
                        (status, reason, response) => Reply(sender, world, id, status, reason, response));
                    if (result.Item1 != (int)OperationStatus.Queued) Reply(sender, world, id, result.Item1, result.Item2, result.Item3);
                }
                catch (Exception error) { MutationGate.Report("operation request rejected: " + error.GetType().Name + ": " + error.Message); }
            }
            private void Reply(long receiver, string world, string id, int status, string reason, byte[] payload)
            {
                if (registered == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                var packet = new ZPackage(); packet.Write(SharedOperationHost.Protocol); packet.Write(world);
                packet.Write(id); packet.Write(status); packet.Write(reason); packet.Write(payload);
                registered.InvokeRoutedRPC(receiver, ResultRpc, packet);
            }
            private void Result(long sender, ZPackage packet)
            {
                try
                {
                    if (ZNet.instance == null || packet == null || packet.Size() > 70500) return;
                    long server = ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0;
                    if (server == 0 || sender != server || packet.ReadInt() != SharedOperationHost.Protocol) return;
                    string world = packet.ReadString(), id = packet.ReadString();
                    int status = packet.ReadInt(); string reason = packet.ReadString(); byte[] payload = OperationPacket.Bytes(packet); OperationPacket.End(packet);
                    if (world != World || !pending.TryGetValue(id, out var request) || request.World != world || payload.Length > 65536 || reason.Length > 4096 ||
                        !Enum.IsDefined(typeof(OperationStatus), status)) return;
                    if (status == (int)OperationStatus.Queued) return;
                    if (status == (int)OperationStatus.Busy || status == (int)OperationStatus.RecoveryPending)
                    {
                        // A live authority explicitly deferred this manual request. Retry
                        // the same immutable operation promptly instead of waiting for the
                        // unanswered-packet timeout. Recovery and automation keep their
                        // existing backoff, and an already earlier deadline stays earlier.
                        if (status == (int)OperationStatus.Busy && reason == "container-live-state-requested" && !BackgroundKind(request.Kind))
                            request.Retry = Math.Min(request.Retry, Time.realtimeSinceStartup + .05f);
                        else if (status == (int)OperationStatus.Busy && !ReadOnlyKind(request.Kind) && !BackgroundKind(request.Kind))
                            request.Retry = Math.Min(request.Retry, Time.realtimeSinceStartup + 0.25f);
                        WarnBusy(request, reason);
                        return;
                    }
                    pending.Remove(id); request.Completed?.Invoke(status, reason, payload);
                }
                catch (Exception error) { MutationGate.Report("operation reply deferred: " + error.GetType().Name + ": " + error.Message); }
            }
            private void WarnBusy(Pending request, string reason)
            {
                float now = Time.realtimeSinceStartup, age = now - request.Created;
                if (age < 3f || now < request.NextBusyWarning) return;
                request.NextBusyWarning = now + 30f;
                int queries = pending.Values.Count(x => x.Kind == QueryKind), viewers = pending.Values.Count(x => x.Kind == ViewerKind);
                UnityEngine.Debug.LogWarning($"Runic inventory request is still waiting: kind={request.Kind}; request={request.Id}; " +
                    $"age={age.ToString("F1", CultureInfo.InvariantCulture)}s; reason={reason}; " +
                    $"pending mutations={pending.Count - queries - viewers}/16, chest queries={queries}/4, viewers={viewers}/2. The same request will retry.");
            }
        }
    }
}
