using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicTransactions.Valheim
{
    public enum DurableOperationHeaderReadState
    {
        Absent = 0,
        Valid = 1,
        Corrupt = 2
    }

    public enum DurableOperationPhase
    {
        Claiming = 1,
        Committed = 2,
        RolledBackExact = 3
    }

    public enum DurableOperationRecoveryDisposition
    {
        None = 0,
        ClearedUnpublished = 1,
        JournalPublishRequired = 2,
        JournalRecoveryRequired = 3,
        JournalCleanupRequired = 4,
        ClearedTerminal = 5,
        AwaitingEndpoints = 6,
        FailedClosed = 7
    }

    /// <summary>
    /// One exact inventory endpoint participating in a durable operation. The stable token is
    /// authoritative across restarts; <see cref="EndpointId"/> is only a current-world cache.
    /// </summary>
    public sealed class DurableOperationEndpoint
    {
        public DurableOperationEndpoint(
            Container container,
            string endpointToken,
            string beforeFingerprint,
            string afterFingerprint)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            if (!WorldObjectToken.TryParse(endpointToken, out WorldObjectToken parsed))
                throw new ArgumentException(
                    "A canonical world-object endpoint token is required.",
                    nameof(endpointToken));
            ZNetView view = WorldObjectIdentity.View(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || zdo.m_uid.IsNone())
                throw new ArgumentException(
                    "The container must be bound to an exact current ZDO.",
                    nameof(container));
            if (WorldObjectIdentity.Read(zdo, out WorldObjectToken actual) !=
                    WorldObjectIdentityStatus.Ready || actual != parsed)
                throw new ArgumentException(
                    "The endpoint token does not belong to the container's exact ZDO.",
                    nameof(endpointToken));

            EndpointToken = parsed.Value;
            EndpointId = zdo.m_uid;
            BeforeFingerprint = DurableOperationValidation.RequireText(
                beforeFingerprint, nameof(beforeFingerprint), 256);
            AfterFingerprint = DurableOperationValidation.RequireText(
                afterFingerprint, nameof(afterFingerprint), 256);
            ValidateChanged(BeforeFingerprint, AfterFingerprint, nameof(afterFingerprint));
        }

        /// <summary>
        /// Compatibility constructor for an in-memory legacy journal. New durable operations
        /// reject endpoints without a stable token; callers should use the token constructor.
        /// </summary>
        public DurableOperationEndpoint(
            Container container,
            ZDOID endpointId,
            string beforeFingerprint,
            string afterFingerprint)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            if (endpointId.IsNone())
                throw new ArgumentException("An exact endpoint ZDOID is required.", nameof(endpointId));
            EndpointToken = string.Empty;
            EndpointId = endpointId;
            BeforeFingerprint = DurableOperationValidation.RequireText(
                beforeFingerprint, nameof(beforeFingerprint), 256);
            AfterFingerprint = DurableOperationValidation.RequireText(
                afterFingerprint, nameof(afterFingerprint), 256);
            ValidateChanged(BeforeFingerprint, AfterFingerprint, nameof(afterFingerprint));
        }

        public Container Container { get; }
        public string EndpointToken { get; }
        public ZDOID EndpointId { get; }
        public string StableEndpointId =>
            WorldObjectToken.TryParse(EndpointToken, out _)
                ? WorldObjectIdentity.EndpointId(EndpointToken)
                : string.Empty;
        public bool IsLegacyIdentity => string.IsNullOrEmpty(EndpointToken);
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }

        private static void ValidateChanged(string before, string after, string parameterName)
        {
            if (string.Equals(before, after, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A durable endpoint must describe an exact inventory change.",
                    parameterName);
        }
    }

    public sealed class DurableOperationEndpointBinding
    {
        internal DurableOperationEndpointBinding(
            string endpointToken,
            ZDOID endpointId,
            string beforeFingerprint,
            string afterFingerprint)
        {
            if (!WorldObjectToken.TryParse(endpointToken, out WorldObjectToken parsed))
                throw new ArgumentException(
                    "A canonical world-object endpoint token is required.",
                    nameof(endpointToken));
            EndpointToken = parsed.Value;
            EndpointId = endpointId;
            BeforeFingerprint = DurableOperationValidation.RequireText(
                beforeFingerprint, nameof(beforeFingerprint), 256);
            AfterFingerprint = DurableOperationValidation.RequireText(
                afterFingerprint, nameof(afterFingerprint), 256);
            ValidateChanged(BeforeFingerprint, AfterFingerprint, nameof(afterFingerprint));
        }

        internal DurableOperationEndpointBinding(
            ZDOID endpointId,
            string beforeFingerprint,
            string afterFingerprint)
        {
            if (endpointId.IsNone())
                throw new ArgumentException("An exact endpoint ZDOID is required.", nameof(endpointId));
            EndpointToken = string.Empty;
            EndpointId = endpointId;
            BeforeFingerprint = DurableOperationValidation.RequireText(
                beforeFingerprint, nameof(beforeFingerprint), 256);
            AfterFingerprint = DurableOperationValidation.RequireText(
                afterFingerprint, nameof(afterFingerprint), 256);
            ValidateChanged(BeforeFingerprint, AfterFingerprint, nameof(afterFingerprint));
        }

        public string EndpointToken { get; }
        public ZDOID EndpointId { get; }
        public string StableEndpointId =>
            WorldObjectToken.TryParse(EndpointToken, out _)
                ? WorldObjectIdentity.EndpointId(EndpointToken)
                : string.Empty;
        public bool IsLegacyIdentity => string.IsNullOrEmpty(EndpointToken);
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }

        internal DurableOperationEndpointBinding WithCurrentId(ZDOID endpointId) =>
            IsLegacyIdentity
                ? new DurableOperationEndpointBinding(
                    endpointId, BeforeFingerprint, AfterFingerprint)
                : new DurableOperationEndpointBinding(
                    EndpointToken, endpointId, BeforeFingerprint, AfterFingerprint);

        private static void ValidateChanged(string before, string after, string parameterName)
        {
            if (string.Equals(before, after, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A durable endpoint binding must describe an exact inventory change.",
                    parameterName);
        }
    }

    /// <summary>
    /// Station-side durable intent. Stable object tokens are persisted; numeric ZDOIDs are only
    /// populated as current-world caches. Schema-one records remain readable as explicit legacy
    /// identities so recovery can fail closed instead of silently binding a renumbered object.
    /// </summary>
    public sealed class DurableOperationHeaderRecord
    {
        internal DurableOperationHeaderRecord(
            string operationId,
            string moduleId,
            string stationToken,
            string stationId,
            string journalStorageKey,
            string journalHash,
            bool journalPublished,
            DurableOperationPhase phase,
            IEnumerable<DurableOperationEndpointBinding> endpoints)
        {
            OperationId = DurableOperationValidation.RequireText(
                operationId, nameof(operationId), 200);
            ModuleId = DurableOperationValidation.RequireText(moduleId, nameof(moduleId), 128);
            if (!WorldObjectToken.TryParse(stationToken, out WorldObjectToken parsed))
                throw new ArgumentException(
                    "A canonical world-object station token is required.",
                    nameof(stationToken));
            StationToken = parsed.Value;
            StationId = stationId ?? string.Empty;
            if (StationId.Length > 200)
                throw new ArgumentOutOfRangeException(nameof(stationId));
            JournalStorageKey = DurableOperationValidation.RequireText(
                journalStorageKey, nameof(journalStorageKey), 256);
            JournalHash = DurableOperationValidation.RequireJournalHash(journalHash);
            ValidatePhase(phase, journalPublished);
            Endpoints = ValidateEndpoints(endpoints, legacy: false);
            JournalPublished = journalPublished;
            Phase = phase;
        }

        internal DurableOperationHeaderRecord(
            string operationId,
            string moduleId,
            string legacyStationId,
            string journalStorageKey,
            string journalHash,
            bool journalPublished,
            DurableOperationPhase phase,
            IEnumerable<DurableOperationEndpointBinding> endpoints)
        {
            OperationId = DurableOperationValidation.RequireText(
                operationId, nameof(operationId), 200);
            ModuleId = DurableOperationValidation.RequireText(moduleId, nameof(moduleId), 128);
            StationToken = string.Empty;
            StationId = DurableOperationValidation.RequireText(
                legacyStationId, nameof(legacyStationId), 200);
            JournalStorageKey = DurableOperationValidation.RequireText(
                journalStorageKey, nameof(journalStorageKey), 256);
            JournalHash = DurableOperationValidation.RequireJournalHash(journalHash);
            ValidatePhase(phase, journalPublished);
            Endpoints = ValidateEndpoints(endpoints, legacy: true);
            JournalPublished = journalPublished;
            Phase = phase;
        }

        public string OperationId { get; }
        public string ModuleId { get; }
        public string StationToken { get; }
        public string StationId { get; }
        public string StableStationId =>
            WorldObjectToken.TryParse(StationToken, out _)
                ? WorldObjectIdentity.EndpointId(StationToken)
                : string.Empty;
        public bool IsLegacyIdentity => string.IsNullOrEmpty(StationToken);
        public string JournalStorageKey { get; }
        public string JournalHash { get; }
        public bool JournalPublished { get; }
        public DurableOperationPhase Phase { get; }
        public IReadOnlyList<DurableOperationEndpointBinding> Endpoints { get; }

        public bool MatchesExact(
            string moduleId,
            string operationId,
            string journalHash,
            DurableOperationPhase phase) =>
            string.Equals(ModuleId, moduleId, StringComparison.Ordinal) &&
            string.Equals(OperationId, operationId, StringComparison.Ordinal) &&
            string.Equals(JournalHash, journalHash, StringComparison.Ordinal) &&
            Phase == phase;

        internal DurableOperationHeaderRecord WithJournalPublished() =>
            Copy(true, Phase);

        internal DurableOperationHeaderRecord WithPhase(DurableOperationPhase phase) =>
            Copy(JournalPublished, phase);

        internal DurableOperationHeaderRecord WithCurrentStationId(string stationId) =>
            IsLegacyIdentity
                ? this
                : new DurableOperationHeaderRecord(
                    OperationId, ModuleId, StationToken, stationId, JournalStorageKey,
                    JournalHash, JournalPublished, Phase, Endpoints);

        private DurableOperationHeaderRecord Copy(bool published, DurableOperationPhase phase) =>
            IsLegacyIdentity
                ? new DurableOperationHeaderRecord(
                    OperationId, ModuleId, StationId, JournalStorageKey, JournalHash,
                    published, phase, Endpoints)
                : new DurableOperationHeaderRecord(
                    OperationId, ModuleId, StationToken, StationId, JournalStorageKey,
                    JournalHash, published, phase, Endpoints);

        private static void ValidatePhase(DurableOperationPhase phase, bool journalPublished)
        {
            if (!Enum.IsDefined(typeof(DurableOperationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (phase != DurableOperationPhase.Claiming && !journalPublished)
                throw new ArgumentException(
                    "A terminal durable operation must have published its exact journal.",
                    nameof(journalPublished));
        }

        private static IReadOnlyList<DurableOperationEndpointBinding> ValidateEndpoints(
            IEnumerable<DurableOperationEndpointBinding> endpoints,
            bool legacy)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            var copy = new List<DurableOperationEndpointBinding>(endpoints);
            if (copy.Count < 1 || copy.Count > 2)
                throw new ArgumentOutOfRangeException(
                    nameof(endpoints), "A durable operation requires one or two endpoints.");
            for (int index = 0; index < copy.Count; index++)
            {
                DurableOperationEndpointBinding current = copy[index];
                if (current == null || current.IsLegacyIdentity != legacy)
                    throw new ArgumentException(
                        "Endpoint bindings must use the header identity schema.",
                        nameof(endpoints));
                if (index == 0) continue;
                DurableOperationEndpointBinding prior = copy[index - 1];
                int comparison = legacy
                    ? DurableOperationValidation.Compare(prior.EndpointId, current.EndpointId)
                    : string.Compare(
                        prior.EndpointToken, current.EndpointToken, StringComparison.Ordinal);
                if (comparison >= 0)
                    throw new ArgumentException(
                        legacy
                            ? "Legacy endpoint bindings must be unique and in numeric ZDOID order."
                            : "Endpoint bindings must be unique and in stable-token order.",
                        nameof(endpoints));
            }
            return new ReadOnlyCollection<DurableOperationEndpointBinding>(copy);
        }
    }

    internal static class DurableOperationValidation
    {
        internal static string RequireText(string value, string parameterName, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable value is required.", parameterName);
            if (value.Length > maximum) throw new ArgumentOutOfRangeException(parameterName);
            foreach (char character in value)
                if (char.IsControl(character))
                    throw new ArgumentException(
                        "Stable values cannot contain control characters.", parameterName);
            return value;
        }

        internal static string RequireJournalHash(string value)
        {
            if (value == null || value.Length != 64)
                throw new ArgumentException(
                    "A canonical lowercase SHA-256 journal hash is required.", nameof(value));
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    throw new ArgumentException(
                        "A canonical lowercase SHA-256 journal hash is required.", nameof(value));
            return value;
        }

        internal static int Compare(ZDOID left, ZDOID right)
        {
            int user = left.UserID.CompareTo(right.UserID);
            return user != 0 ? user : left.ID.CompareTo(right.ID);
        }
    }
}
