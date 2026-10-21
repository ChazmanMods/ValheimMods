using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RunicTransactions.Coordination;

namespace RunicTransactions.Valheim
{
    /// <summary>
    /// Sequences a station journal with one or two container-local durable claims, including
    /// claims-first adoption of journals persisted by a pre-coordinator release. All mutating calls
    /// require the process-wide <see cref="RunicMutationGate"/> to already be held. The adapter
    /// remains responsible for writing/clearing its journal and for proving exact station state,
    /// assignments, and cursors before selecting a terminal phase.
    /// </summary>
    public static class DurableOperationCoordinator
    {
        public const string HeaderStorageKey = "runic.transactions.operation-header.record";

        private const int SchemaVersion = 2;
        private const int LegacySchemaVersion = 1;
        // A schema-three value in HeaderStorageKey is a bounded, non-authoritative upgrade
        // envelope. It preserves the old-to-new endpoint mapping while claims are rewritten one at
        // a time. Normal ReadHeader treats it as corrupt until schema two is published.
        private const int IdentityUpgradeEnvelopeSchemaVersion = 3;
        private const int MaximumEncodedCharacters = 16384;
        private const int MaximumJournalCharacters = 1048576;
        private const int MaximumLegacyClaimIndexEntries = 16384;
        private static readonly int DurableClaimStorageHash =
            DurableContainerSafety.LockStorageKey.GetStableHashCode();
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string ComputeJournalHash(string exactJournalPayload)
        {
            if (string.IsNullOrEmpty(exactJournalPayload))
                throw new ArgumentException(
                    "A non-empty exact journal payload is required.", nameof(exactJournalPayload));
            if (exactJournalPayload.Length > MaximumJournalCharacters)
                throw new ArgumentOutOfRangeException(nameof(exactJournalPayload));
            byte[] bytes = StrictUtf8.GetBytes(exactJournalPayload);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var result = new StringBuilder(64);
                foreach (byte value in digest)
                    result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        public static DurableOperationHeaderReadState ReadHeader(
            ZDO stationZdo,
            out DurableOperationHeaderRecord header)
        {
            header = null;
            if (stationZdo == null) return DurableOperationHeaderReadState.Absent;
            DurableOperationHeaderReadState state = ParseHeader(
                stationZdo.GetString(HeaderStorageKey, string.Empty), out header);
            if (state != DurableOperationHeaderReadState.Valid) return state;
            if (stationZdo.m_uid.IsNone())
            {
                header = null;
                return DurableOperationHeaderReadState.Corrupt;
            }
            if (header.IsLegacyIdentity)
            {
                if (!string.Equals(
                        header.StationId,
                        stationZdo.m_uid.ToString(),
                        StringComparison.Ordinal))
                {
                    header = null;
                    return DurableOperationHeaderReadState.Corrupt;
                }
                return DurableOperationHeaderReadState.Valid;
            }
            if (WorldObjectIdentity.Read(stationZdo, out WorldObjectToken stationToken) !=
                    WorldObjectIdentityStatus.Ready ||
                !string.Equals(
                    header.StationToken, stationToken.Value, StringComparison.Ordinal))
            {
                header = null;
                return DurableOperationHeaderReadState.Corrupt;
            }
            if (WorldObjectIdentity.Resolve(stationToken, out ZDO resolvedStation) !=
                    WorldObjectIdentityStatus.Ready ||
                resolvedStation == null || resolvedStation.m_uid != stationZdo.m_uid)
            {
                header = null;
                return DurableOperationHeaderReadState.Corrupt;
            }
            header = header.WithCurrentStationId(stationZdo.m_uid.ToString());
            return DurableOperationHeaderReadState.Valid;
        }

        /// <summary>
        /// Reads only schema-one recovery evidence physically stored on this station. Unlike
        /// <see cref="ReadHeader"/>, this inspection does not accept the old numeric station ID as
        /// current authority. It exists solely so an adapter can discover guarded endpoint
        /// candidates and pass them to <see cref="TryUpgradeRemappedLegacyOperationIdentity"/>.
        /// A partially completed upgrade envelope is projected back to its exact schema-one view
        /// so the same recovery path remains retryable after every endpoint-claim cut.
        /// </summary>
        public static DurableOperationHeaderReadState ReadLegacyHeaderForIdentityUpgrade(
            ZDO stationZdo,
            out DurableOperationHeaderRecord header)
        {
            header = null;
            if (stationZdo == null) return DurableOperationHeaderReadState.Absent;
            if (stationZdo.m_uid.IsNone()) return DurableOperationHeaderReadState.Corrupt;
            string encoded = stationZdo.GetString(HeaderStorageKey, string.Empty);
            DurableOperationHeaderReadState parsed = ParseHeader(encoded, out header);
            if (parsed == DurableOperationHeaderReadState.Absent) return parsed;
            if (parsed == DurableOperationHeaderReadState.Valid)
            {
                if (header.IsLegacyIdentity) return parsed;
                header = null;
                return DurableOperationHeaderReadState.Corrupt;
            }
            if (TryParseIdentityUpgradeEnvelope(
                    encoded, out IdentityUpgradeEnvelope envelope))
            {
                header = envelope.LegacyHeader;
                return DurableOperationHeaderReadState.Valid;
            }
            header = null;
            return DurableOperationHeaderReadState.Corrupt;
        }

        /// <summary>
        /// Discovers the current loaded Containers that physically carry every exact schema-one
        /// endpoint claim in <paramref name="legacyHeader"/>. This is a bounded, read-only lookup:
        /// it does not mint identity, request ownership, synchronize inventory, or infer proximity.
        /// A caller must still pass the returned candidates to
        /// <see cref="TryUpgradeRemappedLegacyOperationIdentity"/>, which re-proves ownership,
        /// fingerprints, header/journal identity, and the persisted claims before any mutation.
        /// </summary>
        public static bool TryDiscoverLegacyClaimEndpointCandidates(
            DurableOperationHeaderRecord legacyHeader,
            out IReadOnlyList<Container> candidates,
            out string failure)
        {
            candidates = Array.Empty<Container>();
            failure = string.Empty;
            if (legacyHeader == null || !legacyHeader.IsLegacyIdentity)
            {
                failure = "An exact schema-one durable header is required for claim discovery.";
                return false;
            }
            if (ZDOMan.instance == null)
            {
                failure = "The world object registry is unavailable for legacy claim discovery.";
                return false;
            }

            List<ZDOID> indexed;
            try
            {
                indexed = ZDOExtraData.GetAllZDOIDsWithHash(
                    ZDOExtraData.Type.String,
                    DurableClaimStorageHash);
            }
            catch
            {
                failure = "The durable claim index could not be read.";
                return false;
            }
            if (indexed == null ||
                !IsLegacyClaimIndexCountWithinBounds(indexed.Count))
            {
                failure = indexed == null
                    ? "The durable claim index is unavailable."
                    : "The durable claim index exceeds its bounded recovery capacity.";
                return false;
            }

            var uniqueCurrentIds = new HashSet<ZDOID>();
            var indexedClaims = new List<KeyValuePair<ZDOID, DurableEndpointLockRecord>>(
                indexed.Count);
            foreach (ZDOID currentId in indexed)
            {
                if (currentId.IsNone() || !uniqueCurrentIds.Add(currentId))
                {
                    failure = "The durable claim index contains a duplicate or invalid current ZDOID.";
                    return false;
                }
                ZDO current = ZDOMan.instance.GetZDO(currentId);
                if (current == null || !current.IsValid()) continue;
                DurableEndpointLockReadState state = DurableContainerSafety.Parse(
                    current.GetString(DurableContainerSafety.LockStorageKey, string.Empty),
                    out DurableEndpointLockRecord claim);
                if (state == DurableEndpointLockReadState.Valid && claim != null)
                    indexedClaims.Add(
                        new KeyValuePair<ZDOID, DurableEndpointLockRecord>(currentId, claim));
            }

            if (!TryMatchLegacyClaimCandidateIds(
                    legacyHeader,
                    indexedClaims,
                    indexed.Count,
                    out IReadOnlyList<ZDOID> exactIds,
                    out failure)) return false;

            var loaded = new Dictionary<ZDOID, Container>();
            if (!TryRequireLoadedLegacyClaimCandidates(
                    exactIds,
                    exactId =>
                    {
                        Container container =
                            DurableContainerSafety.ResolveExactLoadedContainerForIdentityUpgrade(
                                exactId);
                        if (container == null) return false;
                        loaded.Add(exactId, container);
                        return true;
                    },
                    out failure)) return false;
            var resolved = new List<Container>(exactIds.Count);
            foreach (ZDOID exactId in exactIds) resolved.Add(loaded[exactId]);
            candidates = resolved.AsReadOnly();
            return true;
        }

        internal static bool IsLegacyClaimIndexCountWithinBounds(int indexedCount) =>
            indexedCount >= 0 && indexedCount <= MaximumLegacyClaimIndexEntries;

        internal static bool TryMatchLegacyClaimCandidateIds(
            DurableOperationHeaderRecord legacyHeader,
            IReadOnlyList<KeyValuePair<ZDOID, DurableEndpointLockRecord>> indexedClaims,
            int indexedCount,
            out IReadOnlyList<ZDOID> exactIds,
            out string failure)
        {
            exactIds = Array.Empty<ZDOID>();
            failure = string.Empty;
            if (legacyHeader == null || !legacyHeader.IsLegacyIdentity ||
                indexedClaims == null ||
                !IsLegacyClaimIndexCountWithinBounds(indexedCount) ||
                indexedClaims.Count > indexedCount)
            {
                failure = "Legacy claim discovery evidence is invalid or exceeds its bound.";
                return false;
            }

            var currentIds = new HashSet<ZDOID>();
            var matches = new Dictionary<ZDOID, ZDOID>();
            foreach (KeyValuePair<ZDOID, DurableEndpointLockRecord> evidence in indexedClaims)
            {
                if (evidence.Key.IsNone() || !currentIds.Add(evidence.Key))
                {
                    failure = "Legacy claim discovery contains a duplicate or invalid current ZDOID.";
                    return false;
                }
                if (evidence.Value == null) continue;

                DurableOperationEndpointBinding matchedBinding = null;
                foreach (DurableOperationEndpointBinding binding in legacyHeader.Endpoints)
                {
                    DurableEndpointLockRecord expected = CreateLegacyClaim(
                        legacyHeader, binding);
                    if (!evidence.Value.MatchesExact(expected)) continue;
                    if (matchedBinding != null)
                    {
                        failure = "One physical claim matches more than one legacy header binding.";
                        return false;
                    }
                    matchedBinding = binding;
                }
                if (matchedBinding == null) continue;
                if (matches.ContainsKey(matchedBinding.EndpointId))
                {
                    failure = "More than one physical claim matches one legacy header binding.";
                    return false;
                }
                matches.Add(matchedBinding.EndpointId, evidence.Key);
            }

            if (matches.Count != legacyHeader.Endpoints.Count)
            {
                failure = "The loaded claim index does not cover every legacy header binding exactly once.";
                return false;
            }
            var ordered = new List<ZDOID>(legacyHeader.Endpoints.Count);
            foreach (DurableOperationEndpointBinding binding in legacyHeader.Endpoints)
            {
                if (!matches.TryGetValue(binding.EndpointId, out ZDOID currentId))
                {
                    failure = "The legacy claim candidate set is incomplete.";
                    return false;
                }
                ordered.Add(currentId);
            }
            exactIds = ordered.AsReadOnly();
            return true;
        }

        internal static bool TryRequireLoadedLegacyClaimCandidates(
            IReadOnlyList<ZDOID> exactIds,
            Func<ZDOID, bool> isLoadedAsExactContainer,
            out string failure)
        {
            failure = string.Empty;
            if (exactIds == null || isLoadedAsExactContainer == null ||
                exactIds.Count < 1 || exactIds.Count > 2)
            {
                failure = "One or two exact legacy claim candidate IDs are required.";
                return false;
            }
            var unique = new HashSet<ZDOID>();
            foreach (ZDOID exactId in exactIds)
            {
                if (exactId.IsNone() || !unique.Add(exactId) ||
                    !isLoadedAsExactContainer(exactId))
                {
                    failure = "An exact legacy claim endpoint is not loaded as one unambiguous Container.";
                    return false;
                }
            }
            return true;
        }

        public static string SerializeHeader(DurableOperationHeaderRecord header)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            var package = new ZPackage();
            package.Write(header.IsLegacyIdentity ? LegacySchemaVersion : SchemaVersion);
            package.Write(header.OperationId);
            package.Write(header.ModuleId);
            package.Write(header.IsLegacyIdentity ? header.StationId : header.StationToken);
            package.Write(header.JournalStorageKey);
            package.Write(header.JournalHash);
            package.Write(header.JournalPublished);
            package.Write((int)header.Phase);
            package.Write(header.Endpoints.Count);
            foreach (DurableOperationEndpointBinding endpoint in header.Endpoints)
            {
                if (header.IsLegacyIdentity)
                    package.Write(endpoint.EndpointId);
                else
                    package.Write(endpoint.EndpointToken);
                package.Write(endpoint.BeforeFingerprint);
                package.Write(endpoint.AfterFingerprint);
            }
            string encoded = package.GetBase64();
            if (encoded.Length == 0 || encoded.Length > MaximumEncodedCharacters)
                throw new InvalidOperationException(
                    "The durable operation header exceeds its persisted size bound.");
            return encoded;
        }

