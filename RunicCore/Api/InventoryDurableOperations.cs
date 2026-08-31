using System;
using System.Security.Cryptography;
using System.Text;

namespace Runic.Foundation.Core
{
    /// <summary>
    /// Canonical provider-neutral metadata written to a native grave before a durable inventory
    /// transfer enters vanilla's irreversible move-to-grave seam. Presence is a deny-by-default
    /// interaction gate until the exact account/world-scoped server operation is reconciled.
    /// </summary>
    public static class InventoryDurableCustodyMetadata
    {
        public const string CustodyTokenKey = "runic.core.custody-token";
        public const string CustodySchemaKey = "runic.core.custody-schema";
        public const int CustodySchemaVersion = 1;
        public const int CanonicalOperationTokenCharacters = 147;
        public const int MaximumOpaqueTokenCharacters = 192;
    }

    public enum InventoryDurableMutationKind
    {
        ItemDebit = 1,
        ItemCredit = 2,
        DurabilityDebit = 3
    }

    public enum InventoryDurableOperationPhase
    {
        Journaled = 1,
        LocalPrepared = 2,
        RemoteCommittedObserved = 3,
        LocalMutationApplied = 4,
        CurrentPrimaryProved = 5,
        AcknowledgementSent = 6,
        RemoteAborted = 7,
        Indeterminate = 8,
        Terminal = 9,
        FreshSessionProved = 10
    }

    public enum InventoryProfileReadbackState
    {
        NotAttempted = 0,
        LocalCurrentPrimaryProved = 1,
        CurrentPrimaryUnavailable = 2,
        ReadbackMismatch = 3,
        ServerLedFreshSessionProved = 4
    }

    /// <summary>
    /// Character-profile storage selected by the installed game. LocalCurrentPrimary enables
    /// the provider's exact same-machine readback fast path. A durable account-bound server-led
    /// reconciliation protocol may separately recover Cloud or LegacyCloud profiles; callers
    /// must not infer that capability from this enum alone.
    /// </summary>
    public enum InventoryDurableProfileSource
    {
        Unresolved = 0,
        LocalCurrentPrimary = 1,
        Cloud = 2,
        LegacyCloud = 3
    }

    public enum InventoryDurableCustodyKind
    {
        None = 0,
        Tombstone = 1
    }

    public enum InventoryDurableCustodyEvidenceSource
    {
        None = 0,
        LocalVanillaCapture = 1,
        DurableServerRecovery = 2
    }

    public enum InventoryDurableOutstandingOutcome
    {
        RemoteCommitted = 1,
        RemoteAborted = 2
    }

    public enum InventoryDurableOutstandingObservation
    {
        HistoricalBefore = 1,
        Prepared = 2,
        ExpectedAfter = 3,
        ThirdStateQuarantined = 4
    }

    /// <summary>
    /// Exact server-side disposition of native items after vanilla death transferred them into
    /// world-owned custody. This is an outcome, not an authority claim: the calling gameplay
    /// module must obtain it from its authenticated durable server receipt/status protocol.
    /// </summary>
    public enum InventoryDurableCustodyResolutionOutcome
    {
        RemoteCommittedApplied = 1,
        RemoteAbortedPreserved = 2
    }

    /// <summary>
    /// Provider-neutral, bounded evidence that a durable server workflow reconciled the exact
    /// grave state captured by the local inventory provider. Hashes describe canonical opaque
    /// bytes owned by the caller; no Valheim object or mutable item instance crosses this API.
    /// </summary>
    public sealed class InventoryDurableCustodyResolution
    {
        public InventoryDurableCustodyResolution(
            InventoryDurableCustodyResolutionOutcome outcome,
            string nativeTagValue,
            string custodyBeforeInventorySha256,
            int custodyBeforeTaggedStackCount,
            int custodyBeforeTaggedQuantity,
            string custodyAfterInventorySha256,
            int custodyAfterTaggedStackCount,
            int custodyAfterTaggedQuantity,
            string durableServerReceiptSha256)
        {
            if (!Enum.IsDefined(typeof(InventoryDurableCustodyResolutionOutcome), outcome))
                throw new ArgumentOutOfRangeException(nameof(outcome));
            if (custodyBeforeTaggedStackCount < 0 || custodyBeforeTaggedStackCount > 4096)
                throw new ArgumentOutOfRangeException(nameof(custodyBeforeTaggedStackCount));
            if (custodyAfterTaggedStackCount < 0 || custodyAfterTaggedStackCount > 4096)
                throw new ArgumentOutOfRangeException(nameof(custodyAfterTaggedStackCount));
            if (custodyBeforeTaggedQuantity < 0 || custodyBeforeTaggedQuantity > 100000000)
                throw new ArgumentOutOfRangeException(nameof(custodyBeforeTaggedQuantity));
            if (custodyAfterTaggedQuantity < 0 || custodyAfterTaggedQuantity > 100000000)
                throw new ArgumentOutOfRangeException(nameof(custodyAfterTaggedQuantity));

            Outcome = outcome;
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue, nameof(nativeTagValue));
            DurableContractValidation.RequireSha256(
                custodyBeforeInventorySha256,
                nameof(custodyBeforeInventorySha256),
                false);
            DurableContractValidation.RequireSha256(
                custodyAfterInventorySha256,
                nameof(custodyAfterInventorySha256),
                false);
            DurableContractValidation.RequireSha256(
                durableServerReceiptSha256,
                nameof(durableServerReceiptSha256),
                false);
            CustodyBeforeInventorySha256 = custodyBeforeInventorySha256;
            CustodyBeforeTaggedStackCount = custodyBeforeTaggedStackCount;
            CustodyBeforeTaggedQuantity = custodyBeforeTaggedQuantity;
            CustodyAfterInventorySha256 = custodyAfterInventorySha256;
            CustodyAfterTaggedStackCount = custodyAfterTaggedStackCount;
            CustodyAfterTaggedQuantity = custodyAfterTaggedQuantity;
            DurableServerReceiptSha256 = durableServerReceiptSha256;
        }

