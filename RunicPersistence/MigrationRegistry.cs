using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Runic.Foundation.Persistence
{
    public sealed class MigrationRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Dictionary<int, RegisteredStep>> _steps =
            new Dictionary<string, Dictionary<int, RegisteredStep>>(StringComparer.Ordinal);
        private long _nextToken;

        public MigrationRegistration Register(MigrationStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            lock (_gate)
            {
                if (!_steps.TryGetValue(step.ModuleId, out Dictionary<int, RegisteredStep> moduleSteps))
                {
                    moduleSteps = new Dictionary<int, RegisteredStep>();
                    _steps.Add(step.ModuleId, moduleSteps);
                }

                if (moduleSteps.ContainsKey(step.FromVersion))
                {
                    throw new InvalidOperationException(
                        $"A migration for {step.ModuleId} from schema {step.FromVersion} is already registered.");
                }

                long token = ++_nextToken;
                moduleSteps.Add(step.FromVersion, new RegisteredStep(step, token));
                return new MigrationRegistration(this, step.ModuleId, step.FromVersion, token);
            }
        }

        /// <summary>Removes every registered step owned by one unloading module.</summary>
        public int UnregisterModule(string moduleId)
        {
            string canonicalModuleId = RunicKey.RequireModuleId(moduleId, nameof(moduleId));
            lock (_gate)
            {
                if (!_steps.TryGetValue(canonicalModuleId, out Dictionary<int, RegisteredStep> steps))
                    return 0;
                int count = steps.Count;
                _steps.Remove(canonicalModuleId);
                return count;
            }
        }

        public MigrationResult Migrate(
            string moduleId,
            int targetVersion,
            IRecordStore store,
            IMigrationBackupSink backupSink)
        {
            string canonicalModuleId = RunicKey.RequireModuleId(moduleId, nameof(moduleId));
            if (targetVersion < 0) throw new ArgumentOutOfRangeException(nameof(targetVersion));
            if (store == null) throw new ArgumentNullException(nameof(store));

            string storeModuleId;
            RecordSetSnapshot original;
            try
            {
                storeModuleId = store.ModuleId;
                original = store.ReadSnapshot();
            }
            catch (Exception exception)
            {
                return MigrationResult.Failed(
                    0,
                    "The module store could not provide a migration snapshot: " +
                    exception.GetType().Name + ": " + exception.Message);
            }

            if (!string.Equals(storeModuleId, canonicalModuleId, StringComparison.Ordinal))
            {
                return MigrationResult.Failed(
                    0,
                    $"Store '{storeModuleId}' cannot be migrated as '{canonicalModuleId}'. Stores are module-scoped.");
            }

            if (original == null)
                return MigrationResult.Failed(0, "The record store returned no snapshot.");
            if (original.SchemaVersion == targetVersion)
                return MigrationResult.Completed(false, targetVersion);
            if (original.SchemaVersion > targetVersion)
            {
                return MigrationResult.Failed(
                    original.SchemaVersion,
                    $"Downgrade from schema {original.SchemaVersion} to {targetVersion} is not registered as safe.");
            }

            MigrationStep[] plan;
            lock (_gate)
            {
                if (!_steps.TryGetValue(canonicalModuleId, out Dictionary<int, RegisteredStep> moduleSteps))
                {
                    return MigrationResult.Failed(
                        original.SchemaVersion,
                        "No migrations are registered for this module.");
                }

                var planned = new List<MigrationStep>();
                int version = original.SchemaVersion;
                var visited = new HashSet<int>();
                while (version < targetVersion)
                {
                    if (!visited.Add(version) ||
                        !moduleSteps.TryGetValue(version, out RegisteredStep registered))
                    {
                        return MigrationResult.Failed(
                            version,
                            $"No complete migration path reaches schema {targetVersion}.");
                    }

                    MigrationStep next = registered.Step;
                    if (next.ToVersion > targetVersion)
                    {
                        return MigrationResult.Failed(
                            version,
                            $"Migration from {version} skips requested schema {targetVersion}.");
                    }

                    planned.Add(next);
                    version = next.ToVersion;
                }

                plan = planned.ToArray();
            }

            RecordSetSnapshot working = original.Clone();
            string ownedPrefix = canonicalModuleId + ".";
            try
            {
                backupSink?.CreateBackup(canonicalModuleId, original.Clone());
                foreach (MigrationStep step in plan)
                {
                    RecordSetSnapshot beforeStep = working.Clone();
                    step.Transform(working);
                    if (!beforeStep.ForeignRecordsEqual(working, ownedPrefix))
                    {
                        throw new InvalidOperationException(
                            $"Migration for {canonicalModuleId} attempted to change a foreign persistence key.");
                    }
                    working.SchemaVersion = step.ToVersion;
                }

                if (!store.TryReplace(original, working))
                {
                    return MigrationResult.Failed(
                        original.SchemaVersion,
                        "The module store changed concurrently; no migration data was committed. Retry from a fresh snapshot.");
                }
                return MigrationResult.Completed(true, working.SchemaVersion);
            }
            catch (Exception exception)
            {
                return MigrationResult.Failed(original.SchemaVersion, exception.Message);
            }
        }

        internal bool IsActive(string moduleId, int fromVersion, long token)
        {
            lock (_gate)
            {
                return _steps.TryGetValue(moduleId, out Dictionary<int, RegisteredStep> moduleSteps) &&
                       moduleSteps.TryGetValue(fromVersion, out RegisteredStep step) &&
                       step.Token == token;
            }
        }

        internal bool Unregister(string moduleId, int fromVersion, long token)
        {
            lock (_gate)
            {
                if (!_steps.TryGetValue(moduleId, out Dictionary<int, RegisteredStep> moduleSteps) ||
                    !moduleSteps.TryGetValue(fromVersion, out RegisteredStep step) ||
                    step.Token != token)
                    return false;
                moduleSteps.Remove(fromVersion);
                if (moduleSteps.Count == 0) _steps.Remove(moduleId);
                return true;
            }
        }

        private sealed class RegisteredStep
        {
            internal RegisteredStep(MigrationStep step, long token)
            {
                Step = step;
                Token = token;
            }

            internal MigrationStep Step { get; }
            internal long Token { get; }
        }
    }

    public sealed class MigrationRegistration : IDisposable
    {
        private readonly MigrationRegistry _registry;
        private readonly string _moduleId;
        private readonly int _fromVersion;
        private readonly long _token;
        private int _disposed;

        internal MigrationRegistration(
            MigrationRegistry registry,
            string moduleId,
            int fromVersion,
            long token)
        {
            _registry = registry;
            _moduleId = moduleId;
            _fromVersion = fromVersion;
            _token = token;
        }

        public bool IsActive =>
            Volatile.Read(ref _disposed) == 0 &&
            _registry.IsActive(_moduleId, _fromVersion, _token);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _registry.Unregister(_moduleId, _fromVersion, _token);
        }
    }

    public sealed class MemoryRecordStore : IRecordStore
    {
        private readonly object _gate = new object();
        private RecordSetSnapshot _snapshot;

        public MemoryRecordStore(string moduleId, RecordSetSnapshot initial)
        {
            ModuleId = RunicKey.RequireModuleId(moduleId, nameof(moduleId));
            _snapshot = (initial ?? throw new ArgumentNullException(nameof(initial))).Clone();
        }

        public string ModuleId { get; }

        public RecordSetSnapshot ReadSnapshot()
        {
            lock (_gate) return _snapshot.Clone();
        }

        public bool TryReplace(RecordSetSnapshot expected, RecordSetSnapshot replacement)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            lock (_gate)
            {
                if (!_snapshot.ContentEquals(expected)) return false;
                _snapshot = replacement.Clone();
                return true;
            }
        }
    }
}
