using System;
using System.Reflection;

namespace RunicPortals.Integration
{
    /// <summary>Optional violation telemetry; every request is rejected before this is called.</summary>
    internal static class SentinelSecurityBridge
    {
        private static readonly object Gate = new object();
        private static MethodInfo _report;

        internal static void Report(
            long peerId,
            string rule,
            string correlationId,
            int confidence,
            string detail)
        {
            if (peerId == 0L) return;
            try
            {
                MethodInfo report;
                lock (Gate)
                {
                    if (_report == null)
                    {
                        Type api = Type.GetType(
                            "RunicSentinel.Api.SentinelIntegrationApi, RunicSentinel",
                            false) ?? Type.GetType(
                            "RunicSentinel.Api.SentinelIntegrationApi, RunicSentinelServer",
                            false);
                        _report = api?.GetMethod(
                            "ReportRejectedServerRequest",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[]
                            {
                                typeof(string), typeof(long), typeof(string), typeof(string),
                                typeof(string), typeof(int), typeof(string)
                            },
                            null);
                    }
                    report = _report;
                }
                report?.Invoke(null, new object[]
                {
                    "runic.portals",
                    peerId,
                    "peer:" + peerId,
                    rule,
                    string.IsNullOrEmpty(correlationId) ? "portal-request" : correlationId,
                    confidence,
                    detail
                });
            }
            catch
            {
                // Optional telemetry must never weaken or change the already-made denial.
            }
        }
    }
}
