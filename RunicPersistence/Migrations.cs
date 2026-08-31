using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Runic.Foundation.Persistence
{
    public sealed class RecordSetSnapshot
    {
        private readonly Dictionary<string, byte[]> _records;

        public RecordSetSnapshot(int schemaVersion, IDictionary<string, byte[]> records)
        {
            if (schemaVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            _records = Clone(records ?? throw new ArgumentNullException(nameof(records)));
        }

        public int SchemaVersion { get; set; }

        /// <summary>
        /// Returns a deep, read-only copy. Callers cannot mutate the working migration snapshot
        /// without going through Set or Remove.
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> Records =>
            new ReadOnlyDictionary<string, byte[]>(Clone(_records));

        public bool TryGet(string key, out byte[] value)
        {
            if (_records.TryGetValue(key, out byte[] stored))
            {
                value = stored == null ? null : (byte[])stored.Clone();
                return true;
            }

            value = null;
            return false;
        }

        public void Set(string key, byte[] value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Record key is required.", nameof(key));
            }

            _records[key] = value == null ? null : (byte[])value.Clone();
        }

        public bool Remove(string key)
        {
            return _records.Remove(key);
        }

        public RecordSetSnapshot Clone()
        {
            return new RecordSetSnapshot(SchemaVersion, _records);
        }

        internal bool ContentEquals(RecordSetSnapshot other)
        {
            if (other == null || SchemaVersion != other.SchemaVersion || _records.Count != other._records.Count)
                return false;
            foreach (KeyValuePair<string, byte[]> pair in _records)
            {
                if (!other._records.TryGetValue(pair.Key, out byte[] otherValue) ||
                    !BytesEqual(pair.Value, otherValue))
                    return false;
            }
            return true;
        }

        internal bool ForeignRecordsEqual(RecordSetSnapshot other, string ownedPrefix)
        {
            if (other == null) return false;
            var foreignKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in _records.Keys)
                if (!key.StartsWith(ownedPrefix, StringComparison.Ordinal)) foreignKeys.Add(key);
            foreach (string key in other._records.Keys)
                if (!key.StartsWith(ownedPrefix, StringComparison.Ordinal)) foreignKeys.Add(key);
            foreach (string key in foreignKeys)
            {
                bool leftFound = _records.TryGetValue(key, out byte[] left);
                bool rightFound = other._records.TryGetValue(key, out byte[] right);
                if (leftFound != rightFound || leftFound && !BytesEqual(left, right)) return false;
            }
            return true;
        }

        private static Dictionary<string, byte[]> Clone(IDictionary<string, byte[]> records)
        {
            return records.ToDictionary(
                pair => pair.Key,
                pair => pair.Value == null ? null : (byte[])pair.Value.Clone(),
                StringComparer.Ordinal);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }
    }

    public interface IRecordStore
    {
        /// <summary>The one module whose schema version this store represents.</summary>
        string ModuleId { get; }

        RecordSetSnapshot ReadSnapshot();

        /// <summary>
        /// Atomically replaces expected with replacement only when the current content still
        /// matches expected. False indicates a concurrent writer and no mutation.
        /// </summary>
        bool TryReplace(RecordSetSnapshot expected, RecordSetSnapshot replacement);
    }

    public interface IMigrationBackupSink
    {
        void CreateBackup(string moduleId, RecordSetSnapshot snapshot);
    }

    public delegate void MigrationTransform(RecordSetSnapshot records);

    public sealed class MigrationStep
    {
        public MigrationStep(string moduleId, int fromVersion, int toVersion, MigrationTransform transform)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException("Module ID is required.", nameof(moduleId));
            }

            if (fromVersion < 0 || toVersion <= fromVersion)
            {
                throw new ArgumentException("A migration must advance the schema version.");
            }

            ModuleId = RunicKey.RequireModuleId(moduleId, nameof(moduleId));
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public string ModuleId { get; }

        public int FromVersion { get; }

        public int ToVersion { get; }

        public MigrationTransform Transform { get; }
    }

    public sealed class MigrationResult
    {
        private MigrationResult(bool success, bool changed, int finalVersion, string error)
        {
            Success = success;
            Changed = changed;
            FinalVersion = finalVersion;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }

        public bool Changed { get; }

        public int FinalVersion { get; }

        public string Error { get; }

        public static MigrationResult Completed(bool changed, int version) => new MigrationResult(true, changed, version, null);

        public static MigrationResult Failed(int version, string error) => new MigrationResult(false, false, version, error);
    }
}
