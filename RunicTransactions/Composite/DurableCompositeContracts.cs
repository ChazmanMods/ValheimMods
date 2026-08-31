using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    public enum DurableCompositeOperationPhase
    {
        Claiming = 1,
        Prepared = 2,
        Committing = 3,
        Committed = 4,
        Aborting = 5,
        Aborted = 6
    }

    public enum DurableCompositeResultCode
    {
        Prepared = 0,
        Committed = 1,
        Aborted = 2,
        Replay = 3,
        NotFound = 4,
        ReplayConflict = 5,
        CapacityReached = 6,
        ActorBusy = 7,
        MutationBusy = 8,
        NotReady = 9,
        AuthorityChanged = 10,
        EvidenceConflict = 11,
        FailedClosed = 12,
        Acknowledged = 13
    }

    public enum DurableCompositeJournalReadState
    {
        Absent = 0,
        Present = 1,
        Corrupt = 2,
        Unavailable = 3
    }

    public enum DurableCompositeJournalCreateState
    {
        Created = 0,
        Existing = 1,
        CapacityReached = 2,
        ActorBusy = 3,
        Failed = 4
    }

    public enum DurableCompositeEndpointReadState
    {
        Ready = 0,
        Missing = 1,
        Corrupt = 2,
        Unavailable = 3
    }

    public enum DurableCompositeHistoryReadState
    {
        Ready = 0,
        Corrupt = 1,
        Unavailable = 2
    }

    public enum DurableCompositeCheckpointState
    {
        Advanced = 0,
        Unchanged = 1,
        Rejected = 2,
        Failed = 3
    }

    internal sealed class DurableCompositeWorldCheckpointStage
    {
        internal DurableCompositeWorldCheckpointStage(
            string worldEpoch,
            long throughCommitSequence,
            string databasePath,
            string saveCycleId,
            string originProcessId,
            long markerUserId,
            uint markerObjectId)
            : this(
                worldEpoch,
                throughCommitSequence,
                databasePath,
                saveCycleId,
                originProcessId,
                markerUserId,
                markerObjectId,
                0,
                string.Empty)
        {
        }

        internal DurableCompositeWorldCheckpointStage(
            string worldEpoch,
            long throughCommitSequence,
            string databasePath,
            string saveCycleId,
            string originProcessId,
            long markerUserId,
            uint markerObjectId,
            long firstGenerationLength,
            string firstGenerationSha256)
        {
            if (!Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal))
                throw new ArgumentException("A canonical world epoch is required.", nameof(worldEpoch));
            if (throughCommitSequence < 1)
                throw new ArgumentOutOfRangeException(nameof(throughCommitSequence));
            if (string.IsNullOrEmpty(databasePath) ||
                !System.IO.Path.IsPathRooted(databasePath))
                throw new ArgumentException("An absolute database path is required.", nameof(databasePath));
            if (!Guid.TryParseExact(saveCycleId, "N", out Guid cycle) || cycle == Guid.Empty ||
                !string.Equals(cycle.ToString("N"), saveCycleId, StringComparison.Ordinal))
                throw new ArgumentException("A canonical save cycle is required.", nameof(saveCycleId));
            if (!Guid.TryParseExact(originProcessId, "N", out Guid process) || process == Guid.Empty ||
                !string.Equals(process.ToString("N"), originProcessId, StringComparison.Ordinal))
                throw new ArgumentException("A canonical process identity is required.", nameof(originProcessId));
            if (markerUserId == 0 || markerObjectId == 0)
                throw new ArgumentException("A nonzero persistent marker ZDO identity is required.");
            WorldEpoch = worldEpoch;
            ThroughCommitSequence = throughCommitSequence;
            DatabasePath = System.IO.Path.GetFullPath(databasePath);
            SaveCycleId = saveCycleId;
            OriginProcessId = originProcessId;
            MarkerUserId = markerUserId;
            MarkerObjectId = markerObjectId;
            if (firstGenerationLength < 0)
                throw new ArgumentOutOfRangeException(nameof(firstGenerationLength));
            if (firstGenerationLength == 0)
            {
                if (!string.IsNullOrEmpty(firstGenerationSha256))
                    throw new ArgumentException(
                        "A missing first generation cannot carry a digest.",
                        nameof(firstGenerationSha256));
                FirstGenerationSha256 = string.Empty;
            }
            else FirstGenerationSha256 = CompositeValidation.RequireSha256(
                firstGenerationSha256, nameof(firstGenerationSha256));
            FirstGenerationLength = firstGenerationLength;
        }

        internal string WorldEpoch { get; }
        internal long ThroughCommitSequence { get; }
        internal string DatabasePath { get; }
        internal string SaveCycleId { get; }
        internal string OriginProcessId { get; }
        internal long MarkerUserId { get; }
        internal uint MarkerObjectId { get; }
        internal long FirstGenerationLength { get; }
        internal string FirstGenerationSha256 { get; }
        internal bool HasFirstGenerationProof => FirstGenerationLength > 0;
        internal string CanonicalMarkerValue => "v1:" + WorldEpoch + ":" +
            ThroughCommitSequence.ToString(
                "x16", System.Globalization.CultureInfo.InvariantCulture) + ":" + SaveCycleId;

        internal DurableCompositeWorldCheckpointStage WithFirstGeneration(
            long length,
            string sha256) =>
            new DurableCompositeWorldCheckpointStage(
                WorldEpoch,
                ThroughCommitSequence,
                DatabasePath,
                SaveCycleId,
                OriginProcessId,
                MarkerUserId,
                MarkerObjectId,
                length,
                sha256);

        internal DurableCompositeWorldCheckpointStage WithMarkerIdentity(
            long markerUserId,
            uint markerObjectId) =>
            new DurableCompositeWorldCheckpointStage(
                WorldEpoch,
                ThroughCommitSequence,
                DatabasePath,
                SaveCycleId,
                OriginProcessId,
                markerUserId,
                markerObjectId,
                FirstGenerationLength,
                FirstGenerationSha256);
    }

    public enum DurableCompositeTokenIssueCode
    {
        Issued = 0,
        Replay = 1,
        CapacityReached = 2,
        ActorBusy = 3,
        FailedClosed = 4,
        ReplayConflict = 5
    }

    public enum DurableCompositeTokenCancellationCode
    {
        Cancelled = 0,
        Replay = 1,
        NotFound = 2,
        Conflict = 3,
        FailedClosed = 4
    }

    public sealed class DurableCompositeTokenCancellationResult
    {
        internal DurableCompositeTokenCancellationResult(
            DurableCompositeTokenCancellationCode code,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeTokenCancellationCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
        }

        public DurableCompositeTokenCancellationCode Code { get; }
        public string ReasonCode { get; }
        public bool Success => Code == DurableCompositeTokenCancellationCode.Cancelled ||
                               Code == DurableCompositeTokenCancellationCode.Replay;
    }

    /// <summary>
    /// Opaque server-issued admission identity. The world epoch, monotonic admission sequence,
    /// random operation UUID, and authenticator are persisted before this token is returned. A
    /// caller can parse/transport it but cannot mint an accepted token.
    /// </summary>
    public sealed class DurableCompositeOperationToken
    {
        internal DurableCompositeOperationToken(
            string worldEpoch,
            long admissionSequence,
            Guid operationId,
            string authenticator)
        {
            if (!Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal))
                throw new ArgumentException("A canonical world epoch is required.", nameof(worldEpoch));
            if (admissionSequence < 1) throw new ArgumentOutOfRangeException(nameof(admissionSequence));
            if (operationId == Guid.Empty) throw new ArgumentException(
                "A nonempty operation UUID is required.", nameof(operationId));
            WorldEpoch = worldEpoch;
            AdmissionSequence = admissionSequence;
            OperationId = operationId;
            Authenticator = CompositeValidation.RequireSha256(
                authenticator, nameof(authenticator));
        }

        public string WorldEpoch { get; }
        public long AdmissionSequence { get; }
        public Guid OperationId { get; }
        public string OperationIdText => OperationId.ToString("N");
        public string Authenticator { get; }
        public string CanonicalValue => WorldEpoch + ":" +
            AdmissionSequence.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) +
            ":" + OperationIdText + ":" + Authenticator;

        public static DurableCompositeOperationToken Parse(string value)
        {
            string[] parts = (value ?? string.Empty).Split(':');
            if (parts.Length != 4 || parts[1].Length != 16 ||
                !long.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long sequence) ||
                sequence < 1 ||
                !Guid.TryParseExact(parts[2], "N", out Guid operation) ||
                operation == Guid.Empty)
                throw new FormatException("Composite operation token is not canonical.");
            var result = new DurableCompositeOperationToken(
                parts[0], sequence, operation, parts[3]);
            if (!string.Equals(result.CanonicalValue, value, StringComparison.Ordinal))
                throw new FormatException("Composite operation token is not canonical.");
            return result;
        }
    }

    public sealed class DurableCompositeTokenIssueResult
    {
        internal DurableCompositeTokenIssueResult(
            DurableCompositeTokenIssueCode code,
            string reasonCode,
            DurableCompositeOperationToken token)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeTokenIssueCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            if ((code == DurableCompositeTokenIssueCode.Issued ||
                 code == DurableCompositeTokenIssueCode.Replay) != (token != null))
                throw new ArgumentException("Only a successful issue result carries a token.");
            Code = code;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            Token = token;
        }

        public DurableCompositeTokenIssueCode Code { get; }
        public string ReasonCode { get; }
        public DurableCompositeOperationToken Token { get; }
        public bool Success => Token != null;
    }

    public enum DurableCompositeOutstandingQueryState
    {
        Ready = 0,
        Corrupt = 1,
        Unavailable = 2
    }

    public sealed class DurableCompositeReconciliationRequirement
    {
        public DurableCompositeReconciliationRequirement(
            string consumerModuleId,
            int consumerProtocolMajor,
            string requiredProviderCapability)
        {
            ConsumerModuleId = RunicIdentifier.Require(
                consumerModuleId, nameof(consumerModuleId));
            if (consumerProtocolMajor < 1 || consumerProtocolMajor > 65535)
                throw new ArgumentOutOfRangeException(nameof(consumerProtocolMajor));
            ConsumerProtocolMajor = consumerProtocolMajor;
            RequiredProviderCapability = RunicIdentifier.Require(
                requiredProviderCapability, nameof(requiredProviderCapability));
        }

        public string ConsumerModuleId { get; }
        public int ConsumerProtocolMajor { get; }
        public string RequiredProviderCapability { get; }
    }

    public sealed class DurableCompositeOutstandingOperation
    {
        internal DurableCompositeOutstandingOperation(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            string endpointDomainId,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string intentHash,
            long commitSequence,
            string receiptHash,
            DurableCompositeReconciliationRequirement requirement,
            DurableCompositeOperationPhase terminalPhase)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            OwnerModuleVersion = CompositeValidation.RequireText(
                ownerModuleVersion, nameof(ownerModuleVersion), 64);
            if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
            OwnerProtocolMajor = ownerProtocolMajor;
            EndpointDomainId = RunicIdentifier.Require(endpointDomainId, nameof(endpointDomainId));
            OperationToken = operationToken ??
                throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
            if (terminalPhase != DurableCompositeOperationPhase.Committed &&
                terminalPhase != DurableCompositeOperationPhase.Aborted)
                throw new ArgumentOutOfRangeException(nameof(terminalPhase));
            if ((terminalPhase == DurableCompositeOperationPhase.Committed && commitSequence < 1) ||
                (terminalPhase == DurableCompositeOperationPhase.Aborted && commitSequence != 0))
                throw new ArgumentOutOfRangeException(nameof(commitSequence));
            CommitSequence = commitSequence;
            ReceiptHash = CompositeValidation.RequireSha256(receiptHash, nameof(receiptHash));
            Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            TerminalPhase = terminalPhase;
        }

        public string OwnerModuleId { get; }
        public string OwnerModuleVersion { get; }
        public int OwnerProtocolMajor { get; }
        public string EndpointDomainId { get; }
        public DurableCompositeOperationToken OperationToken { get; }
        public Guid OperationId => OperationToken.OperationId;
        public string OperationIdText => OperationId.ToString("N");
        public RpcPeerIdentity ActorIdentity { get; }
        public string IntentHash { get; }
        public long CommitSequence { get; }
        public string ReceiptHash { get; }
        public DurableCompositeReconciliationRequirement Requirement { get; }
        public DurableCompositeOperationPhase TerminalPhase { get; }
    }

    public sealed class DurableCompositeIssuedOperation
    {
        internal DurableCompositeIssuedOperation(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string durableRequestKeyHash,
            string requestHash)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            OwnerModuleVersion = CompositeValidation.RequireText(
                ownerModuleVersion, nameof(ownerModuleVersion), 64);
            if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
            OwnerProtocolMajor = ownerProtocolMajor;
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            DurableRequestKeyHash = CompositeValidation.RequireSha256(
                durableRequestKeyHash, nameof(durableRequestKeyHash));
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
        }

        public string OwnerModuleId { get; }
        public string OwnerModuleVersion { get; }
        public int OwnerProtocolMajor { get; }
        public DurableCompositeOperationToken OperationToken { get; }
        public RpcPeerIdentity ActorIdentity { get; }
        public string DurableRequestKeyHash { get; }
        public string RequestHash { get; }
    }

    public sealed class DurableCompositeJournaledOutstandingOperation
    {
        internal DurableCompositeJournaledOutstandingOperation(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            string endpointDomainId,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string requestHash,
            DurableCompositeOperationPhase phase)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            OwnerModuleVersion = CompositeValidation.RequireText(
                ownerModuleVersion, nameof(ownerModuleVersion), 64);
            if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
            OwnerProtocolMajor = ownerProtocolMajor;
            EndpointDomainId = RunicIdentifier.Require(
                endpointDomainId, nameof(endpointDomainId));
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            if (!Enum.IsDefined(typeof(DurableCompositeOperationPhase), phase) ||
                phase == DurableCompositeOperationPhase.Committed ||
                phase == DurableCompositeOperationPhase.Aborted)
                throw new ArgumentOutOfRangeException(nameof(phase));
            Phase = phase;
        }

        public string OwnerModuleId { get; }
        public string OwnerModuleVersion { get; }
        public int OwnerProtocolMajor { get; }
        public string EndpointDomainId { get; }
        public DurableCompositeOperationToken OperationToken { get; }
        public RpcPeerIdentity ActorIdentity { get; }
        public string RequestHash { get; }
        public DurableCompositeOperationPhase Phase { get; }
    }

    public sealed class DurableCompositeOutstandingQueryResult
    {
        private readonly IReadOnlyList<DurableCompositeOutstandingOperation> _operations;

        internal DurableCompositeOutstandingQueryResult(
            DurableCompositeOutstandingQueryState state,
            IEnumerable<DurableCompositeOutstandingOperation> operations,
            string reasonCode)
            : this(
                state,
                operations,
                Array.Empty<DurableCompositeIssuedOperation>(),
                Array.Empty<DurableCompositeJournaledOutstandingOperation>(),
                reasonCode)
        {
        }

        internal DurableCompositeOutstandingQueryResult(
            DurableCompositeOutstandingQueryState state,
            IEnumerable<DurableCompositeOutstandingOperation> operations,
            IEnumerable<DurableCompositeIssuedOperation> issuedOperations,
            IEnumerable<DurableCompositeJournaledOutstandingOperation> journaledOperations,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeOutstandingQueryState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            var copy = (operations ??
                throw new ArgumentNullException(nameof(operations)))
                .OrderBy(value => value.CommitSequence)
                .ToArray();
            if (copy.Length > DurableCompositeLimits.MaximumOutstandingPerActor ||
                copy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(operations));
            _operations = new ReadOnlyCollection<DurableCompositeOutstandingOperation>(copy);
            var issuedCopy = (issuedOperations ??
                throw new ArgumentNullException(nameof(issuedOperations)))
                .OrderBy(value => value.OperationToken.AdmissionSequence)
                .ToArray();
            if (issuedCopy.Length > DurableCompositeLimits.MaximumOutstandingPerActor ||
                issuedCopy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(issuedOperations));
            _issuedOperations =
                new ReadOnlyCollection<DurableCompositeIssuedOperation>(issuedCopy);
            var journaledCopy = (journaledOperations ??
                throw new ArgumentNullException(nameof(journaledOperations)))
                .OrderBy(value => value.OperationToken.AdmissionSequence)
                .ToArray();
            if (journaledCopy.Length > DurableCompositeLimits.MaximumOutstandingPerActor ||
                journaledCopy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(journaledOperations));
            _journaledOperations =
                new ReadOnlyCollection<DurableCompositeJournaledOutstandingOperation>(
                    journaledCopy);
        }

        public DurableCompositeOutstandingQueryState State { get; }
        public IReadOnlyList<DurableCompositeOutstandingOperation> Operations => _operations;
        public IReadOnlyList<DurableCompositeIssuedOperation> IssuedOperations => _issuedOperations;
        public IReadOnlyList<DurableCompositeJournaledOutstandingOperation>
            JournaledOperations => _journaledOperations;
        public string ReasonCode { get; }

        private readonly IReadOnlyList<DurableCompositeIssuedOperation> _issuedOperations;
        private readonly IReadOnlyList<DurableCompositeJournaledOutstandingOperation>
            _journaledOperations;
    }

    public enum DurableCompositeOutstandingReadState
    {
        Issued = 0,
        Journaled = 1,
        NotFound = 2,
        Conflict = 3,
        Corrupt = 4,
        Unavailable = 5
    }

    public sealed class DurableCompositeOutstandingOperationDetail
    {
        private readonly byte[] _exactIntent;

        internal DurableCompositeOutstandingOperationDetail(
            DurableCompositeOperationToken token,
            string requestHash,
            RpcPeerIdentity actorIdentity,
            DurableCompositeOperationReference reference,
            DurableCompositeOperationSnapshot snapshot,
            string endpointDomainId,
            byte[] exactIntent,
            DurableCompositeReconciliationRequirement requirement)
        {
            OperationToken = token ?? throw new ArgumentNullException(nameof(token));
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            Reference = reference;
            Snapshot = snapshot;
            EndpointDomainId = endpointDomainId ?? string.Empty;
            exactIntent = exactIntent ?? Array.Empty<byte>();
            if (exactIntent.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactIntent));
            _exactIntent = (byte[])exactIntent.Clone();
            Requirement = requirement;
        }

        public DurableCompositeOperationToken OperationToken { get; }
        public string RequestHash { get; }
        public RpcPeerIdentity ActorIdentity { get; }
        public DurableCompositeOperationReference Reference { get; }
        public DurableCompositeOperationSnapshot Snapshot { get; }
        public string EndpointDomainId { get; }
        public byte[] ExactIntent => (byte[])_exactIntent.Clone();
        public DurableCompositeReconciliationRequirement Requirement { get; }
        public bool HasDurableRoot => Reference != null;
    }

    public sealed class DurableCompositeOutstandingReadResult
    {
        internal DurableCompositeOutstandingReadResult(
            DurableCompositeOutstandingReadState state,
            string reasonCode,
            DurableCompositeOutstandingOperationDetail operation)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeOutstandingReadState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if ((state == DurableCompositeOutstandingReadState.Issued ||
                 state == DurableCompositeOutstandingReadState.Journaled) !=
                (operation != null))
                throw new ArgumentException("Only a ready outstanding read carries an operation.");
            State = state;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            Operation = operation;
        }

        public DurableCompositeOutstandingReadState State { get; }
        public string ReasonCode { get; }
        public DurableCompositeOutstandingOperationDetail Operation { get; }
        public bool Success => Operation != null;
    }

    /// <summary>
    /// Read-only durable idempotency lookup. Issued identifies an authenticated token lease that
    /// has not created a root; Journaled carries the retained root/terminal detail. Conflict means
    /// the same owner/account durable key is already bound to different request bytes. Retired
    /// checkpoint history is NotFound once its finite replay evidence is compacted.
    /// </summary>
    public enum DurableCompositeRequestLookupState
    {
        NotFound = 0,
        Issued = 1,
        Journaled = 2,
        Conflict = 3,
        Corrupt = 4,
        Unavailable = 5
    }

    public sealed class DurableCompositeRequestLookupResult
    {
        internal DurableCompositeRequestLookupResult(
            DurableCompositeRequestLookupState state,
            string reasonCode,
            string durableRequestKeyHash,
            string requestHash,
            DurableCompositeOutstandingOperationDetail operation)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeRequestLookupState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if ((state == DurableCompositeRequestLookupState.Issued ||
                 state == DurableCompositeRequestLookupState.Journaled) != (operation != null))
                throw new ArgumentException("Only a successful request lookup carries an operation.");
            State = state;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            if (string.IsNullOrEmpty(durableRequestKeyHash) &&
                string.IsNullOrEmpty(requestHash) && operation == null)
            {
                DurableRequestKeyHash = string.Empty;
                RequestHash = string.Empty;
            }
            else
            {
                DurableRequestKeyHash = CompositeValidation.RequireSha256(
                    durableRequestKeyHash, nameof(durableRequestKeyHash));
                RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            }
            Operation = operation;
        }

        public DurableCompositeRequestLookupState State { get; }
        public string ReasonCode { get; }
        public string DurableRequestKeyHash { get; }
        public string RequestHash { get; }
        public DurableCompositeOutstandingOperationDetail Operation { get; }
        public bool Success => Operation != null;
    }

    public sealed class DurableCompositeOutstandingChangedEventArgs : EventArgs
    {
        internal DurableCompositeOutstandingChangedEventArgs(RpcPeerIdentity actorIdentity)
        {
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
        }

        public RpcPeerIdentity ActorIdentity { get; }
    }

    public interface IDurableCompositeOutstandingOperationSource
    {
        event EventHandler<DurableCompositeOutstandingChangedEventArgs> OutstandingChanged;
        DurableCompositeOutstandingQueryResult QueryOutstanding(RpcPeerIdentity actorIdentity);
        DurableCompositeOwnerOutstandingQueryResult QueryOwnerOutstanding(
            ModuleRegistration ownerModule);
        DurableCompositeOutstandingReadResult ReadOutstandingOperation(
            ModuleRegistration ownerModule,
            RpcPeerIdentity actorIdentity,
            string canonicalOperationToken);
        DurableCompositeRequestLookupResult LookupRequest(
            ModuleRegistration ownerModule,
            RpcPeerIdentity actorIdentity,
            string durableRequestKeyHash,
            string requestHash);
    }

    /// <summary>
    /// Server-authoritative disposition of an opaque operation token found in synchronized world
    /// metadata. Invalid, unknown, corrupt, and unavailable results are all fail-closed. A caller
    /// may treat only Cancelled, Acknowledged, Aborted, or Retired as authenticated inert tags.
    /// The result does not expose the root payload, actor, request hash, or receipt.
    /// </summary>
    public enum DurableCompositeTokenDispositionState
    {
        Active = 0,
        CommittedReconciliationHold = 1,
        Cancelled = 2,
        Acknowledged = 3,
        Aborted = 4,
        Retired = 5,
        Invalid = 6,
        Unknown = 7,
        Corrupt = 8,
        Unavailable = 9,
        AbortedReconciliationHold = 10
    }

    public sealed class DurableCompositeTokenDispositionResult
    {
        internal DurableCompositeTokenDispositionResult(
            DurableCompositeTokenDispositionState state,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeTokenDispositionState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
        }

        public DurableCompositeTokenDispositionState State { get; }
        public string ReasonCode { get; }
        public bool IsAuthenticatedInert =>
            State == DurableCompositeTokenDispositionState.Cancelled ||
            State == DurableCompositeTokenDispositionState.Acknowledged ||
            State == DurableCompositeTokenDispositionState.Aborted ||
            State == DurableCompositeTokenDispositionState.Retired;
        public bool RequiresCustodyHold => !IsAuthenticatedInert;
    }

    public interface IDurableCompositeTokenDispositionSource
    {
        DurableCompositeTokenDispositionResult QueryTokenDisposition(
            ModuleRegistration requesterModule,
            string canonicalOperationToken);
    }

    /// <summary>
    /// Bounded read-only startup view of every unresolved operation owned by one active module.
    /// This is the recovery seam for a server restart that occurs before the original actor
    /// reconnects. It never performs cleanup or infers abandonment of an issued token.
    /// </summary>
    public sealed class DurableCompositeOwnerOutstandingQueryResult
    {
        private readonly IReadOnlyList<DurableCompositeOutstandingOperation> _operations;
        private readonly IReadOnlyList<DurableCompositeIssuedOperation> _issuedOperations;
        private readonly IReadOnlyList<DurableCompositeJournaledOutstandingOperation>
            _journaledOperations;

        internal DurableCompositeOwnerOutstandingQueryResult(
            DurableCompositeOutstandingQueryState state,
            IEnumerable<DurableCompositeOutstandingOperation> operations,
            IEnumerable<DurableCompositeIssuedOperation> issuedOperations,
            IEnumerable<DurableCompositeJournaledOutstandingOperation> journaledOperations,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeOutstandingQueryState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            DurableCompositeOutstandingOperation[] committed = (operations ??
                throw new ArgumentNullException(nameof(operations)))
                .OrderBy(value => value.CommitSequence)
                .ToArray();
            DurableCompositeIssuedOperation[] issued = (issuedOperations ??
                throw new ArgumentNullException(nameof(issuedOperations)))
                .OrderBy(value => value.OperationToken.AdmissionSequence)
                .ToArray();
            DurableCompositeJournaledOutstandingOperation[] journaled =
                (journaledOperations ??
                    throw new ArgumentNullException(nameof(journaledOperations)))
                .OrderBy(value => value.OperationToken.AdmissionSequence)
                .ToArray();
            if (committed.Length > DurableCompositeLimits.MaximumUnresolvedOperations ||
                issued.Length > DurableCompositeLimits.MaximumUnresolvedOperations ||
                journaled.Length > DurableCompositeLimits.MaximumUnresolvedOperations ||
                committed.Any(value => value == null) ||
                issued.Any(value => value == null) ||
                journaled.Any(value => value == null) ||
                (long)committed.Length + issued.Length + journaled.Length >
                    DurableCompositeLimits.MaximumUnresolvedOperations)
                throw new ArgumentOutOfRangeException(nameof(operations));
            _operations = new ReadOnlyCollection<DurableCompositeOutstandingOperation>(committed);
            _issuedOperations = new ReadOnlyCollection<DurableCompositeIssuedOperation>(issued);
            _journaledOperations =
                new ReadOnlyCollection<DurableCompositeJournaledOutstandingOperation>(journaled);
        }

        public DurableCompositeOutstandingQueryState State { get; }
        public IReadOnlyList<DurableCompositeOutstandingOperation> Operations => _operations;
        public IReadOnlyList<DurableCompositeIssuedOperation> IssuedOperations => _issuedOperations;
        public IReadOnlyList<DurableCompositeJournaledOutstandingOperation>
            JournaledOperations => _journaledOperations;
        public string ReasonCode { get; }
    }

    /// <summary>
    /// Immutable consumer-domain mutation evidence for one stable endpoint. SemanticRevision must
    /// exclude Transactions journal/claim metadata: writing those keys may advance a raw ZDO data
    /// revision without changing this value. Before/after fingerprints are exact SHA-256 values.
    /// </summary>
    public sealed class DurableCompositeEndpointIntent
    {
        private readonly byte[] _exactMutation;

        public DurableCompositeEndpointIntent(
            EndpointId stableEndpointId,
            string beforeFingerprint,
            string afterFingerprint,
            string preparedSemanticRevision,
            byte[] exactMutation)
            : this(
                stableEndpointId,
                beforeFingerprint,
                afterFingerprint,
                preparedSemanticRevision,
                afterFingerprint,
                exactMutation)
        {
        }

        public DurableCompositeEndpointIntent(
            EndpointId stableEndpointId,
            string beforeFingerprint,
            string afterFingerprint,
            string preparedSemanticRevision,
            string afterSemanticRevision,
            byte[] exactMutation)
        {
            if (!stableEndpointId.IsValid)
                throw new ArgumentException("A stable endpoint ID is required.", nameof(stableEndpointId));
            StableEndpointId = stableEndpointId;
            BeforeFingerprint = CompositeValidation.RequireSha256(
                beforeFingerprint, nameof(beforeFingerprint));
            AfterFingerprint = CompositeValidation.RequireSha256(
                afterFingerprint, nameof(afterFingerprint));
            if (string.Equals(BeforeFingerprint, AfterFingerprint, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A composite endpoint must describe an exact state change.",
                    nameof(afterFingerprint));
            PreparedSemanticRevision = CompositeValidation.RequireText(
                preparedSemanticRevision,
                nameof(preparedSemanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            AfterSemanticRevision = CompositeValidation.RequireText(
                afterSemanticRevision,
                nameof(afterSemanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            exactMutation = exactMutation ?? Array.Empty<byte>();
            if (exactMutation.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactMutation));
            _exactMutation = (byte[])exactMutation.Clone();
        }

        public EndpointId StableEndpointId { get; }
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }
        public string PreparedSemanticRevision { get; }
        public string AfterSemanticRevision { get; }
        public byte[] ExactMutation => (byte[])_exactMutation.Clone();

        internal byte[] ExactMutationUnsafe => _exactMutation;
    }

    /// <summary>
    /// Complete immutable operation journal. The intent hash covers the exact consumer payload and
    /// every canonically sorted endpoint ID, before/after fingerprint, semantic revision, and
    /// endpoint mutation payload. Actor and operation UUID are bound separately in the root.
    /// </summary>
    public sealed class DurableCompositeOperationIntent
    {
        private readonly byte[] _exactIntent;
        private readonly IReadOnlyList<DurableCompositeEndpointIntent> _endpoints;

        public DurableCompositeOperationIntent(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string endpointDomainId,
            string requestHash,
            byte[] exactIntent,
            IEnumerable<DurableCompositeEndpointIntent> endpoints)
            : this(
                ownerModule,
                operationToken,
                actorIdentity,
                endpointDomainId,
                requestHash,
                exactIntent,
                endpoints,
                null)
        {
        }

        public DurableCompositeOperationIntent(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string endpointDomainId,
            string requestHash,
            byte[] exactIntent,
            IEnumerable<DurableCompositeEndpointIntent> endpoints,
            DurableCompositeReconciliationRequirement reconciliationRequirement)
            : this(
                ownerModule,
                operationToken,
                actorIdentity,
                endpointDomainId,
                requestHash,
                exactIntent,
                endpoints,
                reconciliationRequirement,
                DurableCompositeLimits.MaximumStandardEndpoints)
        {
        }

        internal DurableCompositeOperationIntent(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string endpointDomainId,
            string requestHash,
            byte[] exactIntent,
            IEnumerable<DurableCompositeEndpointIntent> endpoints,
            DurableCompositeReconciliationRequirement reconciliationRequirement,
            int maximumEndpoints)
        {
            if (maximumEndpoints < 1 || maximumEndpoints > DurableCompositeLimits.MaximumEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumEndpoints));
            OwnerModule = ownerModule ?? throw new ArgumentNullException(nameof(ownerModule));
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            EndpointDomainId = RunicIdentifier.Require(
                endpointDomainId, nameof(endpointDomainId));
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            exactIntent = exactIntent ?? Array.Empty<byte>();
            if (exactIntent.Length == 0 || exactIntent.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactIntent));
            _exactIntent = (byte[])exactIntent.Clone();

            var bounded = new List<DurableCompositeEndpointIntent>(
                maximumEndpoints);
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            using (IEnumerator<DurableCompositeEndpointIntent> iterator = endpoints.GetEnumerator())
            {
                while (iterator.MoveNext())
                {
                    if (bounded.Count == maximumEndpoints)
                        throw new ArgumentOutOfRangeException(
                            nameof(endpoints),
                            "The composite operation exceeds its endpoint-domain bound.");
                    if (iterator.Current == null)
                        throw new ArgumentException("Composite endpoints cannot contain null.", nameof(endpoints));
                    bounded.Add(iterator.Current);
                }
            }
            if (bounded.Count == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(endpoints), "A composite operation requires at least one endpoint.");
            bounded.Sort((left, right) => left.StableEndpointId.CompareTo(right.StableEndpointId));
            long aggregateBytes = exactIntent.Length;
            for (int index = 0; index < bounded.Count; index++)
            {
                if (index > 0 && bounded[index - 1].StableEndpointId == bounded[index].StableEndpointId)
                    throw new ArgumentException(
                        "Composite endpoint IDs must be unique.", nameof(endpoints));
                aggregateBytes += bounded[index].ExactMutationUnsafe.Length;
                if (aggregateBytes > DurableCompositeLimits.MaximumIntentBytes)
                    throw new ArgumentOutOfRangeException(
                        nameof(endpoints), "The aggregate exact intent exceeds 64 KiB.");
            }
            _endpoints = new ReadOnlyCollection<DurableCompositeEndpointIntent>(bounded);
            ReconciliationRequirement = reconciliationRequirement;
            IntentHash = DurableCompositeCodec.ComputeIntentHash(
                EndpointDomainId, _exactIntent, _endpoints);
        }

        public ModuleRegistration OwnerModule { get; }
        public string OwnerModuleId => OwnerModule.Descriptor.ModuleId;
        public DurableCompositeOperationToken OperationToken { get; }
        public Guid OperationId => OperationToken.OperationId;
        public string OperationIdText => OperationToken.OperationIdText;
        public RpcPeerIdentity ActorIdentity { get; }
        public string EndpointDomainId { get; }
        public string IntentHash { get; }
        public string RequestHash { get; }
        public byte[] ExactIntent => (byte[])_exactIntent.Clone();
        public IReadOnlyList<DurableCompositeEndpointIntent> Endpoints => _endpoints;
        /// <summary>
        /// Optional reconnect obligation for a two-authority saga. A committed root remains in
        /// the account-scoped outstanding ledger until an exact durable acknowledgement is
        /// recorded. This is compatibility policy, not validation of a remote client.
        /// </summary>
        public DurableCompositeReconciliationRequirement ReconciliationRequirement { get; }
        public DurableCompositeOperationReference Reference =>
            new DurableCompositeOperationReference(
                OwnerModule, OperationToken, ActorIdentity, RequestHash, IntentHash);

        internal byte[] ExactIntentUnsafe => _exactIntent;
    }

    public sealed class DurableCompositeEndpointDomainDescriptor
    {
        public DurableCompositeEndpointDomainDescriptor(
            string domainId,
            int schemaVersion,
            string stableEndpointPrefix)
            : this(
                domainId,
                schemaVersion,
                stableEndpointPrefix,
                DurableCompositeLimits.MaximumStandardEndpoints)
        {
        }

        public DurableCompositeEndpointDomainDescriptor(
            string domainId,
            int schemaVersion,
            string stableEndpointPrefix,
            int maximumEndpoints)
        {
            DomainId = RunicIdentifier.Require(domainId, nameof(domainId));
            if (schemaVersion < 1 || schemaVersion > 65535)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            SchemaVersion = schemaVersion;
            StableEndpointPrefix = CompositeValidation.RequireText(
                stableEndpointPrefix, nameof(stableEndpointPrefix), 128);
            if (maximumEndpoints < 1 ||
                maximumEndpoints > DurableCompositeLimits.MaximumEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumEndpoints));
            MaximumEndpoints = maximumEndpoints;
        }

        public string DomainId { get; }
        public int SchemaVersion { get; }
        public string StableEndpointPrefix { get; }
        public int MaximumEndpoints { get; }
    }

    public sealed class DurableCompositeEndpointPredecessor
    {
        internal DurableCompositeEndpointPredecessor(
            EndpointId endpointId,
            long commitSequence,
            string operationId)
        {
            if (!endpointId.IsValid) throw new ArgumentException(
                "A stable endpoint ID is required.", nameof(endpointId));
            if (commitSequence < 1) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            if (!Guid.TryParseExact(operationId, "N", out Guid parsed) || parsed == Guid.Empty)
                throw new ArgumentException(
                    "A canonical nonempty operation UUID is required.", nameof(operationId));
            EndpointId = endpointId;
            CommitSequence = commitSequence;
            OperationId = parsed.ToString("N");
        }

        public EndpointId EndpointId { get; }
        public long CommitSequence { get; }
        public string OperationId { get; }
    }

    public sealed class DurableCompositeCommitOrder
    {
        private readonly IReadOnlyList<DurableCompositeEndpointPredecessor> _predecessors;

        internal DurableCompositeCommitOrder(
            long commitSequence,
            IEnumerable<DurableCompositeEndpointPredecessor> predecessors)
        {
            if (commitSequence < 1) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            CommitSequence = commitSequence;
            var copy = (predecessors ?? throw new ArgumentNullException(nameof(predecessors)))
                .OrderBy(value => value.EndpointId)
                .ToArray();
            if (copy.Length > DurableCompositeLimits.MaximumEndpoints ||
                copy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(predecessors));
            for (int index = 1; index < copy.Length; index++)
                if (copy[index - 1].EndpointId == copy[index].EndpointId)
                    throw new ArgumentException(
                        "Commit predecessors must be unique by endpoint.", nameof(predecessors));
            _predecessors = new ReadOnlyCollection<DurableCompositeEndpointPredecessor>(copy);
        }

        public long CommitSequence { get; }
        public IReadOnlyList<DurableCompositeEndpointPredecessor> Predecessors => _predecessors;
    }

    public sealed class DurableCompositeOperationReference
    {
        public DurableCompositeOperationReference(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string requestHash,
            string intentHash)
        {
            OwnerModule = ownerModule ?? throw new ArgumentNullException(nameof(ownerModule));
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
        }

        public ModuleRegistration OwnerModule { get; }
        public string OwnerModuleId => OwnerModule.Descriptor.ModuleId;
        public DurableCompositeOperationToken OperationToken { get; }
        public Guid OperationId => OperationToken.OperationId;
        public string OperationIdText => OperationToken.OperationIdText;
        public RpcPeerIdentity ActorIdentity { get; }
        public string IntentHash { get; }
        public string RequestHash { get; }
    }

    public sealed class DurableCompositeEndpointClaim
    {
        internal DurableCompositeEndpointClaim(
            string ownerModuleId,
            string endpointDomainId,
            Guid operationId,
            RpcPeerIdentity actorIdentity,
            string intentHash,
            DurableCompositeEndpointIntent endpoint)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            EndpointDomainId = RunicIdentifier.Require(endpointDomainId, nameof(endpointDomainId));
            if (operationId == Guid.Empty) throw new ArgumentException(
                "A nonempty operation UUID is required.", nameof(operationId));
            OperationId = operationId;
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            StableEndpointId = endpoint.StableEndpointId;
            BeforeFingerprint = endpoint.BeforeFingerprint;
            AfterFingerprint = endpoint.AfterFingerprint;
            PreparedSemanticRevision = endpoint.PreparedSemanticRevision;
        }

        public string OwnerModuleId { get; }
        public string EndpointDomainId { get; }
        public Guid OperationId { get; }
        public string OperationIdText => OperationId.ToString("N");
        public RpcPeerIdentity ActorIdentity { get; }
        public string IntentHash { get; }
        public EndpointId StableEndpointId { get; }
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }
        public string PreparedSemanticRevision { get; }

        public bool MatchesExact(DurableCompositeEndpointClaim other) =>
            other != null &&
            string.Equals(OwnerModuleId, other.OwnerModuleId, StringComparison.Ordinal) &&
            string.Equals(EndpointDomainId, other.EndpointDomainId, StringComparison.Ordinal) &&
            OperationId == other.OperationId &&
            CompositeValidation.SameIdentity(ActorIdentity, other.ActorIdentity) &&
            string.Equals(IntentHash, other.IntentHash, StringComparison.Ordinal) &&
            StableEndpointId == other.StableEndpointId &&
            string.Equals(BeforeFingerprint, other.BeforeFingerprint, StringComparison.Ordinal) &&
            string.Equals(AfterFingerprint, other.AfterFingerprint, StringComparison.Ordinal) &&
            string.Equals(
                PreparedSemanticRevision,
                other.PreparedSemanticRevision,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Immutable historical mutation context used by the registered domain adapter. It contains
    /// no live caller lease: admitting a new operation requires the owner's active lease, while
    /// finishing an already-Committing WAL entry is mandatory system recovery and requires the
    /// exact active domain/schema adapter instead.
    /// </summary>
    public sealed class DurableCompositeMutationContext
    {
        private readonly byte[] _exactIntent;

        internal DurableCompositeMutationContext(
            string ownerModuleId,
            string endpointDomainId,
            int endpointDomainSchemaVersion,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string intentHash,
            byte[] exactIntent,
            long commitSequence)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            EndpointDomainId = RunicIdentifier.Require(endpointDomainId, nameof(endpointDomainId));
            if (endpointDomainSchemaVersion < 1 || endpointDomainSchemaVersion > 65535)
                throw new ArgumentOutOfRangeException(nameof(endpointDomainSchemaVersion));
            EndpointDomainSchemaVersion = endpointDomainSchemaVersion;
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
            exactIntent = exactIntent ?? Array.Empty<byte>();
            if (exactIntent.Length < 1 || exactIntent.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactIntent));
            _exactIntent = (byte[])exactIntent.Clone();
            if (commitSequence < 1) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            CommitSequence = commitSequence;
        }

        public string OwnerModuleId { get; }
        public string EndpointDomainId { get; }
        public int EndpointDomainSchemaVersion { get; }
        public DurableCompositeOperationToken OperationToken { get; }
        public Guid OperationId => OperationToken.OperationId;
        public string WorldEpoch => OperationToken.WorldEpoch;
        public long AdmissionSequence => OperationToken.AdmissionSequence;
        public RpcPeerIdentity ActorIdentity { get; }
        public string IntentHash { get; }
        public byte[] ExactIntent => (byte[])_exactIntent.Clone();
        public long CommitSequence { get; }
    }

    public sealed class DurableCompositeRollbackContext
    {
        private readonly byte[] _exactIntent;

        internal DurableCompositeRollbackContext(
            string ownerModuleId,
            string endpointDomainId,
            int endpointDomainSchemaVersion,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string intentHash,
            byte[] exactIntent,
            DurableCompositeOperationPhase phase,
            long commitSequence)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            EndpointDomainId = RunicIdentifier.Require(endpointDomainId, nameof(endpointDomainId));
            if (endpointDomainSchemaVersion < 1 || endpointDomainSchemaVersion > 65535)
                throw new ArgumentOutOfRangeException(nameof(endpointDomainSchemaVersion));
            EndpointDomainSchemaVersion = endpointDomainSchemaVersion;
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
            exactIntent = exactIntent ?? Array.Empty<byte>();
            if (exactIntent.Length < 1 || exactIntent.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactIntent));
            _exactIntent = (byte[])exactIntent.Clone();
            if (!Enum.IsDefined(typeof(DurableCompositeOperationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (commitSequence < 0) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            Phase = phase;
            CommitSequence = commitSequence;
        }

        public string OwnerModuleId { get; }
        public string EndpointDomainId { get; }
        public int EndpointDomainSchemaVersion { get; }
        public DurableCompositeOperationToken OperationToken { get; }
        public Guid OperationId => OperationToken.OperationId;
        public RpcPeerIdentity ActorIdentity { get; }
        public string IntentHash { get; }
        public byte[] ExactIntent => (byte[])_exactIntent.Clone();
        public DurableCompositeOperationPhase Phase { get; }
        public long CommitSequence { get; }
    }

    /// <summary>
    /// Exact endpoint evidence returned by the authoritative adapter. Raw transport/ZDO metadata
    /// revisions are diagnostic only; SemanticRevision is consumer-domain state and must not
    /// change merely because a root or claim key was written.
    /// </summary>
    public sealed class DurableCompositeEndpointSnapshot
    {
        public DurableCompositeEndpointSnapshot(
            EndpointId stableEndpointId,
            string fingerprint,
            string semanticRevision,
            bool stableIdentityCurrent,
            bool synchronized,
            bool serverOwned,
            bool destroyPending,
            DurableCompositeEndpointClaim claim)
            : this(
                stableEndpointId,
                fingerprint,
                semanticRevision,
                stableIdentityCurrent,
                synchronized,
                serverOwned,
                destroyPending,
                claim,
                string.Empty,
                0)
        {
        }

        public DurableCompositeEndpointSnapshot(
            EndpointId stableEndpointId,
            string fingerprint,
            string semanticRevision,
            bool stableIdentityCurrent,
            bool synchronized,
            bool serverOwned,
            bool destroyPending,
            DurableCompositeEndpointClaim claim,
            string appliedWorldEpoch,
            long appliedCommitSequence)
        {
            if (!stableEndpointId.IsValid)
                throw new ArgumentException("A stable endpoint ID is required.", nameof(stableEndpointId));
            StableEndpointId = stableEndpointId;
            Fingerprint = CompositeValidation.RequireSha256(fingerprint, nameof(fingerprint));
            SemanticRevision = CompositeValidation.RequireText(
                semanticRevision,
                nameof(semanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            StableIdentityCurrent = stableIdentityCurrent;
            Synchronized = synchronized;
            ServerOwned = serverOwned;
            DestroyPending = destroyPending;
            Claim = claim;
            if (appliedCommitSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(appliedCommitSequence));
            if (appliedCommitSequence == 0)
            {
                if (!string.IsNullOrEmpty(appliedWorldEpoch))
                    throw new ArgumentException(
                        "An unapplied endpoint cannot carry a world epoch.",
                        nameof(appliedWorldEpoch));
                AppliedWorldEpoch = string.Empty;
            }
            else
            {
                if (!Guid.TryParseExact(appliedWorldEpoch, "N", out Guid epoch) ||
                    epoch == Guid.Empty ||
                    !string.Equals(
                        epoch.ToString("N"), appliedWorldEpoch, StringComparison.Ordinal))
                    throw new ArgumentException(
                        "A canonical applied world epoch is required.",
                        nameof(appliedWorldEpoch));
                AppliedWorldEpoch = appliedWorldEpoch;
            }
            AppliedCommitSequence = appliedCommitSequence;
        }

        public EndpointId StableEndpointId { get; }
        public string Fingerprint { get; }
        public string SemanticRevision { get; }
        public bool StableIdentityCurrent { get; }
        public bool Synchronized { get; }
        public bool ServerOwned { get; }
        public bool DestroyPending { get; }
        public DurableCompositeEndpointClaim Claim { get; }
        public string AppliedWorldEpoch { get; }
        public long AppliedCommitSequence { get; }
    }

    public sealed class DurableCompositeEndpointReceipt
    {
        internal DurableCompositeEndpointReceipt(
            EndpointId endpointId,
            string fingerprint,
            string semanticRevision,
            string appliedWorldEpoch,
            long appliedCommitSequence)
        {
            EndpointId = endpointId;
            Fingerprint = CompositeValidation.RequireSha256(fingerprint, nameof(fingerprint));
            SemanticRevision = CompositeValidation.RequireText(
                semanticRevision,
                nameof(semanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            if (appliedCommitSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(appliedCommitSequence));
            if (appliedCommitSequence == 0)
            {
                if (!string.IsNullOrEmpty(appliedWorldEpoch))
                    throw new ArgumentException(
                        "An unapplied receipt cannot carry a world epoch.",
                        nameof(appliedWorldEpoch));
                AppliedWorldEpoch = string.Empty;
            }
            else
            {
                if (!Guid.TryParseExact(appliedWorldEpoch, "N", out Guid epoch) ||
                    epoch == Guid.Empty ||
                    !string.Equals(
                        epoch.ToString("N"), appliedWorldEpoch, StringComparison.Ordinal))
                    throw new ArgumentException(
                        "A canonical applied world epoch is required.",
                        nameof(appliedWorldEpoch));
                AppliedWorldEpoch = appliedWorldEpoch;
            }
            AppliedCommitSequence = appliedCommitSequence;
        }

        public EndpointId EndpointId { get; }
        public string Fingerprint { get; }
        public string SemanticRevision { get; }
        public string AppliedWorldEpoch { get; }
        public long AppliedCommitSequence { get; }
    }

    public sealed class DurableCompositeReceipt
    {
        private readonly IReadOnlyList<DurableCompositeEndpointReceipt> _endpoints;

        internal DurableCompositeReceipt(
            DurableCompositeOperationReference operation,
            DurableCompositeOperationPhase phase,
            string receiptHash,
            IEnumerable<DurableCompositeEndpointReceipt> endpoints)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            if (phase != DurableCompositeOperationPhase.Committed &&
                phase != DurableCompositeOperationPhase.Aborted)
                throw new ArgumentOutOfRangeException(nameof(phase));
            Phase = phase;
            ReceiptHash = CompositeValidation.RequireSha256(receiptHash, nameof(receiptHash));
            var copy = (endpoints ?? throw new ArgumentNullException(nameof(endpoints))).ToArray();
            if (copy.Length < 1 || copy.Length > DurableCompositeLimits.MaximumEndpoints ||
                copy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(endpoints));
            _endpoints = new ReadOnlyCollection<DurableCompositeEndpointReceipt>(copy);
        }

        public DurableCompositeOperationReference Operation { get; }
        public DurableCompositeOperationPhase Phase { get; }
        public string ReceiptHash { get; }
        public IReadOnlyList<DurableCompositeEndpointReceipt> Endpoints => _endpoints;
    }

    public sealed class DurableCompositeOperationSnapshot
    {
        internal DurableCompositeOperationSnapshot(
            DurableCompositeOperationReference operation,
            DurableCompositeOperationPhase phase,
            DurableCompositeReceipt receipt,
            long commitSequence = 0,
            bool reconciliationAcknowledged = false)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            if (!Enum.IsDefined(typeof(DurableCompositeOperationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if ((phase == DurableCompositeOperationPhase.Committed ||
                 phase == DurableCompositeOperationPhase.Aborted) != (receipt != null))
                throw new ArgumentException("Only a terminal snapshot carries a durable receipt.");
            Phase = phase;
            Receipt = receipt;
            if (commitSequence < 0) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            if (phase == DurableCompositeOperationPhase.Committing ||
                phase == DurableCompositeOperationPhase.Committed)
            {
                if (commitSequence < 1)
                    throw new ArgumentException(
                        "Committing and committed snapshots require a durable commit sequence.",
                        nameof(commitSequence));
            }
            else if (commitSequence != 0)
                throw new ArgumentException(
                    "Only committing and committed snapshots carry a commit sequence.",
                    nameof(commitSequence));
            CommitSequence = commitSequence;
            ReconciliationAcknowledged = reconciliationAcknowledged;
        }

        public DurableCompositeOperationReference Operation { get; }
        public DurableCompositeOperationPhase Phase { get; }
        public DurableCompositeReceipt Receipt { get; }
        public long CommitSequence { get; }
        public bool ReconciliationAcknowledged { get; }
        public bool Terminal => Phase == DurableCompositeOperationPhase.Committed ||
                                Phase == DurableCompositeOperationPhase.Aborted;
    }

    public sealed class DurableCompositeOperationResult
    {
        internal DurableCompositeOperationResult(
            DurableCompositeResultCode code,
            string reasonCode,
            DurableCompositeOperationSnapshot snapshot)
        {
            if (!Enum.IsDefined(typeof(DurableCompositeResultCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            Snapshot = snapshot;
        }

        public DurableCompositeResultCode Code { get; }
        public string ReasonCode { get; }
        public DurableCompositeOperationSnapshot Snapshot { get; }
        public bool Success => Code == DurableCompositeResultCode.Prepared ||
                               Code == DurableCompositeResultCode.Committed ||
                               Code == DurableCompositeResultCode.Aborted ||
                               Code == DurableCompositeResultCode.Replay ||
                               Code == DurableCompositeResultCode.Acknowledged;
    }

    /// <summary>
    /// Authoritative primary root storage. Implementations atomically create one unresolved root,
    /// enforce the supplied global/per-actor unresolved limits before returning Created, and make
    /// exact CAS transitions durable. Read never promotes temporary, backup, timestamp, or partial
    /// index data. Terminal roots remain readable for handler-durable status and replay.
    /// </summary>
    internal interface IDurableCompositeJournalStore
    {
        /// <summary>
        /// Exact world namespace of this catalog. Every endpoint domain and owner shares it so
        /// independently ordered WALs cannot mutate the same physical endpoint.
        /// </summary>
        string WorldScope { get; }

        DurableCompositeTokenIssueCode IssueOperationToken(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            string actorKey,
            string durableRequestKeyHash,
            string requestHash,
            out DurableCompositeOperationToken token,
            out string failureCode);

        DurableCompositeTokenCancellationCode CancelIssuedOperationToken(
            string ownerModuleId,
            string actorKey,
            DurableCompositeOperationToken token,
            string requestHash,
            out string failureCode);

        DurableCompositeJournalReadState Read(
            string operationId,
            out byte[] exactRecord,
            out string failureCode);

        DurableCompositeJournalCreateState TryCreateUnresolved(
            string operationId,
            string actorKey,
            byte[] exactRecord,
            int maximumUnresolved,
            int maximumUnresolvedPerActor,
            out byte[] existingRecord,
            out string failureCode);

        bool TryCompareExchange(
            string operationId,
            byte[] expectedRecord,
            byte[] replacementRecord,
            bool replacementIsTerminal,
            out byte[] observedRecord,
            out string failureCode);

        /// <summary>
        /// Atomically allocates one monotonically increasing commit sequence, captures the last
        /// ordered writer of every endpoint, and publishes the factory-produced Committing root
        /// in the same flushed catalog replacement. The factory is invoked while the store owns
        /// its exclusive write lock and must be pure and bounded.
        /// </summary>
        bool TryBeginCommit(
            string operationId,
            byte[] expectedPreparedRecord,
            IReadOnlyList<EndpointId> sortedEndpointIds,
            Func<DurableCompositeCommitOrder, byte[]> committingRecordFactory,
            out DurableCompositeCommitOrder commitOrder,
            out byte[] observedRecord,
            out string failureCode);

        /// <summary>
        /// Returns every retained committing/committed root in strict commit-sequence order.
        /// History required to bridge the last verified world checkpoint is never evicted.
        /// </summary>
        DurableCompositeHistoryReadState ReadCommitHistory(
            out IReadOnlyList<byte[]> exactRecords,
            out long verifiedCheckpointSequence,
            out string failureCode);

        DurableCompositeHistoryReadState ReadOutstanding(
            string actorKey,
            out IReadOnlyList<byte[]> exactRecords,
            out string failureCode);

        DurableCompositeHistoryReadState ReadIssuedOutstanding(
            string actorKey,
            out IReadOnlyList<DurableCompositeIssuedOperation> operations,
            out string failureCode);

        DurableCompositeHistoryReadState ReadOwnerOutstanding(
            string ownerModuleId,
            out IReadOnlyList<byte[]> exactRecords,
            out IReadOnlyList<DurableCompositeIssuedOperation> issuedOperations,
            out string failureCode);

        DurableCompositeHistoryReadState ReadIssuedOperation(
            string operationId,
            out DurableCompositeIssuedOperation operation,
            out string failureCode);

        DurableCompositeRequestLookupState ReadRequest(
            string ownerModuleId,
            string actorKey,
            string durableRequestKeyHash,
            string requestHash,
            out DurableCompositeIssuedOperation issuedOperation,
            out byte[] exactRoot,
            out string failureCode);

        DurableCompositeTokenDispositionState ReadTokenDisposition(
            string canonicalOperationToken,
            out string failureCode);

        bool TryStageWorldCheckpoint(
            long throughCommitSequence,
            string exactWorldDatabasePath,
            string exactSaveCycleId,
            string originProcessId,
            long markerUserId,
            uint markerObjectId,
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode);

        DurableCompositeHistoryReadState ReadStagedWorldCheckpoint(
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode);

        DurableCompositeHistoryReadState ReadCheckpointStreamIdentity(
            out string worldEpoch,
            out string failureCode);

        bool TryRebindStagedWorldCheckpointMarker(
            long expectedMarkerUserId,
            uint expectedMarkerObjectId,
            long currentMarkerUserId,
            uint currentMarkerObjectId,
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode);

        /// <summary>
        /// Advances and compacts history only after the caller proves that the exact local world
        /// database replace completed for a save snapshot captured at throughCommitSequence.
        /// Unsupported/cloud save paths must return Rejected.
        /// </summary>
        DurableCompositeCheckpointState TryAdvanceVerifiedWorldCheckpoint(
            long throughCommitSequence,
            string exactWorldDatabasePath,
            string exactSaveCycleId,
            out string failureCode);
    }

    /// <summary>
    /// Authoritative endpoint adapter. Every method must resolve the exact stable identity and
    /// reject missing, duplicate, unsynchronized, client-owned, or destroy-pending objects. Claim
    /// acquire/release are exact CAS operations. TryApplyAfter must revalidate the supplied exact
    /// claim, before fingerprint, and semantic revision before the first domain mutation, persist
    /// the mutation, then return only after the exact after fingerprint is readable.
    /// </summary>
    public interface IDurableCompositeEndpointStore
    {
        DurableCompositeEndpointReadState Read(
            EndpointId stableEndpointId,
            out DurableCompositeEndpointSnapshot snapshot,
            out string failureCode);

        bool TryAcquireClaim(
            DurableCompositeEndpointClaim claim,
            out string failureCode);

        bool TryApplyAfter(
            DurableCompositeMutationContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            out string failureCode);

        bool TryReleaseClaim(
            DurableCompositeEndpointClaim exactClaim,
            out string failureCode);
    }

    /// <summary>
    /// Optional root-binding seam for endpoint domains whose authoritative read needs immutable
    /// operation evidence in addition to the stable endpoint id. The coordinator invokes this
    /// only after the exact root has been read back from the durable journal and before any
    /// endpoint read, claim, or mutation. Implementations must be bounded and idempotent for the
    /// exact operation; they must reject a conflicting live binding rather than replace it.
    /// </summary>
    public enum DurableCompositeOperationBindState
    {
        Bound = 0,
        NotReady = 1,
        FailedClosed = 2
    }

    public interface IDurableCompositeOperationBoundEndpointStore
    {
        DurableCompositeOperationBindState TryBindOperation(
            DurableCompositeRollbackContext operation,
            IReadOnlyList<DurableCompositeEndpointIntent> endpoints,
            out string failureCode);
    }

    public enum DurableCompositeDeferredClaimState
    {
        Acquired = 0,
        Pending = 1,
        FailedClosed = 2
    }

    /// <summary>
    /// Optional split-phase claim surface. It exists for an exact pre-existing endpoint that must
    /// first receive its server-issued stable identity from the current native owner. Pending
    /// leaves the fsynced root in Claiming and permits no gameplay mutation. Acquired may be
    /// returned only after the stable identity and exact claim are both authoritatively readable.
    /// Exact retries must resume the same enrollment; conflicting tags, duplicate resolution,
    /// stale evidence, or a changed owner must fail closed.
    /// Because this hook can run before the coordinator's normal authority check, the adapter must
    /// itself prove exact before/prepared-after evidence and current server or direct-owner
    /// authority before it publishes identity or a claim.
    /// </summary>
    public interface IDurableCompositeDeferredClaimEndpointStore
    {
        DurableCompositeDeferredClaimState TryBeginOrResumeAcquireClaim(
            DurableCompositeRollbackContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            DurableCompositeEndpointSnapshot authoritativeSnapshot,
            out string failureCode);
    }

    /// <summary>
    /// Optional exact-current-owner authority surface for domains whose native mutation must be
    /// executed by the connected ZDO owner rather than by a loaded server instance. Implementers
    /// must derive the owner from authoritative endpoint state, bind it to a current direct RPC
    /// session, and revalidate every invocation. Payload sender IDs or routed sender UIDs are not
    /// authority. Standard domains remain server-owned unless they implement this interface.
    /// </summary>
    public interface IDurableCompositeDelegatedAuthorityEndpointStore
    {
        bool IsExactDelegatedMutationAuthorityCurrent(
            DurableCompositeRollbackContext operation,
            long expectedCommitSequence,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode);
    }

    public enum DurableCompositeDeferredApplyState
    {
        Applied = 0,
        Pending = 1,
        FailedClosed = 2
    }

    /// <summary>
    /// Optional split-phase apply surface. Pending means the durable root is already Committing
    /// and has a stable commit sequence, but an exact current-owner command/replication readback
    /// has not completed. The coordinator retains claims and returns NotReady; exact retries
    /// resume the same root. Implementations may never report Applied before exact after-state and
    /// epoch/sequence publication are observable to the server.
    /// </summary>
    public interface IDurableCompositeDeferredEndpointStore
    {
        DurableCompositeDeferredApplyState TryBeginOrResumeApplyAfter(
            DurableCompositeMutationContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            DurableCompositeEndpointSnapshot authoritativeSnapshot,
            out string failureCode);
    }

    public interface IDurableCompositeCompensatingEndpointStore
    {
        bool TryRestoreBefore(
            DurableCompositeRollbackContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            out string failureCode);
    }

    /// <summary>
    /// Optional typed-domain seam for a native/client mutation that occurs only after the durable
    /// root reached Prepared but before the server allocates its commit sequence. The adapter must
    /// prove that the supplied snapshot is the exact requested after state, still carries the
    /// predecessor marker (or the canonical no-predecessor marker), and is safe to either publish
    /// with the server commit marker or compensate back to the exact before state. Generic
    /// container domains do not implement this interface.
    /// </summary>
    public interface IDurableCompositePreparedAfterEndpointStore
    {
        bool IsExactPreparedAfter(
            DurableCompositeRollbackContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode);
    }

    public interface IDurableCompositeOperationCoordinator
    {
        /// <summary>
        /// Durably mints (or exactly replays) the server-owned operation identity before any
        /// consumer-side tagging or endpoint claim. requestHash is the caller's exact durable
        /// idempotency request identity; it is not normalized.
        /// </summary>
        DurableCompositeTokenIssueResult IssueOperationToken(
            RpcPeerIdentity actorIdentity,
            string requestHash);

        /// <summary>
        /// Durably binds one domain-separated idempotency-key hash to the exact request hash.
        /// Reusing the durable key with different request bytes returns ReplayConflict and never
        /// allocates a second token. Both values are canonical lowercase SHA-256 hex.
        /// </summary>
        DurableCompositeTokenIssueResult IssueOperationToken(
            RpcPeerIdentity actorIdentity,
            string durableRequestKeyHash,
            string requestHash);

        DurableCompositeTokenCancellationResult CancelIssuedOperationToken(
            RpcPeerIdentity actorIdentity,
            DurableCompositeOperationToken operationToken,
            string requestHash);

        DurableCompositeOperationResult Prepare(DurableCompositeOperationIntent intent);
        DurableCompositeOperationResult Commit(DurableCompositeOperationReference operation);
        DurableCompositeOperationResult Abort(DurableCompositeOperationReference operation);
        DurableCompositeOperationResult Recover(DurableCompositeOperationReference operation);
        DurableCompositeOperationResult ReadStatus(DurableCompositeOperationReference operation);
        DurableCompositeOperationResult AcknowledgeReconciliation(
            DurableCompositeOperationReference operation,
            string durableAcknowledgementHash);
    }

    public interface IDurableCompositeOperationCoordinatorFactory
        : IDurableCompositeOutstandingOperationSource,
          IDurableCompositeTokenDispositionSource
    {
        /// <summary>
        /// Registers the one exact active adapter for a canonical endpoint domain. Historical
        /// committed roots are replayed through their recorded domain/schema adapter even when a
        /// different owner module asks for recovery. Duplicate or mismatched adapters fail closed.
        /// </summary>
        IDisposable RegisterEndpointDomain(
            ModuleRegistration providerModule,
            DurableCompositeEndpointDomainDescriptor descriptor,
            IDurableCompositeEndpointStore endpointStore);

        IDurableCompositeOperationCoordinator Create(
            ModuleRegistration ownerModule,
            string endpointDomainId);
    }

    public static class DurableCompositeLimits
    {
        /// <summary>Default bound for generic/container composite domains.</summary>
        public const int MaximumStandardEndpoints = 32;
        /// <summary>Hard wire/WAL bound; only an explicitly registered domain may exceed 32.</summary>
        public const int MaximumEndpoints = 64;
        public const int MaximumIntentBytes = 64 * 1024;
        public const int MaximumRootRecordBytes = 128 * 1024;
        public const int MaximumSemanticRevisionBytes = 128;
        public const int MaximumUnresolvedOperations = 256;
        public const int MaximumUnresolvedPerActor = 1;
        public const int MaximumRetainedOperations = 65536;
        public const int MaximumJournalBytes = 128 * 1024 * 1024;
        public const int MaximumOutstandingPerActor = 64;
        public const long MaximumWorldDatabaseBytes = 16L * 1024L * 1024L * 1024L;
        public const int MaximumWorldObjectPayloadBytes = 32 * 1024;
    }

    internal static class CompositeValidation
    {
        internal static RpcPeerIdentity RequireBackendIdentity(RpcPeerIdentity value)
        {
            if (value == null || value.Assurance != RpcIdentityAssurance.BackendAccount)
                throw new ArgumentException(
                    "A transport-bound BackendAccount identity is required.", nameof(value));
            return new RpcPeerIdentity(value.Authority, value.SubjectId, value.Assurance);
        }

        internal static bool SameIdentity(RpcPeerIdentity left, RpcPeerIdentity right) =>
            left != null && right != null &&
            left.Assurance == right.Assurance &&
            string.Equals(left.Authority, right.Authority, StringComparison.Ordinal) &&
            string.Equals(left.SubjectId, right.SubjectId, StringComparison.Ordinal);

        internal static string RequireSha256(string value, string parameterName)
        {
            if (value == null || value.Length != 64)
                throw new ArgumentException("A canonical lowercase SHA-256 is required.", parameterName);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') ||
                      (current >= 'a' && current <= 'f')))
                    throw new ArgumentException(
                        "A canonical lowercase SHA-256 is required.", parameterName);
            }
            return value;
        }

        internal static string RequireText(string value, string parameterName, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Exact text is required.", parameterName);
            if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
                throw new ArgumentException(
                    "Exact text cannot have leading or trailing whitespace.", parameterName);
            try
            {
                var utf8 = new System.Text.UTF8Encoding(false, true);
                if (utf8.GetByteCount(value) > maximumBytes)
                    throw new ArgumentOutOfRangeException(parameterName);
            }
            catch (System.Text.EncoderFallbackException exception)
            {
                throw new ArgumentException("Exact text is not valid UTF-8.", parameterName, exception);
            }
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]))
                    throw new ArgumentException("Exact text contains a control character.", parameterName);
            return value;
        }
    }
}
