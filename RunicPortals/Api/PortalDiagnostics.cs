using System;
using System.Collections.Generic;

namespace RunicPortals.Api
{
    public enum PortalDiagnosticCode
    {
        RuntimeReady = 1,
        RuntimeDisabled = 2,
        SnapshotAccepted = 3,
        SnapshotRejected = 4,
        EditAccepted = 5,
        EditRejected = 6,
        RouteSelected = 7,
        RouteRejected = 8,
        RouteCommitted = 9,
        ReturnExpired = 10,
        AuthorityRejected = 11
    }

    public sealed class PortalDiagnosticEvent
    {
        internal PortalDiagnosticEvent(
            string correlationId,
            long utcTicks,
            PortalDiagnosticCode code,
            RouteStopCode stopCode,
            string portalId)
        {
            CorrelationId = correlationId;
            UtcTicks = utcTicks;
            Code = code;
            StopCode = stopCode;
            PortalId = portalId ?? string.Empty;
        }

        public string CorrelationId { get; }
        public long UtcTicks { get; }
        public PortalDiagnosticCode Code { get; }
        public RouteStopCode StopCode { get; }
        public string PortalId { get; }
    }

    public interface IPortalDiagnosticService
    {
        IReadOnlyList<PortalDiagnosticEvent> Snapshot();
        long DroppedCount { get; }
    }
}
