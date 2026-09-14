using System;
using System.Collections.Generic;

namespace RunicCrafting.Domain
{
    // Main-thread, read-only evidence cache. Permissions and writable inventories never enter it.
    internal sealed class PreviewSnapshotCache<TKey, TValue> where TValue : class
    {
        private sealed class Entry
        {
            internal TKey Key;
            internal object Identity;
            internal byte[] Payload;
            internal int Width, Height, WorldLevel;
            internal string Name;
            internal TValue Value;
        }

        private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries =
            new Dictionary<TKey, LinkedListNode<Entry>>();
        private readonly LinkedList<Entry> _recency = new LinkedList<Entry>();
        private readonly int _maximumEntries, _maximumBytes;
        private int _bytes;
        private object _network, _player, _database;
        internal const int MaximumPayloadBytes = 262144;
        internal int Count => _entries.Count;
        internal int PayloadBytes => _bytes;

        internal PreviewSnapshotCache(int maximumEntries = 128, int maximumBytes = 8388608)
        {
            if (maximumEntries < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException();
            _maximumEntries = maximumEntries;
            _maximumBytes = maximumBytes;
        }

        internal void SetContext(object network, object player, object database)
        {
            if (ReferenceEquals(network, _network) && ReferenceEquals(player, _player) &&
                ReferenceEquals(database, _database)) return;
            Clear();
            _network = network;
            _player = player;
            _database = database;
        }

        internal bool TryGet(TKey key, object identity, byte[] payload, int width, int height,
            string name, int worldLevel, out TValue value)
        {
            value = null;
            if (!_entries.TryGetValue(key, out LinkedListNode<Entry> node)) return false;
            Entry entry = node.Value;
            if (!ReferenceEquals(identity, entry.Identity) || width != entry.Width ||
                height != entry.Height || worldLevel != entry.WorldLevel ||
                !string.Equals(name, entry.Name, StringComparison.Ordinal) ||
                !SamePayload(entry.Payload, payload))
            {
                Remove(node);
                return false;
            }
            _recency.Remove(node);
            _recency.AddLast(node);
            value = entry.Value;
            return true;
        }

        // Only verified snapshots may be stored; clone evidence to detect in-place ZDO changes.
        internal void Store(TKey key, object identity, byte[] payload, int width, int height,
            string name, int worldLevel, TValue value)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<Entry> old)) Remove(old);
            int length = payload?.Length ?? 0;
            if (value == null || length > MaximumPayloadBytes || length > _maximumBytes) return;
            while (_entries.Count >= _maximumEntries || _bytes + length > _maximumBytes)
                Remove(_recency.First);
            var entry = new Entry
            {
                Key = key, Identity = identity, Payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone(),
                Width = width, Height = height, Name = name, WorldLevel = worldLevel, Value = value
            };
            _entries.Add(key, _recency.AddLast(entry));
            _bytes += length;
        }

        internal void Clear()
        {
            _entries.Clear();
            _recency.Clear();
            _bytes = 0;
            _network = _player = _database = null;
        }

        internal static bool SamePayload(byte[] left, byte[] right) =>
            (left ?? Array.Empty<byte>()).AsSpan().SequenceEqual(right ?? Array.Empty<byte>());

        private void Remove(LinkedListNode<Entry> node)
        {
            _entries.Remove(node.Value.Key);
            _recency.Remove(node);
            _bytes -= node.Value.Payload.Length;
        }
    }
}
