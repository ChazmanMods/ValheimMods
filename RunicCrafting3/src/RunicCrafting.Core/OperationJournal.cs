using System;
using System.Collections.Generic;

namespace RunicCrafting.Core
{
    public enum OperationPhase { Acquire, Freshness, Debit, Effect, Status, Return, Release }
    public enum OperationOutcome { Pending, Busy, Denied, Unsupported, StaleBeforeSpend, Confirmed, ConfirmedPartial, Unknown }
    public enum Registration { New, Replay, PayloadMismatch, CapacityReached, WrongSession, Retired, InvalidSequence }

    public sealed class OperationId
    {
        public OperationId(string world, string sessionNonce, string actionId, string sourceId, OperationPhase phase, long sequence = 0)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence;
            World = Contract.Id(world, nameof(world)); SessionNonce = Contract.Id(sessionNonce, nameof(sessionNonce));
            ActionId = Contract.Id(actionId, nameof(actionId)); SourceId = Contract.Id(sourceId, nameof(sourceId)); Phase = phase;
            Key = Contract.Hash(w => { w.Write(World); w.Write(SessionNonce); w.Write(ActionId); w.Write(SourceId); w.Write((int)Phase); });
        }
        public string World { get; }
        public string SessionNonce { get; }
        public string ActionId { get; }
        public string SourceId { get; }
        public OperationPhase Phase { get; }
        // Monotonic ACTION number per authenticated requester session. The canonical ActionId must equal it.
        public long Sequence { get; }
        public string Key { get; }
    }

    public sealed class OperationRecord
    {
        internal OperationRecord(OperationId id, string payload) { Id = id; PayloadFingerprint = payload; }
        public OperationId Id { get; }
        public string PayloadFingerprint { get; }
        public OperationOutcome Outcome { get; internal set; } = OperationOutcome.Pending;
        // Opaque canonical receipt digest: includes exact slices/accepted quantity, not just success/failure.
        public string? ReceiptFingerprint { get; internal set; }
    }

    // This is deduplication after authentication by the adapter, not an authentication implementation.
    // No lease timer erases evidence. Acknowledged terminal entries retire behind a sequence fence;
    // unresolved entries survive that fence and remain queryable. Old absent entries can never execute again.
    public sealed class OperationJournal
    {
        private readonly Dictionary<string, OperationRecord> records = new Dictionary<string, OperationRecord>(StringComparer.Ordinal);
        private readonly string world, session;
        private readonly int capacity;
        private bool closed;
        private long retiredThrough;
        public OperationJournal(string world, string sessionNonce, int capacity = 4096)
        {
            this.world = Contract.Id(world, nameof(world)); session = Contract.Id(sessionNonce, nameof(sessionNonce));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity;
        }
        public int Count => records.Count;
        public Registration Register(OperationId id, string payloadFingerprint, out OperationRecord? record)
        {
            Contract.Id(payloadFingerprint, nameof(payloadFingerprint)); record = null;
            if (closed || id.World != world || id.SessionNonce != session) return Registration.WrongSession;
            if (id.Sequence < 1 || id.ActionId != id.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)) return Registration.InvalidSequence;
            if (records.TryGetValue(id.Key, out record))
                return record.PayloadFingerprint == payloadFingerprint && record.Id.Sequence == id.Sequence ? Registration.Replay : Registration.PayloadMismatch;
            if (id.Sequence <= retiredThrough) return Registration.Retired;
            if (records.Count >= capacity) return Registration.CapacityReached;
            record = new OperationRecord(id, payloadFingerprint); records.Add(id.Key, record); return Registration.New;
        }
        public OperationRecord? Query(OperationId id) => records.TryGetValue(id.Key, out var record) ? record : null;
        public bool Resolve(OperationId id, string payloadFingerprint, OperationOutcome outcome, string receiptFingerprint)
        {
            Contract.Id(receiptFingerprint, nameof(receiptFingerprint));
            if (!records.TryGetValue(id.Key, out var record) || record.Id.Sequence != id.Sequence || record.PayloadFingerprint != payloadFingerprint ||
                outcome == OperationOutcome.Pending || !Enum.IsDefined(typeof(OperationOutcome), outcome)) return false;
            if (record.Outcome != OperationOutcome.Pending && record.Outcome != OperationOutcome.Unknown)
                return record.Outcome == outcome && record.ReceiptFingerprint == receiptFingerprint;
            if (record.Outcome == OperationOutcome.Unknown && outcome == OperationOutcome.Unknown)
                return record.ReceiptFingerprint == receiptFingerprint;
            record.Outcome = outcome; record.ReceiptFingerprint = receiptFingerprint; return true;
        }
        // Closed sessions reject new/duplicate execution but retain queryable obligations for reconciliation.
        public void Close() { closed = true; }
        // Caller has acknowledged final receipts through this sequence and will not create older requests.
        // This is an explicit protocol acknowledgement, never a lease/clock timeout.
        public void RetireAcknowledged(long through)
        {
            if (through < retiredThrough) throw new ArgumentOutOfRangeException(nameof(through));
            var remove = new List<string>();
            foreach (var pair in records)
                if (pair.Value.Id.Sequence <= through && pair.Value.Outcome != OperationOutcome.Pending && pair.Value.Outcome != OperationOutcome.Unknown)
                    remove.Add(pair.Key);
            foreach (string key in remove) records.Remove(key);
            retiredThrough = through;
        }
    }
}
