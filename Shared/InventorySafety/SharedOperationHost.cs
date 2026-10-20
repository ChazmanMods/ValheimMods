using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RunicAutomation.Operations
{
    // Each plugin embeds these sources. Only framework delegate/tuple types cross that assembly
    // boundary; casting another plugin's OwnerOperationQueue or OperationPlan would fail.
    internal static class SharedOperationHost
    {
        internal const int Protocol = 2;
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.host.v2", MakeRoutes);
        internal static bool Active => ((Func<bool>)Routes["active"])();
        internal static void Start(string world, string directory) => ((Action<string, string>)Routes["start"])(world, directory);
        internal static void Stop() => ((Action)Routes["stop"])();
        internal static void Tick(int frame) => ((Action<int>)Routes["tick"])(frame);
        internal static void Register(string kind, Func<string, byte[], string> authorize,
            Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> plan) =>
            ((Action<string, Func<string, byte[], string>, Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>>>)Routes["register"])(kind, authorize, plan);
        internal static byte[] Read(string endpoint) => ((Func<string, byte[]>)Routes["read"])(endpoint);
        internal static byte[] ReadStored(string endpoint) => ((Func<string, byte[]>)Routes["read-stored"])(endpoint);
        internal static Tuple<string, byte[]>[] Enumerate(string prefix) => ((Func<string, Tuple<string, byte[]>[]>)Routes["enumerate"])(prefix);
        internal static byte[] Import(string endpoint, byte[] payload) => ((Func<string, byte[], byte[]>)Routes["import"])(endpoint, payload);
        internal static void RegisterProjection(string prefix, Action<string, byte[]> project) => ((Action<string, Action<string, byte[]>>)Routes["projection"])(prefix, project);
        internal static void RefreshProjection(string endpoint) => ((Action<string>)Routes["refresh"])(endpoint);
        internal static void RegisterQuery(string kind, Func<string, byte[], string> authorize,
            Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> plan)
        { Register(kind, authorize, plan); ((Action<string>)Routes["query"])(kind); }
        internal static void RegisterRevisionGuarded(string kind, Func<string, byte[], string> authorize,
            Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> plan)
        { Register(kind, authorize, plan); ((Action<string>)Routes["revision-guarded"])(kind); }
        internal static void RegisterBackground(string kind, Func<string, byte[], string> authorize,
            Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> plan, bool revisionGuarded = false)
        {
            Register(kind, authorize, plan); ((Action<string>)Routes["background"])(kind);
            if (revisionGuarded) ((Action<string>)Routes["revision-guarded"])(kind);
        }
        internal static Tuple<int, string, byte[]> Submit(string world, string actor, string id, string kind, byte[] payload,
            Action<int, string, byte[]> completed) =>
            ((Func<string, string, string, string, byte[], Action<int, string, byte[]>, Tuple<int, string, byte[]>>)Routes["submit"])(world, actor, id, kind, payload, completed);

        private sealed class Handler : IOwnerOperationHandler, IReadOnlyOperationHandler, IRevisionGuardedOperationHandler, IBackgroundOperationHandler
        {
            public bool ReadOnly { get; set; }
            public bool RevisionGuarded { get; set; }
            public bool Background { get; set; }
            internal Func<string, byte[], string> Authorizer;
            internal Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>> Planner;
            public bool Authorize(OperationRequest request, out string reason)
            { reason = Authorizer(request.Actor, request.Payload); return reason == null; }
            public OperationPlan Plan(OperationRequest request, IOperationEndpoints endpoints)
            {
                var plan = Planner(request.Actor, request.Payload, endpoints.Read);
                return plan == null ? null : new OperationPlan(plan.Item1.Select(x => new EndpointChange(x.Item1, x.Item2, x.Item3)), plan.Item2);
            }
        }
        private static Dictionary<string, Delegate> MakeRoutes()
        {
            FileOperationJournal journal = null;
            FileOperationEndpoints endpoints = null;
            OwnerOperationQueue queue = null;
            string currentWorld = null, currentDirectory = null;
            int lastFrame = -1;
            var handlers = new Dictionary<string, Handler>();
            var projections = new Dictionary<string, Action<string, byte[]>>();
            var dirty = new Dictionary<string, byte[]>();
            return new Dictionary<string, Delegate>
            {
                ["active"] = new Func<bool>(() => queue != null),
                ["start"] = new Action<string, string>((world, directory) =>
                {
                    directory = Path.GetFullPath(directory);
                    if (queue != null)
                    {
                        if (world != currentWorld || directory != currentDirectory)
                            throw new InvalidOperationException("Another world already owns the operation authority");
                        return;
                    }
                    var opened = new FileOperationJournal(Path.Combine(directory, "journal"), world);
                    try
                    {
                        var store = new FileOperationEndpoints(Path.Combine(directory, "inventories"), world);
                        store.NativeEndpoint = LiveChestWorld.IsLive; store.NativeRead = LiveChestWorld.Cached;
                        store.Changed = (endpoint, image) => dirty[endpoint] = image;
                        var authority = new OwnerOperationQueue(world, opened, store);
                        authority.ApplyNative = record => { int status = LiveChestWorld.Apply(record, out string reason); return Tuple.Create(status, reason); };
                        authority.NativeEndpoint = LiveChestWorld.IsLive;
                        authority.NativeRequestContext = LiveChestWorld.RequestContext;
                        authority.NativeCompleted = LiveChestWorld.Complete;
                        authority.ReportError = error => { UnityEngine.Debug.LogError("Runic inventory authority: " + error); MutationGate.Report("operation authority: " + error); };
                        foreach (var handler in handlers) authority.Register(handler.Key, handler.Value);
                        currentWorld = world; currentDirectory = directory;
                        journal = opened; endpoints = store; queue = authority; lastFrame = -1;
                    }
                    catch { opened.Dispose(); throw; }
                }),
                ["stop"] = new Action(() =>
                {
                    queue = null; endpoints = null; currentWorld = null; currentDirectory = null;
                    journal?.Dispose(); journal = null; lastFrame = -1;
                    dirty.Clear();
                }),
                ["tick"] = new Action<int>(frame =>
                {
                    if (queue == null || frame == lastFrame) return;
                    lastFrame = frame; queue.Recover(4); queue.Pump(8, milliseconds: 8);
                    foreach (var change in dirty.Take(32).ToArray())
                    {
                        try
                        {
                            bool handled = false;
                            foreach (var projection in projections)
                                if (change.Key.StartsWith(projection.Key, StringComparison.Ordinal))
                                { projection.Value(change.Key, change.Value); handled = true; }
                            if (handled) dirty.Remove(change.Key);
                        }
                        catch (Exception error) { MutationGate.Report("inventory projection deferred: " + error.Message); }
                    }
                }),
                ["projection"] = new Action<string, Action<string, byte[]>>((prefix, projection) => projections[prefix] = projection),
                ["refresh"] = new Action<string>(endpoint => { var image = endpoints?.Read(endpoint); if (image != null) dirty[endpoint] = image; }),
                ["query"] = new Action<string>(kind => handlers[kind].ReadOnly = true),
                ["revision-guarded"] = new Action<string>(kind => handlers[kind].RevisionGuarded = true),
                ["background"] = new Action<string>(kind => handlers[kind].Background = true),
                ["register"] = new Action<string, Func<string, byte[], string>, Func<string, byte[], Func<string, byte[]>, Tuple<Tuple<string, byte[], byte[]>[], byte[]>>>((kind, authorize, plan) =>
                {
                    if (string.IsNullOrWhiteSpace(kind) || authorize == null || plan == null) throw new ArgumentException("Invalid operation handler");
                    if (handlers.TryGetValue(kind, out var existing))
                    { existing.Authorizer = authorize; existing.Planner = plan; return; }
                    var handler = new Handler { Authorizer = authorize, Planner = plan };
                    handlers.Add(kind, handler); queue?.Register(kind, handler);
                }),
                ["read"] = new Func<string, byte[]>(endpoint => endpoints?.Read(endpoint)),
                ["read-stored"] = new Func<string, byte[]>(endpoint => endpoints?.ReadStored(endpoint)),
                ["enumerate"] = new Func<string, Tuple<string, byte[]>[]>(prefix => endpoints?.Enumerate(prefix) ?? Array.Empty<Tuple<string, byte[]>>()),
                ["import"] = new Func<string, byte[], byte[]>((endpoint, payload) =>
                    endpoints?.Import(endpoint, payload) ?? throw new InvalidOperationException("Authority is not started")),
                ["submit"] = new Func<string, string, string, string, byte[], Action<int, string, byte[]>, Tuple<int, string, byte[]>>((world, actor, id, kind, payload, completed) =>
                {
                    if (queue == null) return Tuple.Create((int)OperationStatus.Busy, "authority-not-started", Array.Empty<byte>());
                    var result = queue.Submit(new OperationRequest(world, actor, id, kind, payload),
                        completed == null ? (Action<OperationResult>)null : r => completed((int)r.Status, r.Reason, r.Payload));
                    return Tuple.Create((int)result.Status, result.Reason, result.Payload);
                })
            };
        }
    }
}
