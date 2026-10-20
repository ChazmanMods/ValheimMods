using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    // A game session owns one registry. Uncertainty releases the input control but isolates affected sources.
    // Old registries retain unresolved records after Close; they must never be reused for a new session.
    public sealed class ActionRegistry
    {
        private readonly string world, session;
        private readonly int capacity;
        private long nextId;
        private bool closed;
        private readonly Dictionary<string, ActionCoordinator> actions = new Dictionary<string, ActionCoordinator>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActionCoordinator> controls = new Dictionary<string, ActionCoordinator>(StringComparer.Ordinal);
        public ActionRegistry(string world, string sessionNonce, int capacity = 64)
        {
            this.world = Contract.Id(world, nameof(world)); session = Contract.Id(sessionNonce, nameof(sessionNonce));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity;
        }
        public bool TryStart(string control, ActionKind kind, string selection, long nowMilliseconds, out ActionCoordinator? action,
            string tool = "", string target = "", string upgradeInstance = "", int units = 1)
        {
            action = null; Contract.Id(control, nameof(control)); Refresh();
            if (closed || actions.Count >= capacity || controls.ContainsKey(control)) return false;
            var intent = new ActionIntent(world, session, checked(++nextId).ToString(System.Globalization.CultureInfo.InvariantCulture),
                kind, selection, tool, target, upgradeInstance, units);
            action = new ActionCoordinator(intent, nowMilliseconds); actions.Add(intent.Id, action); controls.Add(control, action); return true;
        }
        public bool IsSourceIsolated(string sourceId, ActionCoordinator? requester = null) => actions.Values.Any(a =>
            !ReferenceEquals(a, requester) && !a.Terminal && a.PossibleMutation && a.SourceIds.Contains(sourceId));
        public bool MarkReady(ActionCoordinator action, MaterialPlan plan, ActionObservation observation)
        {
            if (closed || !actions.TryGetValue(action.Intent.Id, out var owned) || !ReferenceEquals(owned, action)) return false;
            if (plan.Slices.Any(s => IsSourceIsolated(s.SourceId, action))) return false;
            return action.MarkReady(plan, observation);
        }
        // Check again immediately before spend; another prepared action may have started since MarkReady.
        public string? BeginSpend(ActionCoordinator action, string freshPlanFingerprint, ActionObservation observation)
        {
            if (closed || !actions.TryGetValue(action.Intent.Id, out var owned) || !ReferenceEquals(owned, action) ||
                action.SourceIds.Any(id => IsSourceIsolated(id, action))) return null;
            return action.BeginSpend(freshPlanFingerprint, observation);
        }
        public void Refresh()
        {
            foreach (var key in controls.Where(p => p.Value.Terminal || p.Value.State == ActionState.Uncertain || p.Value.State == ActionState.Returning).Select(p => p.Key).ToArray())
                controls.Remove(key);
            foreach (var key in actions.Where(p => p.Value.Terminal).Select(p => p.Key).ToArray()) actions.Remove(key);
        }
        public void Close()
        {
            closed = true; foreach (var action in actions.Values) action.Cancel(); Refresh();
        }
    }
}
