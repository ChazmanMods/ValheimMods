using System;
using RunicSentinel.Runtime;
using RunicSentinel.Contracts;

namespace RunicSentinel.Api
{
    /// <summary>
    /// Optional same-server-process bridge for audited standalone Runic modules. It is not a
    /// client RPC and grants no new authority; the caller must already have rejected the request.
    /// </summary>
    public static class SentinelIntegrationApi
    {
        private static readonly object Gate = new object();
        private static SentinelEnforcementRuntime _service;

        internal static void Attach(SentinelEnforcementRuntime service)
        {
            lock (Gate) _service = service;
        }

        internal static void Detach(SentinelEnforcementRuntime service)
        {
            lock (Gate)
                if (ReferenceEquals(_service, service)) _service = null;
        }

        public static bool ReportRejectedServerRequest(
            string sourceModuleId,
            long peerId,
            string actor,
            string rule,
            string correlationId,
            int confidence,
            string detail)
        {
            if (confidence < (int)FindingConfidence.Informational ||
                confidence > (int)FindingConfidence.Conclusive) return false;
            SentinelEnforcementRuntime service;
            lock (Gate) service = _service;
            if (service == null) return false;
            return service.ReportRejectedServerRequest(
                sourceModuleId,
                peerId,
                actor,
                rule,
                correlationId,
                (FindingConfidence)confidence,
                detail);
        }
    }
}