        public static DurableOperationHeaderReadState ParseHeader(
            string encoded,
            out DurableOperationHeaderRecord header)
        {
            header = null;
            if (string.IsNullOrEmpty(encoded)) return DurableOperationHeaderReadState.Absent;
            if (encoded.Length > MaximumEncodedCharacters)
                return DurableOperationHeaderReadState.Corrupt;
            try
            {
                var package = new ZPackage(encoded);
                int schema = package.ReadInt();
                if (schema != SchemaVersion && schema != LegacySchemaVersion)
                    return DurableOperationHeaderReadState.Corrupt;
                string operationId = package.ReadString();
                string moduleId = package.ReadString();
                string stationIdentity = package.ReadString();
                string journalStorageKey = package.ReadString();
                string journalHash = package.ReadString();
                bool journalPublished = package.ReadBool();
                var phase = (DurableOperationPhase)package.ReadInt();
                int endpointCount = package.ReadInt();
                if (endpointCount < 1 || endpointCount > 2)
                    return DurableOperationHeaderReadState.Corrupt;
                var endpoints = new List<DurableOperationEndpointBinding>(endpointCount);
                for (int index = 0; index < endpointCount; index++)
                {
                    if (schema == LegacySchemaVersion)
                        endpoints.Add(new DurableOperationEndpointBinding(
                            package.ReadZDOID(), package.ReadString(), package.ReadString()));
                    else
                        endpoints.Add(new DurableOperationEndpointBinding(
                            package.ReadString(), ZDOID.None,
                            package.ReadString(), package.ReadString()));
                }
                if (package.GetPos() != package.Size())
                    return DurableOperationHeaderReadState.Corrupt;
                header = schema == LegacySchemaVersion
                    ? new DurableOperationHeaderRecord(
                        operationId, moduleId, stationIdentity, journalStorageKey,
                        journalHash, journalPublished, phase, endpoints)
                    : new DurableOperationHeaderRecord(
                        operationId, moduleId, stationIdentity, string.Empty,
                        journalStorageKey, journalHash, journalPublished, phase, endpoints);
                return DurableOperationHeaderReadState.Valid;
            }
            catch
            {
                header = null;
                return DurableOperationHeaderReadState.Corrupt;
            }
        }

        public static bool TryBegin(
            ZDO stationZdo,
            string operationId,
            string moduleId,
            string journalStorageKey,
            string exactJournalPayload,
            IEnumerable<DurableOperationEndpoint> endpoints,
            Func<Container, string> fingerprintReader,
            out DurableOperationHeaderRecord header,
            out string failure)
        {
            header = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure)) return false;

            DurableOperationEndpoint[] ordered;
            string journalHash;
            string stationToken;
            try
            {
                DurableOperationValidation.RequireText(operationId, nameof(operationId), 200);
                DurableOperationValidation.RequireText(moduleId, nameof(moduleId), 128);
                DurableOperationValidation.RequireText(
                    journalStorageKey, nameof(journalStorageKey), 256);
                if (string.Equals(journalStorageKey, HeaderStorageKey, StringComparison.Ordinal) ||
                    string.Equals(
                        journalStorageKey, DurableContainerSafety.LockStorageKey,
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "The operation journal must use its own station storage key.",
                        nameof(journalStorageKey));
                journalHash = ComputeJournalHash(exactJournalPayload);
                ordered = Canonicalize(endpoints);
                if (fingerprintReader == null)
                    throw new ArgumentNullException(nameof(fingerprintReader));
            }
            catch (Exception exception)
            {
                failure = "Invalid durable operation intent: " + exception.Message;
                return false;
            }
            if (!TryReadStableStationToken(stationZdo, out stationToken, out failure))
                return false;

            DurableOperationHeaderReadState current = ReadHeader(stationZdo, out _);
            if (current != DurableOperationHeaderReadState.Absent)
            {
                failure = current == DurableOperationHeaderReadState.Corrupt
                    ? "The station has a corrupt durable operation header."
                    : "The station already has an unresolved durable operation.";
                return false;
            }
            if (!string.IsNullOrEmpty(
                    stationZdo.GetString(journalStorageKey, string.Empty)))
            {
                failure = "The exact station journal must be absent before claiming endpoints.";
                return false;
            }

            var bindings = ordered.Select(endpoint =>
                new DurableOperationEndpointBinding(
                    endpoint.EndpointToken,
                    endpoint.EndpointId,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint));
            var proposed = new DurableOperationHeaderRecord(
                operationId,
                moduleId,
                stationToken,
                stationZdo.m_uid.ToString(),
                journalStorageKey,
                journalHash,
                false,
                DurableOperationPhase.Claiming,
                bindings);
            if (!TryWriteHeader(stationZdo, proposed, out failure)) return false;

            var acquired = new List<DurableOperationEndpoint>(ordered.Length);
            foreach (DurableOperationEndpoint endpoint in ordered)
            {
                DurableEndpointLockRecord claim = DurableEndpointLockRecord.CreateStable(
                    operationId,
                    moduleId,
                    proposed.StationToken,
                    endpoint.EndpointToken,
                    journalHash,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint,
                    DurableEndpointLockPhase.Prepared);
                if (!DurableContainerSafety.TryAcquire(endpoint.Container, claim, out string claimFailure))
                    return FailBeginAndClean(
                        stationZdo,
                        proposed,
                        acquired,
                        "Could not acquire endpoint " + endpoint.EndpointId + ": " + claimFailure,
                        out header,
                        out failure);
                acquired.Add(endpoint);
                if (!TryReadFingerprint(
                        endpoint.Container, fingerprintReader, out string currentFingerprint,
                        out string fingerprintFailure) ||
                    !string.Equals(
                        currentFingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal))
                    return FailBeginAndClean(
                        stationZdo,
                        proposed,
                        acquired,
                        fingerprintFailure.Length == 0
                            ? "Endpoint " + endpoint.EndpointId +
                              " changed before its durable claim was established."
                            : fingerprintFailure,
                        out header,
                        out failure);
            }

            if (!TryValidateOwnedClaims(proposed, ordered, out failure))
            {
                string validationFailure = failure;
                return FailBeginAndClean(
                    stationZdo,
                    proposed,
                    acquired,
                    validationFailure,
                    out header,
                    out failure);
            }
            header = proposed;
            return true;
        }

