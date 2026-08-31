using System;
using System.Collections.Generic;

namespace RunicInteraction.Core
{
    internal sealed class BoundedContextMemory<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, Entry> _entries;
        private long _sequence;

        internal BoundedContextMemory(int capacity, IEqualityComparer<TKey> comparer = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _entries = new Dictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        internal int Count => _entries.Count;

        internal void Put(TKey key, TValue value)
        {
            long sequence = NextSequence();
            if (_entries.ContainsKey(key))
            {
                _entries[key] = new Entry(value, sequence);
                return;
            }
            if (_entries.Count >= _capacity) EvictOldest();
            _entries.Add(key, new Entry(value, sequence));
        }

        internal bool TryGet(TKey key, out TValue value)
        {
            if (!_entries.TryGetValue(key, out Entry entry))
            {
                value = default;
                return false;
            }
            _entries[key] = new Entry(entry.Value, NextSequence());
            value = entry.Value;
            return true;
        }

        internal void Clear() => _entries.Clear();

        private long NextSequence()
        {
            if (_sequence == long.MaxValue)
            {
                long next = 0;
                var keys = new List<TKey>(_entries.Keys);
                keys.Sort((left, right) => _entries[left].Sequence.CompareTo(_entries[right].Sequence));
                foreach (TKey key in keys)
                {
                    Entry entry = _entries[key];
                    _entries[key] = new Entry(entry.Value, ++next);
                }
                _sequence = next;
            }
            return ++_sequence;
        }

        private void EvictOldest()
        {
            bool found = false;
            TKey oldestKey = default;
            long oldest = 0;
            foreach (KeyValuePair<TKey, Entry> pair in _entries)
            {
                if (found && pair.Value.Sequence >= oldest) continue;
                found = true;
                oldestKey = pair.Key;
                oldest = pair.Value.Sequence;
            }
            if (found) _entries.Remove(oldestKey);
        }

        private readonly struct Entry
        {
            internal Entry(TValue value, long sequence)
            {
                Value = value;
                Sequence = sequence;
            }

            internal TValue Value { get; }
            internal long Sequence { get; }
        }
    }
}
