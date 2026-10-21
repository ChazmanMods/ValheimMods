using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core
{
    public sealed class ZdoObservatorySnapshot
    {
        public ZdoObservatorySnapshot(
            long sequence,
            long sampledUnixMilliseconds,
            int totalObjects,
            int connectedPeers,
            int createdSincePreviousSample,
            int destroyedSincePreviousSample,
            int sentLastSecond,
            int receivedLastSecond,
            double lastSaveMilliseconds,
            double lastLoadMilliseconds)
        {
            if (sequence < 0L || sampledUnixMilliseconds < 0L || totalObjects < 0 ||
                connectedPeers < 0 || createdSincePreviousSample < 0 ||
                destroyedSincePreviousSample < 0 || sentLastSecond < 0 ||
                receivedLastSecond < 0 || lastSaveMilliseconds < 0d ||
                lastLoadMilliseconds < 0d || double.IsNaN(lastSaveMilliseconds) ||
                double.IsInfinity(lastSaveMilliseconds) || double.IsNaN(lastLoadMilliseconds) ||
                double.IsInfinity(lastLoadMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(sequence));

            Sequence = sequence;
            SampledUnixMilliseconds = sampledUnixMilliseconds;
            TotalObjects = totalObjects;
            ConnectedPeers = connectedPeers;
            CreatedSincePreviousSample = createdSincePreviousSample;
            DestroyedSincePreviousSample = destroyedSincePreviousSample;
            SentLastSecond = sentLastSecond;
            ReceivedLastSecond = receivedLastSecond;
            LastSaveMilliseconds = lastSaveMilliseconds;
            LastLoadMilliseconds = lastLoadMilliseconds;
        }

        public long Sequence { get; }
        public long SampledUnixMilliseconds { get; }
        public int TotalObjects { get; }
        public int ConnectedPeers { get; }
        public int CreatedSincePreviousSample { get; }
        public int DestroyedSincePreviousSample { get; }
        public int SentLastSecond { get; }
        public int ReceivedLastSecond { get; }
        public double LastSaveMilliseconds { get; }
        public double LastLoadMilliseconds { get; }

        public static ZdoObservatorySnapshot Empty { get; } =
            new ZdoObservatorySnapshot(0L, 0L, 0, 0, 0, 0, 0, 0, 0d, 0d);
    }

    public interface IZdoObservatoryService
    {
        ZdoObservatorySnapshot Current { get; }
    }

    public sealed class WorldDataOwnershipDeclaration
    {
        public const int MaximumValuesPerKind = 256;
        public const int MaximumInspectedValuesPerKind = 1024;

        public WorldDataOwnershipDeclaration(
            string moduleId,
            string schemaVersion,
            IEnumerable<int> prefabHashes,
            IEnumerable<string> ownedKeys,
            IEnumerable<string> temporaryKeys)
        {
            ModuleId = RunicIdentifier.Require(moduleId, nameof(moduleId));
            SchemaVersion = RequireSchema(schemaVersion);
            PrefabHashes = CopyUnique(prefabHashes);
            OwnedKeys = CopyKeys(ownedKeys);
            TemporaryKeys = CopyKeys(temporaryKeys);
            var owned = new HashSet<string>(OwnedKeys, StringComparer.Ordinal);
            foreach (string temporary in TemporaryKeys)
                if (owned.Contains(temporary))
                    throw new ArgumentException(
                        "One world-data key cannot be both persistent and temporary.",
                        nameof(temporaryKeys));
        }

        public string ModuleId { get; }
        public string SchemaVersion { get; }
        public IReadOnlyList<int> PrefabHashes { get; }
        public IReadOnlyList<string> OwnedKeys { get; }
        public IReadOnlyList<string> TemporaryKeys { get; }

        private static string RequireSchema(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
                throw new ArgumentException("A bounded schema version is required.", nameof(value));
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '.' || character == '-' || character == '_';
                if (!safe) throw new ArgumentException("Schema version contains an unsafe character.", nameof(value));
            }
            return value;
        }

        private static IReadOnlyList<int> CopyUnique(IEnumerable<int> values)
        {
            var result = new SortedSet<int>();
            int inspected = 0;
            if (values != null)
                foreach (int value in values)
                {
                    if (++inspected > MaximumInspectedValuesPerKind)
                        throw new ArgumentOutOfRangeException(nameof(values), "Too many ownership inputs were inspected.");
                    if (value == 0) throw new ArgumentException("Prefab hash zero is not canonical.");
                    result.Add(value);
                    if (result.Count > MaximumValuesPerKind)
                        throw new ArgumentOutOfRangeException(nameof(values));
                }
            return new List<int>(result).AsReadOnly();
        }

        private static IReadOnlyList<string> CopyKeys(IEnumerable<string> values)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            int inspected = 0;
            if (values != null)
                foreach (string value in values)
                {
                    if (++inspected > MaximumInspectedValuesPerKind)
                        throw new ArgumentOutOfRangeException(nameof(values), "Too many ownership inputs were inspected.");
                    string normalized = RunicIdentifier.Require(value, nameof(values));
                    result.Add(normalized);
                    if (result.Count > MaximumValuesPerKind)
                        throw new ArgumentOutOfRangeException(nameof(values));
                }
            return new List<string>(result).AsReadOnly();
        }
    }

    public interface IWorldDataOwnershipRegistry
    {
        IDisposable Register(
            ModuleRegistration owner,
            WorldDataOwnershipDeclaration declaration);
        IReadOnlyList<WorldDataOwnershipDeclaration> GetDeclarations();
    }
}