        /// <summary>
        /// Adopts an exact journal written by a pre-coordinator release. Endpoint claims are
        /// acquired in numeric ZDOID order before the published station header is installed.
        /// Each endpoint may already match either side of the legacy journal transition. Exact
        /// claims are idempotent so a crash after any individual claim can safely retry without
        /// clearing the journal or exposing a partially changed shared container.
        /// </summary>
        public static bool TryAdoptPublishedJournal(
            ZDO stationZdo,
            string operationId,
            string moduleId,
            string journalStorageKey,
            string exactJournalPayload,
            IEnumerable<DurableOperationEndpoint> endpoints,
            Func<Container, string> fingerprintReader,
            out DurableOperationHeaderRecord header,
            out string failure)
        {
            header = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure)) return false;

            DurableOperationEndpoint[] ordered;
            string journalHash;
            string stationToken;
            try
            {
                DurableOperationValidation.RequireText(operationId, nameof(operationId), 200);
                DurableOperationValidation.RequireText(moduleId, nameof(moduleId), 128);
                DurableOperationValidation.RequireText(
                    journalStorageKey, nameof(journalStorageKey), 256);
                if (string.Equals(journalStorageKey, HeaderStorageKey, StringComparison.Ordinal) ||
                    string.Equals(
                        journalStorageKey, DurableContainerSafety.LockStorageKey,
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "The operation journal must use its own station storage key.",
                        nameof(journalStorageKey));
                journalHash = ComputeJournalHash(exactJournalPayload);
                ordered = Canonicalize(endpoints);
                if (fingerprintReader == null)
                    throw new ArgumentNullException(nameof(fingerprintReader));
            }
            catch (Exception exception)
            {
                failure = "Invalid durable legacy operation intent: " + exception.Message;
                return false;
            }
            if (!TryReadStableStationToken(stationZdo, out stationToken, out failure))
                return false;

            if (!string.Equals(
                    stationZdo.GetString(journalStorageKey, string.Empty),
                    exactJournalPayload,
                    StringComparison.Ordinal))
            {
                failure = "The exact legacy station journal changed before adoption.";
                return false;
            }

            var bindings = ordered.Select(endpoint =>
                new DurableOperationEndpointBinding(
                    endpoint.EndpointToken,
                    endpoint.EndpointId,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint));
            var proposed = new DurableOperationHeaderRecord(
                operationId,
                moduleId,
                stationToken,
                stationZdo.m_uid.ToString(),
                journalStorageKey,
                journalHash,
                true,
                DurableOperationPhase.Claiming,
                bindings);

            DurableOperationHeaderReadState current = ReadHeader(
                stationZdo, out DurableOperationHeaderRecord existingHeader);
            if (current == DurableOperationHeaderReadState.Valid)
            {
                if (!HeaderMatches(existingHeader, moduleId, operationId, journalHash) ||
                    existingHeader.Phase != DurableOperationPhase.Claiming ||
                    !existingHeader.JournalPublished ||
                    !TryResolveExactEndpoints(
                        existingHeader, ordered, out DurableOperationEndpoint[] existingExact,
                        out _, out failure) ||
                    !TryValidateExactJournal(
                        stationZdo, existingHeader, required: true, out failure) ||
                    !TryValidateOwnedClaims(existingHeader, existingExact, out failure))
                {
                    if (failure.Length == 0)
                        failure = "The station already has a different durable operation.";
                    return false;
                }
                header = existingHeader;
                return true;
            }
            if (current == DurableOperationHeaderReadState.Corrupt)
            {
                failure = "The station has a corrupt durable operation header.";
                return false;
            }

            foreach (DurableOperationEndpoint endpoint in ordered)
            {
                DurableEndpointLockRecord claim = DurableEndpointLockRecord.CreateStable(
                    operationId,
                    moduleId,
                    proposed.StationToken,
                    endpoint.EndpointToken,
                    journalHash,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint,
                    DurableEndpointLockPhase.Prepared);
                if (!DurableContainerSafety.TryAcquire(
                        endpoint.Container, claim, out string claimFailure))
                {
                    failure = "Could not adopt endpoint " + endpoint.EndpointId + ": " +
                              claimFailure;
                    return false;
                }
                if (!TryReadFingerprint(
                        endpoint.Container, fingerprintReader, out string currentFingerprint,
                        out failure)) return false;
                if (!string.Equals(
                        currentFingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal) &&
                    !string.Equals(
                        currentFingerprint, endpoint.AfterFingerprint, StringComparison.Ordinal))
                {
                    failure = "Endpoint " + endpoint.EndpointId +
                              " matches neither exact legacy journal fingerprint.";
                    return false;
                }
            }

            if (!string.Equals(
                    stationZdo.GetString(journalStorageKey, string.Empty),
                    exactJournalPayload,
                    StringComparison.Ordinal))
            {
                failure = "The exact legacy station journal changed during endpoint adoption.";
                return false;
            }
            if (!TryValidateOwnedClaims(proposed, ordered, out failure) ||
                !TryWriteHeader(stationZdo, proposed, out failure)) return false;
            if (!TryValidateExactJournal(stationZdo, proposed, required: true, out failure) ||
                !TryValidateOwnedClaims(proposed, ordered, out failure)) return false;
            header = proposed;
            return true;
        }

        /// <summary>
        /// Converts a schema-one coordinated operation whose numeric station/endpoint IDs were
        /// rewritten while loading the world. The old header and physically co-located claims are
        /// treated only as correlation evidence: exact operation metadata, old IDs, immutable
        /// fingerprints, current inventory state, server ownership, and unique stable tokens must
        /// all agree. A schema-three envelope preserves the old-to-new mapping while endpoint
        /// claims are rewritten idempotently; the normal schema-two header is the final authority
        /// switch and is always published last.
        /// </summary>
        public static bool TryUpgradeRemappedLegacyOperationIdentity(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<Container> candidateEndpoints,
            Func<Container, string> fingerprintReader,
            out DurableOperationHeaderRecord upgraded,
            out string failure)
        {
            upgraded = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure)) return false;

            Container[] candidates;
            try
            {
                DurableOperationValidation.RequireText(moduleId, nameof(moduleId), 128);
                DurableOperationValidation.RequireText(operationId, nameof(operationId), 200);
                DurableOperationValidation.RequireJournalHash(journalHash);
                if (candidateEndpoints == null)
                    throw new ArgumentNullException(nameof(candidateEndpoints));
                candidates = candidateEndpoints.ToArray();
                if (candidates.Length < 1 || candidates.Length > 2 ||
                    candidates.Any(candidate => candidate == null))
                    throw new ArgumentOutOfRangeException(
                        nameof(candidateEndpoints),
                        "A legacy identity upgrade requires one or two exact Containers.");
                if (fingerprintReader == null)
                    throw new ArgumentNullException(nameof(fingerprintReader));
            }
            catch (Exception exception)
            {
                failure = "Invalid legacy identity-upgrade intent: " + exception.Message;
                return false;
            }

            // A retry after the final single-field authority switch is already complete. Normal
            // recovery will independently validate claims and endpoint fingerprints.
            DurableOperationHeaderReadState strictState = ReadHeader(
                stationZdo, out DurableOperationHeaderRecord strictHeader);
            if (strictState == DurableOperationHeaderReadState.Valid &&
                !strictHeader.IsLegacyIdentity)
            {
                if (!HeaderMatches(strictHeader, moduleId, operationId, journalHash))
                {
                    failure = "The station already has a different stable durable operation.";
                    return false;
                }
                upgraded = strictHeader;
                return true;
            }

            string persistedHeader = stationZdo.GetString(HeaderStorageKey, string.Empty);
            IdentityUpgradeEnvelope envelope = null;
            DurableOperationHeaderRecord legacyHeader;
            if (TryParseIdentityUpgradeEnvelope(persistedHeader, out envelope))
                legacyHeader = envelope.LegacyHeader;
            else if (ParseHeader(persistedHeader, out legacyHeader) !=
                         DurableOperationHeaderReadState.Valid ||
                     legacyHeader == null || !legacyHeader.IsLegacyIdentity)
            {
                failure = string.IsNullOrEmpty(persistedHeader)
                    ? "The station has no legacy durable operation header to upgrade."
                    : "The station header is neither an exact schema-one operation nor a retryable identity-upgrade envelope.";
                return false;
            }
            if (!HeaderMatches(legacyHeader, moduleId, operationId, journalHash))
            {
                failure = "The legacy durable operation identity or journal hash does not match exactly.";
                return false;
            }
            if (!TryValidateUpgradeJournalState(stationZdo, legacyHeader, out failure))
                return false;
            string currentJournal = stationZdo.GetString(
                legacyHeader.JournalStorageKey, string.Empty);
            bool orphanCleanup = IsRemappedLegacyOrphanCleanupEligible(
                legacyHeader,
                envelopePresent: envelope != null,
                journalPresent: !string.IsNullOrEmpty(currentJournal));
            if (orphanCleanup)
            {
                // These are the same two destructive states accepted by TryRecoverOrphan: an
                // unpublished operation with no journal (no inventory mutation can have begun),
                // or a terminal operation after its exact journal was cleared. Claims may already
                // be partially absent because claim release itself is crash-cuttable.
                return TryClearRemappedLegacyOrphan(
                    stationZdo,
                    legacyHeader,
                    persistedHeader,
                    candidates,
                    fingerprintReader,
                    out failure);
            }
            if (!TryReadStableStationToken(
                    stationZdo, out string stationToken, out failure)) return false;
            if (envelope != null && !string.Equals(
                    envelope.StationToken, stationToken, StringComparison.Ordinal))
            {
                failure = "The station token changed after legacy identity upgrade began.";
                return false;
            }

            if (!TryInspectLegacyUpgradeCandidates(
                    legacyHeader,
                    envelope,
                    stationToken,
                    candidates,
                    fingerprintReader,
                    out List<IdentityUpgradeCandidate> inspected,
                    out failure)) return false;

            if (envelope == null)
            {
                var endpointTokens = new Dictionary<ZDOID, string>();
                foreach (IdentityUpgradeCandidate candidate in inspected)
                {
                    if (!TryGetOrEnsureExactEndpointToken(
                            candidate.Container,
                            candidate.CurrentZdo,
                            out string endpointToken,
                            out failure)) return false;
                    endpointTokens.Add(candidate.LegacyBinding.EndpointId, endpointToken);
                }
                try
                {
                    envelope = CreateIdentityUpgradeEnvelope(
                        legacyHeader, stationToken, endpointTokens);
                }
                catch (Exception exception)
                {
                    failure = "Could not construct the bounded identity-upgrade envelope: " +
                              exception.GetType().Name + ".";
                    return false;
                }
                if (!TryWriteIdentityUpgradeEnvelope(stationZdo, envelope, out failure))
                    return false;
                persistedHeader = stationZdo.GetString(HeaderStorageKey, string.Empty);

                // Re-read every old/new correlation after token publication and the envelope
                // write. No claim changes until this complete proof succeeds.
                if (!TryInspectLegacyUpgradeCandidates(
                        legacyHeader,
                        envelope,
                        stationToken,
                        candidates,
                        fingerprintReader,
                        out inspected,
                        out failure)) return false;
            }

            foreach (IdentityUpgradeCandidate candidate in inspected)
            {
                DurableEndpointLockRecord expectedLegacy = CreateLegacyClaim(
                    legacyHeader, candidate.LegacyBinding);
                DurableEndpointLockRecord expectedStable = CreateStableClaim(
                    legacyHeader,
                    stationToken,
                    candidate.EndpointToken,
                    candidate.LegacyBinding);
                if (!DurableContainerSafety.TryUpgradePersistedClaimIdentity(
                        candidate.Container, expectedLegacy, expectedStable,
                        out string claimFailure))
                {
                    failure = "Could not upgrade endpoint " +
                              candidate.LegacyBinding.EndpointId + ": " + claimFailure;
                    return false;
                }
            }

            // A crash may have left a mixed old/new claim set. Re-inspection now requires the
            // exact stable side for every mapping before the final header can become authoritative.
            if (!TryInspectLegacyUpgradeCandidates(
                    legacyHeader,
                    envelope,
                    stationToken,
                    candidates,
                    fingerprintReader,
                    out inspected,
                    out failure,
                    requireStableClaims: true) ||
                !string.Equals(
                    stationZdo.GetString(HeaderStorageKey, string.Empty),
                    persistedHeader,
                    StringComparison.Ordinal) ||
                !TryValidateUpgradeJournalState(stationZdo, legacyHeader, out failure))
            {
                if (failure.Length == 0)
                    failure = "The legacy header changed during endpoint-claim identity upgrade.";
                return false;
            }

            DurableOperationEndpoint[] stableEndpoints;
            try
            {
                stableEndpoints = inspected.Select(candidate =>
                    new DurableOperationEndpoint(
                        candidate.Container,
                        candidate.EndpointToken,
                        candidate.LegacyBinding.BeforeFingerprint,
                        candidate.LegacyBinding.AfterFingerprint)).ToArray();
                Array.Sort(stableEndpoints, EndpointComparer.Instance);
            }
            catch (Exception exception)
            {
                failure = "The upgraded endpoint set is invalid: " +
                          exception.GetType().Name + ".";
                return false;
            }
            var stableBindings = stableEndpoints.Select(endpoint =>
                new DurableOperationEndpointBinding(
                    endpoint.EndpointToken,
                    endpoint.EndpointId,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint));
            DurableOperationHeaderRecord proposed;
            try
            {
                proposed = new DurableOperationHeaderRecord(
                    legacyHeader.OperationId,
                    legacyHeader.ModuleId,
                    stationToken,
                    stationZdo.m_uid.ToString(),
                    legacyHeader.JournalStorageKey,
                    legacyHeader.JournalHash,
                    legacyHeader.JournalPublished,
                    legacyHeader.Phase,
                    stableBindings);
            }
            catch (Exception exception)
            {
                failure = "The stable durable header could not be constructed: " +
                          exception.GetType().Name + ".";
                return false;
            }

            // This single Set is the authority switch. Until it succeeds, the schema-three
            // envelope keeps every old-to-new mapping available for an exact mixed-claim retry.
            if (!TryWriteHeader(stationZdo, proposed, out failure) ||
                !TryValidateOwnedClaims(proposed, stableEndpoints, out failure)) return false;
            upgraded = proposed;
            return true;
        }

        public static bool TryMarkJournalPublished(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<DurableOperationEndpoint> endpoints,
            out DurableOperationHeaderRecord publishedHeader,
            out string failure)
        {
            publishedHeader = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure) ||
                !TryReadExpectedHeader(
                    stationZdo, moduleId, operationId, journalHash,
                    out DurableOperationHeaderRecord header, out failure)) return false;
            if (header.Phase != DurableOperationPhase.Claiming)
            {
                failure = "Only a Claiming operation may publish its journal.";
                return false;
            }
            if (!TryResolveExactEndpoints(header, endpoints, out DurableOperationEndpoint[] exact,
                    out _, out failure) ||
                !TryValidateExactJournal(stationZdo, header, required: true, out failure) ||
                !TryValidateOwnedClaims(header, exact, out failure)) return false;
            if (header.JournalPublished)
            {
                publishedHeader = header;
                return true;
            }
            DurableOperationHeaderRecord next = header.WithJournalPublished();
            if (!TryWriteHeader(stationZdo, next, out failure)) return false;
            publishedHeader = next;
            return true;
        }

        public static bool TryMarkCommittedExact(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<DurableOperationEndpoint> endpoints,
            Func<Container, string> fingerprintReader,
            Func<bool> exactStationStateAndCursorProof,
            out DurableOperationHeaderRecord committedHeader,
            out string failure) =>
            TryMarkTerminalExact(
                stationZdo,
                moduleId,
                operationId,
                journalHash,
                endpoints,
                fingerprintReader,
                exactStationStateAndCursorProof,
                DurableOperationPhase.Committed,
                out committedHeader,
                out failure);

        public static bool TryMarkRolledBackExact(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<DurableOperationEndpoint> endpoints,
            Func<Container, string> fingerprintReader,
            Func<bool> exactStationStateAndCursorProof,
            out DurableOperationHeaderRecord rolledBackHeader,
            out string failure) =>
            TryMarkTerminalExact(
                stationZdo,
                moduleId,
                operationId,
                journalHash,
                endpoints,
                fingerprintReader,
                exactStationStateAndCursorProof,
                DurableOperationPhase.RolledBackExact,
                out rolledBackHeader,
                out failure);

        /// <summary>
        /// Releases this operation's remaining endpoint claims only after the caller has cleared
        /// and verified the exact journal key. A valid claim owned by a later operation is left
        /// untouched, which makes a crash during multi-endpoint release safely retryable.
        /// </summary>
        public static bool TryReleaseTerminal(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            DurableOperationPhase expectedTerminalPhase,
            IEnumerable<DurableOperationEndpoint> endpoints,
            out string failure)
        {
            failure = string.Empty;
            if (expectedTerminalPhase != DurableOperationPhase.Committed &&
                expectedTerminalPhase != DurableOperationPhase.RolledBackExact)
            {
                failure = "A terminal exact phase is required to release durable claims.";
                return false;
            }
            if (!RequireMutationContext(stationZdo, out failure) ||
                !TryReadExpectedHeader(
                    stationZdo, moduleId, operationId, journalHash,
                    out DurableOperationHeaderRecord header, out failure)) return false;
            if (header.Phase != expectedTerminalPhase)
            {
                failure = "The durable operation phase changed before claim release.";
                return false;
            }
            if (!TryValidateExactJournal(stationZdo, header, required: false, out failure) ||
                !TryResolveExactEndpoints(header, endpoints, out DurableOperationEndpoint[] exact,
                    out _, out failure)) return false;
            return TryClearRecoverableClaimsAndHeader(
                stationZdo, header, exact, out failure);
        }

        /// <summary>
        /// Inspects an exact recovery operation and performs only the two safe orphan cleanups:
        /// an unpublished operation whose journal key is still empty, or a terminal exact
        /// operation after its journal was cleared. All other states are returned to the adapter
        /// for exact journal publication, recovery, or cleanup.
        /// </summary>
        public static bool TryRecoverOrphan(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<DurableOperationEndpoint> availableEndpoints,
            out DurableOperationRecoveryDisposition disposition,
            out DurableOperationHeaderRecord header,
            out string failure)
        {
            disposition = DurableOperationRecoveryDisposition.FailedClosed;
            header = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure)) return false;
            DurableOperationHeaderReadState read = ReadHeader(stationZdo, out header);
            if (read == DurableOperationHeaderReadState.Absent)
            {
                disposition = DurableOperationRecoveryDisposition.None;
                return true;
            }
            if (read == DurableOperationHeaderReadState.Corrupt)
            {
                failure = "The station has a corrupt durable operation header.";
                return false;
            }
            if (!HeaderMatches(header, moduleId, operationId, journalHash))
            {
                failure = "The durable recovery identity or journal hash does not match exactly.";
                return false;
            }
            if (!TryResolveExactEndpoints(
                    header, availableEndpoints, out DurableOperationEndpoint[] exact,
                    out bool incomplete, out failure))
            {
                if (incomplete)
                {
                    disposition = DurableOperationRecoveryDisposition.AwaitingEndpoints;
                    failure = string.Empty;
                    return true;
                }
                return false;
            }

            string journal = stationZdo.GetString(header.JournalStorageKey, string.Empty);
            bool journalExists = !string.IsNullOrEmpty(journal);
            if (journalExists && !TryHashEquals(journal, header.JournalHash))
            {
                failure = "The persisted station journal does not match its durable header hash.";
                return false;
            }

            if (header.Phase == DurableOperationPhase.Claiming && !header.JournalPublished)
            {
                if (journalExists)
                {
                    if (!TryValidateOwnedClaims(header, exact, out failure)) return false;
                    disposition = DurableOperationRecoveryDisposition.JournalPublishRequired;
                    return true;
                }
                if (!TryClearRecoverableClaimsAndHeader(
                        stationZdo, header, exact, out failure)) return false;
                disposition = DurableOperationRecoveryDisposition.ClearedUnpublished;
                return true;
            }

            if (header.Phase == DurableOperationPhase.Claiming)
            {
                if (!journalExists)
                {
                    failure = "A published durable operation is missing its exact station journal.";
                    return false;
                }
                if (!TryValidateOwnedClaims(header, exact, out failure)) return false;
                disposition = DurableOperationRecoveryDisposition.JournalRecoveryRequired;
                return true;
            }

            if (journalExists)
            {
                if (!TryValidateOwnedClaims(header, exact, out failure)) return false;
                disposition = DurableOperationRecoveryDisposition.JournalCleanupRequired;
                return true;
            }
            if (!TryClearRecoverableClaimsAndHeader(
                    stationZdo, header, exact, out failure)) return false;
            disposition = DurableOperationRecoveryDisposition.ClearedTerminal;
            return true;
        }

        private static bool TryMarkTerminalExact(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            IEnumerable<DurableOperationEndpoint> endpoints,
            Func<Container, string> fingerprintReader,
            Func<bool> exactStationStateAndCursorProof,
            DurableOperationPhase terminalPhase,
            out DurableOperationHeaderRecord terminalHeader,
            out string failure)
        {
            terminalHeader = null;
            failure = string.Empty;
            if (!RequireMutationContext(stationZdo, out failure) ||
                !TryReadExpectedHeader(
                    stationZdo, moduleId, operationId, journalHash,
                    out DurableOperationHeaderRecord header, out failure)) return false;
            if (header.Phase == terminalPhase)
            {
                terminalHeader = header;
                return true;
            }
            if (header.Phase != DurableOperationPhase.Claiming || !header.JournalPublished)
            {
                failure = "Only a journal-published Claiming operation may become terminal.";
                return false;
            }
            if (fingerprintReader == null || exactStationStateAndCursorProof == null)
            {
                failure = "Exact endpoint and station-state proof callbacks are required.";
                return false;
            }
            if (!TryResolveExactEndpoints(header, endpoints, out DurableOperationEndpoint[] exact,
                    out _, out failure) ||
                !TryValidateExactJournal(stationZdo, header, required: true, out failure) ||
                !TryValidateOwnedClaims(header, exact, out failure)) return false;

            bool stationProven;
            try { stationProven = exactStationStateAndCursorProof(); }
            catch (Exception exception)
            {
                failure = "Exact station-state proof threw " + exception.GetType().Name + ".";
                return false;
            }
            if (!stationProven)
            {
                failure = "Exact station state, assignment, or cursor transition was not proven.";
                return false;
            }

            bool committed = terminalPhase == DurableOperationPhase.Committed;
            foreach (DurableOperationEndpoint endpoint in exact)
            {
                if (!TryReadFingerprint(
                        endpoint.Container, fingerprintReader, out string current,
                        out failure)) return false;
                string expected = committed
                    ? endpoint.AfterFingerprint
                    : endpoint.BeforeFingerprint;
                if (!string.Equals(current, expected, StringComparison.Ordinal))
                {
                    failure = "Endpoint " + endpoint.EndpointId +
                              " does not match its exact terminal fingerprint.";
                    return false;
                }
            }

            // Proof callbacks are adapter-owned. Re-read every durable invariant after they return
            // so a callback cannot accidentally publish a terminal header over changed intent.
            if (!TryReadExpectedHeader(
                    stationZdo, moduleId, operationId, journalHash,
                    out header, out failure) ||
                header.Phase != DurableOperationPhase.Claiming ||
                !header.JournalPublished ||
                !TryValidateExactJournal(stationZdo, header, required: true, out failure) ||
                !TryValidateOwnedClaims(header, exact, out failure))
            {
                if (failure.Length == 0)
                    failure = "The durable operation changed during terminal proof.";
                return false;
            }

            DurableOperationHeaderRecord next = header.WithPhase(terminalPhase);
            if (!TryWriteHeader(stationZdo, next, out failure)) return false;
            terminalHeader = next;
            return true;
        }

        private static bool FailBeginAndClean(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            IReadOnlyList<DurableOperationEndpoint> acquired,
            string originalFailure,
            out DurableOperationHeaderRecord unresolvedHeader,
            out string failure)
        {
            unresolvedHeader = null;
            if (!string.IsNullOrEmpty(
                    stationZdo.GetString(header.JournalStorageKey, string.Empty)))
            {
                unresolvedHeader = header;
                failure = originalFailure +
                          " Cleanup stopped because the station journal is no longer empty.";
                return false;
            }

            for (int index = acquired.Count - 1; index >= 0; index--)
            {
                DurableOperationEndpoint endpoint = acquired[index];
                if (!TryClearExactOwnedClaim(header, endpoint, out string clearFailure))
                {
                    unresolvedHeader = header;
                    failure = originalFailure + " Cleanup also failed: " + clearFailure;
                    return false;
                }
            }
            if (!TryClearHeader(stationZdo, out string headerFailure))
            {
                unresolvedHeader = header;
                failure = originalFailure + " Cleanup also failed: " + headerFailure;
                return false;
            }
            failure = originalFailure;
            return false;
        }

        private static bool TryClearRecoverableClaimsAndHeader(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            IReadOnlyList<DurableOperationEndpoint> endpoints,
            out string failure)
        {
            failure = string.Empty;
            foreach (DurableOperationEndpoint endpoint in endpoints.Reverse())
            {
                DurableEndpointLockReadState state = DurableContainerSafety.Read(
                    endpoint.Container, out DurableEndpointLockRecord claim);
                if (state == DurableEndpointLockReadState.Absent) continue;
                if (state == DurableEndpointLockReadState.Invalid)
                {
                    failure = "Endpoint " + endpoint.EndpointId +
                              " has a corrupt durable claim.";
                    return false;
                }
                if (!ClaimMatches(header, endpoint, claim))
                {
                    // A valid later/foreign claim is never this operation's to clear. This is safe
                    // for an unpublished empty-journal header and for a terminal empty-journal
                    // header, the only callers of this helper.
                    continue;
                }
                if (!DurableContainerSafety.TryClear(
                        endpoint.Container, header.OperationId, out failure)) return false;
            }
            return TryClearHeader(stationZdo, out failure);
        }

        private static bool TryClearExactOwnedClaim(
            DurableOperationHeaderRecord header,
            DurableOperationEndpoint endpoint,
            out string failure)
        {
            failure = string.Empty;
            DurableEndpointLockReadState state = DurableContainerSafety.Read(
                endpoint.Container, out DurableEndpointLockRecord claim);
            if (state == DurableEndpointLockReadState.Absent) return true;
            if (state != DurableEndpointLockReadState.Valid ||
                !ClaimMatches(header, endpoint, claim))
            {
                failure = "Endpoint " + endpoint.EndpointId +
                          " no longer has this operation's exact claim.";
                return false;
            }
            return DurableContainerSafety.TryClear(
                endpoint.Container, header.OperationId, out failure);
        }

        private static bool TryValidateOwnedClaims(
            DurableOperationHeaderRecord header,
            IReadOnlyList<DurableOperationEndpoint> endpoints,
            out string failure)
        {
            failure = string.Empty;
            foreach (DurableOperationEndpoint endpoint in endpoints)
            {
                DurableEndpointLockReadState state = DurableContainerSafety.Read(
                    endpoint.Container, out DurableEndpointLockRecord claim);
                if (state != DurableEndpointLockReadState.Valid ||
                    !ClaimMatches(header, endpoint, claim))
                {
                    failure = state == DurableEndpointLockReadState.Invalid
                        ? "Endpoint " + endpoint.EndpointId + " has a corrupt durable claim."
                        : "Endpoint " + endpoint.EndpointId +
                          " is missing this operation's exact durable claim.";
                    return false;
                }
                if (header.Phase == DurableOperationPhase.Claiming &&
                    claim.Phase != DurableEndpointLockPhase.Prepared)
                {
                    failure = "A Claiming operation has a prematurely terminal endpoint claim.";
                    return false;
                }
            }
            return true;
        }

        private static bool ClaimMatches(
            DurableOperationHeaderRecord header,
            DurableOperationEndpoint endpoint,
            DurableEndpointLockRecord claim) =>
            claim != null &&
            claim.IsLegacyIdentity == header.IsLegacyIdentity &&
            string.Equals(claim.OperationId, header.OperationId, StringComparison.Ordinal) &&
            string.Equals(claim.ModuleId, header.ModuleId, StringComparison.Ordinal) &&
            (header.IsLegacyIdentity
                ? string.Equals(
                      claim.StationId, header.StationId, StringComparison.Ordinal) &&
                  string.Equals(
                      claim.EndpointId, endpoint.EndpointId.ToString(), StringComparison.Ordinal)
                : string.Equals(
                      claim.StationToken, header.StationToken, StringComparison.Ordinal) &&
                  string.Equals(
                      claim.EndpointToken, endpoint.EndpointToken, StringComparison.Ordinal)) &&
            string.Equals(claim.JournalKind, header.JournalHash, StringComparison.Ordinal) &&
            string.Equals(
                claim.BeforeFingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal) &&
            string.Equals(
                claim.AfterFingerprint, endpoint.AfterFingerprint, StringComparison.Ordinal);

        private static bool TryResolveExactEndpoints(
            DurableOperationHeaderRecord header,
            IEnumerable<DurableOperationEndpoint> endpoints,
            out DurableOperationEndpoint[] exact,
            out bool incomplete,
            out string failure)
        {
            exact = Array.Empty<DurableOperationEndpoint>();
            incomplete = false;
            failure = string.Empty;
            DurableOperationEndpoint[] supplied;
            try
            {
                supplied = endpoints == null
                    ? Array.Empty<DurableOperationEndpoint>()
                    : endpoints.ToArray();
                if (supplied.Any(endpoint => endpoint == null))
                    throw new ArgumentException("Endpoint sets cannot contain null.", nameof(endpoints));
                if (supplied.Length > 2)
                    throw new ArgumentOutOfRangeException(nameof(endpoints));
                Array.Sort(supplied, EndpointComparer.Instance);
            }
            catch (Exception exception)
            {
                failure = "Invalid durable endpoint set: " + exception.Message;
                return false;
            }

            var resolved = new List<DurableOperationEndpoint>(header.Endpoints.Count);
            if (header.IsLegacyIdentity)
            {
                var byId = new Dictionary<ZDOID, DurableOperationEndpoint>();
                foreach (DurableOperationEndpoint endpoint in supplied)
                {
                    if (byId.ContainsKey(endpoint.EndpointId))
                    {
                        failure = "The durable endpoint set contains a duplicate current ZDOID.";
                        return false;
                    }
                    byId.Add(endpoint.EndpointId, endpoint);
                }
                foreach (DurableOperationEndpointBinding binding in header.Endpoints)
                {
                    if (!byId.TryGetValue(
                            binding.EndpointId, out DurableOperationEndpoint endpoint))
                    {
                        incomplete = true;
                        continue;
                    }
                    if (!BindingFingerprintsMatch(binding, endpoint))
                    {
                        failure = "Legacy endpoint " + binding.EndpointId +
                                  " does not match the durable header fingerprints.";
                        return false;
                    }
                    resolved.Add(endpoint);
                    byId.Remove(binding.EndpointId);
                }
                if (byId.Count != 0)
                {
                    failure = "The supplied endpoint set contains an endpoint outside the header.";
                    return false;
                }
            }
            else
            {
                var byToken = new Dictionary<string, DurableOperationEndpoint>(
                    StringComparer.Ordinal);
                foreach (DurableOperationEndpoint endpoint in supplied)
                {
                    if (endpoint.IsLegacyIdentity)
                    {
                        failure = "A stable durable header cannot bind a legacy ZDOID endpoint.";
                        return false;
                    }
                    if (byToken.ContainsKey(endpoint.EndpointToken))
                    {
                        failure = "The durable endpoint set contains a duplicate stable token.";
                        return false;
                    }
                    byToken.Add(endpoint.EndpointToken, endpoint);
                }
                foreach (DurableOperationEndpointBinding binding in header.Endpoints)
                {
                    if (!byToken.TryGetValue(
                            binding.EndpointToken, out DurableOperationEndpoint endpoint))
                    {
                        incomplete = true;
                        continue;
                    }
                    if (!BindingFingerprintsMatch(binding, endpoint))
                    {
                        failure = "Endpoint " + binding.StableEndpointId +
                                  " does not match the durable header fingerprints.";
                        return false;
                    }
                    resolved.Add(endpoint);
                    byToken.Remove(binding.EndpointToken);
                }
                if (byToken.Count != 0)
                {
                    failure = "The supplied endpoint set contains an endpoint outside the header.";
                    return false;
                }
            }
            if (incomplete)
            {
                exact = resolved.ToArray();
                return false;
            }
            exact = resolved.ToArray();
            return true;
        }

        private static bool TryClearRemappedLegacyOrphan(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            string expectedPersistedHeader,
            IReadOnlyList<Container> candidates,
            Func<Container, string> fingerprintReader,
            out string failure)
        {
            failure = string.Empty;
            if (stationZdo == null || header == null || !header.IsLegacyIdentity ||
                candidates == null || candidates.Count != header.Endpoints.Count ||
                !string.IsNullOrEmpty(
                    stationZdo.GetString(header.JournalStorageKey, string.Empty)))
            {
                failure = "The remapped legacy operation is not an exact orphan-cleanup state.";
                return false;
            }
            bool committed = header.Phase == DurableOperationPhase.Committed;
            var expectedFingerprints = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DurableOperationEndpointBinding binding in header.Endpoints)
            {
                string expected = committed
                    ? binding.AfterFingerprint
                    : binding.BeforeFingerprint;
                expectedFingerprints.TryGetValue(expected, out int count);
                expectedFingerprints[expected] = count + 1;
            }

            var currentIds = new HashSet<ZDOID>();
            var claimedLegacyIds = new HashSet<ZDOID>();
            var exactClaims = new List<Tuple<Container, DurableEndpointLockRecord>>();
            foreach (Container container in candidates)
            {
                if (!DurableContainerSafety.TrySynchronizeServerOwned(container, out _))
                {
                    failure = "An orphan endpoint candidate is not synchronized, closed, and server-owned.";
                    return false;
                }
                ZNetView view = WorldObjectIdentity.View(container);
                ZDO currentZdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (currentZdo == null || currentZdo.m_uid.IsNone() ||
                    !currentIds.Add(currentZdo.m_uid))
                {
                    failure = "Orphan endpoint candidates do not have distinct exact current ZDO roots.";
                    return false;
                }
                if (!TryReadFingerprint(
                        container, fingerprintReader, out string currentFingerprint,
                        out failure)) return false;
                if (!expectedFingerprints.TryGetValue(
                        currentFingerprint, out int remaining) || remaining <= 0)
                {
                    failure = "An orphan endpoint does not match the exact terminal fingerprint multiset.";
                    return false;
                }
                if (remaining == 1) expectedFingerprints.Remove(currentFingerprint);
                else expectedFingerprints[currentFingerprint] = remaining - 1;

                DurableEndpointLockReadState claimState =
                    DurableContainerSafety.ReadPersistedForIdentityUpgrade(
                        container, out DurableEndpointLockRecord currentClaim);
                if (claimState == DurableEndpointLockReadState.Absent) continue;
                if (claimState != DurableEndpointLockReadState.Valid || currentClaim == null)
                {
                    failure = "An orphan endpoint has corrupt persisted claim evidence.";
                    return false;
                }
                DurableEndpointLockRecord matched = null;
                ZDOID matchedId = ZDOID.None;
                foreach (DurableOperationEndpointBinding binding in header.Endpoints)
                {
                    DurableEndpointLockRecord expected = CreateLegacyClaim(header, binding);
                    if (!currentClaim.MatchesExact(expected)) continue;
                    if (matched != null)
                    {
                        failure = "One orphan claim matches more than one legacy endpoint binding.";
                        return false;
                    }
                    matched = expected;
                    matchedId = binding.EndpointId;
                }
                if (matched == null || !claimedLegacyIds.Add(matchedId))
                {
                    failure = "An orphan endpoint carries a different durable claim.";
                    return false;
                }
                DurableOperationEndpointBinding matchedBinding = header.Endpoints.First(
                    binding => binding.EndpointId == matchedId);
                string matchedFingerprint = committed
                    ? matchedBinding.AfterFingerprint
                    : matchedBinding.BeforeFingerprint;
                if (!string.Equals(
                        currentFingerprint, matchedFingerprint,
                        StringComparison.Ordinal))
                {
                    failure = "An orphan claim is not co-located with its exact terminal fingerprint.";
                    return false;
                }
                exactClaims.Add(Tuple.Create(container, matched));
            }
            if (expectedFingerprints.Count != 0 ||
                !string.Equals(
                    stationZdo.GetString(HeaderStorageKey, string.Empty),
                    expectedPersistedHeader,
                    StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(
                    stationZdo.GetString(header.JournalStorageKey, string.Empty)))
            {
                failure = "The orphan header, journal, or endpoint fingerprints changed during proof.";
                return false;
            }

            foreach (Tuple<Container, DurableEndpointLockRecord> exact in exactClaims)
                if (!DurableContainerSafety.TryClearPersistedLegacyClaimForIdentityUpgrade(
                        exact.Item1, exact.Item2, out failure)) return false;
            foreach (Container container in candidates)
                if (DurableContainerSafety.ReadPersistedForIdentityUpgrade(
                        container, out _) != DurableEndpointLockReadState.Absent)
                {
                    failure = "A remapped legacy orphan claim remains after exact cleanup.";
                    return false;
                }
            if (!string.Equals(
                    stationZdo.GetString(HeaderStorageKey, string.Empty),
                    expectedPersistedHeader,
                    StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(
                    stationZdo.GetString(header.JournalStorageKey, string.Empty)))
            {
                failure = "The orphan header or journal changed before final header cleanup.";
                return false;
            }
            return TryClearHeader(stationZdo, out failure);
        }

        /// <summary>
        /// Pure destructive-transition policy used by focused tests. Only a physical schema-one
        /// header (not a partially published schema-three envelope) may be cleared without its
        /// journal, and a Claiming header is safe only before journal publication. Terminal
        /// headers are eligible because exact terminal fingerprints are independently required
        /// before any claim or header byte is cleared.
        /// </summary>
        internal static bool IsRemappedLegacyOrphanCleanupEligible(
            DurableOperationHeaderRecord header,
            bool envelopePresent,
            bool journalPresent) =>
            header != null && header.IsLegacyIdentity && !envelopePresent && !journalPresent &&
            (header.Phase != DurableOperationPhase.Claiming || !header.JournalPublished);

        private static bool TryValidateUpgradeJournalState(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            out string failure)
        {
            failure = string.Empty;
            if (stationZdo == null || header == null)
            {
                failure = "Legacy journal validation state is unavailable.";
                return false;
            }
            string journal = stationZdo.GetString(header.JournalStorageKey, string.Empty);
            if (string.IsNullOrEmpty(journal))
            {
                if (header.Phase == DurableOperationPhase.Claiming &&
                    header.JournalPublished)
                {
                    failure = "A published Claiming operation is missing its exact station journal.";
                    return false;
                }
                return true;
            }
            if (TryHashEquals(journal, header.JournalHash)) return true;
            failure = "The station journal does not match the legacy durable header hash.";
            return false;
        }

        private static bool TryInspectLegacyUpgradeCandidates(
            DurableOperationHeaderRecord legacyHeader,
            IdentityUpgradeEnvelope envelope,
            string stationToken,
            IReadOnlyList<Container> candidates,
            Func<Container, string> fingerprintReader,
            out List<IdentityUpgradeCandidate> inspected,
            out string failure,
            bool requireStableClaims = false)
        {
            inspected = new List<IdentityUpgradeCandidate>();
            failure = string.Empty;
            if (legacyHeader == null || !legacyHeader.IsLegacyIdentity ||
                candidates == null || candidates.Count != legacyHeader.Endpoints.Count)
            {
                failure = "The candidate count does not exactly cover the legacy header bindings.";
                return false;
            }
            var currentIds = new HashSet<ZDOID>();
            var coveredLegacyIds = new HashSet<ZDOID>();
            foreach (Container container in candidates)
            {
                if (!DurableContainerSafety.TrySynchronizeServerOwned(container, out _))
                {
                    failure = "A legacy endpoint candidate is not synchronized, closed, and server-owned.";
                    return false;
                }
                ZNetView view = WorldObjectIdentity.View(container);
                ZDO currentZdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (currentZdo == null || currentZdo.m_uid.IsNone() ||
                    !currentIds.Add(currentZdo.m_uid))
                {
                    failure = "Legacy endpoint candidates do not have distinct exact current ZDO roots.";
                    return false;
                }
                DurableEndpointLockReadState claimState =
                    DurableContainerSafety.ReadPersistedForIdentityUpgrade(
                        container, out DurableEndpointLockRecord claim);
                if (claimState != DurableEndpointLockReadState.Valid || claim == null)
                {
                    failure = claimState == DurableEndpointLockReadState.Invalid
                        ? "A legacy endpoint candidate has a corrupt persisted claim."
                        : "A legacy endpoint candidate is missing its persisted claim.";
                    return false;
                }

                IdentityUpgradeBinding matched = null;
                string endpointToken = string.Empty;
                if (envelope == null)
                {
                    foreach (DurableOperationEndpointBinding binding in legacyHeader.Endpoints)
                    {
                        DurableEndpointLockRecord expected = CreateLegacyClaim(
                            legacyHeader, binding);
                        if (!claim.MatchesExact(expected)) continue;
                        if (matched != null)
                        {
                            failure = "One persisted claim matches more than one legacy binding.";
                            return false;
                        }
                        matched = new IdentityUpgradeBinding(binding, string.Empty);
                    }
                }
                else
                {
                    foreach (IdentityUpgradeBinding binding in envelope.Bindings)
                    {
                        DurableEndpointLockRecord expectedLegacy = CreateLegacyClaim(
                            legacyHeader, binding.LegacyBinding);
                        DurableEndpointLockRecord expectedStable = CreateStableClaim(
                            legacyHeader,
                            stationToken,
                            binding.EndpointToken,
                            binding.LegacyBinding);
                        bool legacyMatch = claim.MatchesExact(expectedLegacy);
                        bool stableMatch = claim.MatchesExact(expectedStable);
                        if (requireStableClaims && !stableMatch ||
                            !legacyMatch && !stableMatch) continue;
                        if (matched != null)
                        {
                            failure = "One persisted claim matches more than one upgrade mapping.";
                            return false;
                        }
                        matched = binding;
                        endpointToken = binding.EndpointToken;
                    }
                }
                if (matched == null ||
                    !coveredLegacyIds.Add(matched.LegacyBinding.EndpointId))
                {
                    failure = "The candidate claims do not map one-to-one to every legacy header endpoint.";
                    return false;
                }

                if (envelope != null)
                {
                    if (WorldObjectIdentity.Read(
                            currentZdo, out WorldObjectToken actualToken) !=
                            WorldObjectIdentityStatus.Ready ||
                        !string.Equals(
                            actualToken.Value, endpointToken, StringComparison.Ordinal) ||
                        WorldObjectIdentity.Resolve(actualToken, out ZDO exact) !=
                            WorldObjectIdentityStatus.Ready || exact == null ||
                        exact.m_uid != currentZdo.m_uid)
                    {
                        failure = "An upgrade endpoint token is not uniquely bound to its exact current ZDO.";
                        return false;
                    }
                }

                if (!TryReadFingerprint(
                        container, fingerprintReader, out string currentFingerprint,
                        out failure)) return false;
                if (!string.Equals(
                        currentFingerprint,
                        matched.LegacyBinding.BeforeFingerprint,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        currentFingerprint,
                        matched.LegacyBinding.AfterFingerprint,
                        StringComparison.Ordinal))
                {
                    failure = "A legacy endpoint candidate matches neither immutable Before nor After fingerprint.";
                    return false;
                }
                inspected.Add(new IdentityUpgradeCandidate(
                    container,
                    currentZdo,
                    matched.LegacyBinding,
                    endpointToken));
            }
            if (coveredLegacyIds.Count != legacyHeader.Endpoints.Count)
            {
                failure = "The candidates do not completely cover the legacy header endpoint set.";
                return false;
            }
            return true;
        }

        private static bool TryGetOrEnsureExactEndpointToken(
            Container container,
            ZDO expectedZdo,
            out string endpointToken,
            out string failure)
        {
            endpointToken = string.Empty;
            failure = string.Empty;
            ZNetView view = WorldObjectIdentity.View(container);
            ZDO currentZdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (currentZdo == null || expectedZdo == null ||
                currentZdo.m_uid != expectedZdo.m_uid)
            {
                failure = "The endpoint ZDO changed before stable identity capture.";
                return false;
            }
            WorldObjectIdentityStatus status = WorldObjectIdentity.Read(
                currentZdo, out WorldObjectToken token);
            if (status == WorldObjectIdentityStatus.Missing)
                status = WorldObjectIdentity.Ensure(view, out token);
            if (status != WorldObjectIdentityStatus.Ready ||
                WorldObjectIdentity.Resolve(token, out ZDO exact) !=
                    WorldObjectIdentityStatus.Ready || exact == null ||
                exact.m_uid != currentZdo.m_uid)
            {
                failure = "The endpoint could not acquire one unique stable token (" + status + ").";
                return false;
            }
            endpointToken = token.Value;
            return true;
        }

        private static DurableEndpointLockRecord CreateLegacyClaim(
            DurableOperationHeaderRecord header,
            DurableOperationEndpointBinding binding) =>
            new DurableEndpointLockRecord(
                header.OperationId,
                header.ModuleId,
                header.StationId,
                binding.EndpointId.ToString(),
                header.JournalHash,
                binding.BeforeFingerprint,
                binding.AfterFingerprint,
                DurableEndpointLockPhase.Prepared);

        private static DurableEndpointLockRecord CreateStableClaim(
            DurableOperationHeaderRecord header,
            string stationToken,
            string endpointToken,
            DurableOperationEndpointBinding binding) =>
            DurableEndpointLockRecord.CreateStable(
                header.OperationId,
                header.ModuleId,
                stationToken,
                endpointToken,
                header.JournalHash,
                binding.BeforeFingerprint,
                binding.AfterFingerprint,
                DurableEndpointLockPhase.Prepared);

        private static IdentityUpgradeEnvelope CreateIdentityUpgradeEnvelope(
            DurableOperationHeaderRecord legacyHeader,
            string stationToken,
            IReadOnlyDictionary<ZDOID, string> endpointTokens)
        {
            if (legacyHeader == null || !legacyHeader.IsLegacyIdentity)
                throw new ArgumentException("A schema-one header is required.", nameof(legacyHeader));
            if (!WorldObjectToken.TryParse(stationToken, out WorldObjectToken parsedStation))
                throw new ArgumentException("A canonical station token is required.", nameof(stationToken));
            if (endpointTokens == null || endpointTokens.Count != legacyHeader.Endpoints.Count)
                throw new ArgumentException("Every legacy binding requires one endpoint token.", nameof(endpointTokens));
            var bindings = new List<IdentityUpgradeBinding>(legacyHeader.Endpoints.Count);
            var uniqueTokens = new HashSet<string>(StringComparer.Ordinal);
            foreach (DurableOperationEndpointBinding binding in legacyHeader.Endpoints)
            {
                if (!endpointTokens.TryGetValue(
                        binding.EndpointId, out string token) ||
                    !WorldObjectToken.TryParse(token, out WorldObjectToken parsedEndpoint) ||
                    !uniqueTokens.Add(parsedEndpoint.Value))
                    throw new ArgumentException(
                        "Legacy endpoint mappings require distinct canonical tokens.",
                        nameof(endpointTokens));
                bindings.Add(new IdentityUpgradeBinding(binding, parsedEndpoint.Value));
            }
            return new IdentityUpgradeEnvelope(
                legacyHeader, parsedStation.Value, bindings.AsReadOnly());
        }

        internal static string SerializeIdentityUpgradeEnvelope(
            DurableOperationHeaderRecord legacyHeader,
            string stationToken,
            IReadOnlyDictionary<ZDOID, string> endpointTokens) =>
            SerializeIdentityUpgradeEnvelope(
                CreateIdentityUpgradeEnvelope(legacyHeader, stationToken, endpointTokens));

        private static string SerializeIdentityUpgradeEnvelope(
            IdentityUpgradeEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            DurableOperationHeaderRecord header = envelope.LegacyHeader;
            var package = new ZPackage();
            package.Write(IdentityUpgradeEnvelopeSchemaVersion);
            package.Write(header.OperationId);
            package.Write(header.ModuleId);
            package.Write(header.StationId);
            package.Write(envelope.StationToken);
            package.Write(header.JournalStorageKey);
            package.Write(header.JournalHash);
            package.Write(header.JournalPublished);
            package.Write((int)header.Phase);
            package.Write(envelope.Bindings.Count);
            foreach (IdentityUpgradeBinding binding in envelope.Bindings)
            {
                package.Write(binding.LegacyBinding.EndpointId);
                package.Write(binding.EndpointToken);
                package.Write(binding.LegacyBinding.BeforeFingerprint);
                package.Write(binding.LegacyBinding.AfterFingerprint);
            }
            string encoded = package.GetBase64();
            if (encoded.Length == 0 || encoded.Length > MaximumEncodedCharacters)
                throw new InvalidOperationException(
                    "The durable identity-upgrade envelope exceeds its persisted size bound.");
            return encoded;
        }

        internal static bool TryParseIdentityUpgradeEnvelopeForTests(
            string encoded,
            out DurableOperationHeaderRecord legacyHeader,
            out string stationToken,
            out IReadOnlyDictionary<ZDOID, string> endpointTokens)
        {
            legacyHeader = null;
            stationToken = string.Empty;
            endpointTokens = null;
            if (!TryParseIdentityUpgradeEnvelope(
                    encoded, out IdentityUpgradeEnvelope envelope)) return false;
            legacyHeader = envelope.LegacyHeader;
            stationToken = envelope.StationToken;
            endpointTokens = envelope.Bindings.ToDictionary(
                binding => binding.LegacyBinding.EndpointId,
                binding => binding.EndpointToken);
            return true;
        }

        private static bool TryParseIdentityUpgradeEnvelope(
            string encoded,
            out IdentityUpgradeEnvelope envelope)
        {
            envelope = null;
            if (string.IsNullOrEmpty(encoded) ||
                encoded.Length > MaximumEncodedCharacters) return false;
            try
            {
                var package = new ZPackage(encoded);
                if (package.ReadInt() != IdentityUpgradeEnvelopeSchemaVersion) return false;
                string operationId = package.ReadString();
                string moduleId = package.ReadString();
                string oldStationId = package.ReadString();
                string stationToken = package.ReadString();
                string journalKey = package.ReadString();
                string journalHash = package.ReadString();
                bool journalPublished = package.ReadBool();
                var phase = (DurableOperationPhase)package.ReadInt();
                int count = package.ReadInt();
                if (count < 1 || count > 2) return false;
                var legacyBindings = new List<DurableOperationEndpointBinding>(count);
                var mappedTokens = new Dictionary<ZDOID, string>();
                for (int index = 0; index < count; index++)
                {
                    ZDOID oldEndpointId = package.ReadZDOID();
                    string endpointToken = package.ReadString();
                    string before = package.ReadString();
                    string after = package.ReadString();
                    legacyBindings.Add(new DurableOperationEndpointBinding(
                        oldEndpointId, before, after));
                    mappedTokens.Add(oldEndpointId, endpointToken);
                }
                if (package.GetPos() != package.Size()) return false;
                var legacyHeader = new DurableOperationHeaderRecord(
                    operationId,
                    moduleId,
                    oldStationId,
                    journalKey,
                    journalHash,
                    journalPublished,
                    phase,
                    legacyBindings);
                envelope = CreateIdentityUpgradeEnvelope(
                    legacyHeader, stationToken, mappedTokens);
                return true;
            }
            catch
            {
                envelope = null;
                return false;
            }
        }

        private static bool TryWriteIdentityUpgradeEnvelope(
            ZDO stationZdo,
            IdentityUpgradeEnvelope envelope,
            out string failure)
        {
            failure = string.Empty;
            try
            {
                string encoded = SerializeIdentityUpgradeEnvelope(envelope);
                stationZdo.Set(HeaderStorageKey, encoded);
                if (!string.Equals(
                        stationZdo.GetString(HeaderStorageKey, string.Empty), encoded,
                        StringComparison.Ordinal) ||
                    !TryParseIdentityUpgradeEnvelope(
                        encoded, out IdentityUpgradeEnvelope persisted) ||
                    !IdentityUpgradeEnvelopesMatch(envelope, persisted))
                {
                    failure = "The durable identity-upgrade envelope did not publish exactly.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = "Could not publish the durable identity-upgrade envelope: " +
                          exception.GetType().Name + ".";
                return false;
            }
        }

        private static bool IdentityUpgradeEnvelopesMatch(
            IdentityUpgradeEnvelope left,
            IdentityUpgradeEnvelope right)
        {
            if (left == null || right == null ||
                !HeaderMatches(
                    right.LegacyHeader,
                    left.LegacyHeader.ModuleId,
                    left.LegacyHeader.OperationId,
                    left.LegacyHeader.JournalHash) ||
                right.LegacyHeader.Phase != left.LegacyHeader.Phase ||
                right.LegacyHeader.JournalPublished != left.LegacyHeader.JournalPublished ||
                !string.Equals(
                    right.LegacyHeader.StationId,
                    left.LegacyHeader.StationId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    right.LegacyHeader.JournalStorageKey,
                    left.LegacyHeader.JournalStorageKey,
                    StringComparison.Ordinal) ||
                !string.Equals(right.StationToken, left.StationToken, StringComparison.Ordinal) ||
                right.Bindings.Count != left.Bindings.Count) return false;
            for (int index = 0; index < left.Bindings.Count; index++)
            {
                IdentityUpgradeBinding expected = left.Bindings[index];
                IdentityUpgradeBinding actual = right.Bindings[index];
                if (actual.LegacyBinding.EndpointId != expected.LegacyBinding.EndpointId ||
                    !string.Equals(
                        actual.EndpointToken, expected.EndpointToken,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.LegacyBinding.BeforeFingerprint,
                        expected.LegacyBinding.BeforeFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.LegacyBinding.AfterFingerprint,
                        expected.LegacyBinding.AfterFingerprint,
                        StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static DurableOperationEndpoint[] Canonicalize(
            IEnumerable<DurableOperationEndpoint> endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            DurableOperationEndpoint[] result = endpoints.ToArray();
            if (result.Length < 1 || result.Length > 2)
                throw new ArgumentOutOfRangeException(
                    nameof(endpoints), "A durable operation requires one or two endpoints.");
            if (result.Any(endpoint => endpoint == null))
                throw new ArgumentException("Endpoint sets cannot contain null.", nameof(endpoints));
            if (result.Any(endpoint => endpoint.IsLegacyIdentity))
                throw new ArgumentException(
                    "New durable operations require restart-stable endpoint tokens.",
                    nameof(endpoints));
            foreach (DurableOperationEndpoint endpoint in result)
            {
                WorldObjectIdentityStatus status = WorldObjectIdentity.Resolve(
                    endpoint.EndpointToken, out ZDO resolved);
                if (status != WorldObjectIdentityStatus.Ready || resolved == null ||
                    resolved.m_uid != endpoint.EndpointId)
                    throw new ArgumentException(
                        "Each endpoint token must resolve uniquely to its exact current ZDO.",
                        nameof(endpoints));
            }
            Array.Sort(result, EndpointComparer.Instance);
            for (int index = 1; index < result.Length; index++)
                if (string.Equals(
                        result[index - 1].EndpointToken,
                        result[index].EndpointToken,
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "A durable operation cannot claim one endpoint twice.", nameof(endpoints));
            return result;
        }

        private static bool BindingFingerprintsMatch(
            DurableOperationEndpointBinding binding,
            DurableOperationEndpoint endpoint) =>
            string.Equals(
                endpoint.BeforeFingerprint, binding.BeforeFingerprint,
                StringComparison.Ordinal) &&
            string.Equals(
                endpoint.AfterFingerprint, binding.AfterFingerprint,
                StringComparison.Ordinal);

        private static bool TryValidateExactJournal(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            bool required,
            out string failure)
        {
            failure = string.Empty;
            string journal = stationZdo.GetString(header.JournalStorageKey, string.Empty);
            if (string.IsNullOrEmpty(journal))
            {
                if (!required) return true;
                failure = "The exact station journal is absent.";
                return false;
            }
            if (required && TryHashEquals(journal, header.JournalHash)) return true;
            failure = required
                ? "The exact station journal does not match its durable header hash."
                : "The caller must clear and verify the exact station journal before release.";
            return false;
        }

        private static bool TryHashEquals(string journal, string expectedHash)
        {
            try
            {
                return string.Equals(
                    ComputeJournalHash(journal), expectedHash, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static bool TryReadFingerprint(
            Container container,
            Func<Container, string> reader,
            out string fingerprint,
            out string failure)
        {
            fingerprint = string.Empty;
            failure = string.Empty;
            try
            {
                fingerprint = DurableOperationValidation.RequireText(
                    reader(container), "fingerprint", 256);
                return true;
            }
            catch (Exception exception)
            {
                failure = "Exact endpoint fingerprint proof failed: " +
                          exception.GetType().Name + ".";
                fingerprint = string.Empty;
                return false;
            }
        }

        private static bool TryReadExpectedHeader(
            ZDO stationZdo,
            string moduleId,
            string operationId,
            string journalHash,
            out DurableOperationHeaderRecord header,
            out string failure)
        {
            failure = string.Empty;
            DurableOperationHeaderReadState state = ReadHeader(stationZdo, out header);
            if (state != DurableOperationHeaderReadState.Valid)
            {
                failure = state == DurableOperationHeaderReadState.Corrupt
                    ? "The station has a corrupt durable operation header."
                    : "The station has no durable operation header.";
                return false;
            }
            if (!HeaderMatches(header, moduleId, operationId, journalHash))
            {
                failure = "The durable operation identity or journal hash changed.";
                return false;
            }
            return true;
        }

        private static bool HeaderMatches(
            DurableOperationHeaderRecord header,
            string moduleId,
            string operationId,
            string journalHash) =>
            header != null &&
            string.Equals(header.ModuleId, moduleId, StringComparison.Ordinal) &&
            string.Equals(header.OperationId, operationId, StringComparison.Ordinal) &&
            string.Equals(header.JournalHash, journalHash, StringComparison.Ordinal);

        private static bool TryWriteHeader(
            ZDO stationZdo,
            DurableOperationHeaderRecord header,
            out string failure)
        {
            failure = string.Empty;
            try
            {
                string encoded = SerializeHeader(header);
                stationZdo.Set(HeaderStorageKey, encoded);
                if (!string.Equals(
                        stationZdo.GetString(HeaderStorageKey, string.Empty), encoded,
                        StringComparison.Ordinal) ||
                    ReadHeader(stationZdo, out DurableOperationHeaderRecord persisted) !=
                    DurableOperationHeaderReadState.Valid ||
                    !HeaderMatches(
                        persisted, header.ModuleId, header.OperationId, header.JournalHash) ||
                    persisted.Phase != header.Phase ||
                    persisted.JournalPublished != header.JournalPublished)
                {
                    failure = "The durable operation header did not publish exactly.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = "Could not publish the durable operation header: " +
                          exception.GetType().Name + ".";
                return false;
            }
        }

        private static bool TryClearHeader(ZDO stationZdo, out string failure)
        {
            failure = string.Empty;
            stationZdo.Set(HeaderStorageKey, string.Empty);
            if (ReadHeader(stationZdo, out _) == DurableOperationHeaderReadState.Absent)
                return true;
            failure = "The durable operation header did not clear exactly.";
            return false;
        }

        private static bool RequireMutationContext(ZDO stationZdo, out string failure)
        {
            failure = string.Empty;
            if (stationZdo == null || stationZdo.m_uid.IsNone())
            {
                failure = "An exact station ZDO is required.";
                return false;
            }
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                failure = "Durable operations require authoritative server execution.";
                return false;
            }
            if (!RunicMutationGate.IsHeld)
            {
                failure = "The shared Runic mutation gate must be held for this durable transition.";
                return false;
            }
            return true;
        }

        private static bool TryReadStableStationToken(
            ZDO stationZdo,
            out string stationToken,
            out string failure)
        {
            stationToken = string.Empty;
            failure = string.Empty;
            WorldObjectIdentityStatus read = WorldObjectIdentity.Read(
                stationZdo, out WorldObjectToken token);
            if (read != WorldObjectIdentityStatus.Ready)
            {
                failure = "The station is missing a valid restart-stable world-object token.";
                return false;
            }
            WorldObjectIdentityStatus resolved = WorldObjectIdentity.Resolve(token, out ZDO exact);
            if (resolved != WorldObjectIdentityStatus.Ready || exact == null ||
                exact.m_uid != stationZdo.m_uid)
            {
                failure = "The station world-object token is not uniquely bound to this ZDO (" +
                          resolved + ").";
                return false;
            }
            stationToken = token.Value;
            return true;
        }

        private sealed class IdentityUpgradeEnvelope
        {
            internal IdentityUpgradeEnvelope(
                DurableOperationHeaderRecord legacyHeader,
                string stationToken,
                IReadOnlyList<IdentityUpgradeBinding> bindings)
            {
                LegacyHeader = legacyHeader ?? throw new ArgumentNullException(nameof(legacyHeader));
                if (!legacyHeader.IsLegacyIdentity)
                    throw new ArgumentException(
                        "An identity-upgrade envelope requires a schema-one header.",
                        nameof(legacyHeader));
                if (!WorldObjectToken.TryParse(stationToken, out WorldObjectToken parsed))
                    throw new ArgumentException(
                        "An identity-upgrade envelope requires a canonical station token.",
                        nameof(stationToken));
                StationToken = parsed.Value;
                Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
                if (bindings.Count != legacyHeader.Endpoints.Count)
                    throw new ArgumentException(
                        "The identity-upgrade mapping must cover every legacy endpoint.",
                        nameof(bindings));
            }

            internal DurableOperationHeaderRecord LegacyHeader { get; }
            internal string StationToken { get; }
            internal IReadOnlyList<IdentityUpgradeBinding> Bindings { get; }
        }

        private sealed class IdentityUpgradeBinding
        {
            internal IdentityUpgradeBinding(
                DurableOperationEndpointBinding legacyBinding,
                string endpointToken)
            {
                LegacyBinding = legacyBinding ??
                    throw new ArgumentNullException(nameof(legacyBinding));
                if (!legacyBinding.IsLegacyIdentity)
                    throw new ArgumentException(
                        "An identity-upgrade mapping requires a schema-one binding.",
                        nameof(legacyBinding));
                EndpointToken = endpointToken ?? string.Empty;
                if (EndpointToken.Length != 0 &&
                    !WorldObjectToken.TryParse(EndpointToken, out _))
                    throw new ArgumentException(
                        "An identity-upgrade mapping token must be canonical.",
                        nameof(endpointToken));
            }

            internal DurableOperationEndpointBinding LegacyBinding { get; }
            internal string EndpointToken { get; }
        }

        private sealed class IdentityUpgradeCandidate
        {
            internal IdentityUpgradeCandidate(
                Container container,
                ZDO currentZdo,
                DurableOperationEndpointBinding legacyBinding,
                string endpointToken)
            {
                Container = container ?? throw new ArgumentNullException(nameof(container));
                CurrentZdo = currentZdo ?? throw new ArgumentNullException(nameof(currentZdo));
                LegacyBinding = legacyBinding ?? throw new ArgumentNullException(nameof(legacyBinding));
                EndpointToken = endpointToken ?? string.Empty;
            }

            internal Container Container { get; }
            internal ZDO CurrentZdo { get; }
            internal DurableOperationEndpointBinding LegacyBinding { get; }
            internal string EndpointToken { get; }
        }

        private sealed class EndpointComparer : IComparer<DurableOperationEndpoint>
        {
            internal static readonly EndpointComparer Instance = new EndpointComparer();

            public int Compare(DurableOperationEndpoint left, DurableOperationEndpoint right)
            {
                if (left == null || right == null)
                    return ReferenceEquals(left, right) ? 0 : left == null ? -1 : 1;
                if (!left.IsLegacyIdentity && !right.IsLegacyIdentity)
                    return string.Compare(
                        left.EndpointToken, right.EndpointToken, StringComparison.Ordinal);
                return DurableOperationValidation.Compare(left.EndpointId, right.EndpointId);
            }
        }
    }
}
