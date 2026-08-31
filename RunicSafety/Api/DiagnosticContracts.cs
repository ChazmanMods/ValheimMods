using System;
using System.Collections.Generic;

namespace RunicSafety.Api
{
    public enum SafetyDiagnosticSeverity
    {
        Information = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class SafetyDiagnosticEvent
    {
        internal SafetyDiagnosticEvent(
            long sequence,
            DateTime timestampUtc,
            string correlationId,
            string category,
            string code,
            SafetyDiagnosticSeverity severity)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            CorrelationId = correlationId;
            Category = category;
            Code = code;
            Severity = severity;
        }

        public long Sequence { get; }
        public DateTime TimestampUtc { get; }
        public string CorrelationId { get; }
        public string Category { get; }
        public string Code { get; }
        public SafetyDiagnosticSeverity Severity { get; }
    }

    public interface ISafetyDiagnosticService
    {
        int Capacity { get; }
        int Count { get; }
        string NewCorrelationId(string category);
        void Record(
            string correlationId,
            string category,
            string code,
            SafetyDiagnosticSeverity severity = SafetyDiagnosticSeverity.Information);
        IReadOnlyList<SafetyDiagnosticEvent> Snapshot();
    }
}
