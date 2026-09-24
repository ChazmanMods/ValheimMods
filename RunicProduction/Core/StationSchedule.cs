using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    // Session-local backoff. The runtime bounds examinations separately from successful actions.
    internal sealed class StationSchedule
    {
        private readonly Dictionary<int, double> _due = new Dictionary<int, double>();
        private readonly Dictionary<int, int> _failures = new Dictionary<int, int>();
        internal bool IsDue(int id, double now) => !_due.TryGetValue(id, out double next) || now >= next;
        internal void Observe(int id, double now, bool changed, double interval)
        {
            int misses = changed ? 0 : Math.Min(3, (_failures.TryGetValue(id, out int count) ? count : 0) + 1);
            _failures[id] = misses;
            _due[id] = now + Math.Min(10, interval * (1 << misses));
        }
        internal void Remove(int id) { _due.Remove(id); _failures.Remove(id); }
        internal void Clear() { _due.Clear(); _failures.Clear(); }
    }
}
