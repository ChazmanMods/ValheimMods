using System;
using System.Collections.Generic;

namespace RunicAutomation
{
    public enum MutationOutcome { RejectedWithoutChanges, Committed, RolledBack, Indeterminate }

    // Each embedded copy wraps the same framework-only process state.
    public sealed class MutationLease : IDisposable
    {
        internal readonly object[] State;
        private HashSet<object> _endpoints => (HashSet<object>)State[4];
        internal MutationLease(string purpose) { State = new object[] { Guid.NewGuid().ToString("N"), purpose, 0, true, new HashSet<object>() }; }
        internal MutationLease(object[] state) { State = state; }
        public string Id => (string)State[0];
        public string Purpose => (string)State[1];
        public MutationOutcome Outcome { get => (MutationOutcome)(int)State[2]; private set => State[2] = (int)value; }
        public bool Active { get => (bool)State[3]; private set => State[3] = value; }
        public void Track(object endpoint)
        {
            if (!Active || !ReferenceEquals(MutationGate.Current?.State, State)) throw new InvalidOperationException("Inactive mutation lease.");
            if (endpoint != null) _endpoints.Add(endpoint);
        }
        public void Complete(MutationOutcome outcome)
        {
            if (!Active) return;
            if (Outcome == MutationOutcome.Indeterminate) return;
            Outcome = outcome;
            if (outcome == MutationOutcome.Indeterminate)
                foreach (object endpoint in _endpoints) MutationGate.Block(endpoint);
            var endpoints = new List<string>();
            foreach (object endpoint in _endpoints)
                if (endpoints.Count < 128) endpoints.Add(endpoint.ToString());
            MutationGate.Report(Id + " " + Purpose + " " + outcome + " endpoints=[" + string.Join(",", endpoints) + "]");
        }
        public void Dispose()
        {
            lock (MutationGate.Sync)
            {
                if (!Active) return;
                Active = false;
                if (ReferenceEquals(MutationGate.Current?.State, State)) MutationGate.Current = null;
            }
        }
    }

    public static class MutationGate
    {
        internal static object Sync => SharedState.Sync;
        private static object[] State => SharedState.Get("mutation", () => new object[] { null, new HashSet<object>(), false, null });
        private static HashSet<object> Blocked => (HashSet<object>)State[1];
        private static bool _overflow { get => (bool)State[2]; set => State[2] = value; }
        public static MutationLease Current { get => State[0] is object[] active ? new MutationLease(active) : null; internal set => State[0] = value?.State; }
        public static Action<string> Diagnostic { get => (Action<string>)State[3]; set => State[3] = value; }
        public static bool TryBegin(string purpose, out MutationLease lease)
        {
            lock (Sync)
            {
                lease = null;
                if (Current != null || _overflow) return false;
                Current = lease = new MutationLease(purpose);
                return true;
            }
        }
        public static bool IsBlocked(object endpoint)
        { lock (Sync) return _overflow || endpoint != null && Blocked.Contains(endpoint); }
        public static void Block(object endpoint)
        {
            lock (Sync)
            {
                if (endpoint == null) return;
                if (Blocked.Count >= 16384) _overflow = true;
                else Blocked.Add(endpoint);
            }
        }
        internal static void Report(string text) { try { Diagnostic?.Invoke(text); } catch { } }
        // World-session boundary only; never clear uncertainty on a config change or a module reload.
        public static void EndSession()
        { lock (Sync) { Current?.Dispose(); Blocked.Clear(); _overflow = false; } }
    }

    public sealed class MutationIndeterminateException : InvalidOperationException
    {
        public MutationIndeterminateException(string detail, Exception inner = null)
            : base("Inventory recovery needs inspection: " + detail, inner) { }
    }
}
