using System;
using System.Collections.Generic;
using System.Threading;

namespace RunicCrafting.Core
{
    public enum AccessPath { Native, CooperatingOwner, Server, CustomAuthority }

    public sealed class SourceCapability
    {
        public SourceCapability(string authorityId, string sourceClass, AccessPath path, bool permission,
            bool exclusion, bool freshSnapshot, bool exactDebit, bool exactReturn, bool correlatedStatus)
        {
            AuthorityId = Contract.Id(authorityId, nameof(authorityId)); SourceClass = Contract.Id(sourceClass, nameof(sourceClass)); Path = path;
            Permission = permission; Exclusion = exclusion; FreshSnapshot = freshSnapshot;
            ExactDebit = exactDebit; ExactReturn = exactReturn; CorrelatedStatus = correlatedStatus;
        }
        public string AuthorityId { get; }
        public string SourceClass { get; }
        public AccessPath Path { get; }
        public bool Permission { get; }
        public bool Exclusion { get; }
        public bool FreshSnapshot { get; }
        public bool ExactDebit { get; }
        public bool ExactReturn { get; }
        public bool CorrelatedStatus { get; }
        public bool CanSupply => Permission && Exclusion && FreshSnapshot && ExactDebit && ExactReturn && CorrelatedStatus;
    }

    // Implementations must use actual authority, never a UI mirror. No adapter is advertised by this core library.
    public interface ISourceAdapter
    {
        SourceCapability Capability { get; }
        void RequestAccess(OperationId operation, string sourceId);
        AccessResult PollAccess(OperationId operation);
        void Debit(OperationId operation, string payloadFingerprint, IReadOnlyList<MaterialSlice> slices);
        OperationRecord? Query(OperationId operation);
        void Return(OperationId operation, string payloadFingerprint, CustodySlice slice, int quantity);
        void Release(OperationId operation);
    }
    public sealed class AccessResult
    {
        public AccessResult(OperationOutcome outcome, MaterialSource? snapshot = null)
        {
            if (outcome == OperationOutcome.Confirmed && (snapshot == null || snapshot.Availability != SourceAvailability.Usable))
                throw new ArgumentException("A grant must contain freshly loaded authoritative stock.");
            Outcome = outcome; Snapshot = outcome == OperationOutcome.Confirmed ? snapshot : null;
        }
        public OperationOutcome Outcome { get; }
        public MaterialSource? Snapshot { get; }
    }

    public sealed class GameThreadGuard
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        public void AssertCurrent()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Inventory mutation requires the bound game thread.");
        }
    }
}
