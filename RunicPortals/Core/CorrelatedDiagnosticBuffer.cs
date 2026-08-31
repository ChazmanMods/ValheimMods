using System;
using System.Collections.Generic;
using System.Threading;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal sealed class CorrelatedDiagnosticBuffer : IPortalDiagnosticService
    {
        private readonly object _sync = new object();
        private readonly PortalDiagnosticEvent[] _events;
        private int _start;
        private int _count;
        private long _sequence;
        private long _dropped;

        internal CorrelatedDiagnosticBuffer(int capacity)
        {
            if (capacity < 1 || capacity > PortalContractLimits.MaximumDiagnostics)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _events = new PortalDiagnosticEvent[capacity];
        }

        public long DroppedCount
        {
            get { lock (_sync) return _dropped; }
        }

        internal string Record(
            PortalDiagnosticCode code,
            RouteStopCode stopCode = RouteStopCode.Ready,
            string portalId = "",
            long utcTicks = 0)
        {
            string correlation = "rp-" + Interlocked.Increment(ref _sequence).ToString("x16");
            var item = new PortalDiagnosticEvent(
                correlation,
                utcTicks == 0 ? DateTime.UtcNow.Ticks : utcTicks,
                code,
                stopCode,
                BoundPortalId(portalId));
            lock (_sync)
            {
                if (_count < _events.Length)
                {
                    _events[(_start + _count) % _events.Length] = item;
                    _count++;
                }
                else
                {
                    _events[_start] = item;
                    _start = (_start + 1) % _events.Length;
                    _dropped++;
                }
            }
            return correlation;
        }

        public IReadOnlyList<PortalDiagnosticEvent> Snapshot()
        {
            lock (_sync)
            {
                var result = new PortalDiagnosticEvent[_count];
                for (int index = 0; index < _count; index++)
                    result[index] = _events[(_start + index) % _events.Length];
                return Array.AsReadOnly(result);
            }
        }

        private static string BoundPortalId(string value)
        {
            string normalized = value ?? string.Empty;
            if (normalized.Length > PortalContractLimits.MaximumPortalIdLength)
                normalized = normalized.Substring(0, PortalContractLimits.MaximumPortalIdLength);
            for (int index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index])) return string.Empty;
            return normalized;
        }
    }
}
