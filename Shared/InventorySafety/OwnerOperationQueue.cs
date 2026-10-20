using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicAutomation.Operations
{
    internal sealed class OperationRejectedException : Exception
    { public OperationRejectedException(string reason) : base(reason) { Data["Runic.OperationDisposition"] = "rejected"; } }
    internal sealed class OperationDeferredException : Exception
    { public OperationDeferredException(string reason) : base(reason) { Data["Runic.OperationDisposition"] = "deferred"; } }
    // Independent of Unity and transport. All methods run on the authority's game thread.
    // Only authority-side handlers may construct a plan; clients never submit inventory bytes.
    internal sealed class OperationRequest
    {
        public readonly string World, Actor, Id, Kind;
        private readonly byte[] payload;
        public byte[] Payload => (byte[])payload.Clone();
        public readonly string Fingerprint;
        public string Key => Hash(World + "\n" + Actor + "\n" + Id);

        public OperationRequest(string world, string actor, string id, string kind, byte[] payload)
        {
            World = Valid(world); Actor = Valid(actor); Id = Valid(id); Kind = Valid(kind);
            if (payload == null || payload.Length > 65536) throw new ArgumentException("Invalid payload");
            this.payload = (byte[])payload.Clone();
            Fingerprint = Hash(Kind + "\n" + Convert.ToBase64String(this.payload));
        }
        private static string Valid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Contains("\n") || value.Contains("\r"))
                throw new ArgumentException("Invalid operation identity");
            return value;
        }
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
    }

    internal sealed class EndpointChange
    {
        public readonly string Endpoint;
        private readonly byte[] before, after;
        public byte[] Before => (byte[])before.Clone();
        public byte[] After => (byte[])after.Clone();
        public EndpointChange(string endpoint, byte[] before, byte[] after)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 256 || before == null || after == null ||
                before.Length > 4 * 1024 * 1024 || after.Length > 4 * 1024 * 1024)
                throw new ArgumentException("Invalid endpoint change");
            Endpoint = endpoint; this.before = (byte[])before.Clone(); this.after = (byte[])after.Clone();
        }
    }

    internal sealed class OperationPlan
    {
        public readonly IReadOnlyList<EndpointChange> Changes;
        public readonly bool Native;
        private readonly byte[] response;
        public byte[] Response => (byte[])response.Clone();
        public OperationPlan(IEnumerable<EndpointChange> changes, byte[] response, bool native = true)
        {
            var copy = changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
            if (copy.Length > 128 || copy.Any(x => x == null) ||
                copy.Select(x => x.Endpoint).Distinct(StringComparer.Ordinal).Count() != copy.Length ||
                response == null || response.Length > 65536)
                throw new ArgumentException("Invalid operation plan");
            Changes = Array.AsReadOnly(copy); this.response = (byte[])response.Clone(); Native = native;
        }
    }

    internal enum OperationStatus { Queued, Busy, Committed, Denied, RecoveryPending, IdConflict, WrongWorld }
    internal sealed class OperationResult
    {
        public readonly OperationStatus Status;
        public readonly string Reason;
        private readonly byte[] payload;
        public byte[] Payload => (byte[])payload.Clone();
        public OperationResult(OperationStatus status, string reason = "", byte[] payload = null)
        { Status = status; Reason = reason ?? ""; this.payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone(); }
    }

    internal sealed class OperationRecord
    {
        public readonly OperationRequest Request;
        public readonly OperationPlan Plan;
        public readonly OperationResult Result;
        public OperationRecord(OperationRequest request, OperationPlan plan, OperationResult result)
        { Request = request; Plan = plan; Result = result; }
    }

    internal interface IOperationJournal
    {
        OperationRecord Find(string key);
        IEnumerable<OperationRecord> Pending();
        // Must durably replace the entire record before returning, or throw.
        void Save(OperationRecord record);
    }
    internal interface IOperationEndpoints
    {
        byte[] Read(string endpoint);
        // Durable atomic compare-and-replace, including the endpoint's monotonic operation revision.
        // Return true only after persistence; an in-memory ZDO update does not meet this contract.
        // A native snapshot containing only item counts is NOT an adequate endpoint image.
        bool CompareExchange(string endpoint, byte[] expected, byte[] replacement);
    }
    internal interface IOwnerOperationHandler
    {
        // Actor is supplied by the authenticated transport, never copied from request payload.
        bool Authorize(OperationRequest request, out string reason);
        OperationPlan Plan(OperationRequest request, IOperationEndpoints endpoints);
    }
    internal interface IReadOnlyOperationHandler { bool ReadOnly { get; } }
    // Only the authority's registration classifies background work. Payloads cannot
    // opt a mutation into a different scheduling class or bypass its normal checks.
    internal interface IBackgroundOperationHandler { bool Background { get; } }
    // Only automation without character escrow may opt in. Its planner must compare a
    // request revision with a durable endpoint revision and advance that endpoint atomically.
    // A zero-change plan is a repeatable no-op: neither planning nor its response may cause
    // an inventory/output mutation or any other once-only effect. Such no-ops have no receipt.
    internal interface IRevisionGuardedOperationHandler { bool RevisionGuarded { get; } }
    internal interface ICompactOperationJournal { void ForgetTerminal(string key); }

    internal sealed class OwnerOperationQueue
    {
        private sealed class Waiting
        {
            public OperationRequest Request;
            public bool ReadOnly;
            public readonly List<Action<OperationResult>> Replies = new List<Action<OperationResult>>();
        }
        private readonly string world;
        private readonly IOperationJournal journal;
        private readonly IOperationEndpoints endpoints;
        private readonly Dictionary<string, IOwnerOperationHandler> handlers = new Dictionary<string, IOwnerOperationHandler>();
        private readonly Dictionary<string, Waiting> waiting = new Dictionary<string, Waiting>();
        private readonly Dictionary<string, LinkedList<Waiting>> byActor = new Dictionary<string, LinkedList<Waiting>>();
        private readonly Queue<string> actors = new Queue<string>();
        private readonly Dictionary<string, int> manualBursts = new Dictionary<string, int>();
        private readonly Func<double> millisecondsNow;
        private readonly Dictionary<string, string> recovering = new Dictionary<string, string>();
        private readonly Dictionary<string, OperationRecord> unsaved = new Dictionary<string, OperationRecord>();
        private readonly Dictionary<string, List<Action<OperationResult>>> recoveryReplies = new Dictionary<string, List<Action<OperationResult>>>();
        private bool pumping;
        private int recoveryCursor;
        private int queuedQueries, queuedMutations;
        internal Action<Exception> ReportError = _ => { };
        internal Func<OperationRecord, Tuple<int, string>> ApplyNative = _ => Tuple.Create(0, "");
        internal Func<string, bool> NativeEndpoint = _ => false;
        internal Action<string> NativeRequestContext = _ => { };
        internal Action<string> NativeCompleted = _ => { };

        public OwnerOperationQueue(string world, IOperationJournal journal, IOperationEndpoints endpoints,
            Func<double> millisecondsNow = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
            this.endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
            this.millisecondsNow = millisecondsNow ?? (() => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
            foreach (var record in journal.Pending())
            {
                if (record.Request.World != world) throw new InvalidOperationException("Journal belongs to another world");
                TrackRecovery(record);
            }
        }
        public void Register(string kind, IOwnerOperationHandler handler)
        { handlers.Add(kind, handler ?? throw new ArgumentNullException(nameof(handler))); }

        private bool IsReadOnly(string kind) => handlers.TryGetValue(kind, out var handler) &&
            handler is IReadOnlyOperationHandler readOnly && readOnly.ReadOnly;
        private bool IsBackground(string kind) => handlers.TryGetValue(kind, out var handler) &&
            handler is IBackgroundOperationHandler background && background.Background;

        public OperationResult Submit(OperationRequest request, Action<OperationResult> reply)
        {
            if (request.World != world) return new OperationResult(OperationStatus.WrongWorld);
            var previous = journal.Find(request.Key);
            if (previous != null)
                return previous.Request.Fingerprint != request.Fingerprint
                    ? new OperationResult(OperationStatus.IdConflict) : previous.Result;
            if (unsaved.TryGetValue(request.Key, out var uncertain))
                return uncertain.Request.Fingerprint != request.Fingerprint
                    ? new OperationResult(OperationStatus.IdConflict)
                    : new OperationResult(OperationStatus.RecoveryPending, "journal-write-pending");
            if (waiting.TryGetValue(request.Key, out var duplicate))
            {
                if (duplicate.Request.Fingerprint != request.Fingerprint) return new OperationResult(OperationStatus.IdConflict);
                // Retries must not allocate an unbounded number of callbacks.
                if (reply != null && duplicate.Replies.Count < 4) duplicate.Replies.Add(reply);
                return new OperationResult(OperationStatus.Queued);
            }
            bool query = IsReadOnly(request.Kind);
            byActor.TryGetValue(request.Actor, out var queue);
            int actorLaneCount = queue?.Count(entry => entry.ReadOnly == query) ?? 0;
            // Read-only admission is independent of durable work, matching the client's
            // four inspection and two presence slots. Never displace an accepted mutation.
            // Only an authority-registered read-only handler qualifies for this lane.
            if (query ? queuedQueries >= 64 || actorLaneCount >= 6 : queuedMutations >= 256 || actorLaneCount >= 16)
                return new OperationResult(OperationStatus.Busy, "queue-full");
            var item = new Waiting { Request = request, ReadOnly = query };
            if (reply != null) item.Replies.Add(reply);
            if (queue == null)
            { queue = new LinkedList<Waiting>(); byActor.Add(request.Actor, queue); actors.Enqueue(request.Actor); }
            queue.AddLast(item); waiting.Add(request.Key, item);
            if (query) queuedQueries++; else queuedMutations++;
            return new OperationResult(OperationStatus.Queued);
        }

        // Queries keep priority. Within each actor, player actions may overtake background
        // automation, while each mutation class keeps its own FIFO order. A waiting
        // background request gets a turn after four manual actions, preventing starvation.
        // Round robin between actors is unchanged; no accepted request is discarded.
        private Waiting TakeNext(bool queriesOnly)
        {
            int count = actors.Count;
            for (int n = 0; n < count; n++)
            {
                string actor = actors.Dequeue();
                var queue = byActor[actor];
                var node = queue.First;
                if (queriesOnly)
                    while (node != null && !IsReadOnly(node.Value.Request.Kind)) node = node.Next;
                else
                {
                    var manual = queue.First;
                    while (manual != null && IsBackground(manual.Value.Request.Kind)) manual = manual.Next;
                    var background = queue.First;
                    while (background != null && !IsBackground(background.Value.Request.Kind)) background = background.Next;
                    manualBursts.TryGetValue(actor, out int burst);
                    if (manual != null && background != null && burst < 4)
                    { node = manual; manualBursts[actor] = burst + 1; }
                    else
                    { node = background ?? manual; manualBursts.Remove(actor); }
                }
                if (node == null) { actors.Enqueue(actor); continue; }
                var item = node.Value;
                queue.Remove(node);
                if (queue.Count == 0) { byActor.Remove(actor); manualBursts.Remove(actor); }
                else actors.Enqueue(actor);
                return item;
            }
            return null;
        }

        // An individual durable operation cannot be interrupted safely. If it overruns the
        // elapsed-time budget, yield immediately afterwards rather than doing a full batch.
        public void Pump(int budget = 8, double milliseconds = double.PositiveInfinity)
        {
            if (double.IsNaN(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if (pumping || budget <= 0 || milliseconds == 0) return;
            pumping = true;
            try
            {
                double started = millisecondsNow();
                for (int n = 0; n < budget && actors.Count != 0; n++)
                {
                    if (n != 0 && millisecondsNow() - started >= milliseconds) break;
                    var item = TakeNext(true) ?? TakeNext(false);
                    OperationResult result;
                    try { NativeRequestContext(item.Request.Key); result = Execute(item.Request); }
                    catch (Exception error)
                    {
                        ReportError(error);
                        // A failed durable write has an unknown outcome. Preserve the id and retry it.
                        result = new OperationResult(OperationStatus.RecoveryPending, "authority-io-failure");
                    }
                    finally { NativeRequestContext(null); }
                    waiting.Remove(item.Request.Key);
                    if (item.ReadOnly) queuedQueries--; else queuedMutations--;
                    if (result.Status == OperationStatus.RecoveryPending)
                        recoveryReplies[item.Request.Key] = new List<Action<OperationResult>>(item.Replies);
                    foreach (var reply in item.Replies)
                        try { reply(result); } catch (Exception error) { ReportError(error); }
                }
            }
            finally { pumping = false; }
        }

        private OperationResult Execute(OperationRequest request)
        {
            var previous = journal.Find(request.Key);
            if (previous != null) return previous.Result;
            if (!handlers.TryGetValue(request.Kind, out var handler)) return Deny(request, "unsupported-operation");
            bool query = handler is IReadOnlyOperationHandler readOnly && readOnly.ReadOnly;
            bool guarded = handler is IRevisionGuardedOperationHandler revisionGuarded && revisionGuarded.RevisionGuarded;
            OperationResult Reject(string message) => query || guarded ? new OperationResult(OperationStatus.Denied, message) : Deny(request, message);
            string reason;
            OperationPlan plan;
            try
            {
                if (!handler.Authorize(request, out reason)) return Reject(reason);
                plan = handler.Plan(request, endpoints);
            }
            // Embedded adapters can throw their own assembly's copy of these exception types.
            catch (Exception rejected) when ((string)rejected.Data["Runic.OperationDisposition"] == "rejected") { return Reject(rejected.Message); }
            catch (Exception deferred) when ((string)deferred.Data["Runic.OperationDisposition"] == "deferred") { return new OperationResult(OperationStatus.Busy, deferred.Message); }
            if (plan == null) return Reject("requirements-not-met");
            if (query && plan.Changes.Count != 0) throw new InvalidOperationException("Read-only handler attempted an inventory mutation");
            foreach (var change in plan.Changes)
            {
                if (recovering.ContainsKey(change.Endpoint)) return new OperationResult(OperationStatus.Busy, "endpoint-recovering");
                if (!Same(endpoints.Read(change.Endpoint), change.Before)) return new OperationResult(OperationStatus.Busy, "snapshot-changed");
            }
            // Planning cannot grant permission: check it again at the actual commit boundary.
            try { if (!handler.Authorize(request, out reason)) return Reject(reason); }
            catch (Exception rejected) when ((string)rejected.Data["Runic.OperationDisposition"] == "rejected") { return Reject(rejected.Message); }
            catch (Exception deferred) when ((string)deferred.Data["Runic.OperationDisposition"] == "deferred") { return new OperationResult(OperationStatus.Busy, deferred.Message); }
            if (query || (guarded && plan.Changes.Count == 0))
                return new OperationResult(OperationStatus.Committed, payload: plan.Response);
            var prepared = new OperationRecord(request, plan, new OperationResult(OperationStatus.RecoveryPending));
            TrackRecovery(prepared);
            unsaved[request.Key] = prepared;
            journal.Save(prepared); // write-ahead: no endpoint changes before this succeeds
            unsaved.Remove(request.Key);
            return Apply(prepared);
        }

        public void Recover(int budget = 8)
        {
            if (pumping) return;
            pumping = true;
            try
            {
                foreach (var record in unsaved.Values.Take(budget).ToArray())
                    try { journal.Save(record); unsaved.Remove(record.Request.Key); }
                    catch (Exception error) { ReportError(error); }
                var pending = journal.Pending().ToArray();
                int count = Math.Min(Math.Max(0, budget), pending.Length);
                for (int n = 0; n < count; n++)
                {
                    var record = pending[(recoveryCursor + n) % pending.Length];
                    try
                    {
                        TrackRecovery(record); var result = Apply(record);
                        if (result.Status != OperationStatus.RecoveryPending && recoveryReplies.TryGetValue(record.Request.Key, out var replies))
                        {
                            recoveryReplies.Remove(record.Request.Key);
                            foreach (var reply in replies) try { reply(result); } catch (Exception error) { ReportError(error); }
                        }
                    }
                    catch (Exception error) { ReportError(error); }
                }
                recoveryCursor = pending.Length == 0 ? 0 : (recoveryCursor + count) % pending.Length;
            }
            finally { pumping = false; }
        }

        private OperationResult Apply(OperationRecord record)
        {
            var native = ApplyNative(record); int live = native.Item1; string nativeReason = native.Item2;
            if (live == 1) return new OperationResult(OperationStatus.RecoveryPending, nativeReason);
            if (live == 3)
            {
                var denied = new OperationResult(OperationStatus.Denied, nativeReason);
                journal.Save(new OperationRecord(record.Request, record.Plan, denied));
                foreach (var change in record.Plan.Changes) recovering.Remove(change.Endpoint);
                NativeCompleted(record.Request.Key);
                return denied;
            }
            foreach (var change in record.Plan.Changes)
            {
                if (NativeEndpoint(change.Endpoint)) continue;
                byte[] current = endpoints.Read(change.Endpoint);
                if (Same(current, change.After)) continue;
                if (!Same(current, change.Before) || !endpoints.CompareExchange(change.Endpoint, change.Before, change.After))
                    return new OperationResult(OperationStatus.RecoveryPending, "endpoint-unavailable-or-conflicting");
            }
            var result = new OperationResult(OperationStatus.Committed, "", record.Plan.Response);
            journal.Save(new OperationRecord(record.Request, record.Plan, result));
            foreach (var change in record.Plan.Changes) recovering.Remove(change.Endpoint);
            NativeCompleted(record.Request.Key);
            if (handlers.TryGetValue(record.Request.Kind, out var handler) && handler is IRevisionGuardedOperationHandler guarded && guarded.RevisionGuarded && journal is ICompactOperationJournal compact)
            {
                try { compact.ForgetTerminal(record.Request.Key); }
                catch (Exception error) { ReportError(error); } // Retaining a receipt is always safe.
            }
            return result;
        }
        private void TrackRecovery(OperationRecord record)
        {
            foreach (var change in record.Plan.Changes)
            {
                if (recovering.TryGetValue(change.Endpoint, out string key) && key != record.Request.Key)
                    throw new InvalidOperationException("Overlapping unfinished operations");
                recovering[change.Endpoint] = record.Request.Key;
            }
        }
        private OperationResult Deny(OperationRequest request, string reason)
        {
            var result = new OperationResult(OperationStatus.Denied, reason);
            journal.Save(new OperationRecord(request, new OperationPlan(Array.Empty<EndpointChange>(), Array.Empty<byte>()), result));
            return result;
        }
        internal static bool Same(byte[] left, byte[] right) => left != null && right != null && left.SequenceEqual(right);
    }
}
