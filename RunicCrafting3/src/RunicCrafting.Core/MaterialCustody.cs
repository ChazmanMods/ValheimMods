using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    public enum ReturnState { None, Pending, Unknown, Confirmed, Blocked }
    public sealed class CustodySlice
    {
        internal CustodySlice(string id, MaterialSlice material, int quantity)
        { Id = id; Material = material; Debited = quantity; }
        public string Id { get; }
        public MaterialSlice Material { get; }
        public int Debited { get; }
        public int Consumed { get; internal set; }
        public int Returned { get; internal set; }
        public int Held => Debited - Consumed - Returned;
        public int ConfirmedUnused { get; internal set; }
        public ReturnState ReturnState { get; internal set; }
        public string? ReturnOperation { get; internal set; }
        public int PendingReturn { get; internal set; }
    }

    // Bookkeeping only. Adapters perform exact native removals/additions on the game thread.
    // No snapshot restore, drop, refund-on-timeout, or inventory mutation is implemented here.
    public sealed class MaterialCustody
    {
        private readonly Dictionary<string, CustodySlice> slices = new Dictionary<string, CustodySlice>(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Slice, int Quantity, OperationOutcome Outcome, int Returned)> returns =
            new Dictionary<string, (string, int, OperationOutcome, int)>(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Digest, Dictionary<string, int> Consumed)> resolutions =
            new Dictionary<string, (string, Dictionary<string, int>)>(StringComparer.Ordinal);
        public IReadOnlyCollection<CustodySlice> Slices => slices.Values.ToArray();
        public int Count => slices.Count;
        public long Held => slices.Values.Sum(x => (long)x.Held);
        public bool AllSettled => slices.Values.All(x => x.Held == 0);

        public void ConfirmDebit(string receiptSliceId, MaterialSlice material, int quantity)
        {
            Contract.Id(receiptSliceId, nameof(receiptSliceId));
            if (quantity < 1 || quantity > material.Quantity) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (slices.TryGetValue(receiptSliceId, out var prior))
            {
                if (prior.Debited != quantity || !SameSlice(prior.Material, material)) throw new InvalidOperationException("Changed debit receipt.");
                return;
            }
            if (resolutions.Count != 0) throw new InvalidOperationException("Custody already has a final disposition.");
            if (slices.Values.Where(x => SameSlice(x.Material, material)).Sum(x => (long)x.Debited) + quantity > material.Quantity)
                throw new InvalidOperationException("Debit exceeds planned slice.");
            slices.Add(receiptSliceId, new CustodySlice(receiptSliceId, material, quantity));
        }
        private static bool SameSlice(MaterialSlice a, MaterialSlice b) => a.CostId == b.CostId && a.SourceId == b.SourceId
            && a.SourceRevision == b.SourceRevision && a.InstanceId == b.InstanceId && a.Identity.Fingerprint == b.Identity.Fingerprint
            && a.Quantity == b.Quantity && a.ExpectedStack == b.ExpectedStack;

        // A correlated, final effect receipt assigns every debited unit as consumed or confirmed unused.
        // An unknown effect has no such receipt and cannot call this successfully through the coordinator.
        public void ConfirmDisposition(string effectOperation, string receiptFingerprint, IReadOnlyDictionary<string, int> consumed)
        {
            Contract.Id(effectOperation, nameof(effectOperation)); Contract.Id(receiptFingerprint, nameof(receiptFingerprint));
            var copy = consumed.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (resolutions.TryGetValue(effectOperation, out var prior))
            {
                if (prior.Digest != receiptFingerprint || prior.Consumed.Count != copy.Count ||
                    prior.Consumed.Any(x => !copy.TryGetValue(x.Key, out int count) || count != x.Value))
                    throw new InvalidOperationException("Changed effect receipt.");
                return;
            }
            if (resolutions.Count != 0 || consumed.Count != slices.Count || consumed.Keys.Any(x => !slices.ContainsKey(x)))
                throw new InvalidOperationException("Disposition must cover exactly this action's custody.");
            foreach (var pair in consumed)
                if (pair.Value < 0 || pair.Value > slices[pair.Key].Debited) throw new ArgumentOutOfRangeException(nameof(consumed));
            foreach (var pair in consumed)
            {
                var slice = slices[pair.Key]; slice.Consumed = pair.Value; slice.ConfirmedUnused = slice.Debited - pair.Value;
            }
            resolutions.Add(effectOperation, (receiptFingerprint, copy));
        }
        public bool BeginReturn(string sliceId, string returnOperation, int quantity)
        {
            var slice = slices[sliceId]; Contract.Id(returnOperation, nameof(returnOperation));
            if (quantity < 1 || quantity > slice.ConfirmedUnused - slice.Returned) return false;
            if (slice.ReturnState == ReturnState.Pending || slice.ReturnState == ReturnState.Unknown) return false;
            if (returns.ContainsKey(returnOperation)) return false;
            returns.Add(returnOperation, (sliceId, quantity, OperationOutcome.Pending, 0));
            slice.ReturnOperation = returnOperation; slice.PendingReturn = quantity; slice.ReturnState = ReturnState.Pending; return true;
        }
        public bool ResolveReturn(string sliceId, string operation, OperationOutcome outcome, int returned)
        {
            var slice = slices[sliceId];
            if (!returns.TryGetValue(operation, out var previous) || previous.Slice != sliceId) return false;
            if (previous.Outcome != OperationOutcome.Pending && previous.Outcome != OperationOutcome.Unknown)
                return previous.Outcome == outcome && previous.Returned == returned;
            if (slice.ReturnOperation != operation) return false;
            if (slice.ReturnState != ReturnState.Pending && slice.ReturnState != ReturnState.Unknown) return false;
            if (outcome == OperationOutcome.Unknown)
            {
                if (returned != 0) return false; slice.ReturnState = ReturnState.Unknown;
                returns[operation] = (sliceId, previous.Quantity, outcome, 0); return true;
            }
            if (outcome == OperationOutcome.Busy || outcome == OperationOutcome.Denied || outcome == OperationOutcome.StaleBeforeSpend)
            {
                if (returned != 0) return false; slice.ReturnState = ReturnState.Blocked;
                returns[operation] = (sliceId, previous.Quantity, outcome, 0); return true;
            }
            if ((outcome != OperationOutcome.Confirmed || returned != slice.PendingReturn) &&
                (outcome != OperationOutcome.ConfirmedPartial || returned < 0 || returned >= slice.PendingReturn)) return false;
            slice.Returned = checked(slice.Returned + returned); slice.ReturnState = ReturnState.Confirmed;
            returns[operation] = (sliceId, previous.Quantity, outcome, returned); return true;
        }
    }
}
