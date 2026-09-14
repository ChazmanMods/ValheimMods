using System;
using System.Collections.Generic;

namespace RunicWorldEngine.Core
{
    internal static class SendWindowPressure
    {
        internal static bool? Estimate(bool playFab, double? queuedBytes, double? inFlightBytes, int? appMessages)
        {
            if (appMessages > 0) return true;
            // Valheim 1.0.12 stops ordinary ZDO sends below 2 KiB free in its 10 KiB budget.
            // PlayFab's game-side queue-size estimate discounts outstanding payloads by 0.25.
            if (playFab) return inFlightBytes.HasValue ? (bool?)(inFlightBytes.Value * 0.25 >= 8192) : null;
            return queuedBytes.HasValue && inFlightBytes.HasValue ? (bool?)(queuedBytes.Value + inFlightBytes.Value >= 8192) : null;
        }
    }
    internal sealed class ByteRate
    {
        private int _previous;
        private double _time;
        private bool _seeded;
        internal double? Sample(int value, double now)
        {
            uint delta = unchecked((uint)(value - _previous));
            double elapsed = now - _time;
            double? result = _seeded && elapsed > 0 && delta <= int.MaxValue ? delta / elapsed : (double?)null;
            _seeded = true;
            _previous = value;
            _time = now;
            return result;
        }
    }

    internal sealed class WarningLatch
    {
        private bool _condition, _active, _seeded;
        private double _changedAt, _lastWarning = double.NegativeInfinity;
        // 1=warn, -1=recovered, 0=no log. Both entry and recovery require sustained evidence.
        internal int Observe(bool condition, double now, double hold, double cooldown)
        {
            if (!_seeded || condition != _condition)
            { _seeded = true; _condition = condition; _changedAt = now; }
            if (now - _changedAt < hold) return 0;
            if (condition && now - _lastWarning >= cooldown)
            { _active = true; _lastWarning = now; return 1; }
            if (!condition && _active) { _active = false; return -1; }
            return 0;
        }
    }

    internal sealed class PayloadMeter
    {
        private int _sent, _received;
        private readonly ByteRate _sentRate = new ByteRate(), _receivedRate = new ByteRate();
        internal void Sent(int bytes) { if (bytes > 0) System.Threading.Interlocked.Add(ref _sent, bytes); }
        internal void Received(int bytes) { if (bytes > 0) System.Threading.Interlocked.Add(ref _received, bytes); }
        internal (double? Sent, double? Received) Sample(double now) =>
            (_sentRate.Sample(System.Threading.Volatile.Read(ref _sent), now), _receivedRate.Sample(System.Threading.Volatile.Read(ref _received), now));
    }

    internal sealed class TransferCounter<TKey>
    {
        private readonly int _maximum;
        private readonly Dictionary<TKey, double> _recent = new Dictionary<TKey, double>();
        private readonly Queue<TKey> _order = new Queue<TKey>();
        internal long Assigned, Released, Transferred, RapidTransfers;
        internal int Tracked => _recent.Count;
        internal TransferCounter(int maximum) { _maximum = Math.Max(1, maximum); }
        internal void Record(TKey id, long before, long after, double now)
        {
            if (before == after) return;
            if (before == 0) Assigned++;
            else if (after == 0) Released++;
            else
            {
                Transferred++;
                if (_recent.TryGetValue(id, out double previous) && now - previous <= 10) RapidTransfers++;
                if (!_recent.ContainsKey(id))
                {
                    if (_recent.Count >= _maximum) _recent.Remove(_order.Dequeue());
                    _order.Enqueue(id);
                }
                _recent[id] = now;
            }
        }
        internal void ResetInterval() { Assigned = Released = Transferred = RapidTransfers = 0; }
        internal void Clear() { ResetInterval(); _recent.Clear(); _order.Clear(); }
    }
}
