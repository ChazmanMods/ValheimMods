using System;
using System.Diagnostics;
using RunicWorldEngine.Contracts;

namespace RunicWorldEngine.Core
{
    internal sealed class ObservatoryCounter
    {
        private readonly object _gate = new object();
        private int _created;
        private int _destroyed;
        private long _sequence;
        private double _lastSaveMilliseconds;
        private double _lastLoadMilliseconds;
        private ZdoObservatorySnapshot _current = ZdoObservatorySnapshot.Empty;

        internal ZdoObservatorySnapshot Current
        {
            get { lock (_gate) return _current; }
        }

        internal void MarkCreated()
        {
            lock (_gate) _created = SaturatingIncrement(_created);
        }

        internal void MarkDestroyed()
        {
            lock (_gate) _destroyed = SaturatingIncrement(_destroyed);
        }

        internal void RecordSaveDuration(long startedTimestamp) =>
            RecordDuration(startedTimestamp, save: true);

        internal void RecordLoadDuration(long startedTimestamp) =>
            RecordDuration(startedTimestamp, save: false);

        internal ZdoObservatorySnapshot Capture(
            long unixMilliseconds,
            int totalObjects,
            int connectedPeers,
            int sentLastSecond,
            int receivedLastSecond)
        {
            lock (_gate)
            {
                _sequence = _sequence == long.MaxValue ? long.MaxValue : _sequence + 1L;
                _current = new ZdoObservatorySnapshot(
                    _sequence,
                    Math.Max(0L, unixMilliseconds),
                    Math.Max(0, totalObjects),
                    Math.Max(0, connectedPeers),
                    _created,
                    _destroyed,
                    Math.Max(0, sentLastSecond),
                    Math.Max(0, receivedLastSecond),
                    _lastSaveMilliseconds,
                    _lastLoadMilliseconds);
                _created = 0;
                _destroyed = 0;
                return _current;
            }
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _created = 0;
                _destroyed = 0;
                _sequence = 0L;
                _lastSaveMilliseconds = 0d;
                _lastLoadMilliseconds = 0d;
                _current = ZdoObservatorySnapshot.Empty;
            }
        }

        private void RecordDuration(long startedTimestamp, bool save)
        {
            if (startedTimestamp <= 0L) return;
            long elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
            if (elapsed < 0L) return;
            double milliseconds = elapsed * 1000d / Stopwatch.Frequency;
            lock (_gate)
            {
                if (save) _lastSaveMilliseconds = milliseconds;
                else _lastLoadMilliseconds = milliseconds;
            }
        }

        private static int SaturatingIncrement(int value) =>
            value == int.MaxValue ? value : value + 1;
    }
}
