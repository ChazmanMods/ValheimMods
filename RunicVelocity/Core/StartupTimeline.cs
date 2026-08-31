using System;
using System.Collections.Generic;
using System.Diagnostics;
using RunicVelocity.Contracts;

namespace RunicVelocity.Core
{
    internal sealed class StartupTimeline
    {
        internal const int MaximumStages = 64;
        private readonly object _gate = new object();
        private readonly long _origin;
        private readonly List<StartupStageSample> _stages = new List<StartupStageSample>();
        private readonly HashSet<string> _recorded = new HashSet<string>(StringComparer.Ordinal);

        internal StartupTimeline(long origin)
        {
            _origin = origin > 0L ? origin : Stopwatch.GetTimestamp();
        }

        internal StartupTimelineSnapshot Current
        {
            get { lock (_gate) return new StartupTimelineSnapshot(_stages); }
        }

        internal bool Record(string stageId, long timestamp = 0L)
        {
            if (string.IsNullOrWhiteSpace(stageId) || stageId.Length > 96) return false;
            lock (_gate)
            {
                if (_stages.Count >= MaximumStages || !_recorded.Add(stageId)) return false;
                long current = timestamp > 0L ? timestamp : Stopwatch.GetTimestamp();
                long elapsed = Math.Max(0L, current - _origin);
                _stages.Add(new StartupStageSample(
                    stageId,
                    elapsed * 1000d / Stopwatch.Frequency));
                return true;
            }
        }
    }
}
