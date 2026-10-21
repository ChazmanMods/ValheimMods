using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    /// <summary>
    /// One process-exclusive, world-scoped authoritative catalog. ZDO roots/claims are mirrors;
    /// only a verified primary catalog replacement is a durable authority transition.
    /// </summary>
    internal sealed class FileDurableCompositeJournalStore : IDurableCompositeJournalStore, IDisposable
    {
        private const int CatalogMagic = 0x52444357; // RDCW
        private const int CatalogSchema = 5;
        private const int LegacyCatalogSchema = 4;
        private const int MaximumEnvelopeOverhead = 4096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly string _primaryPath;
        private readonly FileStream _worldLease;
        private bool _disposed;

        internal FileDurableCompositeJournalStore(
            string serverRootDirectory,
            string worldScope)
        {
            if (string.IsNullOrEmpty(serverRootDirectory))
                throw new ArgumentException("A server journal root is required.", nameof(serverRootDirectory));
            WorldScope = RequirePathAtom(worldScope, nameof(worldScope));
            string root = Path.GetFullPath(serverRootDirectory);
            EnsureNoReparseComponents(root, true);
            Directory.CreateDirectory(root);
            EnsureNoReparseComponents(root, false);
            string directory = Path.GetFullPath(Path.Combine(root, WorldScope));
            if (!IsStrictChild(root, directory))
                throw new ArgumentException("The world journal path escapes its server root.", nameof(worldScope));
            EnsureNoReparseComponents(directory, true);
            Directory.CreateDirectory(directory);
            EnsureNoReparseComponents(directory, false);
            _directory = directory;
            _primaryPath = CheckedChild(directory, "runic-composite.catalog");
            string lockPath = CheckedChild(directory, "runic-composite.lock");
            EnsureNoReparseComponents(lockPath, true);
            _worldLease = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            EnsureNoReparseComponents(lockPath, false);
            EnsureNoReparseComponents(_directory, false);

            try
            {
                lock (_sync)
                {
                    bool primaryExists = File.Exists(_primaryPath);
                    // Do not rely on Win32 wildcard semantics here: on Windows, a pattern ending
                    // in ".*" may also match the extensionless primary itself. Inspect exact file
                    // names so only staged/backup siblings trigger forensic refusal.
                    if (Directory.EnumerateFiles(
                            _directory,
                            "*",
                            SearchOption.TopDirectoryOnly).Any(path =>
                                Path.GetFileName(path).StartsWith(
                                    "runic-composite.catalog.",
                                    StringComparison.Ordinal)))
                        throw new InvalidDataException(
                            "Forensic catalog artifacts require operator review.");
                    if (!primaryExists)
                    {
                        PersistLocked(new Catalog(WorldScope));
                    }
                    ReadCatalogLocked();
                }
            }
            catch
            {
                _worldLease.Dispose();
                throw;
            }
        }

        public string WorldScope { get; }

        public DurableCompositeTokenIssueCode IssueOperationToken(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            string actorKey,
            string durableRequestKeyHash,
            string requestHash,
            out DurableCompositeOperationToken token,
            out string failureCode)
        {
            token = null;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || !TryCanonicalActorKey(actorKey, out string exactActor))
                {
                    failureCode = _disposed
                        ? "composite.journal.disposed"
                        : "composite.actor.invalid";
                    return DurableCompositeTokenIssueCode.FailedClosed;
                }
                string owner;
                string ownerVersion;
                string exactRequest;
                try
                {
                    owner = Runic.Foundation.Core.RunicIdentifier.Require(
                        ownerModuleId, nameof(ownerModuleId));
                    ownerVersion = Runic.Foundation.Core.SemanticVersion.Parse(
                        ownerModuleVersion).ToString();
                    if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                        throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
                    durableRequestKeyHash = CompositeValidation.RequireSha256(
                        durableRequestKeyHash, nameof(durableRequestKeyHash));
                    exactRequest = CompositeValidation.RequireSha256(
                        requestHash, nameof(requestHash));
                }
                catch
                {
                    failureCode = "composite.token.request-invalid";
                    return DurableCompositeTokenIssueCode.FailedClosed;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    foreach (TokenLease lease in catalog.TokenLeases.Values)
                    {
                        if (!string.Equals(lease.OwnerModuleId, owner, StringComparison.Ordinal) ||
                            !string.Equals(lease.ActorKey, exactActor, StringComparison.Ordinal) ||
                            !string.Equals(
                                lease.DurableRequestKeyHash,
                                durableRequestKeyHash,
                                StringComparison.Ordinal)) continue;
                        if (!string.Equals(lease.RequestHash, exactRequest, StringComparison.Ordinal))
                        {
                            failureCode = "composite.token.replay-conflict";
                            return DurableCompositeTokenIssueCode.ReplayConflict;
                        }
                        if (string.Equals(
                                lease.OwnerModuleVersion,
                                ownerVersion,
                                StringComparison.Ordinal) &&
                            lease.OwnerProtocolMajor == ownerProtocolMajor)
                        {
                            if (lease.Cancelled)
                            {
                                failureCode = "composite.token.cancelled";
                                return DurableCompositeTokenIssueCode.FailedClosed;
                            }
                            token = lease.Token;
                            failureCode = "composite.token.replay";
                            return DurableCompositeTokenIssueCode.Replay;
                        }
                        failureCode = "composite.token.owner-version-conflict";
                        return DurableCompositeTokenIssueCode.FailedClosed;
                    }
                    if (catalog.TokenLeases.Count >= DurableCompositeLimits.MaximumRetainedOperations)
                    {
                        failureCode = "composite.token.capacity";
                        return DurableCompositeTokenIssueCode.CapacityReached;
                    }
                    if (catalog.TokenLeases.Values.Any(value =>
                            !value.Consumed && !value.Cancelled &&
                            string.Equals(value.ActorKey, exactActor, StringComparison.Ordinal)))
                    {
                        failureCode = "composite.token.actor-busy";
                        return DurableCompositeTokenIssueCode.ActorBusy;
                    }
                    if (catalog.NextAdmissionSequence < catalog.MinimumAcceptedAdmissionSequence ||
                        catalog.NextAdmissionSequence == long.MaxValue)
                    {
                        failureCode = "composite.token.sequence-exhausted";
                        return DurableCompositeTokenIssueCode.FailedClosed;
                    }
                    long sequence = catalog.NextAdmissionSequence;
                    Guid operationId;
                    do { operationId = Guid.NewGuid(); }
                    while (operationId == Guid.Empty ||
                           catalog.TokenLeases.ContainsKey(operationId.ToString("N")));
                    string authenticator = ComputeTokenAuthenticator(
                        catalog, sequence, operationId);
                    token = new DurableCompositeOperationToken(
                        catalog.WorldEpoch, sequence, operationId, authenticator);
                    catalog.TokenLeases.Add(
                        token.OperationIdText,
                        new TokenLease(
                            token,
                            owner,
                            ownerVersion,
                            ownerProtocolMajor,
                            exactActor,
                            durableRequestKeyHash,
                            exactRequest,
                            false,
                            false));
                    catalog.NextAdmissionSequence = sequence + 1;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (verified.NextAdmissionSequence != sequence + 1 ||
                        !verified.TokenLeases.TryGetValue(
                            token.OperationIdText, out TokenLease readback) ||
                        !SameTokenLease(readback, catalog.TokenLeases[token.OperationIdText]))
                        throw new IOException("The issued token did not read back exactly.");
                    token = readback.Token;
                    failureCode = "composite.token.issued";
                    return DurableCompositeTokenIssueCode.Issued;
                }
                catch (InvalidDataException)
                {
                    token = null;
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeTokenIssueCode.FailedClosed;
                }
                catch
                {
                    token = null;
                    failureCode = "composite.token.persist-failed";
                    return DurableCompositeTokenIssueCode.FailedClosed;
                }
            }
        }

        public DurableCompositeTokenCancellationCode CancelIssuedOperationToken(
            string ownerModuleId,
            string actorKey,
            DurableCompositeOperationToken token,
            string requestHash,
            out string failureCode)
        {
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || token == null ||
                    !TryCanonicalActorKey(actorKey, out string exactActor))
                {
                    failureCode = "composite.token.cancel-invalid";
                    return DurableCompositeTokenCancellationCode.FailedClosed;
                }
                string owner;
                string exactRequest;
                try
                {
                    owner = Runic.Foundation.Core.RunicIdentifier.Require(
                        ownerModuleId, nameof(ownerModuleId));
                    exactRequest = CompositeValidation.RequireSha256(
                        requestHash, nameof(requestHash));
                }
                catch
                {
                    failureCode = "composite.token.cancel-invalid";
                    return DurableCompositeTokenCancellationCode.FailedClosed;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!catalog.TokenLeases.TryGetValue(
                            token.OperationIdText, out TokenLease lease))
                    {
                        failureCode = token.AdmissionSequence <
                                catalog.MinimumAcceptedAdmissionSequence
                            ? "composite.token.stale"
                            : "composite.token.not-found";
                        return DurableCompositeTokenCancellationCode.NotFound;
                    }
                    if (!string.Equals(
                            lease.Token.CanonicalValue,
                            token.CanonicalValue,
                            StringComparison.Ordinal) ||
                        !string.Equals(lease.OwnerModuleId, owner, StringComparison.Ordinal) ||
                        !string.Equals(lease.ActorKey, exactActor, StringComparison.Ordinal) ||
                        !string.Equals(lease.RequestHash, exactRequest, StringComparison.Ordinal) ||
                        !ValidTokenAuthenticator(catalog, lease))
                    {
                        failureCode = "composite.token.cancel-conflict";
                        return DurableCompositeTokenCancellationCode.Conflict;
                    }
                    if (lease.Cancelled)
                    {
                        failureCode = "composite.token.cancel-replay";
                        return DurableCompositeTokenCancellationCode.Replay;
                    }
                    if (lease.Consumed || catalog.Entries.ContainsKey(token.OperationIdText))
                    {
                        failureCode = "composite.token.root-exists";
                        return DurableCompositeTokenCancellationCode.Conflict;
                    }
                    lease.Cancelled = true;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (!verified.TokenLeases.TryGetValue(
                            token.OperationIdText, out TokenLease readback) ||
                        !readback.Cancelled || !SameTokenLease(readback, lease))
                        throw new IOException("The token cancellation did not read back exactly.");
                    failureCode = "composite.token.cancelled";
                    return DurableCompositeTokenCancellationCode.Cancelled;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeTokenCancellationCode.FailedClosed;
                }
                catch
                {
                    failureCode = "composite.token.cancel-failed";
                    return DurableCompositeTokenCancellationCode.FailedClosed;
                }
            }
        }

        public DurableCompositeJournalReadState Read(
            string operationId,
            out byte[] exactRecord,
            out string failureCode)
        {
            exactRecord = Array.Empty<byte>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeJournalReadState.Unavailable;
                }
                if (!TryCanonicalOperationId(operationId, out string canonical))
                {
                    failureCode = "composite.operation.invalid";
                    return DurableCompositeJournalReadState.Corrupt;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!catalog.Entries.TryGetValue(canonical, out JournalEntry entry))
                        return DurableCompositeJournalReadState.Absent;
                    exactRecord = (byte[])entry.Record.Clone();
                    return DurableCompositeJournalReadState.Present;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeJournalReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeJournalReadState.Unavailable;
                }
            }
        }

        public DurableCompositeJournalCreateState TryCreateUnresolved(
            string operationId,
            string actorKey,
            byte[] exactRecord,
            int maximumUnresolved,
            int maximumUnresolvedPerActor,
            out byte[] existingRecord,
            out string failureCode)
        {
            existingRecord = Array.Empty<byte>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeJournalCreateState.Failed;
                }
                if (!TryCanonicalOperationId(operationId, out string canonical) ||
                    !TryCanonicalActorKey(actorKey, out string exactActor) ||
                    !ValidateRoot(exactRecord, canonical, exactActor, out CompositeRootRecord root) ||
                    root.Phase != DurableCompositeOperationPhase.Claiming ||
                    maximumUnresolved < 1 ||
                    maximumUnresolved > DurableCompositeLimits.MaximumUnresolvedOperations ||
                    maximumUnresolvedPerActor < 1 ||
                    maximumUnresolvedPerActor > maximumUnresolved)
                {
                    failureCode = "composite.journal.create-invalid";
                    return DurableCompositeJournalCreateState.Failed;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (catalog.Entries.TryGetValue(canonical, out JournalEntry existing))
                    {
                        existingRecord = (byte[])existing.Record.Clone();
                        return DurableCompositeJournalCreateState.Existing;
                    }
                    if (!catalog.TokenLeases.TryGetValue(canonical, out TokenLease lease) ||
                        lease.Consumed || lease.Cancelled ||
                        !string.Equals(
                            lease.Token.CanonicalValue,
                            root.OperationToken.CanonicalValue,
                            StringComparison.Ordinal) ||
                        !string.Equals(lease.OwnerModuleId, root.OwnerModuleId, StringComparison.Ordinal) ||
                        !string.Equals(
                            lease.OwnerModuleVersion,
                            root.OwnerModuleVersion,
                            StringComparison.Ordinal) ||
                        lease.OwnerProtocolMajor != root.OwnerProtocolMajor ||
                        !string.Equals(lease.ActorKey, exactActor, StringComparison.Ordinal) ||
                        !string.Equals(lease.RequestHash, root.RequestHash, StringComparison.Ordinal) ||
                        root.OperationToken.AdmissionSequence <
                            catalog.MinimumAcceptedAdmissionSequence ||
                        !ValidTokenAuthenticator(catalog, lease))
                    {
                        failureCode = "composite.token.not-issued";
                        return DurableCompositeJournalCreateState.Failed;
                    }
                    if (catalog.Entries.Count >= DurableCompositeLimits.MaximumRetainedOperations)
                    {
                        failureCode = "composite.journal.retained-capacity";
                        return DurableCompositeJournalCreateState.CapacityReached;
                    }
                    int unresolved = 0;
                    int actorUnresolved = 0;
                    int actorOutstanding = 0;
                    foreach (JournalEntry candidate in catalog.Entries.Values)
                    {
                        bool sameActor = string.Equals(
                            candidate.ActorKey, exactActor, StringComparison.Ordinal);
                        if (candidate.Unresolved)
                        {
                            unresolved++;
                            if (sameActor) actorUnresolved++;
                        }
                        if (sameActor && DurableCompositeCodec.TryDecode(
                                candidate.Record, out CompositeRootRecord candidateRoot, out _) &&
                            candidateRoot.ReconciliationPending)
                            actorOutstanding++;
                    }
                    if (unresolved >= maximumUnresolved)
                    {
                        failureCode = "composite.journal.unresolved-capacity";
                        return DurableCompositeJournalCreateState.CapacityReached;
                    }
                    if (actorUnresolved >= maximumUnresolvedPerActor)
                    {
                        failureCode = "composite.journal.actor-busy";
                        return DurableCompositeJournalCreateState.ActorBusy;
                    }
                    if (root.ReconciliationRequirement != null &&
                        actorOutstanding >= DurableCompositeLimits.MaximumOutstandingPerActor)
                    {
                        failureCode = "composite.journal.outstanding-capacity";
                        return DurableCompositeJournalCreateState.CapacityReached;
                    }
                    catalog.Entries.Add(
                        canonical,
                        new JournalEntry(canonical, exactActor, true, exactRecord));
                    lease.Consumed = true;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (!verified.Entries.TryGetValue(canonical, out JournalEntry readback) ||
                        !Exact(readback.Record, exactRecord) || !readback.Unresolved ||
                        !verified.TokenLeases.TryGetValue(canonical, out TokenLease verifiedLease) ||
                        !verifiedLease.Consumed)
                        throw new IOException("The created root did not read back exactly.");
                    return DurableCompositeJournalCreateState.Created;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeJournalCreateState.Failed;
                }
                catch
                {
                    failureCode = "composite.journal.create-failed";
                    return DurableCompositeJournalCreateState.Failed;
                }
            }
        }

        public bool TryCompareExchange(
            string operationId,
            byte[] expectedRecord,
            byte[] replacementRecord,
            bool replacementIsTerminal,
            out byte[] observedRecord,
            out string failureCode)
        {
            observedRecord = Array.Empty<byte>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return false;
                }
                if (!TryCanonicalOperationId(operationId, out string canonical))
                {
                    failureCode = "composite.operation.invalid";
                    return false;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!catalog.Entries.TryGetValue(canonical, out JournalEntry entry))
                    {
                        failureCode = "composite.journal.not-found";
                        return false;
                    }
                    observedRecord = (byte[])entry.Record.Clone();
                    if (!Exact(entry.Record, expectedRecord))
                    {
                        failureCode = "composite.journal.cas-conflict";
                        return false;
                    }
                    if (!ValidateReplacement(
                            entry,
                            replacementRecord,
                            replacementIsTerminal,
                            out CompositeRootRecord replacement))
                    {
                        failureCode = "composite.journal.replacement-invalid";
                        return false;
                    }
                    entry.Record = (byte[])replacementRecord.Clone();
                    entry.Unresolved = !replacementIsTerminal;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    JournalEntry readback = verified.Entries[canonical];
                    observedRecord = (byte[])readback.Record.Clone();
                    if (!Exact(readback.Record, replacementRecord) ||
                        readback.Unresolved == replacementIsTerminal)
                        throw new IOException("The replacement root did not read back exactly.");
                    return true;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return false;
                }
                catch
                {
                    failureCode = "composite.journal.cas-failed";
                    return false;
                }
            }
        }

        public bool TryBeginCommit(
            string operationId,
            byte[] expectedPreparedRecord,
            IReadOnlyList<EndpointId> sortedEndpointIds,
            Func<DurableCompositeCommitOrder, byte[]> committingRecordFactory,
            out DurableCompositeCommitOrder commitOrder,
            out byte[] observedRecord,
            out string failureCode)
        {
            commitOrder = null;
            observedRecord = Array.Empty<byte>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || committingRecordFactory == null ||
                    !TryCanonicalOperationId(operationId, out string canonical) ||
                    !CanonicalEndpoints(sortedEndpointIds))
                {
                    failureCode = "composite.commit-order.invalid";
                    return false;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!catalog.Entries.TryGetValue(canonical, out JournalEntry entry))
                    {
                        failureCode = "composite.journal.not-found";
                        return false;
                    }
                    observedRecord = (byte[])entry.Record.Clone();
                    if (!Exact(entry.Record, expectedPreparedRecord))
                    {
                        failureCode = "composite.journal.cas-conflict";
                        return false;
                    }
                    if (!DurableCompositeCodec.TryDecode(
                            entry.Record, out CompositeRootRecord prepared, out _) ||
                        prepared.Phase != DurableCompositeOperationPhase.Prepared ||
                        prepared.Endpoints.Count != sortedEndpointIds.Count)
                    {
                        failureCode = "composite.prepared.invalid";
                        return false;
                    }
                    for (int index = 0; index < sortedEndpointIds.Count; index++)
                        if (prepared.Endpoints[index].StableEndpointId != sortedEndpointIds[index])
                        {
                            failureCode = "composite.prepared.endpoint-mismatch";
                            return false;
                        }
                    if (catalog.NextCommitSequence < 1 ||
                        catalog.NextCommitSequence == long.MaxValue)
                    {
                        failureCode = "composite.commit-order.exhausted";
                        return false;
                    }
                    long sequence = catalog.NextCommitSequence;
                    var predecessors = new List<DurableCompositeEndpointPredecessor>();
                    foreach (EndpointId endpoint in sortedEndpointIds)
                    {
                        if (catalog.EndpointLast.TryGetValue(endpoint.Value, out EndpointWriter last))
                        {
                            if (!string.Equals(
                                    last.DomainId,
                                    prepared.EndpointDomainId,
                                    StringComparison.Ordinal))
                            {
                                failureCode = "composite.endpoint.domain-conflict";
                                return false;
                            }
                            if (last.Sequence > catalog.VerifiedCheckpointSequence)
                                predecessors.Add(new DurableCompositeEndpointPredecessor(
                                    endpoint, last.Sequence, last.OperationId));
                        }
                    }
                    commitOrder = new DurableCompositeCommitOrder(sequence, predecessors);
                    byte[] replacement;
                    try { replacement = committingRecordFactory(commitOrder); }
                    catch
                    {
                        failureCode = "composite.commit-order.factory-failed";
                        commitOrder = null;
                        return false;
                    }
                    if (!ValidateReplacement(entry, replacement, false, out CompositeRootRecord committing) ||
                        committing.Phase != DurableCompositeOperationPhase.Committing ||
                        committing.CommitSequence != sequence ||
                        !SameOrder(committing.Predecessors, predecessors))
                    {
                        failureCode = "composite.committing.invalid";
                        commitOrder = null;
                        return false;
                    }
                    entry.Record = (byte[])replacement.Clone();
                    foreach (EndpointId endpoint in sortedEndpointIds)
                        catalog.EndpointLast[endpoint.Value] = new EndpointWriter(
                            endpoint.Value,
                            prepared.EndpointDomainId,
                            canonical,
                            sequence);
                    catalog.NextCommitSequence = sequence + 1;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    JournalEntry readback = verified.Entries[canonical];
                    observedRecord = (byte[])readback.Record.Clone();
                    if (!Exact(readback.Record, replacement) ||
                        verified.NextCommitSequence != sequence + 1)
                        throw new IOException("The commit order did not read back exactly.");
                    foreach (EndpointId endpoint in sortedEndpointIds)
                    {
                        EndpointWriter writer = verified.EndpointLast[endpoint.Value];
                        if (writer.Sequence != sequence ||
                            !string.Equals(writer.OperationId, canonical, StringComparison.Ordinal))
                            throw new IOException("An endpoint writer did not read back exactly.");
                    }
                    return true;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    commitOrder = null;
                    return false;
                }
                catch
                {
                    failureCode = "composite.commit-order.failed";
                    commitOrder = null;
                    return false;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadCommitHistory(
            out IReadOnlyList<byte[]> exactRecords,
            out long verifiedCheckpointSequence,
            out string failureCode)
        {
            exactRecords = Array.Empty<byte[]>();
            verifiedCheckpointSequence = 0;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    var ordered = new SortedDictionary<long, byte[]>();
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("A root record is corrupt.");
                        if (root.Phase != DurableCompositeOperationPhase.Committing &&
                            root.Phase != DurableCompositeOperationPhase.Committed) continue;
                        if (root.CommitSequence <= catalog.VerifiedCheckpointSequence) continue;
                        if (ordered.ContainsKey(root.CommitSequence))
                            throw new InvalidDataException("Commit sequences are not unique.");
                        ordered.Add(root.CommitSequence, (byte[])entry.Record.Clone());
                    }
                    exactRecords = new ReadOnlyCollection<byte[]>(ordered.Values.ToArray());
                    verifiedCheckpointSequence = catalog.VerifiedCheckpointSequence;
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadOutstanding(
            string actorKey,
            out IReadOnlyList<byte[]> exactRecords,
            out string failureCode)
        {
            exactRecords = Array.Empty<byte[]>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || !TryCanonicalActorKey(actorKey, out string exactActor))
                {
                    failureCode = _disposed
                        ? "composite.journal.disposed"
                        : "composite.actor.invalid";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    var ordered = new SortedDictionary<long, byte[]>();
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        if (!string.Equals(entry.ActorKey, exactActor, StringComparison.Ordinal))
                            continue;
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("An actor root is corrupt.");
                        if (!root.ReconciliationPending &&
                            (root.Phase == DurableCompositeOperationPhase.Committed ||
                             root.Phase == DurableCompositeOperationPhase.Aborted)) continue;
                        long order = root.OperationToken.AdmissionSequence;
                        if (ordered.Count == DurableCompositeLimits.MaximumOutstandingPerActor ||
                            ordered.ContainsKey(order))
                            throw new InvalidDataException("Actor outstanding ledger exceeds its bound.");
                        ordered.Add(order, (byte[])entry.Record.Clone());
                    }
                    exactRecords = new ReadOnlyCollection<byte[]>(ordered.Values.ToArray());
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadIssuedOutstanding(
            string actorKey,
            out IReadOnlyList<DurableCompositeIssuedOperation> operations,
            out string failureCode)
        {
            operations = Array.Empty<DurableCompositeIssuedOperation>();
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || !TryCanonicalActorKey(actorKey, out string exactActor) ||
                    !TryIdentityFromActorKey(exactActor, out var identity))
                {
                    failureCode = _disposed
                        ? "composite.journal.disposed"
                        : "composite.actor.invalid";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    var result = new List<DurableCompositeIssuedOperation>();
                    foreach (TokenLease lease in catalog.TokenLeases.Values.OrderBy(
                        value => value.Token.AdmissionSequence))
                    {
                        if (lease.Consumed || lease.Cancelled ||
                            !string.Equals(
                                lease.ActorKey, exactActor, StringComparison.Ordinal)) continue;
                        if (result.Count == DurableCompositeLimits.MaximumOutstandingPerActor)
                            throw new InvalidDataException(
                                "Issued actor operations exceed their bound.");
                        result.Add(ToIssuedOperation(lease, identity));
                    }
                    operations = new ReadOnlyCollection<DurableCompositeIssuedOperation>(result);
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadOwnerOutstanding(
            string ownerModuleId,
            out IReadOnlyList<byte[]> exactRecords,
            out IReadOnlyList<DurableCompositeIssuedOperation> issuedOperations,
            out string failureCode)
        {
            exactRecords = Array.Empty<byte[]>();
            issuedOperations = Array.Empty<DurableCompositeIssuedOperation>();
            failureCode = string.Empty;
            string owner;
            try { owner = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId)); }
            catch
            {
                failureCode = "composite.owner.invalid";
                return DurableCompositeHistoryReadState.Unavailable;
            }
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    var records = new SortedDictionary<long, byte[]>();
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        // Decode every retained entry before filtering. A corrupt catalog is one
                        // authority boundary and may not be presented as a partial owner view.
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("An owner root is corrupt.");
                        if (!string.Equals(root.OwnerModuleId, owner, StringComparison.Ordinal) ||
                            !root.ReconciliationPending &&
                            (root.Phase == DurableCompositeOperationPhase.Committed ||
                             root.Phase == DurableCompositeOperationPhase.Aborted)) continue;
                        long order = root.OperationToken.AdmissionSequence;
                        if (records.Count == DurableCompositeLimits.MaximumUnresolvedOperations ||
                            records.ContainsKey(order))
                            throw new InvalidDataException(
                                "Owner outstanding roots exceed their global bound.");
                        records.Add(order, (byte[])entry.Record.Clone());
                    }

                    var issued = new List<DurableCompositeIssuedOperation>();
                    foreach (TokenLease lease in catalog.TokenLeases.Values.OrderBy(
                        value => value.Token.AdmissionSequence))
                    {
                        if (lease.Cancelled || lease.Consumed ||
                            !string.Equals(lease.OwnerModuleId, owner, StringComparison.Ordinal))
                            continue;
                        if (!TryIdentityFromActorKey(lease.ActorKey, out var identity) ||
                            !ValidTokenAuthenticator(catalog, lease) ||
                            issued.Count == DurableCompositeLimits.MaximumUnresolvedOperations)
                            throw new InvalidDataException(
                                "An issued owner operation is corrupt or over capacity.");
                        issued.Add(ToIssuedOperation(lease, identity));
                    }
                    if ((long)records.Count + issued.Count >
                        DurableCompositeLimits.MaximumUnresolvedOperations)
                        throw new InvalidDataException(
                            "Owner outstanding operations exceed their global bound.");
                    exactRecords = new ReadOnlyCollection<byte[]>(records.Values.ToArray());
                    issuedOperations =
                        new ReadOnlyCollection<DurableCompositeIssuedOperation>(issued);
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadIssuedOperation(
            string operationId,
            out DurableCompositeIssuedOperation operation,
            out string failureCode)
        {
            operation = null;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || !TryCanonicalOperationId(operationId, out string canonical))
                {
                    failureCode = _disposed
                        ? "composite.journal.disposed"
                        : "composite.operation.invalid";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!catalog.TokenLeases.TryGetValue(canonical, out TokenLease lease) ||
                        lease.Cancelled) return DurableCompositeHistoryReadState.Ready;
                    if (!TryIdentityFromActorKey(lease.ActorKey, out var identity) ||
                        !ValidTokenAuthenticator(catalog, lease))
                        throw new InvalidDataException("An issued operation is corrupt.");
                    operation = ToIssuedOperation(lease, identity);
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeRequestLookupState ReadRequest(
            string ownerModuleId,
            string actorKey,
            string durableRequestKeyHash,
            string requestHash,
            out DurableCompositeIssuedOperation issuedOperation,
            out byte[] exactRoot,
            out string failureCode)
        {
            issuedOperation = null;
            exactRoot = Array.Empty<byte>();
            failureCode = string.Empty;
            string owner;
            string exactActor;
            string exactKey;
            string exactRequest;
            try
            {
                owner = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
                if (!TryCanonicalActorKey(actorKey, out exactActor) ||
                    !TryIdentityFromActorKey(exactActor, out _))
                    throw new ArgumentException();
                exactKey = CompositeValidation.RequireSha256(
                    durableRequestKeyHash, nameof(durableRequestKeyHash));
                exactRequest = CompositeValidation.RequireSha256(
                    requestHash, nameof(requestHash));
            }
            catch
            {
                failureCode = "composite.request-lookup.invalid";
                return DurableCompositeRequestLookupState.Unavailable;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeRequestLookupState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    TokenLease match = null;
                    foreach (TokenLease lease in catalog.TokenLeases.Values)
                    {
                        if (!string.Equals(lease.OwnerModuleId, owner, StringComparison.Ordinal) ||
                            !string.Equals(lease.ActorKey, exactActor, StringComparison.Ordinal) ||
                            !string.Equals(
                                lease.DurableRequestKeyHash,
                                exactKey,
                                StringComparison.Ordinal)) continue;
                        if (match != null)
                            throw new InvalidDataException(
                                "A durable request key has duplicate token leases.");
                        match = lease;
                    }
                    if (match == null)
                    {
                        failureCode = "composite.request-lookup.not-found";
                        return DurableCompositeRequestLookupState.NotFound;
                    }
                    if (!ValidTokenAuthenticator(catalog, match))
                        throw new InvalidDataException("A durable request token is not authentic.");
                    if (!string.Equals(match.RequestHash, exactRequest, StringComparison.Ordinal) ||
                        match.Cancelled)
                    {
                        failureCode = match.Cancelled
                            ? "composite.request-lookup.cancelled"
                            : "composite.request-lookup.replay-conflict";
                        return DurableCompositeRequestLookupState.Conflict;
                    }
                    if (!TryIdentityFromActorKey(exactActor, out var identity))
                        throw new InvalidDataException("A durable request actor is invalid.");
                    issuedOperation = ToIssuedOperation(match, identity);
                    bool hasRoot = catalog.Entries.TryGetValue(
                        match.Token.OperationIdText, out JournalEntry entry);
                    if (match.Consumed != hasRoot)
                        throw new InvalidDataException(
                            "A durable request lease/root binding is incomplete.");
                    if (!hasRoot)
                    {
                        failureCode = "composite.request-lookup.issued";
                        return DurableCompositeRequestLookupState.Issued;
                    }
                    if (!ValidateRoot(
                            entry.Record, match.Token.OperationIdText, exactActor,
                            out CompositeRootRecord root) ||
                        !string.Equals(root.OwnerModuleId, owner, StringComparison.Ordinal) ||
                        !string.Equals(root.RequestHash, exactRequest, StringComparison.Ordinal) ||
                        !string.Equals(
                            root.OperationToken.CanonicalValue,
                            match.Token.CanonicalValue,
                            StringComparison.Ordinal))
                        throw new InvalidDataException("A durable request root is inconsistent.");
                    exactRoot = (byte[])entry.Record.Clone();
                    failureCode = "composite.request-lookup.journaled";
                    return DurableCompositeRequestLookupState.Journaled;
                }
                catch (InvalidDataException)
                {
                    issuedOperation = null;
                    exactRoot = Array.Empty<byte>();
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeRequestLookupState.Corrupt;
                }
                catch
                {
                    issuedOperation = null;
                    exactRoot = Array.Empty<byte>();
                    failureCode = "composite.request-lookup.unavailable";
                    return DurableCompositeRequestLookupState.Unavailable;
                }
            }
        }

        public DurableCompositeTokenDispositionState ReadTokenDisposition(
            string canonicalOperationToken,
            out string failureCode)
        {
            failureCode = string.Empty;
            DurableCompositeOperationToken token;
            try { token = DurableCompositeOperationToken.Parse(canonicalOperationToken); }
            catch
            {
                failureCode = "composite.token-disposition.invalid";
                return DurableCompositeTokenDispositionState.Invalid;
            }
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeTokenDispositionState.Unavailable;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (!string.Equals(
                            token.WorldEpoch, catalog.WorldEpoch, StringComparison.Ordinal) ||
                        token.AdmissionSequence < 1 ||
                        token.AdmissionSequence >= catalog.NextAdmissionSequence)
                    {
                        failureCode = "composite.token-disposition.unknown";
                        return DurableCompositeTokenDispositionState.Unknown;
                    }
                    if (!string.Equals(
                            token.Authenticator,
                            ComputeTokenAuthenticator(
                                catalog, token.AdmissionSequence, token.OperationId),
                            StringComparison.Ordinal))
                    {
                        failureCode = "composite.token-disposition.invalid";
                        return DurableCompositeTokenDispositionState.Invalid;
                    }
                    if (!catalog.TokenLeases.TryGetValue(
                            token.OperationIdText, out TokenLease lease))
                    {
                        if (catalog.Entries.ContainsKey(token.OperationIdText))
                            throw new InvalidDataException(
                                "A root exists without its authenticated token lease.");
                        // A valid token below NextAdmissionSequence can be absent only after an
                        // explicit terminal/cancellation disposition was compacted. The HMAC makes
                        // the sparse gap authoritative; MinimumAcceptedAdmissionSequence remains
                        // the contiguous replay-rejection frontier.
                        failureCode = "composite.token-disposition.retired";
                        return DurableCompositeTokenDispositionState.Retired;
                    }
                    if (!string.Equals(
                            lease.Token.CanonicalValue,
                            token.CanonicalValue,
                            StringComparison.Ordinal) ||
                        !ValidTokenAuthenticator(catalog, lease))
                        throw new InvalidDataException("A retained token lease is invalid.");
                    if (lease.Cancelled)
                    {
                        if (lease.Consumed || catalog.Entries.ContainsKey(token.OperationIdText))
                            throw new InvalidDataException("A cancelled token retained a root.");
                        failureCode = "composite.token-disposition.cancelled";
                        return DurableCompositeTokenDispositionState.Cancelled;
                    }
                    if (!lease.Consumed)
                    {
                        if (catalog.Entries.ContainsKey(token.OperationIdText))
                            throw new InvalidDataException("An unconsumed token retained a root.");
                        failureCode = "composite.token-disposition.active";
                        return DurableCompositeTokenDispositionState.Active;
                    }
                    if (!catalog.Entries.TryGetValue(
                            token.OperationIdText, out JournalEntry entry) ||
                        !DurableCompositeCodec.TryDecode(
                            entry.Record, out CompositeRootRecord root, out _) ||
                        !string.Equals(
                            root.OperationToken.CanonicalValue,
                            token.CanonicalValue,
                            StringComparison.Ordinal))
                        throw new InvalidDataException("A consumed token root is invalid.");
                    switch (root.Phase)
                    {
                        case DurableCompositeOperationPhase.Claiming:
                        case DurableCompositeOperationPhase.Prepared:
                        case DurableCompositeOperationPhase.Committing:
                        case DurableCompositeOperationPhase.Aborting:
                            failureCode = "composite.token-disposition.active";
                            return DurableCompositeTokenDispositionState.Active;
                        case DurableCompositeOperationPhase.Committed:
                            if (root.ReconciliationPending)
                            {
                                failureCode =
                                    "composite.token-disposition.reconciliation-hold";
                                return DurableCompositeTokenDispositionState.
                                    CommittedReconciliationHold;
                            }
                            failureCode = root.ReconciliationRequirement == null
                                ? "composite.token-disposition.retired"
                                : "composite.token-disposition.acknowledged";
                            return root.ReconciliationRequirement == null
                                ? DurableCompositeTokenDispositionState.Retired
                                : DurableCompositeTokenDispositionState.Acknowledged;
                        case DurableCompositeOperationPhase.Aborted:
                            if (root.ReconciliationPending)
                            {
                                failureCode =
                                    "composite.token-disposition.abort-reconciliation-hold";
                                return DurableCompositeTokenDispositionState.
                                    AbortedReconciliationHold;
                            }
                            failureCode = "composite.token-disposition.aborted";
                            return DurableCompositeTokenDispositionState.Aborted;
                        default:
                            throw new InvalidDataException("A token root phase is invalid.");
                    }
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeTokenDispositionState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.token-disposition.unavailable";
                    return DurableCompositeTokenDispositionState.Unavailable;
                }
            }
        }

        public bool TryStageWorldCheckpoint(
            long throughCommitSequence,
            string exactWorldDatabasePath,
            string exactSaveCycleId,
            string originProcessId,
            long markerUserId,
            uint markerObjectId,
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode)
        {
            stage = null;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || throughCommitSequence < 1 ||
                    string.IsNullOrEmpty(exactWorldDatabasePath) ||
                    !Path.IsPathRooted(exactWorldDatabasePath))
                {
                    failureCode = "composite.checkpoint.stage-invalid";
                    return false;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    var requested = new DurableCompositeWorldCheckpointStage(
                        catalog.WorldEpoch,
                        throughCommitSequence,
                        exactWorldDatabasePath,
                        exactSaveCycleId,
                        originProcessId,
                        markerUserId,
                        markerObjectId);
                    if (catalog.PendingCheckpoint != null)
                    {
                        if (catalog.CheckpointMarkerUserId != markerUserId ||
                            catalog.CheckpointMarkerObjectId != markerObjectId ||
                            !string.Equals(
                                catalog.PendingCheckpoint.DatabasePath,
                                requested.DatabasePath,
                                PathComparison))
                        {
                            failureCode = "composite.checkpoint.stage-conflict";
                            return false;
                        }
                        stage = catalog.PendingCheckpoint;
                        failureCode = "composite.checkpoint.stage-replay";
                        return true;
                    }
                    if (throughCommitSequence <= catalog.VerifiedCheckpointSequence ||
                        throughCommitSequence >= catalog.NextCommitSequence)
                    {
                        failureCode = "composite.checkpoint.stage-sequence";
                        return false;
                    }
                    if (catalog.CheckpointMarkerUserId != 0 &&
                        (catalog.CheckpointMarkerUserId != markerUserId ||
                         catalog.CheckpointMarkerObjectId != markerObjectId))
                    {
                        failureCode = "composite.checkpoint.marker-conflict";
                        return false;
                    }
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("A root record is corrupt.");
                        if (root.CommitSequence > 0 &&
                            root.CommitSequence <= throughCommitSequence &&
                            root.Phase != DurableCompositeOperationPhase.Committed)
                        {
                            failureCode = "composite.checkpoint.unreconciled";
                            return false;
                        }
                    }
                    catalog.CheckpointMarkerUserId = markerUserId;
                    catalog.CheckpointMarkerObjectId = markerObjectId;
                    catalog.PendingCheckpoint = requested;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (!SameCheckpointStage(verified.PendingCheckpoint, requested) ||
                        verified.CheckpointMarkerUserId != markerUserId ||
                        verified.CheckpointMarkerObjectId != markerObjectId)
                        throw new IOException("The checkpoint stage did not read back exactly.");
                    stage = verified.PendingCheckpoint;
                    failureCode = "composite.checkpoint.staged";
                    return true;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return false;
                }
                catch
                {
                    failureCode = "composite.checkpoint.stage-failed";
                    return false;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadStagedWorldCheckpoint(
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode)
        {
            stage = null;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    stage = ReadCatalogLocked().PendingCheckpoint;
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public DurableCompositeHistoryReadState ReadCheckpointStreamIdentity(
            out string worldEpoch,
            out string failureCode)
        {
            worldEpoch = string.Empty;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed)
                {
                    failureCode = "composite.journal.disposed";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
                try
                {
                    worldEpoch = ReadCatalogLocked().WorldEpoch;
                    return DurableCompositeHistoryReadState.Ready;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeHistoryReadState.Corrupt;
                }
                catch
                {
                    failureCode = "composite.journal.unavailable";
                    return DurableCompositeHistoryReadState.Unavailable;
                }
            }
        }

        public bool TryRebindStagedWorldCheckpointMarker(
            long expectedMarkerUserId,
            uint expectedMarkerObjectId,
            long currentMarkerUserId,
            uint currentMarkerObjectId,
            out DurableCompositeWorldCheckpointStage stage,
            out string failureCode)
        {
            stage = null;
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || expectedMarkerUserId == 0 || expectedMarkerObjectId == 0 ||
                    currentMarkerUserId == 0 || currentMarkerObjectId == 0)
                {
                    failureCode = "composite.checkpoint.rebind-invalid";
                    return false;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    DurableCompositeWorldCheckpointStage pending = catalog.PendingCheckpoint;
                    if (pending == null ||
                        pending.MarkerUserId != expectedMarkerUserId ||
                        pending.MarkerObjectId != expectedMarkerObjectId ||
                        catalog.CheckpointMarkerUserId != expectedMarkerUserId ||
                        catalog.CheckpointMarkerObjectId != expectedMarkerObjectId)
                    {
                        failureCode = "composite.checkpoint.rebind-stale";
                        return false;
                    }
                    if (expectedMarkerUserId == currentMarkerUserId &&
                        expectedMarkerObjectId == currentMarkerObjectId)
                    {
                        stage = pending;
                        failureCode = "composite.checkpoint.rebind-current";
                        return true;
                    }
                    DurableCompositeWorldCheckpointStage rebound = pending.WithMarkerIdentity(
                        currentMarkerUserId, currentMarkerObjectId);
                    catalog.CheckpointMarkerUserId = currentMarkerUserId;
                    catalog.CheckpointMarkerObjectId = currentMarkerObjectId;
                    catalog.PendingCheckpoint = rebound;
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (!SameCheckpointStage(verified.PendingCheckpoint, rebound) ||
                        verified.CheckpointMarkerUserId != currentMarkerUserId ||
                        verified.CheckpointMarkerObjectId != currentMarkerObjectId)
                        throw new IOException("The rebound checkpoint marker did not read back exactly.");
                    stage = verified.PendingCheckpoint;
                    failureCode = "composite.checkpoint.rebound";
                    return true;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return false;
                }
                catch
                {
                    failureCode = "composite.checkpoint.rebind-failed";
                    return false;
                }
            }
        }

        public DurableCompositeCheckpointState TryAdvanceVerifiedWorldCheckpoint(
            long throughCommitSequence,
            string exactWorldDatabasePath,
            string exactSaveCycleId,
            out string failureCode)
        {
            failureCode = string.Empty;
            lock (_sync)
            {
                if (_disposed || throughCommitSequence < 0 ||
                    string.IsNullOrEmpty(exactWorldDatabasePath) ||
                    !Path.IsPathRooted(exactWorldDatabasePath) ||
                    !TryCanonicalSaveCycle(exactSaveCycleId))
                {
                    failureCode = "composite.checkpoint.invalid";
                    return DurableCompositeCheckpointState.Rejected;
                }
                try
                {
                    Catalog catalog = ReadCatalogLocked();
                    if (catalog.PendingCheckpoint == null ||
                        catalog.PendingCheckpoint.ThroughCommitSequence != throughCommitSequence ||
                        !string.Equals(
                            catalog.PendingCheckpoint.DatabasePath,
                            Path.GetFullPath(exactWorldDatabasePath),
                            PathComparison) ||
                        !string.Equals(
                            catalog.PendingCheckpoint.SaveCycleId,
                            exactSaveCycleId,
                            StringComparison.Ordinal))
                    {
                        failureCode = "composite.checkpoint.stage-mismatch";
                        return DurableCompositeCheckpointState.Rejected;
                    }
                    if (throughCommitSequence <= catalog.VerifiedCheckpointSequence)
                        return DurableCompositeCheckpointState.Unchanged;
                    if (throughCommitSequence >= catalog.NextCommitSequence)
                    {
                        failureCode = "composite.checkpoint.future-sequence";
                        return DurableCompositeCheckpointState.Rejected;
                    }
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("A root record is corrupt.");
                        if (root.CommitSequence > 0 &&
                            root.CommitSequence <= throughCommitSequence &&
                            root.Phase != DurableCompositeOperationPhase.Committed)
                        {
                            failureCode = "composite.checkpoint.unreconciled";
                            return DurableCompositeCheckpointState.Rejected;
                        }
                    }
                    if (!catalog.PendingCheckpoint.HasFirstGenerationProof)
                    {
                        if (!TryHashFile(
                                Path.GetFullPath(exactWorldDatabasePath),
                                out long primaryLength,
                                out string primaryHash))
                        {
                            failureCode = "composite.checkpoint.primary-unreadable";
                            return DurableCompositeCheckpointState.Failed;
                        }
                        catalog.PendingCheckpoint =
                            catalog.PendingCheckpoint.WithFirstGeneration(
                                primaryLength, primaryHash);
                        PersistLocked(catalog);
                        Catalog firstGeneration = ReadCatalogLocked();
                        if (!SameCheckpointStage(
                                firstGeneration.PendingCheckpoint,
                                catalog.PendingCheckpoint))
                            throw new IOException(
                                "The first checkpoint generation did not read back exactly.");
                        failureCode = "composite.checkpoint.first-generation-recorded";
                        return DurableCompositeCheckpointState.Unchanged;
                    }
                    string fallback = Path.GetFullPath(exactWorldDatabasePath + ".old");
                    if (!TryHashFile(fallback, out long fallbackLength, out string fallbackHash) ||
                        fallbackLength != catalog.PendingCheckpoint.FirstGenerationLength ||
                        !string.Equals(
                            fallbackHash,
                            catalog.PendingCheckpoint.FirstGenerationSha256,
                            StringComparison.Ordinal))
                    {
                        failureCode = "composite.checkpoint.fallback-generation-mismatch";
                        return DurableCompositeCheckpointState.Rejected;
                    }
                    catalog.VerifiedCheckpointSequence = throughCommitSequence;
                    catalog.CheckpointDatabasePath = Path.GetFullPath(exactWorldDatabasePath);
                    catalog.CheckpointSaveCycleId = exactSaveCycleId;
                    catalog.PendingCheckpoint = null;
                    var removeOperations = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JournalEntry entry in catalog.Entries.Values)
                    {
                        if (!DurableCompositeCodec.TryDecode(
                                entry.Record, out CompositeRootRecord root, out _))
                            throw new InvalidDataException("A root record is corrupt.");
                        if (root.Phase == DurableCompositeOperationPhase.Committed &&
                            root.CommitSequence > 0 &&
                            root.CommitSequence <= throughCommitSequence)
                        {
                            if (root.ReconciliationPending)
                            {
                                // The exact intent is the reconnect manifest. It remains durable
                                // until an exact owner acknowledgement; checkpointing never strips
                                // data needed by a fresh client to reconcile.
                            }
                            else removeOperations.Add(entry.OperationId);
                        }
                        else if (root.Phase == DurableCompositeOperationPhase.Aborted &&
                                 !root.ReconciliationPending)
                            removeOperations.Add(entry.OperationId);
                    }
                    foreach (TokenLease lease in catalog.TokenLeases.Values)
                        if (lease.Cancelled)
                            removeOperations.Add(lease.Token.OperationIdText);
                    foreach (string operation in removeOperations)
                    {
                        catalog.Entries.Remove(operation);
                        catalog.TokenLeases.Remove(operation);
                    }
                    catalog.MinimumAcceptedAdmissionSequence =
                        catalog.TokenLeases.Count == 0
                            ? catalog.NextAdmissionSequence
                            : catalog.TokenLeases.Values.Min(
                                value => value.Token.AdmissionSequence);
                    PersistLocked(catalog);
                    Catalog verified = ReadCatalogLocked();
                    if (verified.VerifiedCheckpointSequence != throughCommitSequence ||
                        !string.Equals(
                            verified.CheckpointDatabasePath,
                            catalog.CheckpointDatabasePath,
                            PathComparison) ||
                        !string.Equals(
                            verified.CheckpointSaveCycleId,
                            exactSaveCycleId,
                            StringComparison.Ordinal) ||
                        verified.MinimumAcceptedAdmissionSequence !=
                            catalog.MinimumAcceptedAdmissionSequence)
                        throw new IOException("The checkpoint did not read back exactly.");
                    return DurableCompositeCheckpointState.Advanced;
                }
                catch (InvalidDataException)
                {
                    failureCode = "composite.journal.corrupt";
                    return DurableCompositeCheckpointState.Failed;
                }
                catch
                {
                    failureCode = "composite.checkpoint.failed";
                    return DurableCompositeCheckpointState.Failed;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _worldLease.Dispose();
            }
        }

        private Catalog ReadCatalogLocked()
        {
            EnsureNoReparseComponents(_directory, false);
            EnsureNoReparseComponents(_primaryPath, false);
            byte[] encoded;
            using (var stream = new FileStream(
                _primaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                4096,
                FileOptions.SequentialScan))
            {
                if (stream.Length < 40 || stream.Length > DurableCompositeLimits.MaximumJournalBytes)
                    throw new InvalidDataException("Catalog size is invalid.");
                encoded = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < encoded.Length)
                {
                    int read = stream.Read(encoded, offset, encoded.Length - offset);
                    if (read <= 0) throw new EndOfStreamException();
                    offset += read;
                }
            }
            return DecodeCatalog(encoded);
        }

        internal void RewritePrimaryAsLegacySchema4ForMigrationTest()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileDurableCompositeJournalStore));
                Catalog catalog = ReadCatalogLocked();
                if (catalog.TokenLeases.Values.Any(lease => !string.Equals(
                        lease.DurableRequestKeyHash,
                        lease.RequestHash,
                        StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "Only legacy single-hash leases can be encoded as schema 4.");
                PersistLocked(catalog, LegacyCatalogSchema);
            }
        }

        private void PersistLocked(Catalog catalog) => PersistLocked(catalog, CatalogSchema);

        private void PersistLocked(Catalog catalog, int schema)
        {
            byte[] encoded = EncodeCatalog(catalog, schema);
            EnsureNoReparseComponents(_directory, false);
            EnsureNoReparseComponents(_primaryPath, true);
            string temporary = CheckedChild(_directory, "runic-composite.catalog.new");
            EnsureNoReparseComponents(temporary, true);
            if (File.Exists(temporary) || Directory.Exists(temporary))
                throw new InvalidDataException(
                    "A forensic staged catalog already exists.");
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(encoded, 0, encoded.Length);
                stream.Flush(true);
            }
            EnsureNoReparseComponents(temporary, false);
            EnsureNoReparseComponents(_directory, false);
            EnsureNoReparseComponents(_primaryPath, true);
            if (File.Exists(_primaryPath))
                File.Replace(temporary, _primaryPath, null, true);
            else
                File.Move(temporary, _primaryPath);
            EnsureNoReparseComponents(_directory, false);
            EnsureNoReparseComponents(_primaryPath, false);
            byte[] readback;
            using (var stream = new FileStream(
                _primaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                4096,
                FileOptions.SequentialScan))
            {
                if (stream.Length != encoded.Length)
                    throw new IOException("Catalog replacement length changed.");
                readback = new byte[encoded.Length];
                int offset = 0;
                while (offset < readback.Length)
                {
                    int read = stream.Read(readback, offset, readback.Length - offset);
                    if (read <= 0) throw new EndOfStreamException();
                    offset += read;
                }
            }
            if (!Exact(encoded, readback))
                throw new IOException("Catalog replacement did not read back exactly.");
            DecodeCatalog(readback);
        }

        private byte[] EncodeCatalog(Catalog catalog, int schema)
        {
            if (schema != CatalogSchema && schema != LegacyCatalogSchema)
                throw new ArgumentOutOfRangeException(nameof(schema));
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, StrictUtf8, true))
            {
                writer.Write(CatalogMagic);
                writer.Write(schema);
                WriteText(writer, catalog.WorldScope, 160, false);
                WriteText(writer, catalog.WorldEpoch, 32, false);
                WriteBytes(writer, catalog.TokenSecret, 32);
                writer.Write(catalog.NextAdmissionSequence);
                writer.Write(catalog.MinimumAcceptedAdmissionSequence);
                writer.Write(catalog.NextCommitSequence);
                writer.Write(catalog.VerifiedCheckpointSequence);
                WriteText(writer, catalog.CheckpointDatabasePath, 4096, true);
                WriteText(writer, catalog.CheckpointSaveCycleId, 128, true);
                writer.Write(catalog.CheckpointMarkerUserId);
                writer.Write(catalog.CheckpointMarkerObjectId);
                writer.Write(catalog.PendingCheckpoint != null);
                if (catalog.PendingCheckpoint != null)
                {
                    writer.Write(catalog.PendingCheckpoint.ThroughCommitSequence);
                    WriteText(writer, catalog.PendingCheckpoint.DatabasePath, 4096, false);
                    WriteText(writer, catalog.PendingCheckpoint.SaveCycleId, 32, false);
                    WriteText(writer, catalog.PendingCheckpoint.OriginProcessId, 32, false);
                    writer.Write(catalog.PendingCheckpoint.MarkerUserId);
                    writer.Write(catalog.PendingCheckpoint.MarkerObjectId);
                    writer.Write(catalog.PendingCheckpoint.FirstGenerationLength);
                    WriteText(
                        writer,
                        catalog.PendingCheckpoint.FirstGenerationSha256,
                        64,
                        true);
                }
                writer.Write(catalog.TokenLeases.Count);
                foreach (TokenLease lease in catalog.TokenLeases.Values.OrderBy(
                    value => value.Token.AdmissionSequence))
                {
                    WriteText(writer, lease.Token.CanonicalValue, 192, false);
                    WriteText(writer, lease.OwnerModuleId, 160, false);
                    WriteText(writer, lease.OwnerModuleVersion, 64, false);
                    writer.Write(lease.OwnerProtocolMajor);
                    WriteText(writer, lease.ActorKey, 1400, false);
                    if (schema >= CatalogSchema)
                        WriteText(writer, lease.DurableRequestKeyHash, 64, false);
                    WriteText(writer, lease.RequestHash, 64, false);
                    writer.Write(lease.Consumed);
                    writer.Write(lease.Cancelled);
                }
                writer.Write(catalog.Entries.Count);
                foreach (JournalEntry entry in catalog.Entries.Values.OrderBy(
                    value => value.OperationId, StringComparer.Ordinal))
                {
                    WriteText(writer, entry.OperationId, 32, false);
                    WriteText(writer, entry.ActorKey, 1400, false);
                    writer.Write(entry.Unresolved);
                    WriteBytes(writer, entry.Record, DurableCompositeLimits.MaximumRootRecordBytes);
                }
                writer.Write(catalog.EndpointLast.Count);
                foreach (EndpointWriter endpoint in catalog.EndpointLast.Values.OrderBy(
                    value => value.EndpointId, StringComparer.Ordinal))
                {
                    WriteText(writer, endpoint.EndpointId, 160, false);
                    WriteText(writer, endpoint.DomainId, 160, false);
                    WriteText(writer, endpoint.OperationId, 32, false);
                    writer.Write(endpoint.Sequence);
                }
                writer.Flush();
                byte[] bodyBytes = body.ToArray();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(bodyBytes);
                    using (var result = new MemoryStream())
                    using (var envelope = new BinaryWriter(result, StrictUtf8, true))
                    {
                        envelope.Write(bodyBytes.Length);
                        envelope.Write(bodyBytes);
                        envelope.Write(digest);
                        envelope.Flush();
                        byte[] encoded = result.ToArray();
                        if (encoded.Length > DurableCompositeLimits.MaximumJournalBytes)
                            throw new IOException("The authoritative journal reached its byte bound.");
                        return encoded;
                    }
                }
            }
        }

        private Catalog DecodeCatalog(byte[] encoded)
        {
            using (var stream = new MemoryStream(encoded, false))
            using (var reader = new BinaryReader(stream, StrictUtf8, true))
            {
                int bodyLength = reader.ReadInt32();
                if (bodyLength < 16 || bodyLength >
                    DurableCompositeLimits.MaximumJournalBytes - MaximumEnvelopeOverhead)
                    throw new InvalidDataException("Catalog body length is invalid.");
                byte[] body = ReadExact(reader, bodyLength);
                byte[] expectedDigest = ReadExact(reader, 32);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Catalog has trailing bytes.");
                using (SHA256 sha = SHA256.Create())
                    if (!Exact(sha.ComputeHash(body), expectedDigest))
                        throw new InvalidDataException("Catalog checksum does not match.");

                using (var bodyStream = new MemoryStream(body, false))
                using (var bodyReader = new BinaryReader(bodyStream, StrictUtf8, true))
                {
                    int magic = bodyReader.ReadInt32();
                    int schema = bodyReader.ReadInt32();
                    if (magic != CatalogMagic ||
                        schema != CatalogSchema && schema != LegacyCatalogSchema)
                        throw new InvalidDataException("Catalog schema is invalid.");
                    string world = ReadText(bodyReader, 160, false);
                    if (!string.Equals(world, WorldScope, StringComparison.Ordinal))
                        throw new InvalidDataException("Catalog world scope does not match.");
                    var catalog = new Catalog(world)
                    {
                        WorldEpoch = ReadText(bodyReader, 32, false),
                        TokenSecret = ReadBytes(bodyReader, 32, false),
                        NextAdmissionSequence = bodyReader.ReadInt64(),
                        MinimumAcceptedAdmissionSequence = bodyReader.ReadInt64(),
                        NextCommitSequence = bodyReader.ReadInt64(),
                        VerifiedCheckpointSequence = bodyReader.ReadInt64(),
                        CheckpointDatabasePath = ReadText(bodyReader, 4096, true),
                        CheckpointSaveCycleId = ReadText(bodyReader, 128, true),
                        CheckpointMarkerUserId = bodyReader.ReadInt64(),
                        CheckpointMarkerObjectId = bodyReader.ReadUInt32()
                    };
                    if (!Guid.TryParseExact(catalog.WorldEpoch, "N", out Guid epoch) ||
                        epoch == Guid.Empty ||
                        !string.Equals(
                            epoch.ToString("N"), catalog.WorldEpoch, StringComparison.Ordinal) ||
                        catalog.TokenSecret.Length != 32 ||
                        catalog.NextAdmissionSequence < 1 ||
                        catalog.MinimumAcceptedAdmissionSequence < 1 ||
                        catalog.MinimumAcceptedAdmissionSequence > catalog.NextAdmissionSequence ||
                        catalog.NextCommitSequence < 1 ||
                        catalog.VerifiedCheckpointSequence < 0 ||
                        catalog.VerifiedCheckpointSequence >= catalog.NextCommitSequence)
                        throw new InvalidDataException("Catalog sequence bounds are invalid.");
                    if ((catalog.CheckpointMarkerUserId == 0) !=
                            (catalog.CheckpointMarkerObjectId == 0))
                        throw new InvalidDataException("Catalog marker identity is partial.");
                    if (bodyReader.ReadBoolean())
                    {
                        if (catalog.CheckpointMarkerUserId == 0)
                            throw new InvalidDataException("A checkpoint stage has no marker.");
                        catalog.PendingCheckpoint = new DurableCompositeWorldCheckpointStage(
                            catalog.WorldEpoch,
                            bodyReader.ReadInt64(),
                            ReadText(bodyReader, 4096, false),
                            ReadText(bodyReader, 32, false),
                            ReadText(bodyReader, 32, false),
                            bodyReader.ReadInt64(),
                            bodyReader.ReadUInt32(),
                            bodyReader.ReadInt64(),
                            ReadText(bodyReader, 64, true));
                        if (catalog.PendingCheckpoint.ThroughCommitSequence <=
                                catalog.VerifiedCheckpointSequence ||
                            catalog.PendingCheckpoint.ThroughCommitSequence >=
                                catalog.NextCommitSequence ||
                            catalog.PendingCheckpoint.MarkerUserId !=
                                catalog.CheckpointMarkerUserId ||
                            catalog.PendingCheckpoint.MarkerObjectId !=
                                catalog.CheckpointMarkerObjectId)
                            throw new InvalidDataException("Catalog checkpoint stage is invalid.");
                    }
                    int leaseCount = bodyReader.ReadInt32();
                    if (leaseCount < 0 ||
                        leaseCount > DurableCompositeLimits.MaximumRetainedOperations)
                        throw new InvalidDataException("Catalog token count is invalid.");
                    long previousAdmission = 0;
                    for (int index = 0; index < leaseCount; index++)
                    {
                        DurableCompositeOperationToken token =
                            DurableCompositeOperationToken.Parse(
                                ReadText(bodyReader, 192, false));
                        string owner = ReadText(bodyReader, 160, false);
                        string ownerVersion = ReadText(bodyReader, 64, false);
                        int ownerProtocol = bodyReader.ReadInt32();
                        string actor = ReadText(bodyReader, 1400, false);
                        string durableRequestKey = schema >= CatalogSchema
                            ? ReadText(bodyReader, 64, false)
                            : string.Empty;
                        string request = ReadText(bodyReader, 64, false);
                        if (schema == LegacyCatalogSchema) durableRequestKey = request;
                        bool consumed = bodyReader.ReadBoolean();
                        bool cancelled = bodyReader.ReadBoolean();
                        if (consumed && cancelled)
                            throw new InvalidDataException("A token cannot be consumed and cancelled.");
                        if (token.AdmissionSequence <= previousAdmission ||
                            token.AdmissionSequence < catalog.MinimumAcceptedAdmissionSequence ||
                            token.AdmissionSequence >= catalog.NextAdmissionSequence ||
                            !string.Equals(
                                token.WorldEpoch, catalog.WorldEpoch, StringComparison.Ordinal) ||
                             !TryCanonicalActorKey(actor, out _) ||
                             !string.Equals(
                                 CompositeValidation.RequireSha256(
                                     durableRequestKey, nameof(durableRequestKey)),
                                 durableRequestKey,
                                 StringComparison.Ordinal) ||
                             !string.Equals(
                                 CompositeValidation.RequireSha256(request, nameof(request)),
                                request,
                                StringComparison.Ordinal))
                            throw new InvalidDataException("Catalog token lease is invalid.");
                        previousAdmission = token.AdmissionSequence;
                        var lease = new TokenLease(
                            token,
                            owner,
                            ownerVersion,
                            ownerProtocol,
                            actor,
                            durableRequestKey,
                            request,
                            consumed,
                            cancelled);
                        if (!ValidTokenAuthenticator(catalog, lease) ||
                            !catalog.TokenLeases.TryAdd(token.OperationIdText, lease))
                            throw new InvalidDataException("Catalog token lease is not authentic.");
                    }
                    int entryCount = bodyReader.ReadInt32();
                    if (entryCount < 0 ||
                        entryCount > DurableCompositeLimits.MaximumRetainedOperations)
                        throw new InvalidDataException("Catalog entry count is invalid.");
                    string previousOperation = string.Empty;
                    for (int index = 0; index < entryCount; index++)
                    {
                        string operation = ReadText(bodyReader, 32, false);
                        if (!TryCanonicalOperationId(operation, out string canonical) ||
                            index > 0 && string.CompareOrdinal(previousOperation, canonical) >= 0)
                            throw new InvalidDataException("Catalog operations are not canonical.");
                        previousOperation = canonical;
                        string actor = ReadText(bodyReader, 1400, false);
                        if (!TryCanonicalActorKey(actor, out _))
                            throw new InvalidDataException("Catalog actor key is invalid.");
                        bool unresolved = bodyReader.ReadBoolean();
                        byte[] record = ReadBytes(
                            bodyReader, DurableCompositeLimits.MaximumRootRecordBytes, false);
                        if (!ValidateRoot(record, canonical, actor, out CompositeRootRecord root) ||
                            unresolved == (root.Phase == DurableCompositeOperationPhase.Committed ||
                                           root.Phase == DurableCompositeOperationPhase.Aborted))
                            throw new InvalidDataException("Catalog root is inconsistent.");
                        catalog.Entries.Add(
                            canonical,
                            new JournalEntry(canonical, actor, unresolved, record));
                    }
                    int endpointCount = bodyReader.ReadInt32();
                    if (endpointCount < 0 ||
                        endpointCount > DurableCompositeLimits.MaximumRetainedOperations *
                                        DurableCompositeLimits.MaximumEndpoints)
                        throw new InvalidDataException("Catalog endpoint index is invalid.");
                    string previousEndpoint = string.Empty;
                    for (int index = 0; index < endpointCount; index++)
                    {
                        string endpoint = ReadText(bodyReader, 160, false);
                        if (index > 0 && string.CompareOrdinal(previousEndpoint, endpoint) >= 0)
                            throw new InvalidDataException("Endpoint index is not canonical.");
                        previousEndpoint = endpoint;
                        string domain = ReadText(bodyReader, 160, false);
                        string operation = ReadText(bodyReader, 32, false);
                        long sequence = bodyReader.ReadInt64();
                        if (!TryCanonicalOperationId(operation, out _) ||
                            sequence < 1 || sequence >= catalog.NextCommitSequence)
                            throw new InvalidDataException("Endpoint writer is invalid.");
                        catalog.EndpointLast.Add(
                            endpoint,
                            new EndpointWriter(endpoint, domain, operation, sequence));
                    }
                    if (bodyStream.Position != bodyStream.Length)
                        throw new InvalidDataException("Catalog body has trailing bytes.");
                    ValidateCatalogIndexes(catalog);
                    return catalog;
                }
            }
        }

        private static void ValidateCatalogIndexes(Catalog catalog)
        {
            var sequences = new HashSet<long>();
            var durableRequestKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TokenLease lease in catalog.TokenLeases.Values)
            {
                string compositeKey = lease.OwnerModuleId + "\n" + lease.ActorKey + "\n" +
                                      lease.DurableRequestKeyHash;
                if (!durableRequestKeys.Add(compositeKey))
                    throw new InvalidDataException(
                        "A durable request key has more than one retained token lease.");
            }
            foreach (JournalEntry entry in catalog.Entries.Values)
            {
                if (!DurableCompositeCodec.TryDecode(
                        entry.Record, out CompositeRootRecord root, out _))
                    throw new InvalidDataException("A catalog root is corrupt.");
                if (root.CommitSequence > 0 && !sequences.Add(root.CommitSequence))
                    throw new InvalidDataException("Commit sequence collision.");
                if (!catalog.TokenLeases.TryGetValue(
                        root.OperationIdText, out TokenLease lease) ||
                    !lease.Consumed || lease.Cancelled ||
                    !string.Equals(
                        lease.Token.CanonicalValue,
                        root.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !string.Equals(lease.OwnerModuleId, root.OwnerModuleId, StringComparison.Ordinal) ||
                    !string.Equals(
                        lease.OwnerModuleVersion,
                        root.OwnerModuleVersion,
                        StringComparison.Ordinal) ||
                    lease.OwnerProtocolMajor != root.OwnerProtocolMajor ||
                    !string.Equals(lease.ActorKey, entry.ActorKey, StringComparison.Ordinal) ||
                    !string.Equals(lease.RequestHash, root.RequestHash, StringComparison.Ordinal))
                    throw new InvalidDataException("A catalog root is not bound to its issued token.");
            }
            foreach (EndpointWriter writer in catalog.EndpointLast.Values)
            {
                if (writer.Sequence <= catalog.VerifiedCheckpointSequence) continue;
                if (!catalog.Entries.TryGetValue(writer.OperationId, out JournalEntry entry) ||
                    !DurableCompositeCodec.TryDecode(
                        entry.Record, out CompositeRootRecord root, out _) ||
                    root.CommitSequence != writer.Sequence ||
                    !string.Equals(root.EndpointDomainId, writer.DomainId, StringComparison.Ordinal) ||
                    !root.Endpoints.Any(endpoint =>
                        string.Equals(
                            endpoint.StableEndpointId.Value,
                            writer.EndpointId,
                            StringComparison.Ordinal)))
                    throw new InvalidDataException("Endpoint writer index is not backed by its root.");
            }
        }

        private static bool ValidateReplacement(
            JournalEntry entry,
            byte[] replacementRecord,
            bool replacementIsTerminal,
            out CompositeRootRecord replacement)
        {
            replacement = null;
            if (!DurableCompositeCodec.TryDecode(entry.Record, out CompositeRootRecord current, out _) ||
                !DurableCompositeCodec.TryDecode(replacementRecord, out replacement, out _) ||
                !SameIdentity(current, replacement) ||
                !AllowedTransition(current.Phase, replacement.Phase) ||
                replacementIsTerminal !=
                    (replacement.Phase == DurableCompositeOperationPhase.Committed ||
                     replacement.Phase == DurableCompositeOperationPhase.Aborted))
                return false;
            if (current.CommitSequence > 0 &&
                (replacement.CommitSequence != current.CommitSequence ||
                 !SameOrder(current.Predecessors, replacement.Predecessors)))
                return false;
            return true;
        }

        private static bool AllowedTransition(
            DurableCompositeOperationPhase current,
            DurableCompositeOperationPhase replacement) =>
            current == DurableCompositeOperationPhase.Claiming &&
                (replacement == DurableCompositeOperationPhase.Prepared ||
                 replacement == DurableCompositeOperationPhase.Aborting) ||
            current == DurableCompositeOperationPhase.Prepared &&
                (replacement == DurableCompositeOperationPhase.Committing ||
                 replacement == DurableCompositeOperationPhase.Aborting) ||
            current == DurableCompositeOperationPhase.Committing &&
                 replacement == DurableCompositeOperationPhase.Committed ||
            current == DurableCompositeOperationPhase.Aborting &&
                 replacement == DurableCompositeOperationPhase.Aborted ||
            current == DurableCompositeOperationPhase.Committed &&
                 replacement == DurableCompositeOperationPhase.Committed ||
            current == DurableCompositeOperationPhase.Aborted &&
                 replacement == DurableCompositeOperationPhase.Aborted;

        private static bool SameIdentity(CompositeRootRecord left, CompositeRootRecord right) =>
            string.Equals(left.OwnerModuleId, right.OwnerModuleId, StringComparison.Ordinal) &&
            string.Equals(
                left.OwnerModuleVersion,
                right.OwnerModuleVersion,
                StringComparison.Ordinal) &&
            left.OwnerProtocolMajor == right.OwnerProtocolMajor &&
            string.Equals(left.OwnerModuleVersion, right.OwnerModuleVersion, StringComparison.Ordinal) &&
            left.OwnerProtocolMajor == right.OwnerProtocolMajor &&
            string.Equals(left.EndpointDomainId, right.EndpointDomainId, StringComparison.Ordinal) &&
            left.EndpointDomainSchemaVersion == right.EndpointDomainSchemaVersion &&
            string.Equals(
                left.OperationToken.CanonicalValue,
                right.OperationToken.CanonicalValue,
                StringComparison.Ordinal) &&
            CompositeValidation.SameIdentity(left.ActorIdentity, right.ActorIdentity) &&
            string.Equals(left.RequestHash, right.RequestHash, StringComparison.Ordinal) &&
            string.Equals(left.IntentHash, right.IntentHash, StringComparison.Ordinal);

        private static bool SameTokenLease(TokenLease left, TokenLease right) =>
            left != null && right != null &&
            string.Equals(
                left.Token.CanonicalValue,
                right.Token.CanonicalValue,
                StringComparison.Ordinal) &&
            string.Equals(left.OwnerModuleId, right.OwnerModuleId, StringComparison.Ordinal) &&
            string.Equals(left.ActorKey, right.ActorKey, StringComparison.Ordinal) &&
            string.Equals(
                left.DurableRequestKeyHash,
                right.DurableRequestKeyHash,
                StringComparison.Ordinal) &&
            string.Equals(left.RequestHash, right.RequestHash, StringComparison.Ordinal) &&
            left.Consumed == right.Consumed &&
            left.Cancelled == right.Cancelled;

        private static bool SameCheckpointStage(
            DurableCompositeWorldCheckpointStage left,
            DurableCompositeWorldCheckpointStage right) =>
            left != null && right != null &&
            string.Equals(left.WorldEpoch, right.WorldEpoch, StringComparison.Ordinal) &&
            left.ThroughCommitSequence == right.ThroughCommitSequence &&
            string.Equals(left.DatabasePath, right.DatabasePath, PathComparison) &&
            string.Equals(left.SaveCycleId, right.SaveCycleId, StringComparison.Ordinal) &&
            string.Equals(left.OriginProcessId, right.OriginProcessId, StringComparison.Ordinal) &&
            left.MarkerUserId == right.MarkerUserId &&
            left.MarkerObjectId == right.MarkerObjectId &&
            left.FirstGenerationLength == right.FirstGenerationLength &&
            string.Equals(
                left.FirstGenerationSha256,
                right.FirstGenerationSha256,
                StringComparison.Ordinal);

        private static bool ValidTokenAuthenticator(Catalog catalog, TokenLease lease) =>
            string.Equals(
                lease.Token.Authenticator,
                ComputeTokenAuthenticator(
                    catalog, lease.Token.AdmissionSequence, lease.Token.OperationId),
                StringComparison.Ordinal);

        private static string ComputeTokenAuthenticator(
            Catalog catalog,
            long admissionSequence,
            Guid operationId)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                WriteText(writer, "runic.transactions.composite.token.v2", 64, false);
                WriteText(writer, catalog.WorldScope, 160, false);
                WriteText(writer, catalog.WorldEpoch, 32, false);
                writer.Write(admissionSequence);
                writer.Write(operationId.ToByteArray());
                writer.Flush();
                using (var hmac = new HMACSHA256(catalog.TokenSecret))
                    return Hex(hmac.ComputeHash(stream.ToArray()));
            }
        }

        private static string Hex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                result.Append(bytes[index].ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static bool SameOrder(
            IReadOnlyList<DurableCompositeEndpointPredecessor> left,
            IReadOnlyList<DurableCompositeEndpointPredecessor> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (left[index].EndpointId != right[index].EndpointId ||
                    left[index].CommitSequence != right[index].CommitSequence ||
                    !string.Equals(
                        left[index].OperationId,
                        right[index].OperationId,
                        StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool ValidateRoot(
            byte[] record,
            string operationId,
            string actorKey,
            out CompositeRootRecord root) =>
            DurableCompositeCodec.TryDecode(record, out root, out _) &&
            string.Equals(root.OperationIdText, operationId, StringComparison.Ordinal) &&
            string.Equals(root.ActorIdentity.CanonicalKey, actorKey, StringComparison.Ordinal);

        private static bool CanonicalEndpoints(IReadOnlyList<EndpointId> endpoints)
        {
            if (endpoints == null || endpoints.Count < 1 ||
                endpoints.Count > DurableCompositeLimits.MaximumEndpoints) return false;
            for (int index = 0; index < endpoints.Count; index++)
                if (!endpoints[index].IsValid ||
                    index > 0 && endpoints[index - 1].CompareTo(endpoints[index]) >= 0)
                    return false;
            return true;
        }

        private static bool TryCanonicalOperationId(string value, out string canonical)
        {
            canonical = string.Empty;
            if (!Guid.TryParseExact(value, "N", out Guid parsed) || parsed == Guid.Empty)
                return false;
            canonical = parsed.ToString("N");
            return string.Equals(value, canonical, StringComparison.Ordinal);
        }

        private static bool TryCanonicalActorKey(string value, out string exact)
        {
            exact = value ?? string.Empty;
            if (exact.Length < 3 || exact.Length > 1400 ||
                char.IsWhiteSpace(exact[0]) || char.IsWhiteSpace(exact[exact.Length - 1]))
                return false;
            try { StrictUtf8.GetByteCount(exact); }
            catch { return false; }
            for (int index = 0; index < exact.Length; index++)
                if (char.IsControl(exact[index])) return false;
            return true;
        }

        private static bool TryIdentityFromActorKey(
            string actorKey,
            out Runic.Foundation.Persistence.RpcPeerIdentity identity)
        {
            identity = null;
            try
            {
                int separator = actorKey.IndexOf(':');
                if (separator < 1 || separator == actorKey.Length - 1) return false;
                string authority = actorKey.Substring(0, separator);
                string escaped = actorKey.Substring(separator + 1);
                string subject = Uri.UnescapeDataString(escaped);
                var parsed = new Runic.Foundation.Persistence.RpcPeerIdentity(
                    authority,
                    subject,
                    Runic.Foundation.Persistence.RpcIdentityAssurance.BackendAccount);
                if (!string.Equals(parsed.CanonicalKey, actorKey, StringComparison.Ordinal))
                    return false;
                identity = parsed;
                return true;
            }
            catch { return false; }
        }

        private static DurableCompositeIssuedOperation ToIssuedOperation(
            TokenLease lease,
            Runic.Foundation.Persistence.RpcPeerIdentity identity) =>
            new DurableCompositeIssuedOperation(
                lease.OwnerModuleId,
                lease.OwnerModuleVersion,
                lease.OwnerProtocolMajor,
                lease.Token,
                identity,
                lease.DurableRequestKeyHash,
                lease.RequestHash);

        private static string RequirePathAtom(string value, string parameterName)
        {
            string exact = CompositeValidation.RequireText(value, parameterName, 160);
            for (int index = 0; index < exact.Length; index++)
            {
                char current = exact[index];
                bool valid = current >= 'a' && current <= 'z' ||
                             current >= '0' && current <= '9' ||
                             current == '.' || current == '-' || current == '_';
                if (!valid) throw new ArgumentException(
                    "A canonical lowercase path atom is required.", parameterName);
            }
            return exact;
        }

        private static string CheckedChild(string parent, string name)
        {
            string child = Path.GetFullPath(Path.Combine(parent, name));
            if (!IsStrictChild(parent, child))
                throw new InvalidOperationException("A journal path escaped its world directory.");
            return child;
        }

        private static bool IsStrictChild(string parent, string child)
        {
            string exactParent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return child.StartsWith(exactParent, PathComparison);
        }

        private static void EnsureNoReparseComponents(string path, bool allowMissingLeaf)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
                throw new InvalidDataException("A journal path has no filesystem root.");
            string relative = full.Substring(root.Length);
            string current = root;
            string[] components = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < components.Length; index++)
            {
                current = Path.Combine(current, components[index]);
                bool exists = Directory.Exists(current) || File.Exists(current);
                if (!exists)
                {
                    if (!allowMissingLeaf && index == components.Length - 1)
                        throw new FileNotFoundException(
                            "A required journal path does not exist.", current);
                    continue;
                }
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Journal paths cannot traverse a reparse point.");
            }
        }

        private static StringComparison PathComparison =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static bool TryCanonicalSaveCycle(string value)
        {
            if (!Guid.TryParseExact(value, "N", out Guid parsed) || parsed == Guid.Empty)
                return false;
            return string.Equals(value, parsed.ToString("N"), StringComparison.Ordinal);
        }

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool TryHashFile(
            string exactPath,
            out long length,
            out string sha256)
        {
            length = 0;
            sha256 = string.Empty;
            try
            {
                string path = Path.GetFullPath(exactPath);
                EnsureNoReparseComponents(path, false);
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.SequentialScan))
                using (SHA256 hash = SHA256.Create())
                {
                    if (stream.Length < 1 ||
                        stream.Length > DurableCompositeLimits.MaximumWorldDatabaseBytes)
                        return false;
                    length = stream.Length;
                    sha256 = Hex(hash.ComputeHash(stream));
                    bool exact = stream.Position == stream.Length;
                    EnsureNoReparseComponents(path, false);
                    return exact;
                }
            }
            catch
            {
                length = 0;
                sha256 = string.Empty;
                return false;
            }
        }

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes, bool allowEmpty)
        {
            string exact = value ?? string.Empty;
            if (!allowEmpty || exact.Length != 0)
                CompositeValidation.RequireText(exact, nameof(value), maximumBytes);
            byte[] bytes = StrictUtf8.GetBytes(exact);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes, bool allowEmpty)
        {
            int length = reader.ReadInt32();
            if (length < (allowEmpty ? 0 : 1) || length > maximumBytes)
                throw new InvalidDataException("Catalog text length is invalid.");
            string result = StrictUtf8.GetString(ReadExact(reader, length));
            if (result.Length != 0)
                CompositeValidation.RequireText(result, nameof(result), maximumBytes);
            return result;
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes, int maximum)
        {
            if (bytes == null || bytes.Length < 1 || bytes.Length > maximum)
                throw new InvalidDataException("Catalog byte length is invalid.");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader, int maximum, bool allowEmpty)
        {
            int length = reader.ReadInt32();
            if (length < (allowEmpty ? 0 : 1) || length > maximum)
                throw new InvalidDataException("Catalog byte length is invalid.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new EndOfStreamException();
            return result;
        }

        private sealed class Catalog
        {
            internal Catalog(string worldScope)
            {
                WorldScope = worldScope;
                WorldEpoch = Guid.NewGuid().ToString("N");
                TokenSecret = new byte[32];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    random.GetBytes(TokenSecret);
                TokenLeases = new Dictionary<string, TokenLease>(StringComparer.Ordinal);
                Entries = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
                EndpointLast = new Dictionary<string, EndpointWriter>(StringComparer.Ordinal);
                NextAdmissionSequence = 1;
                MinimumAcceptedAdmissionSequence = 1;
                NextCommitSequence = 1;
                CheckpointDatabasePath = string.Empty;
                CheckpointSaveCycleId = string.Empty;
            }

            internal string WorldScope { get; }
            internal string WorldEpoch { get; set; }
            internal byte[] TokenSecret { get; set; }
            internal long NextAdmissionSequence { get; set; }
            internal long MinimumAcceptedAdmissionSequence { get; set; }
            internal long NextCommitSequence { get; set; }
            internal long VerifiedCheckpointSequence { get; set; }
            internal string CheckpointDatabasePath { get; set; }
            internal string CheckpointSaveCycleId { get; set; }
            internal long CheckpointMarkerUserId { get; set; }
            internal uint CheckpointMarkerObjectId { get; set; }
            internal DurableCompositeWorldCheckpointStage PendingCheckpoint { get; set; }
            internal Dictionary<string, TokenLease> TokenLeases { get; }
            internal Dictionary<string, JournalEntry> Entries { get; }
            internal Dictionary<string, EndpointWriter> EndpointLast { get; }
        }

        private sealed class TokenLease
        {
            internal TokenLease(
                DurableCompositeOperationToken token,
                string ownerModuleId,
                string ownerModuleVersion,
                int ownerProtocolMajor,
                string actorKey,
                string durableRequestKeyHash,
                string requestHash,
                bool consumed,
                bool cancelled)
            {
                Token = token ?? throw new ArgumentNullException(nameof(token));
                OwnerModuleId = Runic.Foundation.Core.RunicIdentifier.Require(
                    ownerModuleId, nameof(ownerModuleId));
                OwnerModuleVersion = Runic.Foundation.Core.SemanticVersion.Parse(
                    ownerModuleVersion).ToString();
                if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                    throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
                OwnerProtocolMajor = ownerProtocolMajor;
                ActorKey = actorKey;
                DurableRequestKeyHash = CompositeValidation.RequireSha256(
                    durableRequestKeyHash, nameof(durableRequestKeyHash));
                RequestHash = CompositeValidation.RequireSha256(
                    requestHash, nameof(requestHash));
                Consumed = consumed;
                Cancelled = cancelled;
            }

            internal DurableCompositeOperationToken Token { get; }
            internal string OwnerModuleId { get; }
            internal string OwnerModuleVersion { get; }
            internal int OwnerProtocolMajor { get; }
            internal string ActorKey { get; }
            internal string DurableRequestKeyHash { get; }
            internal string RequestHash { get; }
            internal bool Consumed { get; set; }
            internal bool Cancelled { get; set; }
        }

        private sealed class JournalEntry
        {
            internal JournalEntry(string operationId, string actorKey, bool unresolved, byte[] record)
            {
                OperationId = operationId;
                ActorKey = actorKey;
                Unresolved = unresolved;
                Record = (byte[])record.Clone();
            }

            internal string OperationId { get; }
            internal string ActorKey { get; }
            internal bool Unresolved { get; set; }
            internal byte[] Record { get; set; }
        }

        private sealed class EndpointWriter
        {
            internal EndpointWriter(
                string endpointId,
                string domainId,
                string operationId,
                long sequence)
            {
                EndpointId = endpointId;
                DomainId = domainId;
                OperationId = operationId;
                Sequence = sequence;
            }

            internal string EndpointId { get; }
            internal string DomainId { get; }
            internal string OperationId { get; }
            internal long Sequence { get; }
        }
    }
}
