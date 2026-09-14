using System;
using System.Collections.Generic;
using System.Threading;

namespace RunicCrafting.Domain
{
    // Aedis identified the repeated source queries within a single native UI refresh.
    // Nested refreshes share evidence; no results survive the outer call or a mutation.
    internal sealed class RefreshQueryCache<TKey, TValue> where TValue : class
    {
        private readonly Dictionary<TKey, TValue> _values = new Dictionary<TKey, TValue>();
        private readonly object _gate = new object();
        private readonly int _maximumEntries;
        private object _network, _player, _database;
        private int _depth, _frame;
        private long _scope, _epoch;
        internal bool Active => Volatile.Read(ref _depth) > 0;
        internal long Epoch { get { lock (_gate) return _epoch; } }
        internal int Count { get { lock (_gate) return _values.Count; } }

        internal RefreshQueryCache(int maximumEntries = 32)
        {
            if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            _maximumEntries = maximumEntries;
        }

        internal long Begin(int frame, object network, object player, object database)
        {
            lock (_gate)
            {
                if (!Active || frame != _frame || !ReferenceEquals(network, _network) ||
                    !ReferenceEquals(player, _player) || !ReferenceEquals(database, _database))
                {
                    Reset();
                    _frame = frame;
                    _network = network; _player = player; _database = database;
                }
                _depth++;
                return _scope;
            }
        }

        internal void End(long scope)
        {
            lock (_gate)
            {
                if (scope != _scope || !Active) return;
                if (--_depth == 0) Reset();
            }
        }

        internal void Invalidate()
        {
            if (!Active) return;
            lock (_gate)
            {
                if (!Active) return;
                _values.Clear();
                _epoch++;
            }
        }

        internal bool TryGet(TKey key, out TValue value)
        {
            lock (_gate)
            {
                value = null;
                return Active && _values.TryGetValue(key, out value);
            }
        }

        internal void Store(TKey key, TValue value, long expectedEpoch)
        {
            lock (_gate)
            {
                if (!Active || _epoch != expectedEpoch || value == null) return;
                // Don't flush hot anchors when a modded menu exceeds the bound.
                if (_values.Count >= _maximumEntries && !_values.ContainsKey(key)) return;
                _values[key] = value;
            }
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _values.Clear();
                _depth = 0;
                _scope++;
                _epoch++;
                _network = _player = _database = null;
            }
        }
    }
}
