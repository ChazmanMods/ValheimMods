using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class CorrelatedDiagnosticBuffer : ISafetyDiagnosticService
    {
        public const int DefaultCapacity = 256;
        private const int MaximumTokenLength = 64;

        private readonly object _sync = new object();
        private readonly SafetyDiagnosticEvent[] _events;
        private readonly Func<DateTime> _clock;
        private ManualLogSource _log;
        private int _start;
        private int _count;
        private long _sequence;

        public CorrelatedDiagnosticBuffer(
            int capacity = DefaultCapacity,
            Func<DateTime> clock = null,
            ManualLogSource log = null)
        {
            if (capacity < 8 || capacity > 4096)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _events = new SafetyDiagnosticEvent[capacity];
            _clock = clock ?? (() => DateTime.UtcNow);
            _log = log;
        }

        public int Capacity => _events.Length;

        public int Count
        {
            get { lock (_sync) return _count; }
        }

        internal void SetLog(ManualLogSource log)
        {
            lock (_sync) _log = log;
        }

        public string NewCorrelationId(string category)
        {
            string safeCategory = Normalize(category, "transaction");
            long sequence;
            lock (_sync) sequence = NextSequenceLocked();
            return safeCategory + "-" + sequence.ToString("x16", CultureInfo.InvariantCulture);
        }

        public void Record(
            string correlationId,
            string category,
            string code,
            SafetyDiagnosticSeverity severity = SafetyDiagnosticSeverity.Information)
        {
            string safeCorrelation = Normalize(correlationId, "uncorrelated");
            string safeCategory = Normalize(category, "unknown");
            string safeCode = Normalize(code, "unspecified");
            ManualLogSource log;
            SafetyDiagnosticEvent entry;
            lock (_sync)
            {
                entry = new SafetyDiagnosticEvent(
                    NextSequenceLocked(),
                    _clock().ToUniversalTime(),
                    safeCorrelation,
                    safeCategory,
                    safeCode,
                    severity);
                int index = (_start + _count) % _events.Length;
                if (_count == _events.Length)
                {
                    index = _start;
                    _start = (_start + 1) % _events.Length;
                }
                else
                {
                    _count++;
                }
                _events[index] = entry;
                log = _log;
            }

            if (log == null) return;
            string line = "[" + entry.CorrelationId + "] " + entry.Category + "/" + entry.Code;
            if (severity == SafetyDiagnosticSeverity.Error) log.LogError(line);
            else if (severity == SafetyDiagnosticSeverity.Warning) log.LogWarning(line);
            else log.LogInfo(line);
        }

        public IReadOnlyList<SafetyDiagnosticEvent> Snapshot()
        {
            lock (_sync)
            {
                var result = new List<SafetyDiagnosticEvent>(_count);
                for (int offset = 0; offset < _count; offset++)
                    result.Add(_events[(_start + offset) % _events.Length]);
                return result.AsReadOnly();
            }
        }

        private long NextSequenceLocked()
        {
            if (_sequence == long.MaxValue) _sequence = 0;
            return ++_sequence;
        }

        private static string Normalize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            int length = Math.Min(trimmed.Length, MaximumTokenLength);
            char[] chars = new char[length];
            for (int index = 0; index < length; index++)
            {
                char current = trimmed[index];
                chars[index] = char.IsLetterOrDigit(current) || current == '-' || current == '_' || current == '.'
                    ? current
                    : '_';
            }
            return new string(chars);
        }
    }
}