        public InventoryDurableCustodyResolutionOutcome Outcome { get; }
        public string NativeTagValue { get; }
        public string CustodyBeforeInventorySha256 { get; }
        public int CustodyBeforeTaggedStackCount { get; }
        public int CustodyBeforeTaggedQuantity { get; }
        public string CustodyAfterInventorySha256 { get; }
        public int CustodyAfterTaggedStackCount { get; }
        public int CustodyAfterTaggedQuantity { get; }
        public string DurableServerReceiptSha256 { get; }
    }

    /// <summary>
    /// Opaque server proof binding used only when the local process crashed before it could flush
    /// the vanilla grave identity. The gameplay module owns the canonical proof bytes; Core
    /// enforces exact hashes, one unique tagged grave, operation/token binding, and a genuinely
    /// later observation session without depending on Persistence or Valheim types.
    /// </summary>
    public sealed class InventoryDurableServerCustodyBinding
    {
        public InventoryDurableServerCustodyBinding(
            Guid operationId,
            string nativeTagValue,
            string accountIdentitySha256,
            string worldIdentitySha256,
            int exactTaggedGraveCandidateCount,
            string taggedGraveSetSha256,
            string conservationProofSha256,
            string lastMutationSessionSha256,
            string observationSessionSha256)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("A non-empty operation UUID is required.", nameof(operationId));
            if (exactTaggedGraveCandidateCount != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(exactTaggedGraveCandidateCount),
                    "Server custody recovery requires exactly one tagged grave candidate.");
            OperationId = operationId;
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue, nameof(nativeTagValue));
            DurableContractValidation.RequireSha256(
                accountIdentitySha256, nameof(accountIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                worldIdentitySha256, nameof(worldIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                taggedGraveSetSha256, nameof(taggedGraveSetSha256), false);
            DurableContractValidation.RequireSha256(
                conservationProofSha256, nameof(conservationProofSha256), false);
            DurableContractValidation.RequireSha256(
                lastMutationSessionSha256, nameof(lastMutationSessionSha256), false);
            DurableContractValidation.RequireSha256(
                observationSessionSha256, nameof(observationSessionSha256), false);
            if (string.Equals(
                    lastMutationSessionSha256,
                    observationSessionSha256,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "Server custody recovery requires a later-session observation.",
                    nameof(observationSessionSha256));
            AccountIdentitySha256 = accountIdentitySha256;
            WorldIdentitySha256 = worldIdentitySha256;
            ExactTaggedGraveCandidateCount = exactTaggedGraveCandidateCount;
            TaggedGraveSetSha256 = taggedGraveSetSha256;
            ConservationProofSha256 = conservationProofSha256;
            LastMutationSessionSha256 = lastMutationSessionSha256;
            ObservationSessionSha256 = observationSessionSha256;
        }

        public Guid OperationId { get; }
        public string NativeTagValue { get; }
        public string AccountIdentitySha256 { get; }
        public string WorldIdentitySha256 { get; }
        public int ExactTaggedGraveCandidateCount { get; }
        public string TaggedGraveSetSha256 { get; }
        public string ConservationProofSha256 { get; }
        public string LastMutationSessionSha256 { get; }
        public string ObservationSessionSha256 { get; }
    }

    /// <summary>
    /// Manifest-free immutable view of the authenticated server record adopted by the local
    /// inventory provider. The exact manifest remains available only through
    /// TryReadExactManifest so routine status snapshots stay small.
    /// </summary>
    public sealed class InventoryDurableOutstandingSnapshot
    {
        public InventoryDurableOutstandingSnapshot(
            Guid operationId,
            string ownerModuleId,
            string purposeId,
            InventoryDurableMutationKind mutationKind,
            string nativeTagValue,
            string manifestSha256,
            InventoryDurableOutstandingOutcome outcome,
            InventoryDurableProfileSource profileSource,
            string historicalBeforeInventorySha256,
            string preparedInventorySha256,
            string expectedAfterInventorySha256,
            string accountIdentitySha256,
            string worldIdentitySha256,
            string lastMutationSessionSha256,
            string durableServerIntentSha256,
            string durableServerReceiptSha256,
            InventoryDurableOutstandingObservation observation,
            string freshSessionObservationSha256 = null)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("A non-empty operation UUID is required.", nameof(operationId));
            OperationId = operationId;
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            PurposeId = RunicIdentifier.Require(purposeId, nameof(purposeId));
            if (!Enum.IsDefined(typeof(InventoryDurableMutationKind), mutationKind))
                throw new ArgumentOutOfRangeException(nameof(mutationKind));
            if (!Enum.IsDefined(typeof(InventoryDurableOutstandingOutcome), outcome))
                throw new ArgumentOutOfRangeException(nameof(outcome));
            if (profileSource != InventoryDurableProfileSource.LocalCurrentPrimary &&
                profileSource != InventoryDurableProfileSource.Cloud &&
                profileSource != InventoryDurableProfileSource.LegacyCloud)
                throw new ArgumentOutOfRangeException(nameof(profileSource));
            if (!Enum.IsDefined(typeof(InventoryDurableOutstandingObservation), observation))
                throw new ArgumentOutOfRangeException(nameof(observation));
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue, nameof(nativeTagValue));
            DurableContractValidation.RequireSha256(manifestSha256, nameof(manifestSha256), false);
            DurableContractValidation.RequireSha256(
                historicalBeforeInventorySha256,
                nameof(historicalBeforeInventorySha256),
                false);
            DurableContractValidation.RequireSha256(
                preparedInventorySha256,
                nameof(preparedInventorySha256),
                true);
            DurableContractValidation.RequireSha256(
                expectedAfterInventorySha256,
                nameof(expectedAfterInventorySha256),
                false);
            DurableContractValidation.RequireSha256(
                accountIdentitySha256, nameof(accountIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                worldIdentitySha256, nameof(worldIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                lastMutationSessionSha256, nameof(lastMutationSessionSha256), false);
            DurableContractValidation.RequireSha256(
                durableServerIntentSha256, nameof(durableServerIntentSha256), false);
            DurableContractValidation.RequireSha256(
                durableServerReceiptSha256, nameof(durableServerReceiptSha256), false);
            DurableContractValidation.RequireSha256(
                freshSessionObservationSha256,
                nameof(freshSessionObservationSha256),
                true);
            if (string.Equals(
                    historicalBeforeInventorySha256,
                    expectedAfterInventorySha256,
                    StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(preparedInventorySha256) &&
                string.Equals(
                    preparedInventorySha256,
                    historicalBeforeInventorySha256,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "Outstanding inventory history and preparation must be exact and distinct.");
            if (observation == InventoryDurableOutstandingObservation.Prepared &&
                string.IsNullOrEmpty(preparedInventorySha256))
                throw new ArgumentException(
                    "A prepared observation requires a server-recorded prepared fingerprint.",
                    nameof(observation));

            MutationKind = mutationKind;
            ManifestSha256 = manifestSha256;
            Outcome = outcome;
            ProfileSource = profileSource;
            HistoricalBeforeInventorySha256 = historicalBeforeInventorySha256;
            PreparedInventorySha256 = preparedInventorySha256 ?? string.Empty;
            ExpectedAfterInventorySha256 = expectedAfterInventorySha256;
            AccountIdentitySha256 = accountIdentitySha256;
            WorldIdentitySha256 = worldIdentitySha256;
            LastMutationSessionSha256 = lastMutationSessionSha256;
            DurableServerIntentSha256 = durableServerIntentSha256;
            DurableServerReceiptSha256 = durableServerReceiptSha256;
            Observation = observation;
            FreshSessionObservationSha256 = freshSessionObservationSha256 ?? string.Empty;
        }

        public Guid OperationId { get; }
        public string OwnerModuleId { get; }
        public string PurposeId { get; }
        public InventoryDurableMutationKind MutationKind { get; }
        public string NativeTagValue { get; }
        public string ManifestSha256 { get; }
        public InventoryDurableOutstandingOutcome Outcome { get; }
        public InventoryDurableProfileSource ProfileSource { get; }
        public string HistoricalBeforeInventorySha256 { get; }
        /// <summary>
        /// Exact reversible local-preparation state. It may equal ExpectedAfterInventorySha256
        /// when preparation already applied the complete local debit; the authenticated remote
        /// outcome then determines whether recovery keeps that state or compensates to before.
        /// </summary>
        public string PreparedInventorySha256 { get; }
        public string ExpectedAfterInventorySha256 { get; }
        public string AccountIdentitySha256 { get; }
        public string WorldIdentitySha256 { get; }
        public string LastMutationSessionSha256 { get; }
        public string DurableServerIntentSha256 { get; }
        public string DurableServerReceiptSha256 { get; }
        public InventoryDurableOutstandingObservation Observation { get; }
        public string FreshSessionObservationSha256 { get; }

        public InventoryDurableOutstandingSnapshot WithFreshSessionObservation(
            string freshSessionObservationSha256) =>
            new InventoryDurableOutstandingSnapshot(
                OperationId,
                OwnerModuleId,
                PurposeId,
                MutationKind,
                NativeTagValue,
                ManifestSha256,
                Outcome,
                ProfileSource,
                HistoricalBeforeInventorySha256,
                PreparedInventorySha256,
                ExpectedAfterInventorySha256,
                AccountIdentitySha256,
                WorldIdentitySha256,
                LastMutationSessionSha256,
                DurableServerIntentSha256,
                DurableServerReceiptSha256,
                Observation,
                freshSessionObservationSha256);
    }

    /// <summary>
    /// Exact authenticated server operation used to restore a missing local journal. Its intent
    /// retains a cloned bounded manifest; historical fingerprints are never replaced by whatever
    /// state happens to be loaded on the fresh machine.
    /// </summary>
    public sealed class InventoryDurableOutstandingOperation
    {
        public InventoryDurableOutstandingOperation(
            InventoryDurableOperationIntent intent,
            InventoryDurableOutstandingOutcome outcome,
            InventoryDurableProfileSource profileSource,
            string historicalBeforeInventorySha256,
            string preparedInventorySha256,
            string expectedAfterInventorySha256,
            string accountIdentitySha256,
            string worldIdentitySha256,
            string lastMutationSessionSha256,
            string durableServerIntentSha256,
            string durableServerReceiptSha256)
        {
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            // Constructing the manifest-free view performs every shared bound/canonical check.
            _validated = new InventoryDurableOutstandingSnapshot(
                intent.OperationId,
                intent.OwnerModuleId,
                intent.PurposeId,
                intent.MutationKind,
                intent.NativeTagValue,
                intent.ManifestSha256,
                outcome,
                profileSource,
                historicalBeforeInventorySha256,
                preparedInventorySha256,
                expectedAfterInventorySha256,
                accountIdentitySha256,
                worldIdentitySha256,
                lastMutationSessionSha256,
                durableServerIntentSha256,
                durableServerReceiptSha256,
                InventoryDurableOutstandingObservation.HistoricalBefore);
        }

        private readonly InventoryDurableOutstandingSnapshot _validated;
        public InventoryDurableOperationIntent Intent { get; }
        public InventoryDurableOutstandingOutcome Outcome => _validated.Outcome;
        public InventoryDurableProfileSource ProfileSource => _validated.ProfileSource;
        public string HistoricalBeforeInventorySha256 =>
            _validated.HistoricalBeforeInventorySha256;
        public string PreparedInventorySha256 => _validated.PreparedInventorySha256;
        public string ExpectedAfterInventorySha256 => _validated.ExpectedAfterInventorySha256;
        public string AccountIdentitySha256 => _validated.AccountIdentitySha256;
        public string WorldIdentitySha256 => _validated.WorldIdentitySha256;
        public string LastMutationSessionSha256 => _validated.LastMutationSessionSha256;
        public string DurableServerIntentSha256 => _validated.DurableServerIntentSha256;
        public string DurableServerReceiptSha256 => _validated.DurableServerReceiptSha256;

        public InventoryDurableOutstandingSnapshot CreateSnapshot(
            InventoryDurableOutstandingObservation observation) =>
            new InventoryDurableOutstandingSnapshot(
                Intent.OperationId,
                Intent.OwnerModuleId,
                Intent.PurposeId,
                Intent.MutationKind,
                Intent.NativeTagValue,
                Intent.ManifestSha256,
                Outcome,
                ProfileSource,
                HistoricalBeforeInventorySha256,
                PreparedInventorySha256,
                ExpectedAfterInventorySha256,
                AccountIdentitySha256,
                WorldIdentitySha256,
                LastMutationSessionSha256,
                DurableServerIntentSha256,
                DurableServerReceiptSha256,
                observation,
                string.Empty);
    }

    /// <summary>
    /// Opaque direct-session evidence for the server-led Cloud/LegacyCloud acknowledgement path.
    /// The provider independently recomputes the loaded inventory fingerprint and requires every
    /// field to match the previously adopted outstanding record.
    /// </summary>
    public sealed class InventoryDurableFreshSessionProof
    {
        public InventoryDurableFreshSessionProof(
            Guid operationId,
            string nativeTagValue,
            string accountIdentitySha256,
            string worldIdentitySha256,
            string durableServerIntentSha256,
            string durableServerReceiptSha256,
            string lastMutationSessionSha256,
            string observationSessionSha256,
            string observedInventorySha256,
            bool observedBeforeAnyMutationThisSession,
            bool mutationIssuedThisSession)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("A non-empty operation UUID is required.", nameof(operationId));
            OperationId = operationId;
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue, nameof(nativeTagValue));
            DurableContractValidation.RequireSha256(
                accountIdentitySha256, nameof(accountIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                worldIdentitySha256, nameof(worldIdentitySha256), false);
            DurableContractValidation.RequireSha256(
                durableServerIntentSha256, nameof(durableServerIntentSha256), false);
            DurableContractValidation.RequireSha256(
                durableServerReceiptSha256, nameof(durableServerReceiptSha256), false);
            DurableContractValidation.RequireSha256(
                lastMutationSessionSha256, nameof(lastMutationSessionSha256), false);
            DurableContractValidation.RequireSha256(
                observationSessionSha256, nameof(observationSessionSha256), false);
            DurableContractValidation.RequireSha256(
                observedInventorySha256, nameof(observedInventorySha256), false);
            AccountIdentitySha256 = accountIdentitySha256;
            WorldIdentitySha256 = worldIdentitySha256;
            DurableServerIntentSha256 = durableServerIntentSha256;
            DurableServerReceiptSha256 = durableServerReceiptSha256;
            LastMutationSessionSha256 = lastMutationSessionSha256;
            ObservationSessionSha256 = observationSessionSha256;
            ObservedInventorySha256 = observedInventorySha256;
            ObservedBeforeAnyMutationThisSession = observedBeforeAnyMutationThisSession;
            MutationIssuedThisSession = mutationIssuedThisSession;
        }

        public Guid OperationId { get; }
        public string NativeTagValue { get; }
        public string AccountIdentitySha256 { get; }
        public string WorldIdentitySha256 { get; }
        public string DurableServerIntentSha256 { get; }
        public string DurableServerReceiptSha256 { get; }
        public string LastMutationSessionSha256 { get; }
        public string ObservationSessionSha256 { get; }
        public string ObservedInventorySha256 { get; }
        public bool ObservedBeforeAnyMutationThisSession { get; }
        public bool MutationIssuedThisSession { get; }
    }

    /// <summary>
    /// Provider-neutral evidence that vanilla death moved an operation's native items into one
    /// grave. Numeric world/player IDs are correlation hints, never authentication. No Valheim
    /// object or mutable item instance crosses this contract.
    /// </summary>
    public sealed class InventoryDurableCustodySnapshot
    {
        public InventoryDurableCustodySnapshot(
            InventoryDurableCustodyKind kind,
            long zdoUserId,
            uint zdoObjectId,
            long claimedPlayerId,
            string stableWorldObjectToken,
            string inventorySha256,
            int taggedStackCount,
            int taggedQuantity,
            InventoryDurableCustodyResolution resolution = null)
            : this(
                kind,
                zdoUserId,
                zdoObjectId,
                claimedPlayerId,
                stableWorldObjectToken,
                inventorySha256,
                taggedStackCount,
                taggedQuantity,
                kind == InventoryDurableCustodyKind.None
                    ? InventoryDurableCustodyEvidenceSource.None
                    : InventoryDurableCustodyEvidenceSource.LocalVanillaCapture,
                resolution,
                null)
        {
        }

        public InventoryDurableCustodySnapshot(
            InventoryDurableCustodyKind kind,
            long zdoUserId,
            uint zdoObjectId,
            long claimedPlayerId,
            string stableWorldObjectToken,
            string inventorySha256,
            int taggedStackCount,
            int taggedQuantity,
            InventoryDurableCustodyEvidenceSource evidenceSource,
            InventoryDurableCustodyResolution resolution,
            InventoryDurableServerCustodyBinding serverRecoveryBinding)
        {
            if (!Enum.IsDefined(typeof(InventoryDurableCustodyKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(InventoryDurableCustodyEvidenceSource), evidenceSource))
                throw new ArgumentOutOfRangeException(nameof(evidenceSource));
            string token = stableWorldObjectToken ?? string.Empty;
            string fingerprint = inventorySha256 ?? string.Empty;
            if (kind == InventoryDurableCustodyKind.None)
            {
                if (zdoUserId != 0L || zdoObjectId != 0U || claimedPlayerId != 0L ||
                    token.Length != 0 || fingerprint.Length != 0 ||
                    taggedStackCount != 0 || taggedQuantity != 0 || resolution != null ||
                    evidenceSource != InventoryDurableCustodyEvidenceSource.None ||
                    serverRecoveryBinding != null)
                    throw new ArgumentException("Empty custody cannot contain evidence.");
            }
            else
            {
                if (kind != InventoryDurableCustodyKind.Tombstone || zdoObjectId == 0U ||
                    claimedPlayerId == 0L || taggedStackCount < 0 || taggedStackCount > 4096 ||
                    taggedQuantity < 0 || taggedQuantity > 100000000)
                    throw new ArgumentException("Tombstone custody header is invalid.");
                DurableContractValidation.RequireSha256(fingerprint, nameof(inventorySha256), false);
                if (token.Length != 0 &&
                    (!Guid.TryParseExact(token, "N", out Guid parsed) || parsed == Guid.Empty ||
                     !string.Equals(parsed.ToString("N"), token, StringComparison.Ordinal)))
                    throw new ArgumentException(
                        "The stable world-object token is not a canonical UUID.",
                        nameof(stableWorldObjectToken));
                if (resolution != null &&
                    (!string.Equals(
                         resolution.CustodyBeforeInventorySha256,
                         fingerprint,
                         StringComparison.Ordinal) ||
                     resolution.CustodyBeforeTaggedStackCount != taggedStackCount ||
                     resolution.CustodyBeforeTaggedQuantity != taggedQuantity))
                    throw new ArgumentException(
                        "Custody resolution does not bind the captured grave evidence.",
                        nameof(resolution));
                if (evidenceSource == InventoryDurableCustodyEvidenceSource.None)
                    throw new ArgumentException(
                        "Tombstone custody requires an evidence provenance.",
                        nameof(evidenceSource));
                if (evidenceSource == InventoryDurableCustodyEvidenceSource.LocalVanillaCapture &&
                    serverRecoveryBinding != null)
                    throw new ArgumentException(
                        "Locally captured custody cannot contain a server-recovery binding.",
                        nameof(serverRecoveryBinding));
                if (evidenceSource == InventoryDurableCustodyEvidenceSource.DurableServerRecovery &&
                    (token.Length == 0 || resolution == null || serverRecoveryBinding == null ||
                     !string.Equals(
                         resolution.NativeTagValue,
                         serverRecoveryBinding.NativeTagValue,
                         StringComparison.Ordinal)))
                    throw new ArgumentException(
                        "Server-recovered custody requires one exact resolved server binding.",
                        nameof(serverRecoveryBinding));
            }
            Kind = kind;
            ZdoUserId = zdoUserId;
            ZdoObjectId = zdoObjectId;
            ClaimedPlayerId = claimedPlayerId;
            StableWorldObjectToken = token;
            InventorySha256 = fingerprint;
            TaggedStackCount = taggedStackCount;
            TaggedQuantity = taggedQuantity;
            EvidenceSource = evidenceSource;
            Resolution = resolution;
            ServerRecoveryBinding = serverRecoveryBinding;
        }

        public InventoryDurableCustodyKind Kind { get; }
        public bool IsPresent => Kind != InventoryDurableCustodyKind.None;
        public long ZdoUserId { get; }
        public uint ZdoObjectId { get; }
        public long ClaimedPlayerId { get; }
        public string StableWorldObjectToken { get; }
        public string InventorySha256 { get; }
        public int TaggedStackCount { get; }
        public int TaggedQuantity { get; }
        public InventoryDurableCustodyEvidenceSource EvidenceSource { get; }
        public bool IsResolved => Resolution != null;
        public InventoryDurableCustodyResolution Resolution { get; }
        public InventoryDurableServerCustodyBinding ServerRecoveryBinding { get; }

        public static InventoryDurableCustodySnapshot None { get; } =
            new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.None,
                0L,
                0U,
                0L,
                string.Empty,
                string.Empty,
                0,
                0,
                null);
    }

    /// <summary>
    /// Bounded, immutable intent persisted before a provider permits its owning local character
    /// inventory to mutate. ExactManifest is caller-canonical opaque data; it never contains a
    /// live ItemData reference.
    /// </summary>
    public sealed class InventoryDurableOperationIntent
    {
        public const int MaximumManifestBytes = 64 * 1024;
        private readonly byte[] _manifest;

        public InventoryDurableOperationIntent(
            Guid operationId,
            string ownerModuleId,
            string purposeId,
            InventoryDurableMutationKind mutationKind,
            string nativeTagKey,
            byte[] exactManifest)
            : this(
                operationId,
                ownerModuleId,
                purposeId,
                mutationKind,
                nativeTagKey,
                operationId.ToString("N"),
                exactManifest)
        {
        }

        public InventoryDurableOperationIntent(
            Guid operationId,
            string ownerModuleId,
            string purposeId,
            InventoryDurableMutationKind mutationKind,
            string nativeTagKey,
            string nativeTagValue,
            byte[] exactManifest)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("A non-empty operation UUID is required.", nameof(operationId));
            OperationId = operationId;
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            PurposeId = RunicIdentifier.Require(purposeId, nameof(purposeId));
            if (!Enum.IsDefined(typeof(InventoryDurableMutationKind), mutationKind))
                throw new ArgumentOutOfRangeException(nameof(mutationKind));
            MutationKind = mutationKind;
            NativeTagKey = RunicIdentifier.Require(nativeTagKey, nameof(nativeTagKey));
            if (!NativeTagKey.StartsWith(OwnerModuleId + ".", StringComparison.Ordinal))
                throw new ArgumentException(
                    "The operation tag key must be owned by the caller module.",
                    nameof(nativeTagKey));
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue, nameof(nativeTagValue));
            if (exactManifest == null || exactManifest.Length == 0 ||
                exactManifest.Length > MaximumManifestBytes)
                throw new ArgumentOutOfRangeException(nameof(exactManifest));
            _manifest = (byte[])exactManifest.Clone();
            ManifestSha256 = ComputeSha256(_manifest);
        }

        public Guid OperationId { get; }
        public string OwnerModuleId { get; }
        public string PurposeId { get; }
        public InventoryDurableMutationKind MutationKind { get; }
        public string NativeTagKey { get; }
        /// <summary>
        /// Exact opaque operation token persisted in native item custom data. Remote workflows
        /// must use the server-issued world/admission token; the legacy constructor retains the
        /// operation UUID value only for existing local-only callers.
        /// </summary>
        public string NativeTagValue { get; }
        public byte[] ExactManifest => (byte[])_manifest.Clone();
        public string ManifestSha256 { get; }

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var text = new StringBuilder(64);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }
    }

    public sealed class InventoryDurableOperationSnapshot
    {
        public InventoryDurableOperationSnapshot(
            Guid operationId,
            string ownerModuleId,
            string purposeId,
            InventoryDurableMutationKind mutationKind,
            InventoryDurableOperationPhase phase,
            string nativeTagKey,
            string manifestSha256,
            string beforeInventorySha256,
            string afterInventorySha256,
            InventoryProfileReadbackState readbackState,
            string playerDataSha256,
            bool reconciliationLockHeld,
            InventoryDurableCustodySnapshot custody = null,
            string nativeTagValue = null,
            InventoryDurableOutstandingSnapshot outstanding = null,
            string preparedInventorySha256 = null)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("A non-empty operation UUID is required.", nameof(operationId));
            OperationId = operationId;
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            PurposeId = RunicIdentifier.Require(purposeId, nameof(purposeId));
            if (!Enum.IsDefined(typeof(InventoryDurableMutationKind), mutationKind))
                throw new ArgumentOutOfRangeException(nameof(mutationKind));
            if (!Enum.IsDefined(typeof(InventoryDurableOperationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (!Enum.IsDefined(typeof(InventoryProfileReadbackState), readbackState))
                throw new ArgumentOutOfRangeException(nameof(readbackState));
            NativeTagKey = RunicIdentifier.Require(nativeTagKey, nameof(nativeTagKey));
            if (!NativeTagKey.StartsWith(OwnerModuleId + ".", StringComparison.Ordinal))
                throw new ArgumentException(
                    "The operation tag key must be owned by the provider module.",
                    nameof(nativeTagKey));
            NativeTagValue = DurableContractValidation.RequireOpaqueToken(
                nativeTagValue ?? operationId.ToString("N"),
                nameof(nativeTagValue));
            DurableContractValidation.RequireSha256(manifestSha256, nameof(manifestSha256), false);
            DurableContractValidation.RequireSha256(
                beforeInventorySha256, nameof(beforeInventorySha256), false);
            DurableContractValidation.RequireSha256(
                afterInventorySha256, nameof(afterInventorySha256), true);
            DurableContractValidation.RequireSha256(
                preparedInventorySha256, nameof(preparedInventorySha256), true);
            DurableContractValidation.RequireSha256(
                playerDataSha256, nameof(playerDataSha256), true);
            if ((phase == InventoryDurableOperationPhase.LocalMutationApplied ||
                 phase == InventoryDurableOperationPhase.CurrentPrimaryProved ||
                 phase == InventoryDurableOperationPhase.FreshSessionProved ||
                 phase == InventoryDurableOperationPhase.AcknowledgementSent) &&
                string.IsNullOrEmpty(afterInventorySha256))
                throw new ArgumentException(
                    "An applied operation requires an after-inventory fingerprint.",
                    nameof(afterInventorySha256));
            if (readbackState == InventoryProfileReadbackState.LocalCurrentPrimaryProved &&
                string.IsNullOrEmpty(playerDataSha256))
                throw new ArgumentException(
                    "A proved readback requires exact player-data evidence.",
                    nameof(playerDataSha256));

            MutationKind = mutationKind;
            Phase = phase;
            ManifestSha256 = manifestSha256;
            BeforeInventorySha256 = beforeInventorySha256;
            AfterInventorySha256 = afterInventorySha256 ?? string.Empty;
            PreparedInventorySha256 = preparedInventorySha256 ?? string.Empty;
            ReadbackState = readbackState;
            PlayerDataSha256 = playerDataSha256 ?? string.Empty;
            ReconciliationLockHeld = reconciliationLockHeld;
            Custody = custody ?? InventoryDurableCustodySnapshot.None;
            Outstanding = outstanding;
            if (Custody.IsResolved)
            {
                bool committedPhase =
                    Custody.Resolution.Outcome ==
                        InventoryDurableCustodyResolutionOutcome.RemoteCommittedApplied &&
                    (phase == InventoryDurableOperationPhase.LocalMutationApplied ||
                     phase == InventoryDurableOperationPhase.CurrentPrimaryProved ||
                     phase == InventoryDurableOperationPhase.FreshSessionProved ||
                     phase == InventoryDurableOperationPhase.AcknowledgementSent ||
                     phase == InventoryDurableOperationPhase.Indeterminate ||
                     phase == InventoryDurableOperationPhase.Terminal);
                bool abortedPhase =
                    Custody.Resolution.Outcome ==
                        InventoryDurableCustodyResolutionOutcome.RemoteAbortedPreserved &&
                    (phase == InventoryDurableOperationPhase.RemoteAborted ||
                     phase == InventoryDurableOperationPhase.CurrentPrimaryProved ||
                     phase == InventoryDurableOperationPhase.FreshSessionProved ||
                     phase == InventoryDurableOperationPhase.AcknowledgementSent ||
                     phase == InventoryDurableOperationPhase.Indeterminate ||
                     phase == InventoryDurableOperationPhase.Terminal);
                if (!committedPhase && !abortedPhase ||
                    string.IsNullOrEmpty(AfterInventorySha256))
                    throw new ArgumentException(
                        "Resolved custody is inconsistent with the operation phase.",
                        nameof(custody));
            }
            if (Custody.EvidenceSource ==
                    InventoryDurableCustodyEvidenceSource.DurableServerRecovery &&
                (Custody.ServerRecoveryBinding == null ||
                 Custody.ServerRecoveryBinding.OperationId != operationId ||
                 !string.Equals(
                     Custody.ServerRecoveryBinding.NativeTagValue,
                     NativeTagValue,
                     StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Recovered custody does not bind this operation snapshot.",
                    nameof(custody));
            if (Outstanding != null &&
                (Outstanding.OperationId != operationId ||
                 !string.Equals(Outstanding.OwnerModuleId, OwnerModuleId, StringComparison.Ordinal) ||
                 !string.Equals(Outstanding.PurposeId, PurposeId, StringComparison.Ordinal) ||
                 Outstanding.MutationKind != MutationKind ||
                 !string.Equals(Outstanding.NativeTagValue, NativeTagValue, StringComparison.Ordinal) ||
                 !string.Equals(Outstanding.ManifestSha256, ManifestSha256, StringComparison.Ordinal) ||
                 !string.Equals(
                     Outstanding.HistoricalBeforeInventorySha256,
                     BeforeInventorySha256,
                     StringComparison.Ordinal)))
                throw new ArgumentException(
                    "The outstanding server record does not bind this operation snapshot.",
                    nameof(outstanding));
            if (readbackState == InventoryProfileReadbackState.ServerLedFreshSessionProved &&
                (Outstanding == null ||
                 string.IsNullOrEmpty(Outstanding.FreshSessionObservationSha256)))
                throw new ArgumentException(
                    "Server-led proof requires one exact fresh-session observation.",
                    nameof(outstanding));
            if (phase == InventoryDurableOperationPhase.FreshSessionProved &&
                (readbackState != InventoryProfileReadbackState.ServerLedFreshSessionProved ||
                 Outstanding == null || string.IsNullOrEmpty(AfterInventorySha256) ||
                 string.IsNullOrEmpty(Outstanding.FreshSessionObservationSha256)))
                throw new ArgumentException(
                    "Fresh-session proof requires one exact outstanding server binding.",
                    nameof(outstanding));
        }

        public Guid OperationId { get; }
        public string OwnerModuleId { get; }
        public string PurposeId { get; }
        public InventoryDurableMutationKind MutationKind { get; }
        public InventoryDurableOperationPhase Phase { get; }
        public string NativeTagKey { get; }
        public string NativeTagValue { get; }
        public string ManifestSha256 { get; }
        public string BeforeInventorySha256 { get; }
        /// <summary>
        /// Provider-computed exact fingerprint after the caller's scoped reversible local
        /// preparation. It is persisted before a remote commit and may differ from the terminal
        /// after fingerprint. Empty for predecessor journals that predate prepared-state proof.
        /// </summary>
        public string PreparedInventorySha256 { get; }
        public string AfterInventorySha256 { get; }
        public InventoryProfileReadbackState ReadbackState { get; }
        public string PlayerDataSha256 { get; }
        public bool ReconciliationLockHeld { get; }
        public InventoryDurableCustodySnapshot Custody { get; }
        public InventoryDurableOutstandingSnapshot Outstanding { get; }
    }

    /// <summary>
    /// Provider-neutral local-owner crash journal and reconciliation lock. Calls are synchronous;
    /// a successful TryBegin proves only that the provider's independent journal was durably
    /// admitted, never that Valheim acknowledged a profile save.
    /// </summary>
    public interface IInventoryDurableOperationService
    {
        string ProviderId { get; }

        /// <summary>
        /// Classifies the current owning character's installed profile source. Classification is
        /// not a save acknowledgement or durability proof; Cloud and LegacyCloud require an
        /// authenticated server-led fresh-session reconciliation protocol.
        /// </summary>
        bool TryGetProfileSource(
            out InventoryDurableProfileSource source,
            out string failureCode);

        bool TryCheckCurrentPrimarySupport(
            out InventoryDurableProfileSource source,
            out string failureCode);

        bool TryBegin(
            InventoryDurableOperationIntent intent,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        /// <summary>
        /// Forecasts the provider's canonical fingerprint for opaque native-state bytes produced
        /// by the caller's pure transformation. The operation must be exactly Journaled with its
        /// reconciliation lock held. The callback receives and returns bounded defensive byte
        /// arrays; no Valheim type crosses this contract. A successful forecast does not validate
        /// gameplay semantics, authorize mutation, persist evidence, or advance the operation.
        /// The later owned mutation and TryCaptureLocalPrepared proof must still match this value.
        /// </summary>
        bool TryForecastOwnedMutation(
            string ownerModuleId,
            Guid operationId,
            Func<byte[], byte[]> transformExactNativeState,
            out string expectedInventorySha256,
            out string failureCode);

        /// <summary>
        /// Adopts an exact authenticated server record when the independent local journal is
        /// absent or enriches the matching unresolved journal after reconnect. Historical before,
        /// prepared, and expected-after fingerprints come from the durable server evidence and
        /// are never replaced by the freshly loaded current state. A third state is persisted as
        /// Indeterminate so native inventory use remains quarantined.
        /// </summary>
        bool TryAdoptOutstanding(
            InventoryDurableOutstandingOperation outstanding,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryGetActive(
            string ownerModuleId,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryResume(
            string ownerModuleId,
            Guid operationId,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        /// <summary>
        /// Returns a bounded defensive copy of the exact canonical manifest belonging to the
        /// caller's current unresolved operation. The provider denies foreign owners, mismatched
        /// operation IDs, terminal evidence, and any journal/mirror conflict; consumers never
        /// receive a reference to provider-owned journal memory.
        /// </summary>
        bool TryReadExactManifest(
            string ownerModuleId,
            Guid operationId,
            out byte[] exactManifest,
            out string failureCode);

        bool TryEnterOwnedMutation(
            string ownerModuleId,
            Guid operationId,
            out IDisposable mutationScope,
            out string failureCode);

        /// <summary>
        /// Compare-and-sets Journaled to LocalPrepared after the owning caller has completed its
        /// exact reversible tag/debit preparation inside TryEnterOwnedMutation. The provider
        /// recomputes, durably persists, and returns the current inventory fingerprint; callers
        /// must bind this value into the authenticated server intent before remote commit.
        /// </summary>
        bool TryCaptureLocalPrepared(
            string ownerModuleId,
            Guid operationId,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        /// <summary>
        /// Atomically binds authenticated durable server evidence to an already-captured vanilla
        /// death custody record. The provider computes and persists the current local-player
        /// inventory fingerprint itself; callers cannot supply or overwrite that local fact.
        /// Exact replays are accepted and conflicting resolutions fail closed.
        /// </summary>
        bool TryResolveCustody(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableCustodyResolution resolution,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        /// <summary>
        /// Recovers exact grave custody when the client crashed after vanilla moved the items but
        /// before local grave identity was flushed. Only a fully resolved DurableServerRecovery
        /// snapshot with unique-grave, account/world/operation, conservation, receipt, and
        /// later-session evidence is accepted.
        /// </summary>
        bool TryRecoverCustody(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableCustodySnapshot recoveredCustody,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryAdvance(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableOperationPhase expectedPhase,
            InventoryDurableOperationPhase nextPhase,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryProveCurrentPrimary(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableOperationPhase expectedPhase,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryProveFreshSession(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableFreshSessionProof proof,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryMarkIndeterminate(
            string ownerModuleId,
            Guid operationId,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        bool TryComplete(
            string ownerModuleId,
            Guid operationId,
            InventoryDurableOperationPhase expectedPhase,
            out string failureCode);
    }

    /// <summary>
    /// Optional backward-compatible admission seam implemented by Inventory providers that can
    /// support a remote durable root before activating the local crash journal. Preview is
    /// read-only and journal-free. TryBeginPrepared then compares the immutable root's historical
    /// fingerprint inside the same local mutation gate that persists the journal. Consumers must
    /// still call IInventoryDurableOperationService.TryForecastOwnedMutation after admission and
    /// require the exact same prepared digest before entering an owned mutation.
    /// </summary>
    public interface IInventoryDurablePreparedAdmissionService
    {
        bool TryPreviewOwnedMutation(
            string ownerModuleId,
            Func<byte[], byte[]> transformExactNativeState,
            out string currentInventorySha256,
            out string expectedInventorySha256,
            out string failureCode);

        bool TryBeginPrepared(
            InventoryDurableOperationIntent intent,
            string expectedBeforeInventorySha256,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);

        /// <summary>
        /// Recreates a missing local journal from an authenticated server root that is already
        /// durably Prepared. The provider admits LocalPrepared only when its current canonical
        /// inventory fingerprint is exactly expectedPreparedInventorySha256, the historical and
        /// prepared fingerprints are distinct, the current profile source is exact, and no local
        /// journal, custody, or conflicting evidence exists. The complete root binding remains an
        /// opaque, bounded part of intent.ExactManifest and exact retries must be idempotent.
        /// This method never infers a server outcome or authorizes an Abort.
        /// </summary>
        bool TryAdoptPreparedRoot(
            InventoryDurableOperationIntent intent,
            InventoryDurableProfileSource expectedProfileSource,
            string historicalBeforeInventorySha256,
            string expectedPreparedInventorySha256,
            out InventoryDurableOperationSnapshot snapshot,
            out string failureCode);
    }

    internal static class DurableContractValidation
    {
        internal const int MaximumOpaqueTokenCharacters =
            InventoryDurableCustodyMetadata.MaximumOpaqueTokenCharacters;

        internal static string RequireOpaqueToken(string value, string parameterName)
        {
            string exact = value ?? string.Empty;
            if (exact.Length < 1 || exact.Length > MaximumOpaqueTokenCharacters)
                throw new ArgumentOutOfRangeException(parameterName);
            for (int index = 0; index < exact.Length; index++)
            {
                char character = exact[index];
                bool allowed = character >= 'a' && character <= 'z' ||
                               character >= 'A' && character <= 'Z' ||
                               character >= '0' && character <= '9' ||
                               character == '.' || character == '_' || character == '-' ||
                               character == ':';
                if (!allowed)
                    throw new ArgumentException(
                        "The native operation tag value is not canonical opaque text.",
                        parameterName);
            }
            return exact;
        }

        internal static void RequireSha256(
            string value,
            string parameterName,
            bool allowEmpty)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (allowEmpty) return;
                throw new ArgumentException("A SHA-256 digest is required.", parameterName);
            }
            if (value.Length != 64) throw new ArgumentException("SHA-256 length is invalid.", parameterName);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                    throw new ArgumentException(
                        "SHA-256 must use canonical lowercase hexadecimal.",
                        parameterName);
            }
        }
    }
}
