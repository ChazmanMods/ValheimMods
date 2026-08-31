using System;
using System.Collections.Generic;

namespace RunicCrafting.Integration
{
    internal static class CraftingDiagnostics
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, DateTime> LastRepeatedTraceUtc =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(2);

        internal static void TraceAction(string operation, string reason, string detail = null) =>
            Trace(operation, reason, detail, suppressRepeated: false);

        internal static void TraceGate(string operation, string reason, string detail = null) =>
            Trace(operation, reason, detail, suppressRepeated: true);

        internal static void ResetRepeatSuppression()
        {
            lock (Gate) LastRepeatedTraceUtc.Clear();
        }

        private static void Trace(
            string operation,
            string reason,
            string detail,
            bool suppressRepeated)
        {
            if (Configuration.DetailedLogging == null || !Configuration.DetailedLogging.Value) return;
            operation = string.IsNullOrWhiteSpace(operation) ? "operation" : operation.Trim();
            reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();

            if (suppressRepeated)
            {
                string key = operation + "\n" + reason;
                DateTime now = DateTime.UtcNow;
                lock (Gate)
                {
                    if (LastRepeatedTraceUtc.TryGetValue(key, out DateTime previous) &&
                        now - previous < RepeatWindow)
                        return;
                    if (LastRepeatedTraceUtc.Count >= 256) LastRepeatedTraceUtc.Clear();
                    LastRepeatedTraceUtc[key] = now;
                }
            }

            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : "; " + detail.Trim();
            Plugin.Log?.LogInfo("[Detailed] " + operation + " -> " + reason + suffix);
        }
    }
}
