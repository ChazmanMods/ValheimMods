using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    public enum ActionKind { Craft, Upgrade, Build, ManualInput, Repair }
    public enum ActionState { Planned, Acquiring, Ready, Spending, MaterialsConfirmed, Applying, Completed, Returning, Uncertain, Cancelled, ConfirmedReturned }

    public sealed class ActionIntent
    {
        public ActionIntent(string world, string sessionNonce, string id, ActionKind kind, string selection,
            string tool = "", string target = "", string upgradeInstance = "", int units = 1)
        {
            World = Contract.Id(world, nameof(world)); SessionNonce = Contract.Id(sessionNonce, nameof(sessionNonce));
            Id = Contract.Id(id, nameof(id)); Selection = Contract.Id(selection, nameof(selection)); Kind = kind;
            Tool = tool; Target = target; UpgradeInstance = upgradeInstance;
            if (units < 1 || (kind != ActionKind.ManualInput && units != 1)) throw new ArgumentOutOfRangeException(nameof(units));
            if (kind == ActionKind.Upgrade) Contract.Id(upgradeInstance, nameof(upgradeInstance));
            if (kind == ActionKind.Build) Contract.Id(tool, nameof(tool));
            Units = units;
        }
        public string World { get; }
        public string SessionNonce { get; }
        public string Id { get; }
        public ActionKind Kind { get; }
        public string Selection { get; }
        public string Tool { get; }
        public string Target { get; }
        // Binding-issued instance + location/change generation, not a prefab, name, or slot alone.
        public string UpgradeInstance { get; }
        public int Units { get; }
        public bool Matches(ActionObservation current) => current.ConditionsValid && current.World == World && current.SessionNonce == SessionNonce
            && current.Selection == Selection && current.Tool == Tool && current.Target == Target && current.UpgradeInstance == UpgradeInstance;
    }

    public sealed class PlacementPose
    {
        public PlacementPose(double x, double y, double z, double rotationX, double rotationY, double rotationZ, double rotationW, string snapIdentity = "")
        {
            var values = new[] { x, y, z, rotationX, rotationY, rotationZ, rotationW };
            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Y = y; Z = z; RotationX = rotationX; RotationY = rotationY; RotationZ = rotationZ; RotationW = rotationW; SnapIdentity = snapIdentity;
        }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
        public double RotationW { get; }
        public string SnapIdentity { get; }
    }

    public sealed class ActionObservation
    {
        public ActionObservation(string world, string sessionNonce, string selection, bool conditionsValid,
            string tool = "", string target = "", string upgradeInstance = "", PlacementPose? pose = null)
        {
            World = world; SessionNonce = sessionNonce; Selection = selection; ConditionsValid = conditionsValid;
            Tool = tool; Target = target; UpgradeInstance = upgradeInstance; Pose = pose;
        }
        public string World { get; }
        public string SessionNonce { get; }
        public string Selection { get; }
        // Adapter freshly checks native station/capacity/reach/stamina/permission, death/teleport/menu/build mode.
        public bool ConditionsValid { get; }
        public string Tool { get; }
        public string Target { get; }
        public string UpgradeInstance { get; }
        public PlacementPose? Pose { get; }
    }

    public sealed class DebitEvidence
    {
        public DebitEvidence(string receiptId, int sliceIndex, int quantity)
        {
            ReceiptId = Contract.Id(receiptId, nameof(receiptId));
            if (sliceIndex < 0 || quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
            SliceIndex = sliceIndex; Quantity = quantity;
        }
        public string ReceiptId { get; }
        public int SliceIndex { get; }
        public int Quantity { get; }
    }

    public sealed class EffectCommand
    {
        internal EffectCommand(string operation, PlacementPose? pose) { Operation = operation; Pose = pose; }
        public string Operation { get; }
        public PlacementPose? Pose { get; }
    }

    // One intent, explicitly driven by a game-thread adapter. No Unity references, thread-static pending state,
    // global busy gate, timers that refund, or hidden mutation retries.
    public sealed class ActionCoordinator
    {
        private readonly Dictionary<string, DebitEvidence> debitEvidence = new Dictionary<string, DebitEvidence>(StringComparer.Ordinal);
        private readonly long startedAt;
        private MaterialPlan? plan;
        private string? spendOperation, effectOperation;
        private bool effectIssued, spendResolved;
        private int acceptedUnits;
        private string? finalEffectDigest;
        private OperationOutcome? finalSpendOutcome;
        public ActionCoordinator(ActionIntent intent, long nowMilliseconds)
        { Intent = intent; startedAt = nowMilliseconds; }
        public ActionIntent Intent { get; }
        public ActionState State { get; private set; } = ActionState.Planned;
        public MaterialCustody Custody { get; } = new MaterialCustody();
        public string? AuthorityPath { get; private set; }
        public bool CancellationRequested { get; private set; }
        public bool PossibleMutation => spendOperation != null;
        public int Replans { get; private set; }
        public IReadOnlyList<string> SourceIds { get; private set; } = Array.Empty<string>();
        public bool Terminal => State == ActionState.Completed || State == ActionState.Cancelled || State == ActionState.ConfirmedReturned;
        public bool ShowPending(long nowMilliseconds) => !Terminal && nowMilliseconds - startedAt >= 250;

        public bool Acquire(string path, ActionObservation current)
        {
            if (State != ActionState.Planned || CancellationRequested || !Intent.Matches(current)) return false;
            AuthorityPath = Contract.Id(path, nameof(path)); State = ActionState.Acquiring; return true;
        }
        public bool Replan(string path, long nowMilliseconds, ActionObservation current)
        {
            if (PossibleMutation || (State != ActionState.Acquiring && State != ActionState.Ready) ||
                Replans >= 2 || nowMilliseconds < startedAt || nowMilliseconds - startedAt > 1200 || !Intent.Matches(current)) return false;
            Replans++; AuthorityPath = Contract.Id(path, nameof(path)); plan = null; SourceIds = Array.Empty<string>(); State = ActionState.Acquiring; return true;
        }
        public bool MarkReady(MaterialPlan freshPlan, ActionObservation current)
        {
            if (State != ActionState.Acquiring || freshPlan.Status != PlanStatus.Ready || !Intent.Matches(current)) return false;
            plan = freshPlan; SourceIds = Contract.Copy(freshPlan.Slices.Select(x => x.SourceId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
            State = ActionState.Ready; return true;
        }
        // freshPlanFingerprint must come from immediate validation under actual-authority exclusion.
        public string? BeginSpend(string freshPlanFingerprint, ActionObservation current)
        {
            if (State != ActionState.Ready || plan == null || plan.Fingerprint != freshPlanFingerprint || spendOperation != null) return null;
            if (!Intent.Matches(current)) { Cancel(); return null; }
            spendOperation = new OperationId(Intent.World, Intent.SessionNonce, Intent.Id, AuthorityPath!, OperationPhase.Debit).Key;
            State = ActionState.Spending; return spendOperation;
        }

        public bool ResolveSpend(string path, string operation, OperationOutcome outcome, IEnumerable<DebitEvidence> evidence)
        {
            if (path != AuthorityPath || operation != spendOperation || plan == null) return false;
            var incoming = evidence.ToArray();
            if (incoming.Any(x => x.SliceIndex >= plan.Slices.Count) || incoming.Select(x => x.ReceiptId).Distinct(StringComparer.Ordinal).Count() != incoming.Length) return false;
            var byId = incoming.ToDictionary(x => x.ReceiptId, StringComparer.Ordinal);
            if (debitEvidence.Any(x => !byId.TryGetValue(x.Key, out var value) || value.SliceIndex != x.Value.SliceIndex || value.Quantity != x.Value.Quantity)) return false;
            var totals = incoming.GroupBy(x => x.SliceIndex).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
            if (totals.Any(x => x.Value > plan.Slices[x.Key].Quantity)) return false;
            bool full = Enumerable.Range(0, plan.Slices.Count).All(i => totals.TryGetValue(i, out long q) && q == plan.Slices[i].Quantity);
            if (outcome == OperationOutcome.Confirmed && !full) return false;
            if (outcome == OperationOutcome.ConfirmedPartial && (full || incoming.Length == 0)) return false;
            if ((outcome == OperationOutcome.Busy || outcome == OperationOutcome.Denied || outcome == OperationOutcome.Unsupported || outcome == OperationOutcome.StaleBeforeSpend) && incoming.Length != 0) return false;
            if (outcome == OperationOutcome.Pending || !Enum.IsDefined(typeof(OperationOutcome), outcome)) return false;
            if (spendResolved) return finalSpendOutcome == outcome && incoming.Length == debitEvidence.Count;
            if (State != ActionState.Spending && State != ActionState.Uncertain) return false;
            foreach (var value in incoming)
            {
                Custody.ConfirmDebit(value.ReceiptId, plan.Slices[value.SliceIndex], value.Quantity);
                debitEvidence[value.ReceiptId] = value;
            }
            if (outcome == OperationOutcome.Unknown) { State = ActionState.Uncertain; return true; }
            spendResolved = true; finalSpendOutcome = outcome;
            if (outcome != OperationOutcome.Confirmed && outcome != OperationOutcome.ConfirmedPartial)
            { State = ActionState.Cancelled; return true; }
            State = ActionState.MaterialsConfirmed;
            if (CancellationRequested || outcome == OperationOutcome.ConfirmedPartial) ReturnWithoutEffect();
            return true;
        }
        public EffectCommand? BeginEffect(ActionObservation current)
        {
            if (State != ActionState.MaterialsConfirmed || effectIssued) return null;
            if (CancellationRequested || !Intent.Matches(current) || (Intent.Kind == ActionKind.Build && current.Pose == null))
            { ReturnWithoutEffect(); return null; }
            effectIssued = true;
            effectOperation = new OperationId(Intent.World, Intent.SessionNonce, Intent.Id, AuthorityPath!, OperationPhase.Effect).Key;
            State = ActionState.Applying;
            return new EffectCommand(effectOperation, Intent.Kind == ActionKind.Build ? current.Pose : null);
        }

        public bool ResolveEffect(string path, string operation, OperationOutcome outcome, string receiptFingerprint,
            int accepted, IReadOnlyDictionary<string, int> consumed)
        {
            if (path != AuthorityPath || operation != effectOperation || !effectIssued || plan == null) return false;
            if (outcome == OperationOutcome.Unknown)
            {
                if (State != ActionState.Applying && State != ActionState.Uncertain) return false;
                if (accepted != 0 || consumed.Count != 0) return false;
                State = ActionState.Uncertain; return true;
            }
            if (outcome != OperationOutcome.Confirmed && outcome != OperationOutcome.ConfirmedPartial) return false;
            if (accepted < 0 || accepted > Intent.Units) return false;
            if (outcome == OperationOutcome.ConfirmedPartial && (accepted == 0 || accepted == Intent.Units)) return false;
            var held = Custody.Slices;
            if (consumed.Count != held.Count || held.Any(s => !consumed.TryGetValue(s.Id, out int quantity) || quantity < 0 || quantity > s.Debited)) return false;
            foreach (var cost in plan.Costs)
            {
                long numerator = checked((long)cost.Quantity * accepted);
                if (numerator % Intent.Units != 0) return false;
                long actual = held.Where(s => s.Material.CostId == cost.Id).Sum(s => (long)consumed[s.Id]);
                if (actual != numerator / Intent.Units) return false;
            }
            if (finalEffectDigest != null)
            {
                if (finalEffectDigest != receiptFingerprint || acceptedUnits != accepted) return false;
                return held.All(s => s.Consumed == consumed[s.Id]);
            }
            if (State != ActionState.Applying && State != ActionState.Uncertain) return false;
            Custody.ConfirmDisposition(operation, receiptFingerprint, consumed);
            finalEffectDigest = receiptFingerprint; acceptedUnits = accepted;
            State = Custody.AllSettled ? (accepted > 0 ? ActionState.Completed : ActionState.ConfirmedReturned) : ActionState.Returning; return true;
        }
        public void Cancel()
        {
            if (Terminal) return;
            CancellationRequested = true;
            if (!PossibleMutation) { State = ActionState.Cancelled; return; }
            if (State == ActionState.MaterialsConfirmed) ReturnWithoutEffect();
            // Spending/applying/unknown keep their obligation. Cancellation is not proof of zero mutation.
        }
        private void ReturnWithoutEffect()
        {
            if (effectIssued || !spendResolved) throw new InvalidOperationException("Effect absence is unproven.");
            Custody.ConfirmDisposition(Intent.Id + ":no-effect", "effect-never-issued", Custody.Slices.ToDictionary(x => x.Id, _ => 0, StringComparer.Ordinal));
            State = Custody.AllSettled ? ActionState.ConfirmedReturned : ActionState.Returning;
        }
        public bool FinishReturns()
        {
            if (State != ActionState.Returning || !Custody.AllSettled) return false;
            State = acceptedUnits > 0 ? ActionState.Completed : ActionState.ConfirmedReturned; return true;
        }
    }
}
