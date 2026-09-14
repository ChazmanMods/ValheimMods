using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class CachePerformance
    {
        internal static long UiHits, UiMisses, RefreshHits, SourceQueries, PayloadHits, PayloadLoads, QueryTicks;
        private static float _nextReport;
        private static bool _reporting;
        internal static long StartQuery() { SourceQueries++; return Stopwatch.GetTimestamp(); }
        internal static void EndQuery(long started) => QueryTicks += Stopwatch.GetTimestamp() - started;
        internal static void Update()
        {
            bool enabled = Configuration.LogCacheStats != null && Configuration.LogCacheStats.Value;
            float now = Time.realtimeSinceStartup;
            if (!enabled || !_reporting)
            {
                Reset(); _reporting = enabled; _nextReport = now + 5f;
                return;
            }
            if (now < _nextReport) return;
            Plugin.Log?.LogInfo("[Cache " + Plugin.Version + "] 5s: UI hits=" + UiHits + ", misses=" + UiMisses +
                "; refresh reuse=" + RefreshHits + "; source queries=" + SourceQueries +
                "; chest hits=" + PayloadHits + ", loads=" + PayloadLoads +
                "; source-query ms=" + (QueryTicks * 1000.0 / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture));
            Reset(); _nextReport = now + 5f;
        }
        private static void Reset() => UiHits = UiMisses = RefreshHits = SourceQueries = PayloadHits = PayloadLoads = QueryTicks = 0;
    }
}
