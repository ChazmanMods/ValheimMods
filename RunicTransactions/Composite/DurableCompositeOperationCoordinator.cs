using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using Runic.Foundation.Core;
using RunicTransactions.Coordination;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    public sealed class DurableCompositeOperationCoordinatorFactory :
        IDurableCompositeOperationCoordinatorFactory,
        IDisposable
    {
        private readonly object _sync = new object();
        private readonly RunicRegistry _registry;
        private readonly Dictionary<string, DomainRegistration> _domains =
            new Dictionary<string, DomainRegistration>(StringComparer.Ordinal);
        private IDurableCompositeJournalStore _journal;
        private readonly bool _useValheimWorldJournal;
        private bool _disposed;

        public event EventHandler<DurableCompositeOutstandingChangedEventArgs> OutstandingChanged;

        public DurableCompositeOperationCoordinatorFactory()
            : this(RunicRegistry.Shared, null, true)
        {
        }

        internal DurableCompositeOperationCoordinatorFactory(
            RunicRegistry registry,
            IDurableCompositeJournalStore journal)
            : this(registry, journal, false)
        {
        }

        private DurableCompositeOperationCoordinatorFactory(
            RunicRegistry registry,
            IDurableCompositeJournalStore journal,
            bool useValheimWorldJournal)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _journal = journal;
            _useValheimWorldJournal = useValheimWorldJournal;
        }

        public IDisposable RegisterEndpointDomain(
            ModuleRegistration providerModule,
            DurableCompositeEndpointDomainDescriptor descriptor,
            IDurableCompositeEndpointStore endpointStore)
        {
            if (providerModule == null) throw new ArgumentNullException(nameof(providerModule));
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (endpointStore == null) throw new ArgumentNullException(nameof(endpointStore));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_registry.IsModuleRegistrationActive(providerModule))
                    throw new InvalidOperationException(
                        "The endpoint-domain provider lease is not active in this registry.");
                if (_domains.TryGetValue(descriptor.DomainId, out DomainRegistration existing) &&
                    existing.IsActive)
                    throw new InvalidOperationException(
                        "The endpoint domain already has an active adapter.");
                var registration = new DomainRegistration(
                    this, providerModule, descriptor, endpointStore);
                _domains[descriptor.DomainId] = registration;
                return registration;
            }
        }

        public IDurableCompositeOperationCoordinator Create(
            ModuleRegistration ownerModule,
            string endpointDomainId)
        {
            if (ownerModule == null) throw new ArgumentNullException(nameof(ownerModule));
            string domain = RunicIdentifier.Require(
                endpointDomainId, nameof(endpointDomainId));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_registry.IsModuleRegistrationActive(ownerModule))
                    throw new InvalidOperationException(
                        "The composite-operation owner lease is not active in this registry.");
                return new DurableCompositeOperationCoordinator(
                    this, _registry, ownerModule, domain);
            }
        }

        public DurableCompositeOutstandingQueryResult QueryOutstanding(
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity)
        {
            Runic.Foundation.Persistence.RpcPeerIdentity actor;
            try { actor = CompositeValidation.RequireBackendIdentity(actorIdentity); }
            catch
            {
                return new DurableCompositeOutstandingQueryResult(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    Array.Empty<DurableCompositeOutstandingOperation>(),
                    "composite.actor.invalid");
            }
            if (!TryGetJournal(out IDurableCompositeJournalStore journal))
                return new DurableCompositeOutstandingQueryResult(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    Array.Empty<DurableCompositeOutstandingOperation>(),
                    "composite.journal.not-ready");
            try
            {
                DurableCompositeHistoryReadState state = journal.ReadOutstanding(
                    actor.CanonicalKey,
                    out IReadOnlyList<byte[]> records,
                    out string reason);
                if (state != DurableCompositeHistoryReadState.Ready)
                    return new DurableCompositeOutstandingQueryResult(
                        state == DurableCompositeHistoryReadState.Corrupt
                            ? DurableCompositeOutstandingQueryState.Corrupt
                            : DurableCompositeOutstandingQueryState.Unavailable,
                        Array.Empty<DurableCompositeOutstandingOperation>(),
                        SafeReason(reason, "composite.outstanding.unavailable"));
                DurableCompositeHistoryReadState issuedState =
                    journal.ReadIssuedOutstanding(
                        actor.CanonicalKey,
                        out IReadOnlyList<DurableCompositeIssuedOperation> issued,
                        out string issuedReason);
                if (issuedState != DurableCompositeHistoryReadState.Ready)
                    return new DurableCompositeOutstandingQueryResult(
                        issuedState == DurableCompositeHistoryReadState.Corrupt
                            ? DurableCompositeOutstandingQueryState.Corrupt
                            : DurableCompositeOutstandingQueryState.Unavailable,
                        Array.Empty<DurableCompositeOutstandingOperation>(),
                        Array.Empty<DurableCompositeIssuedOperation>(),
                        Array.Empty<DurableCompositeJournaledOutstandingOperation>(),
                        SafeReason(
                            issuedReason,
                            "composite.outstanding-issued.unavailable"));
                var operations = new List<DurableCompositeOutstandingOperation>(records.Count);
                var journaled =
                    new List<DurableCompositeJournaledOutstandingOperation>(records.Count);
                foreach (byte[] record in records)
                {
                    if (!DurableCompositeCodec.TryDecode(
                            record, out CompositeRootRecord root, out _) ||
                        !CompositeValidation.SameIdentity(root.ActorIdentity, actor))
                        return new DurableCompositeOutstandingQueryResult(
                            DurableCompositeOutstandingQueryState.Corrupt,
                            Array.Empty<DurableCompositeOutstandingOperation>(),
                            "composite.outstanding.corrupt");
                    if (root.ReconciliationPending)
                        operations.Add(new DurableCompositeOutstandingOperation(
                            root.OwnerModuleId,
                            root.OwnerModuleVersion,
                            root.OwnerProtocolMajor,
                            root.EndpointDomainId,
                            root.OperationToken,
                            root.ActorIdentity,
                            root.IntentHash,
                            root.CommitSequence,
                            root.ReceiptHash,
                            root.ReconciliationRequirement,
                            root.Phase));
                    else if (root.Phase != DurableCompositeOperationPhase.Committed &&
                             root.Phase != DurableCompositeOperationPhase.Aborted)
                        journaled.Add(
                            new DurableCompositeJournaledOutstandingOperation(
                                root.OwnerModuleId,
                                root.OwnerModuleVersion,
                                root.OwnerProtocolMajor,
                                root.EndpointDomainId,
                                root.OperationToken,
                                root.ActorIdentity,
                                root.RequestHash,
                                root.Phase));
                    else return new DurableCompositeOutstandingQueryResult(
                        DurableCompositeOutstandingQueryState.Corrupt,
                        Array.Empty<DurableCompositeOutstandingOperation>(),
                        "composite.outstanding.terminal-unexpected");
                }
                return new DurableCompositeOutstandingQueryResult(
                    DurableCompositeOutstandingQueryState.Ready,
                    operations,
                    issued,
                    journaled,
                    "composite.outstanding.ready");
            }
            catch
            {
                return new DurableCompositeOutstandingQueryResult(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    Array.Empty<DurableCompositeOutstandingOperation>(),
                    "composite.outstanding.exception");
            }
        }

        public DurableCompositeOutstandingReadResult ReadOutstandingOperation(
            ModuleRegistration ownerModule,
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            string canonicalOperationToken)
        {
            try
            {
                if (!IsOwnerActive(ownerModule)) return OutstandingRead(
                    DurableCompositeOutstandingReadState.Conflict,
                    "composite.outstanding.owner-inactive");
                var actor = CompositeValidation.RequireBackendIdentity(actorIdentity);
                DurableCompositeOperationToken token =
                    DurableCompositeOperationToken.Parse(canonicalOperationToken);
                if (!TryGetJournal(out IDurableCompositeJournalStore journal))
                    return OutstandingRead(
                        DurableCompositeOutstandingReadState.Unavailable,
                        "composite.outstanding.journal-unavailable");
                DurableCompositeHistoryReadState issuedState = journal.ReadIssuedOperation(
                    token.OperationIdText,
                    out DurableCompositeIssuedOperation issued,
                    out string issuedReason);
                if (issuedState != DurableCompositeHistoryReadState.Ready)
                    return OutstandingRead(
                        issuedState == DurableCompositeHistoryReadState.Corrupt
                            ? DurableCompositeOutstandingReadState.Corrupt
                            : DurableCompositeOutstandingReadState.Unavailable,
                        SafeReason(
                            issuedReason,
                            "composite.outstanding-issued.unavailable"));
                if (issued == null)
                    return OutstandingRead(
                        DurableCompositeOutstandingReadState.NotFound,
                        "composite.outstanding.not-found");
                if (!string.Equals(
                        issued.OperationToken.CanonicalValue,
                        token.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        issued.OwnerModuleId,
                        ownerModule.Descriptor.ModuleId,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(issued.ActorIdentity, actor))
                    return OutstandingRead(
                        DurableCompositeOutstandingReadState.Conflict,
                        "composite.outstanding.identity-conflict");
                DurableCompositeJournalReadState rootState = journal.Read(
                    token.OperationIdText,
                    out byte[] exactRoot,
                    out string rootReason);
                if (rootState == DurableCompositeJournalReadState.Absent)
                    return new DurableCompositeOutstandingReadResult(
                        DurableCompositeOutstandingReadState.Issued,
                        "composite.outstanding.issued",
                        new DurableCompositeOutstandingOperationDetail(
                            token,
                            issued.RequestHash,
                            actor,
                            null,
                            null,
                            string.Empty,
                            Array.Empty<byte>(),
                            null));
                if (rootState != DurableCompositeJournalReadState.Present ||
                    !DurableCompositeCodec.TryDecode(
                        exactRoot, out CompositeRootRecord root, out _) ||
                    !string.Equals(
                        root.OperationToken.CanonicalValue,
                        token.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !string.Equals(root.OwnerModuleId, issued.OwnerModuleId, StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(root.ActorIdentity, actor))
                    return OutstandingRead(
                        rootState == DurableCompositeJournalReadState.Corrupt
                            ? DurableCompositeOutstandingReadState.Corrupt
                            : DurableCompositeOutstandingReadState.Unavailable,
                        SafeReason(rootReason, "composite.outstanding.root-corrupt"));
                DurableCompositeOperationReference reference = root.ToReference(ownerModule);
                return new DurableCompositeOutstandingReadResult(
                    DurableCompositeOutstandingReadState.Journaled,
                    "composite.outstanding.journaled",
                    new DurableCompositeOutstandingOperationDetail(
                        token,
                        root.RequestHash,
                        actor,
                        reference,
                        root.ToSnapshot(ownerModule),
                        root.EndpointDomainId,
                        root.ExactIntentUnsafe,
                        root.ReconciliationRequirement));
            }
            catch (FormatException)
            {
                return OutstandingRead(
                    DurableCompositeOutstandingReadState.Conflict,
                    "composite.outstanding.token-invalid");
            }
            catch
            {
                return OutstandingRead(
                    DurableCompositeOutstandingReadState.Unavailable,
                    "composite.outstanding.exception");
            }
        }

        public DurableCompositeRequestLookupResult LookupRequest(
            ModuleRegistration ownerModule,
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            string durableRequestKeyHash,
            string requestHash)
        {
            string exactKey = string.Empty;
            string exactRequest = string.Empty;
            try
            {
                if (!IsOwnerActive(ownerModule))
                    return RequestLookup(
                        DurableCompositeRequestLookupState.Conflict,
                        "composite.request-lookup.owner-inactive",
                        string.Empty,
                        string.Empty);
                var actor = CompositeValidation.RequireBackendIdentity(actorIdentity);
                exactKey = CompositeValidation.RequireSha256(
                    durableRequestKeyHash, nameof(durableRequestKeyHash));
                exactRequest = CompositeValidation.RequireSha256(
                    requestHash, nameof(requestHash));
                if (!TryGetJournal(out IDurableCompositeJournalStore journal))
                    return RequestLookup(
                        DurableCompositeRequestLookupState.Unavailable,
                        "composite.request-lookup.journal-unavailable",
                        exactKey,
                        exactRequest);
                DurableCompositeRequestLookupState state = journal.ReadRequest(
                    ownerModule.Descriptor.ModuleId,
                    actor.CanonicalKey,
                    exactKey,
                    exactRequest,
                    out DurableCompositeIssuedOperation issued,
                    out byte[] exactRoot,
                    out string reason);
                if (state != DurableCompositeRequestLookupState.Issued &&
                    state != DurableCompositeRequestLookupState.Journaled)
                    return RequestLookup(
                        state,
                        SafeReason(reason, "composite.request-lookup.unavailable"),
                        exactKey,
                        exactRequest);
                if (issued == null ||
                    !string.Equals(
                        issued.OwnerModuleId,
                        ownerModule.Descriptor.ModuleId,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(issued.ActorIdentity, actor) ||
                    !string.Equals(
                        issued.DurableRequestKeyHash, exactKey, StringComparison.Ordinal) ||
                    !string.Equals(issued.RequestHash, exactRequest, StringComparison.Ordinal))
                    return RequestLookup(
                        DurableCompositeRequestLookupState.Corrupt,
                        "composite.request-lookup.issued-corrupt",
                        exactKey,
                        exactRequest);

                DurableCompositeOutstandingOperationDetail detail;
                if (state == DurableCompositeRequestLookupState.Issued)
                {
                    if (exactRoot != null && exactRoot.Length != 0)
                        return RequestLookup(
                            DurableCompositeRequestLookupState.Corrupt,
                            "composite.request-lookup.root-unexpected",
                            exactKey,
                            exactRequest);
                    detail = new DurableCompositeOutstandingOperationDetail(
                        issued.OperationToken,
                        issued.RequestHash,
                        actor,
                        null,
                        null,
                        string.Empty,
                        Array.Empty<byte>(),
                        null);
                }
                else
                {
                    if (exactRoot == null ||
                        !DurableCompositeCodec.TryDecode(
                            exactRoot, out CompositeRootRecord root, out _) ||
                        !string.Equals(
                            root.OperationToken.CanonicalValue,
                            issued.OperationToken.CanonicalValue,
                            StringComparison.Ordinal) ||
                        !string.Equals(root.RequestHash, exactRequest, StringComparison.Ordinal) ||
                        !string.Equals(
                            root.OwnerModuleId,
                            ownerModule.Descriptor.ModuleId,
                            StringComparison.Ordinal) ||
                        !CompositeValidation.SameIdentity(root.ActorIdentity, actor))
                        return RequestLookup(
                            DurableCompositeRequestLookupState.Corrupt,
                            "composite.request-lookup.root-corrupt",
                            exactKey,
                            exactRequest);
                    detail = new DurableCompositeOutstandingOperationDetail(
                        issued.OperationToken,
                        root.RequestHash,
                        actor,
                        root.ToReference(ownerModule),
                        root.ToSnapshot(ownerModule),
                        root.EndpointDomainId,
                        root.ExactIntentUnsafe,
                        root.ReconciliationRequirement);
                }
                return new DurableCompositeRequestLookupResult(
                    state,
                    SafeReason(reason, state == DurableCompositeRequestLookupState.Issued
                        ? "composite.request-lookup.issued"
                        : "composite.request-lookup.journaled"),
                    exactKey,
                    exactRequest,
                    detail);
            }
            catch
            {
                return RequestLookup(
                    DurableCompositeRequestLookupState.Unavailable,
                    "composite.request-lookup.exception",
                    string.Empty,
                    string.Empty);
            }
        }

        private static DurableCompositeRequestLookupResult RequestLookup(
            DurableCompositeRequestLookupState state,
            string reason,
            string durableRequestKeyHash,
            string requestHash) => new DurableCompositeRequestLookupResult(
                state,
                reason,
                durableRequestKeyHash,
                requestHash,
                null);

        public DurableCompositeOwnerOutstandingQueryResult QueryOwnerOutstanding(
            ModuleRegistration ownerModule)
        {
            if (!IsOwnerActive(ownerModule))
                return OwnerOutstanding(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    "composite.outstanding.owner-inactive");
            if (!TryGetJournal(out IDurableCompositeJournalStore journal))
                return OwnerOutstanding(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    "composite.journal.not-ready");
            try
            {
                DurableCompositeHistoryReadState state = journal.ReadOwnerOutstanding(
                    ownerModule.Descriptor.ModuleId,
                    out IReadOnlyList<byte[]> records,
                    out IReadOnlyList<DurableCompositeIssuedOperation> issued,
                    out string reason);
                if (state != DurableCompositeHistoryReadState.Ready)
                    return OwnerOutstanding(
                        state == DurableCompositeHistoryReadState.Corrupt
                            ? DurableCompositeOutstandingQueryState.Corrupt
                            : DurableCompositeOutstandingQueryState.Unavailable,
                        SafeReason(reason, "composite.owner-outstanding.unavailable"));
                var committed = new List<DurableCompositeOutstandingOperation>();
                var journaled = new List<DurableCompositeJournaledOutstandingOperation>();
                foreach (byte[] record in records)
                {
                    if (!DurableCompositeCodec.TryDecode(
                            record, out CompositeRootRecord root, out _) ||
                        !string.Equals(
                            root.OwnerModuleId,
                            ownerModule.Descriptor.ModuleId,
                            StringComparison.Ordinal))
                        return OwnerOutstanding(
                            DurableCompositeOutstandingQueryState.Corrupt,
                            "composite.owner-outstanding.corrupt");
                    if (root.ReconciliationPending)
                        committed.Add(new DurableCompositeOutstandingOperation(
                            root.OwnerModuleId,
                            root.OwnerModuleVersion,
                            root.OwnerProtocolMajor,
                            root.EndpointDomainId,
                            root.OperationToken,
                            root.ActorIdentity,
                            root.IntentHash,
                            root.CommitSequence,
                            root.ReceiptHash,
                            root.ReconciliationRequirement,
                            root.Phase));
                    else if (root.Phase != DurableCompositeOperationPhase.Committed &&
                             root.Phase != DurableCompositeOperationPhase.Aborted)
                        journaled.Add(new DurableCompositeJournaledOutstandingOperation(
                            root.OwnerModuleId,
                            root.OwnerModuleVersion,
                            root.OwnerProtocolMajor,
                            root.EndpointDomainId,
                            root.OperationToken,
                            root.ActorIdentity,
                            root.RequestHash,
                            root.Phase));
                    else return OwnerOutstanding(
                        DurableCompositeOutstandingQueryState.Corrupt,
                        "composite.owner-outstanding.terminal-unexpected");
                }
                return new DurableCompositeOwnerOutstandingQueryResult(
                    DurableCompositeOutstandingQueryState.Ready,
                    committed,
                    issued,
                    journaled,
                    "composite.owner-outstanding.ready");
            }
            catch
            {
                return OwnerOutstanding(
                    DurableCompositeOutstandingQueryState.Unavailable,
                    "composite.owner-outstanding.exception");
            }
        }

        public DurableCompositeTokenDispositionResult QueryTokenDisposition(
            ModuleRegistration requesterModule,
            string canonicalOperationToken)
        {
            if (requesterModule == null ||
                !_registry.IsModuleRegistrationActive(requesterModule))
                return new DurableCompositeTokenDispositionResult(
                    DurableCompositeTokenDispositionState.Unavailable,
                    "composite.token-disposition.requester-inactive");
            if (!TryGetJournal(out IDurableCompositeJournalStore journal))
                return new DurableCompositeTokenDispositionResult(
                    DurableCompositeTokenDispositionState.Unavailable,
                    "composite.token-disposition.journal-unavailable");
            try
            {
                DurableCompositeTokenDispositionState state =
                    journal.ReadTokenDisposition(canonicalOperationToken, out string reason);
                return new DurableCompositeTokenDispositionResult(
                    state,
                    SafeReason(reason, "composite.token-disposition.unavailable"));
            }
            catch
            {
                return new DurableCompositeTokenDispositionResult(
                    DurableCompositeTokenDispositionState.Unavailable,
                    "composite.token-disposition.exception");
            }
        }

        private static DurableCompositeOwnerOutstandingQueryResult OwnerOutstanding(
            DurableCompositeOutstandingQueryState state,
            string reason) => new DurableCompositeOwnerOutstandingQueryResult(
                state,
                Array.Empty<DurableCompositeOutstandingOperation>(),
                Array.Empty<DurableCompositeIssuedOperation>(),
                Array.Empty<DurableCompositeJournaledOutstandingOperation>(),
                reason);

        internal void ConfigureJournal(IDurableCompositeJournalStore journal)
        {
            if (journal == null) throw new ArgumentNullException(nameof(journal));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_journal != null && !ReferenceEquals(_journal, journal))
                    throw new InvalidOperationException(
                        "A different authoritative world journal is already active.");
                _journal = journal;
            }
        }

        internal bool TryGetJournal(out IDurableCompositeJournalStore journal)
        {
            lock (_sync)
            {
                if (!_disposed && _useValheimWorldJournal)
                {
                    string currentScope = CurrentWorldScope();
                    if (string.IsNullOrEmpty(currentScope))
                    {
                        journal = null;
                        return false;
                    }
                    if (_journal == null ||
                        !string.Equals(
                            _journal.WorldScope, currentScope, StringComparison.Ordinal))
                    {
                        if (_journal is IDisposable old) old.Dispose();
                        string root = Path.Combine(
                            Paths.ConfigPath,
                            "RunicTransactions",
                            "composite-world-journals");
                        try { _journal = new FileDurableCompositeJournalStore(root, currentScope); }
                        catch
                        {
                            _journal = null;
                            journal = null;
                            return false;
                        }
                    }
                }
                journal = !_disposed ? _journal : null;
                return journal != null;
            }
        }

        private static string CurrentWorldScope()
        {
            try
            {
                ZNet network = ZNet.instance;
                if (network == null || !network.IsServer()) return string.Empty;
                long world = network.GetWorldUID();
                return world == 0L
                    ? string.Empty
                    : "valheim." + unchecked((ulong)world).ToString(
                        "x16", CultureInfo.InvariantCulture);
            }
            catch { return string.Empty; }
        }

        internal bool TryGetDomain(
            string domainId,
            int requiredSchema,
            out DurableCompositeEndpointDomainDescriptor descriptor,
            out IDurableCompositeEndpointStore endpointStore)
        {
            lock (_sync)
            {
                descriptor = null;
                endpointStore = null;
                if (_disposed || !_domains.TryGetValue(domainId, out DomainRegistration registration) ||
                    !registration.IsActive ||
                    !_registry.IsModuleRegistrationActive(registration.ProviderModule) ||
                    registration.Descriptor.SchemaVersion != requiredSchema)
                    return false;
                descriptor = registration.Descriptor;
                endpointStore = registration.EndpointStore;
                return true;
            }
        }

        internal bool TryGetDomainForNewOperation(
            string domainId,
            out DurableCompositeEndpointDomainDescriptor descriptor,
            out IDurableCompositeEndpointStore endpointStore)
        {
            lock (_sync)
            {
                descriptor = null;
                endpointStore = null;
                if (_disposed || !_domains.TryGetValue(domainId, out DomainRegistration registration) ||
                    !registration.IsActive ||
                    !_registry.IsModuleRegistrationActive(registration.ProviderModule))
                    return false;
                descriptor = registration.Descriptor;
                endpointStore = registration.EndpointStore;
                return true;
            }
        }

        internal bool IsOwnerActive(ModuleRegistration owner) =>
            owner != null && _registry.IsModuleRegistrationActive(owner);

        internal bool TryBeginWorldCheckpointCapture(
            out long commitSequence,
            out IDurableCompositeJournalStore journal,
            out IDisposable mutationBarrier)
        {
            commitSequence = 0;
            journal = null;
            mutationBarrier = null;
            if (!TryGetJournal(out journal) ||
                !RunicMutationGate.TryEnter(
                    "runic.transactions.composite-checkpoint", out mutationBarrier))
                return false;
            try
            {
                if (journal.ReadCommitHistory(
                        out IReadOnlyList<byte[]> records,
                        out _,
                        out _) != DurableCompositeHistoryReadState.Ready ||
                    records.Count == 0)
                {
                    mutationBarrier.Dispose();
                    mutationBarrier = null;
                    return false;
                }
                long previous = 0;
                foreach (byte[] record in records)
                {
                    if (!DurableCompositeCodec.TryDecode(
                            record, out CompositeRootRecord root, out _) ||
                        root.Phase != DurableCompositeOperationPhase.Committed ||
                        root.CommitSequence <= previous)
                    {
                        mutationBarrier.Dispose();
                        mutationBarrier = null;
                        return false;
                    }
                    previous = root.CommitSequence;
                }
                commitSequence = previous;
                return commitSequence > 0;
            }
            catch
            {
                try { mutationBarrier?.Dispose(); }
                catch { }
                mutationBarrier = null;
                commitSequence = 0;
                return false;
            }
        }

        internal void NotifyOutstandingChanged(
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity)
        {
            EventHandler<DurableCompositeOutstandingChangedEventArgs> handlers;
            lock (_sync) handlers = !_disposed ? OutstandingChanged : null;
            if (handlers == null) return;
            var arguments = new DurableCompositeOutstandingChangedEventArgs(actorIdentity);
            foreach (EventHandler<DurableCompositeOutstandingChangedEventArgs> handler in
                handlers.GetInvocationList())
            {
                try { handler(this, arguments); }
                catch { }
            }
        }

        internal void Unregister(DomainRegistration registration)
        {
            lock (_sync)
            {
                if (_domains.TryGetValue(
                        registration.Descriptor.DomainId,
                        out DomainRegistration current) &&
                    ReferenceEquals(current, registration))
                    _domains.Remove(registration.Descriptor.DomainId);
            }
        }

        public void Dispose()
        {
            IDurableCompositeJournalStore journal;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (DomainRegistration registration in _domains.Values)
                    registration.Deactivate();
                _domains.Clear();
                journal = _journal;
                _journal = null;
            }
            if (journal is IDisposable disposable) disposable.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        }

        private static string SafeReason(string value, string fallback)
        {
            try { return RunicIdentifier.Require(value, nameof(value)); }
            catch { return fallback; }
        }

        private static DurableCompositeOutstandingReadResult OutstandingRead(
            DurableCompositeOutstandingReadState state,
            string reason) =>
            new DurableCompositeOutstandingReadResult(state, reason, null);

        internal sealed class DomainRegistration : IDisposable
        {
            private readonly DurableCompositeOperationCoordinatorFactory _owner;
            private bool _active = true;

            internal DomainRegistration(
                DurableCompositeOperationCoordinatorFactory owner,
                ModuleRegistration providerModule,
                DurableCompositeEndpointDomainDescriptor descriptor,
                IDurableCompositeEndpointStore endpointStore)
            {
                _owner = owner;
                ProviderModule = providerModule;
                Descriptor = descriptor;
                EndpointStore = endpointStore;
            }

            internal ModuleRegistration ProviderModule { get; }
            internal DurableCompositeEndpointDomainDescriptor Descriptor { get; }
            internal IDurableCompositeEndpointStore EndpointStore { get; }
            internal bool IsActive => _active;

            public void Dispose()
            {
                if (!_active) return;
                _active = false;
                _owner.Unregister(this);
            }

            internal void Deactivate() => _active = false;
        }

    }

    internal sealed class DurableCompositeOperationCoordinator :
        IDurableCompositeOperationCoordinator
    {
        private const string MutationOwner = "runic.transactions.composite";
        private readonly DurableCompositeOperationCoordinatorFactory _factory;
        private readonly RunicRegistry _registry;
        private readonly ModuleRegistration _owner;
        private readonly string _domainId;

        internal DurableCompositeOperationCoordinator(
            DurableCompositeOperationCoordinatorFactory factory,
            RunicRegistry registry,
            ModuleRegistration owner,
            string domainId)
        {
            _factory = factory;
            _registry = registry;
            _owner = owner;
            _domainId = domainId;
        }

        public DurableCompositeTokenIssueResult IssueOperationToken(
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            string requestHash) => IssueOperationToken(
                actorIdentity, requestHash, requestHash);

        public DurableCompositeTokenIssueResult IssueOperationToken(
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            string durableRequestKeyHash,
            string requestHash)
        {
            if (!TryEnter(out IDisposable gate, out DurableCompositeOperationResult failure))
                return TokenFailure(failure.ReasonCode);
            using (gate)
            {
                try
                {
                    Runic.Foundation.Persistence.RpcPeerIdentity actor =
                        CompositeValidation.RequireBackendIdentity(actorIdentity);
                    string exactDurableRequestKeyHash = CompositeValidation.RequireSha256(
                        durableRequestKeyHash, nameof(durableRequestKeyHash));
                    string exactRequestHash = CompositeValidation.RequireSha256(
                        requestHash, nameof(requestHash));
                    if (!_factory.TryGetJournal(out IDurableCompositeJournalStore journal))
                        return TokenFailure("composite.journal.not-ready");
                    DurableCompositeTokenIssueCode code = journal.IssueOperationToken(
                        _owner.Descriptor.ModuleId,
                        _owner.Descriptor.SemanticVersion.ToString(),
                        _owner.Descriptor.ProtocolVersion.Major,
                        actor.CanonicalKey,
                        exactDurableRequestKeyHash,
                        exactRequestHash,
                        out DurableCompositeOperationToken token,
                        out string reason);
                    if (code == DurableCompositeTokenIssueCode.Issued)
                        _factory.NotifyOutstandingChanged(actor);
                    return new DurableCompositeTokenIssueResult(
                        code,
                        SafeReason(reason, code == DurableCompositeTokenIssueCode.Issued
                            ? "composite.token.issued"
                            : code == DurableCompositeTokenIssueCode.Replay
                                ? "composite.token.replay"
                                : "composite.token.failed"),
                        token);
                }
                catch
                {
                    return TokenFailure("composite.token.exception");
                }
            }
        }

        public DurableCompositeTokenCancellationResult CancelIssuedOperationToken(
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            DurableCompositeOperationToken operationToken,
            string requestHash)
        {
            if (!TryEnter(out IDisposable gate, out DurableCompositeOperationResult failure))
                return TokenCancellationFailure(failure.ReasonCode);
            using (gate)
            {
                try
                {
                    Runic.Foundation.Persistence.RpcPeerIdentity actor =
                        CompositeValidation.RequireBackendIdentity(actorIdentity);
                    if (operationToken == null)
                        return TokenCancellationFailure("composite.token.invalid");
                    string exactRequest = CompositeValidation.RequireSha256(
                        requestHash, nameof(requestHash));
                    if (!_factory.TryGetJournal(out IDurableCompositeJournalStore journal))
                        return TokenCancellationFailure("composite.journal.not-ready");
                    DurableCompositeTokenCancellationCode code =
                        journal.CancelIssuedOperationToken(
                            _owner.Descriptor.ModuleId,
                            actor.CanonicalKey,
                            operationToken,
                            exactRequest,
                            out string reason);
                    if (code == DurableCompositeTokenCancellationCode.Cancelled)
                        _factory.NotifyOutstandingChanged(actor);
                    return new DurableCompositeTokenCancellationResult(
                        code,
                        SafeReason(reason, "composite.token.cancel-failed"));
                }
                catch
                {
                    return TokenCancellationFailure("composite.token.cancel-exception");
                }
            }
        }

        public DurableCompositeOperationResult Prepare(DurableCompositeOperationIntent intent)
        {
            if (!TryEnter(out IDisposable gate, out DurableCompositeOperationResult failure))
                return failure;
            using (gate)
            {
                try
                {
                    if (!ValidIntentOwner(intent)) return Fail(
                        DurableCompositeResultCode.FailedClosed,
                        "composite.owner.invalid");
                    if (!_factory.TryGetJournal(out IDurableCompositeJournalStore journal) ||
                        !_factory.TryGetDomainForNewOperation(
                            _domainId,
                            out DurableCompositeEndpointDomainDescriptor descriptor,
                            out IDurableCompositeEndpointStore endpoints))
                        return Fail(
                            DurableCompositeResultCode.NotReady,
                            "composite.authority.not-ready");
                    if (!EndpointsUsePrefix(intent.Endpoints, descriptor.StableEndpointPrefix))
                        return Fail(
                            DurableCompositeResultCode.FailedClosed,
                            "composite.endpoint.noncanonical-domain");
                    if (intent.Endpoints.Count > descriptor.MaximumEndpoints)
                        return Fail(
                            DurableCompositeResultCode.FailedClosed,
                            "composite.endpoint.domain-capacity");
                    var claiming = new CompositeRootRecord(intent, descriptor.SchemaVersion);
                    byte[] claimingBytes = DurableCompositeCodec.Encode(claiming);
                    DurableCompositeJournalCreateState created = journal.TryCreateUnresolved(
                        intent.OperationIdText,
                        intent.ActorIdentity.CanonicalKey,
                        claimingBytes,
                        DurableCompositeLimits.MaximumUnresolvedOperations,
                        DurableCompositeLimits.MaximumUnresolvedPerActor,
                        out byte[] existing,
                        out string reason);
                    if (created == DurableCompositeJournalCreateState.CapacityReached)
                        return Fail(DurableCompositeResultCode.CapacityReached,
                            SafeReason(reason, "composite.capacity"));
                    if (created == DurableCompositeJournalCreateState.ActorBusy)
                        return Fail(DurableCompositeResultCode.ActorBusy,
                            SafeReason(reason, "composite.actor-busy"));
                    if (created == DurableCompositeJournalCreateState.Failed)
                        return Fail(DurableCompositeResultCode.FailedClosed,
                            SafeReason(reason, "composite.journal.failed"));
                    CompositeRootRecord root = claiming;
                    byte[] rootBytes = claimingBytes;
                    bool replay = created == DurableCompositeJournalCreateState.Existing;
                    if (replay)
                    {
                        rootBytes = existing;
                        if (!DurableCompositeCodec.TryDecode(existing, out root, out reason))
                            return Fail(DurableCompositeResultCode.FailedClosed,
                                SafeReason(reason, "composite.root.corrupt"));
                        if (!root.Matches(intent.Reference) ||
                            !string.Equals(root.EndpointDomainId, _domainId, StringComparison.Ordinal))
                            return Fail(DurableCompositeResultCode.ReplayConflict,
                                "composite.replay-conflict");
                    }
                    return AdvanceForPrepare(journal, endpoints, root, rootBytes, replay);
                }
                catch
                {
                    return Fail(DurableCompositeResultCode.FailedClosed,
                        "composite.prepare.exception");
                }
            }
        }

        public DurableCompositeOperationResult Commit(DurableCompositeOperationReference operation) =>
            ExecuteExisting(operation, ExistingAction.Commit);

        public DurableCompositeOperationResult Abort(DurableCompositeOperationReference operation) =>
            ExecuteExisting(operation, ExistingAction.Abort);

        public DurableCompositeOperationResult Recover(DurableCompositeOperationReference operation) =>
            ExecuteExisting(operation, ExistingAction.Recover);

        public DurableCompositeOperationResult ReadStatus(DurableCompositeOperationReference operation) =>
            ExecuteExisting(operation, ExistingAction.Status);

        public DurableCompositeOperationResult AcknowledgeReconciliation(
            DurableCompositeOperationReference operation,
            string durableAcknowledgementHash)
        {
            if (!TryEnter(out IDisposable gate, out DurableCompositeOperationResult failure))
                return failure;
            using (gate)
            {
                try
                {
                    string acknowledgement = CompositeValidation.RequireSha256(
                        durableAcknowledgementHash, nameof(durableAcknowledgementHash));
                    if (!TryReadExpected(
                            operation,
                            out IDurableCompositeJournalStore journal,
                            out CompositeRootRecord root,
                            out byte[] current,
                            out failure)) return failure;
                    if ((root.Phase != DurableCompositeOperationPhase.Committed &&
                         root.Phase != DurableCompositeOperationPhase.Aborted) ||
                        root.ReconciliationRequirement == null)
                        return Result(
                            DurableCompositeResultCode.FailedClosed,
                            "composite.ack.not-required",
                            root);
                    if (root.Phase == DurableCompositeOperationPhase.Committed)
                    {
                        DurableCompositeOperationResult reconciled = ReconcileHistory(journal, root);
                        if (!reconciled.Success) return reconciled;
                    }
                    if (!string.IsNullOrEmpty(root.AcknowledgementHash))
                        return Result(
                            string.Equals(
                                root.AcknowledgementHash,
                                acknowledgement,
                                StringComparison.Ordinal)
                                ? DurableCompositeResultCode.Replay
                                : DurableCompositeResultCode.ReplayConflict,
                            string.Equals(
                                root.AcknowledgementHash,
                                acknowledgement,
                                StringComparison.Ordinal)
                                ? "composite.ack.replay"
                                : "composite.ack.conflict",
                            root);
                    CompositeRootRecord acknowledged = root.WithAcknowledgement(acknowledgement);
                    byte[] replacement = DurableCompositeCodec.Encode(acknowledged);
                    if (!journal.TryCompareExchange(
                            root.OperationIdText,
                            current,
                            replacement,
                            true,
                            out byte[] observed,
                            out string reason))
                    {
                        if (DurableCompositeCodec.TryDecode(observed, out CompositeRootRecord raced, out _) &&
                            raced.Matches(operation) &&
                            string.Equals(raced.AcknowledgementHash, acknowledgement, StringComparison.Ordinal))
                            return Result(
                                DurableCompositeResultCode.Replay,
                                "composite.ack.replay",
                                raced);
                        return Fail(DurableCompositeResultCode.FailedClosed,
                            SafeReason(reason, "composite.ack.persist-failed"));
                    }
                    _factory.NotifyOutstandingChanged(acknowledged.ActorIdentity);
                    return Result(
                        DurableCompositeResultCode.Acknowledged,
                        root.Phase == DurableCompositeOperationPhase.Aborted
                            ? "composite.abort-reconciliation-acknowledged"
                            : "composite.acknowledged",
                        acknowledged);
                }
                catch
                {
                    return Fail(DurableCompositeResultCode.FailedClosed,
                        "composite.ack.exception");
                }
            }
        }

        private DurableCompositeOperationResult ExecuteExisting(
            DurableCompositeOperationReference operation,
            ExistingAction action)
        {
            if (!TryEnter(out IDisposable gate, out DurableCompositeOperationResult failure))
                return failure;
            using (gate)
            {
                try
                {
                    if (!TryReadExpected(
                            operation,
                            out IDurableCompositeJournalStore journal,
                            out CompositeRootRecord root,
                            out byte[] current,
                            out failure)) return failure;
                    if (!string.Equals(root.EndpointDomainId, _domainId, StringComparison.Ordinal))
                        return Fail(DurableCompositeResultCode.ReplayConflict,
                            "composite.domain.conflict");
                    if (!_factory.TryGetDomain(
                            root.EndpointDomainId,
                            root.EndpointDomainSchemaVersion,
                            out DurableCompositeEndpointDomainDescriptor descriptor,
                            out IDurableCompositeEndpointStore endpoints))
                        return Result(
                            DurableCompositeResultCode.NotReady,
                            "composite.domain.adapter-missing",
                            root);
                    if (root.Endpoints.Count > descriptor.MaximumEndpoints)
                        return Result(
                            DurableCompositeResultCode.FailedClosed,
                            "composite.endpoint.domain-capacity",
                            root);
                    if (!TryBindOperation(endpoints, root, out failure))
                        return failure;

                    if (action == ExistingAction.Abort)
                        return AbortExisting(journal, endpoints, root, current);
                    if (action == ExistingAction.Status &&
                        (root.Phase == DurableCompositeOperationPhase.Claiming ||
                         root.Phase == DurableCompositeOperationPhase.Prepared))
                        return Result(
                            root.Phase == DurableCompositeOperationPhase.Prepared
                                ? DurableCompositeResultCode.Prepared
                                : DurableCompositeResultCode.NotReady,
                            root.Phase == DurableCompositeOperationPhase.Prepared
                                ? "composite.prepared.status"
                                : "composite.claiming.status",
                            root);
                    if (root.Phase == DurableCompositeOperationPhase.Claiming)
                    {
                        DurableCompositeOperationResult prepared = AdvanceForPrepare(
                            journal, endpoints, root, current, true);
                        if (!prepared.Success || action == ExistingAction.Status)
                            return prepared;
                        if (!TryReadExpected(
                                operation, out journal, out root, out current, out failure))
                            return failure;
                    }
                    if (root.Phase == DurableCompositeOperationPhase.Prepared &&
                        action == ExistingAction.Commit)
                        return BeginOrResumeCommit(journal, endpoints, root, current);
                    if (root.Phase == DurableCompositeOperationPhase.Committing ||
                        root.Phase == DurableCompositeOperationPhase.Committed)
                        return ReconcileHistory(journal, root);
                    if (root.Phase == DurableCompositeOperationPhase.Aborting)
                        return FinishAbort(journal, endpoints, root, current);
                    if (root.Phase == DurableCompositeOperationPhase.Aborted)
                        return Result(
                            DurableCompositeResultCode.Replay,
                            "composite.aborted.replay",
                            root);
                    return Result(
                        DurableCompositeResultCode.Prepared,
                        action == ExistingAction.Status
                            ? "composite.prepared.status"
                            : "composite.prepared.replay",
                        root);
                }
                catch
                {
                    return Fail(DurableCompositeResultCode.FailedClosed,
                        "composite.existing.exception");
                }
            }
        }

        private DurableCompositeOperationResult AdvanceForPrepare(
            IDurableCompositeJournalStore journal,
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            byte[] current,
            bool replay)
        {
            if (root.Phase == DurableCompositeOperationPhase.Prepared)
                return Result(
                    replay ? DurableCompositeResultCode.Replay : DurableCompositeResultCode.Prepared,
                    replay ? "composite.prepared.replay" : "composite.prepared",
                    root);
            if (root.Phase == DurableCompositeOperationPhase.Committing ||
                root.Phase == DurableCompositeOperationPhase.Committed)
                return ReconcileHistory(journal, root);
            if (root.Phase == DurableCompositeOperationPhase.Aborting ||
                root.Phase == DurableCompositeOperationPhase.Aborted)
                return Result(DurableCompositeResultCode.Aborted,
                    "composite.abort-in-progress", root);
            if (root.Phase != DurableCompositeOperationPhase.Claiming)
                return Fail(DurableCompositeResultCode.FailedClosed,
                    "composite.phase.invalid");
            if (!TryBindOperation(
                    endpoints, root, out DurableCompositeOperationResult bindFailure))
                return bindFailure;

            foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
            {
                DurableCompositeEndpointClaim expected = root.ClaimFor(endpoint);
                DurableCompositeEndpointSnapshot snapshot;
                DurableCompositeOperationResult failure;
                if (endpoints is IDurableCompositeDeferredClaimEndpointStore)
                {
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out snapshot,
                            out failure)) return failure;
                    if (!IsExactBefore(endpoint, snapshot))
                        return Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            "composite.endpoint.before-conflict",
                            root);
                    if (snapshot.Claim != null && !expected.MatchesExact(snapshot.Claim))
                        return Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            "composite.endpoint.claim-conflict",
                            root);
                }
                else if (!TryReadAuthority(
                             endpoints,
                             root,
                             endpoint,
                             expected,
                             true,
                             out snapshot,
                             out failure))
                    return failure;
                DurableCompositeDeferredClaimState claimState = TryAcquireOrResumeClaim(
                    endpoints, root, endpoint, expected, snapshot, out string reason);
                if (claimState == DurableCompositeDeferredClaimState.Pending)
                    return Result(
                        DurableCompositeResultCode.NotReady,
                        SafeReason(reason, "composite.identity-enrollment.pending"),
                        root);
                if (claimState != DurableCompositeDeferredClaimState.Acquired)
                    return Fail(
                        DurableCompositeResultCode.EvidenceConflict,
                        SafeReason(reason, "composite.claim.acquire-failed"));
                if (!TryReadAuthority(
                        endpoints, root, endpoint, expected, false, out _, out failure))
                    return failure;
            }
            foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
                if (!TryReadAuthority(
                        endpoints,
                        root,
                        endpoint,
                        root.ClaimFor(endpoint),
                        false,
                        out _,
                        out DurableCompositeOperationResult failure))
                    return failure;

            CompositeRootRecord prepared = root.WithPhase(DurableCompositeOperationPhase.Prepared);
            byte[] replacement = DurableCompositeCodec.Encode(prepared);
            if (!journal.TryCompareExchange(
                    root.OperationIdText,
                    current,
                    replacement,
                    false,
                    out byte[] observed,
                    out string publishReason))
            {
                if (DurableCompositeCodec.TryDecode(observed, out CompositeRootRecord raced, out _) &&
                    raced.Phase == DurableCompositeOperationPhase.Prepared &&
                    SameRootIdentity(root, raced))
                    return Result(DurableCompositeResultCode.Replay,
                        "composite.prepared.replay", raced);
                return Fail(DurableCompositeResultCode.FailedClosed,
                    SafeReason(publishReason, "composite.prepared.persist-failed"));
            }
            return Result(DurableCompositeResultCode.Prepared, "composite.prepared", prepared);
        }

        private DurableCompositeOperationResult BeginOrResumeCommit(
            IDurableCompositeJournalStore journal,
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            byte[] current)
        {
            if (root.Phase == DurableCompositeOperationPhase.Prepared)
            {
                foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
                {
                    DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                    if (!TryReadCommitAuthority(
                            endpoints,
                            root,
                            endpoint,
                            claim,
                            true,
                            out DurableCompositeEndpointSnapshot snapshot,
                            out DurableCompositeOperationResult failure))
                        return failure;
                    if (snapshot.Claim == null)
                    {
                        DurableCompositeDeferredClaimState claimState =
                            TryAcquireOrResumeClaim(
                                endpoints,
                                root,
                                endpoint,
                                claim,
                                snapshot,
                                out string claimReason);
                        if (claimState == DurableCompositeDeferredClaimState.Pending)
                            return Result(
                                DurableCompositeResultCode.NotReady,
                                SafeReason(
                                    claimReason,
                                    "composite.identity-enrollment.pending"),
                                root);
                        if (claimState != DurableCompositeDeferredClaimState.Acquired)
                            return Result(
                                DurableCompositeResultCode.EvidenceConflict,
                                SafeReason(
                                    claimReason,
                                    "composite.claim.reacquire-failed"),
                                root);
                    }
                    if (!TryReadCommitAuthority(
                            endpoints,
                            root,
                            endpoint,
                            claim,
                            false,
                            out _,
                            out failure))
                        return failure;
                }
                EndpointId[] ids = root.Endpoints.Select(value => value.StableEndpointId).ToArray();
                if (!journal.TryBeginCommit(
                        root.OperationIdText,
                        current,
                        ids,
                        order => DurableCompositeCodec.Encode(root.WithCommitOrder(order)),
                        out _,
                        out byte[] observed,
                        out string reason))
                {
                    if (!DurableCompositeCodec.TryDecode(observed, out root, out _) ||
                        root.Phase != DurableCompositeOperationPhase.Committing)
                        return Fail(DurableCompositeResultCode.FailedClosed,
                            SafeReason(reason, "composite.commit-order.persist-failed"));
                }
                else if (!DurableCompositeCodec.TryDecode(observed, out root, out _))
                    return Fail(DurableCompositeResultCode.FailedClosed,
                        "composite.committing.readback-corrupt");
            }
            return ReconcileHistory(journal, root);
        }

        private DurableCompositeOperationResult ReconcileHistory(
            IDurableCompositeJournalStore journal,
            CompositeRootRecord requested)
        {
            DurableCompositeHistoryReadState historyState = journal.ReadCommitHistory(
                out IReadOnlyList<byte[]> historyBytes,
                out long checkpoint,
                out string historyReason);
            if (historyState != DurableCompositeHistoryReadState.Ready)
                return Result(
                    DurableCompositeResultCode.FailedClosed,
                    SafeReason(historyReason, "composite.history.unavailable"),
                    requested);
            var history = new List<CompositeRootRecord>(historyBytes.Count);
            foreach (byte[] bytes in historyBytes)
            {
                if (!DurableCompositeCodec.TryDecode(bytes, out CompositeRootRecord root, out _))
                    return Result(DurableCompositeResultCode.FailedClosed,
                        "composite.history.corrupt", requested);
                history.Add(root);
            }
            history.Sort((left, right) => left.CommitSequence.CompareTo(right.CommitSequence));
            if (requested.CommitSequence <= checkpoint)
                return Result(DurableCompositeResultCode.Replay,
                    "composite.committed.checkpointed", requested);

            for (int historyIndex = 0; historyIndex < history.Count; historyIndex++)
            {
                CompositeRootRecord root = history[historyIndex];
                if (!_factory.TryGetDomain(
                        root.EndpointDomainId,
                        root.EndpointDomainSchemaVersion,
                        out DurableCompositeEndpointDomainDescriptor descriptor,
                        out IDurableCompositeEndpointStore endpoints))
                    return Result(DurableCompositeResultCode.NotReady,
                        "composite.history.adapter-missing", requested);
                if (root.Endpoints.Count > descriptor.MaximumEndpoints)
                    return Result(DurableCompositeResultCode.FailedClosed,
                        "composite.history.domain-capacity", requested);
                if (!TryBindOperation(
                        endpoints,
                        root,
                        out DurableCompositeOperationResult bindFailure))
                    return AttachSnapshot(bindFailure, requested);
                var terminalEvidence = new DurableCompositeEndpointReceipt[root.Endpoints.Count];
                for (int endpointIndex = 0; endpointIndex < root.Endpoints.Count; endpointIndex++)
                {
                    DurableCompositeEndpointIntent endpoint = root.Endpoints[endpointIndex];
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out DurableCompositeEndpointSnapshot snapshot,
                            out DurableCompositeOperationResult failure))
                        return AttachSnapshot(failure, requested);
                    if (!AuthorityReady(endpoints, root, endpoint, snapshot))
                        return Result(DurableCompositeResultCode.AuthorityChanged,
                            "composite.endpoint.authority-changed", requested);

                    if (MarkerExactlyMatches(root, snapshot))
                    {
                        if (root.Phase != DurableCompositeOperationPhase.Committed &&
                            (!string.Equals(
                                snapshot.Fingerprint,
                                endpoint.AfterFingerprint,
                                StringComparison.Ordinal) ||
                             !string.Equals(
                                snapshot.SemanticRevision,
                                endpoint.AfterSemanticRevision,
                                StringComparison.Ordinal)))
                            return Result(DurableCompositeResultCode.EvidenceConflict,
                                "composite.endpoint.marker-state-conflict", requested);
                        terminalEvidence[endpointIndex] = root.Phase ==
                                DurableCompositeOperationPhase.Committed
                            ? root.TerminalEndpoints[endpointIndex]
                            : ExpectedCommittedReceipt(root, endpoint);
                        continue;
                    }
                    if (IsSupersededByLaterAppliedMarker(
                            history, historyIndex + 1, endpoint.StableEndpointId, snapshot))
                    {
                        terminalEvidence[endpointIndex] = root.Phase ==
                                DurableCompositeOperationPhase.Committed
                            ? root.TerminalEndpoints[endpointIndex]
                            : ExpectedCommittedReceipt(root, endpoint);
                        continue;
                    }
                    bool exactPreparedAfter = IsExactPreparedAfter(
                        endpoints, root, endpoint, root.ClaimFor(endpoint), snapshot, out _);
                    bool exactBefore = IsExactBefore(endpoint, snapshot);
                    if ((!exactBefore && !exactPreparedAfter) ||
                        !MarkerMatchesPredecessor(
                            root, endpoint.StableEndpointId, snapshot, checkpoint))
                        return Result(DurableCompositeResultCode.EvidenceConflict,
                            "composite.endpoint.third-state", requested);

                    DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                    if (snapshot.Claim != null && !claim.MatchesExact(snapshot.Claim))
                        return Result(DurableCompositeResultCode.EvidenceConflict,
                            "composite.endpoint.claim-conflict", requested);
                    if (snapshot.Claim == null)
                    {
                        DurableCompositeDeferredClaimState claimState =
                            TryAcquireOrResumeClaim(
                                endpoints,
                                root,
                                endpoint,
                                claim,
                                snapshot,
                                out string claimReason);
                        if (claimState == DurableCompositeDeferredClaimState.Pending)
                            return Result(
                                DurableCompositeResultCode.NotReady,
                                SafeReason(
                                    claimReason,
                                    "composite.identity-enrollment.pending"),
                                requested);
                        if (claimState != DurableCompositeDeferredClaimState.Acquired)
                            return Result(
                                DurableCompositeResultCode.EvidenceConflict,
                                SafeReason(
                                    claimReason,
                                    "composite.claim.reacquire-failed"),
                                requested);
                    }
                    if (!TryReadCommitAuthority(
                            endpoints,
                            root,
                            endpoint,
                            claim,
                            false,
                            out DurableCompositeEndpointSnapshot applySnapshot,
                            out failure))
                        return AttachSnapshot(failure, requested);
                    DurableCompositeDeferredApplyState applyState = TryApplyAfter(
                        endpoints,
                        root,
                        endpoint,
                        claim,
                        applySnapshot,
                        out string applyReason);
                    if (applyState == DurableCompositeDeferredApplyState.Pending)
                        return Result(
                            DurableCompositeResultCode.NotReady,
                            SafeReason(applyReason, "composite.endpoint.apply-pending"),
                            requested);
                    if (applyState != DurableCompositeDeferredApplyState.Applied)
                        return Result(DurableCompositeResultCode.EvidenceConflict,
                            SafeReason(applyReason, "composite.endpoint.apply-failed"), requested);
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out snapshot,
                            out failure))
                        return AttachSnapshot(failure, requested);
                    if (!AuthorityReady(endpoints, root, endpoint, snapshot) ||
                        snapshot.Claim == null || !claim.MatchesExact(snapshot.Claim) ||
                        !string.Equals(
                            snapshot.Fingerprint,
                            endpoint.AfterFingerprint,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            snapshot.SemanticRevision,
                            endpoint.AfterSemanticRevision,
                            StringComparison.Ordinal) ||
                        !MarkerExactlyMatches(root, snapshot))
                        return Result(DurableCompositeResultCode.EvidenceConflict,
                            "composite.endpoint.after-readback-failed", requested);
                    terminalEvidence[endpointIndex] = ExpectedCommittedReceipt(root, endpoint);
                }

                if (root.Phase == DurableCompositeOperationPhase.Committing)
                {
                    byte[] expected = historyBytes.First(bytes =>
                    {
                        return DurableCompositeCodec.TryDecode(bytes, out CompositeRootRecord value, out _) &&
                               value.OperationId == root.OperationId;
                    });
                    CompositeRootRecord committed = root.WithTerminal(
                        DurableCompositeOperationPhase.Committed,
                        terminalEvidence);
                    byte[] replacement = DurableCompositeCodec.Encode(committed);
                    if (!journal.TryCompareExchange(
                            root.OperationIdText,
                            expected,
                            replacement,
                            true,
                            out byte[] observed,
                            out string terminalReason))
                    {
                        if (!DurableCompositeCodec.TryDecode(
                                observed, out committed, out _) ||
                            committed.Phase != DurableCompositeOperationPhase.Committed ||
                            !SameRootIdentity(root, committed))
                            return Result(DurableCompositeResultCode.FailedClosed,
                                SafeReason(
                                    terminalReason,
                                    "composite.committed.persist-failed"),
                                requested);
                    }
                    root = committed;
                    history[historyIndex] = committed;
                    if (root.OperationId == requested.OperationId) requested = root;
                    if (root.ReconciliationRequirement != null)
                        _factory.NotifyOutstandingChanged(root.ActorIdentity);
                }

                // A terminal root is already durable here. Claim release is prompt but best-effort;
                // later recovery retries exact remnants and never rolls the receipt back.
                foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
                {
                    DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                    if (endpoints.Read(
                            endpoint.StableEndpointId,
                            out DurableCompositeEndpointSnapshot snapshot,
                            out _) == DurableCompositeEndpointReadState.Ready &&
                        snapshot.Claim != null && claim.MatchesExact(snapshot.Claim))
                        endpoints.TryReleaseClaim(claim, out _);
                }
            }

            if (journal.Read(
                    requested.OperationIdText,
                    out byte[] refreshed,
                    out _) == DurableCompositeJournalReadState.Present &&
                DurableCompositeCodec.TryDecode(refreshed, out CompositeRootRecord exact, out _) &&
                SameRootIdentity(requested, exact))
                requested = exact;
            return Result(
                requested.Phase == DurableCompositeOperationPhase.Committed
                    ? DurableCompositeResultCode.Committed
                    : DurableCompositeResultCode.Replay,
                requested.Phase == DurableCompositeOperationPhase.Committed
                    ? "composite.committed"
                    : "composite.history.reconciled",
                requested);
        }

        private DurableCompositeOperationResult AbortExisting(
            IDurableCompositeJournalStore journal,
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            byte[] current)
        {
            if (root.Phase == DurableCompositeOperationPhase.Committing ||
                root.Phase == DurableCompositeOperationPhase.Committed)
                return Result(DurableCompositeResultCode.ReplayConflict,
                    "composite.committed.never-aborts", root);
            if (root.Phase == DurableCompositeOperationPhase.Aborted)
                return Result(DurableCompositeResultCode.Replay,
                    "composite.aborted.replay", root);
            if (root.Phase != DurableCompositeOperationPhase.Aborting)
            {
                foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
                {
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out DurableCompositeEndpointSnapshot snapshot,
                            out DurableCompositeOperationResult failure))
                        return failure;
                    DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                    if ((!IsExactBefore(endpoint, snapshot) &&
                         !IsExactPreparedAfter(
                             endpoints, root, endpoint, claim, snapshot, out _)) ||
                        snapshot.Claim != null && !claim.MatchesExact(snapshot.Claim))
                        return Result(DurableCompositeResultCode.EvidenceConflict,
                            "composite.abort.before-conflict", root);
                    if (snapshot.Claim == null)
                    {
                        DurableCompositeDeferredClaimState claimState =
                            TryAcquireOrResumeClaim(
                                endpoints,
                                root,
                                endpoint,
                                claim,
                                snapshot,
                                out string claimReason);
                        if (claimState == DurableCompositeDeferredClaimState.Pending)
                            return Result(
                                DurableCompositeResultCode.NotReady,
                                SafeReason(
                                    claimReason,
                                    "composite.identity-enrollment.pending"),
                                root);
                        if (claimState != DurableCompositeDeferredClaimState.Acquired)
                            return Result(
                                DurableCompositeResultCode.EvidenceConflict,
                                SafeReason(
                                    claimReason,
                                    "composite.abort.claim-acquire-failed"),
                                root);
                    }
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out snapshot,
                            out failure)) return failure;
                    if (!AuthorityReady(endpoints, root, endpoint, snapshot) ||
                        (!IsExactBefore(endpoint, snapshot) &&
                         !IsExactPreparedAfter(
                             endpoints, root, endpoint, claim, snapshot, out _)) ||
                        snapshot.Claim == null || !claim.MatchesExact(snapshot.Claim))
                        return Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            "composite.abort.claim-readback-conflict",
                            root);
                }
                CompositeRootRecord aborting = root.WithPhase(DurableCompositeOperationPhase.Aborting);
                byte[] replacement = DurableCompositeCodec.Encode(aborting);
                if (!journal.TryCompareExchange(
                        root.OperationIdText,
                        current,
                        replacement,
                        false,
                        out byte[] observed,
                        out string reason))
                {
                    if (!DurableCompositeCodec.TryDecode(observed, out aborting, out _) ||
                        aborting.Phase != DurableCompositeOperationPhase.Aborting)
                        return Result(DurableCompositeResultCode.FailedClosed,
                            SafeReason(reason, "composite.aborting.persist-failed"), root);
                }
                root = aborting;
                current = replacement;
            }
            return FinishAbort(journal, endpoints, root, current);
        }

        private DurableCompositeOperationResult FinishAbort(
            IDurableCompositeJournalStore journal,
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            byte[] current)
        {
            var evidence = new DurableCompositeEndpointReceipt[root.Endpoints.Count];
            for (int index = 0; index < root.Endpoints.Count; index++)
            {
                DurableCompositeEndpointIntent endpoint = root.Endpoints[index];
                if (!TryReadSnapshot(
                        endpoints,
                        endpoint.StableEndpointId,
                        out DurableCompositeEndpointSnapshot snapshot,
                        out DurableCompositeOperationResult failure))
                    return failure;
                DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                if (!AuthorityReady(endpoints, root, endpoint, snapshot) ||
                    snapshot.Claim != null && !claim.MatchesExact(snapshot.Claim))
                    return Result(DurableCompositeResultCode.EvidenceConflict,
                        "composite.abort.before-conflict", root);
                if (!IsExactBefore(endpoint, snapshot))
                {
                    string stagedReason = "composite.abort.staged-unsupported";
                    if (!(endpoints is IDurableCompositeCompensatingEndpointStore compensating) ||
                        !IsExactPreparedAfter(
                            endpoints, root, endpoint, claim, snapshot, out stagedReason))
                        return Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            SafeReason(stagedReason, "composite.abort.staged-conflict"),
                            root);
                    if (snapshot.Claim == null)
                    {
                        DurableCompositeDeferredClaimState claimState =
                            TryAcquireOrResumeClaim(
                                endpoints,
                                root,
                                endpoint,
                                claim,
                                snapshot,
                                out string claimReason);
                        if (claimState == DurableCompositeDeferredClaimState.Pending)
                            return Result(
                                DurableCompositeResultCode.NotReady,
                                SafeReason(
                                    claimReason,
                                    "composite.identity-enrollment.pending"),
                                root);
                        if (claimState != DurableCompositeDeferredClaimState.Acquired)
                            return Result(
                                DurableCompositeResultCode.EvidenceConflict,
                                SafeReason(
                                    claimReason,
                                    "composite.abort.claim-reacquire-failed"),
                                root);
                    }
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out snapshot,
                            out failure) ||
                        !AuthorityReady(endpoints, root, endpoint, snapshot) ||
                        snapshot.Claim == null || !claim.MatchesExact(snapshot.Claim) ||
                        !IsExactPreparedAfter(
                            endpoints, root, endpoint, claim, snapshot, out stagedReason))
                        return failure ?? Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            SafeReason(stagedReason, "composite.abort.staged-recheck-failed"),
                            root);
                    if (!compensating.TryRestoreBefore(
                            root.ToRollbackContext(), endpoint, claim, out string restoreReason))
                        return Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            SafeReason(restoreReason, "composite.abort.compensation-failed"),
                            root);
                    if (!TryReadSnapshot(
                            endpoints,
                            endpoint.StableEndpointId,
                            out snapshot,
                            out failure) ||
                        !AuthorityReady(endpoints, root, endpoint, snapshot) ||
                        !IsExactBefore(endpoint, snapshot) ||
                        snapshot.Claim == null || !claim.MatchesExact(snapshot.Claim))
                        return failure ?? Result(
                            DurableCompositeResultCode.EvidenceConflict,
                            "composite.abort.compensation-readback-failed",
                            root);
                }
                evidence[index] = new DurableCompositeEndpointReceipt(
                    endpoint.StableEndpointId,
                    snapshot.Fingerprint,
                    snapshot.SemanticRevision,
                    snapshot.AppliedWorldEpoch,
                    snapshot.AppliedCommitSequence);
            }
            CompositeRootRecord aborted = root.WithTerminal(
                DurableCompositeOperationPhase.Aborted, evidence);
            byte[] terminal = DurableCompositeCodec.Encode(aborted);
            if (!journal.TryCompareExchange(
                    root.OperationIdText,
                    current,
                    terminal,
                    true,
                    out byte[] observed,
                    out string reason))
            {
                if (!DurableCompositeCodec.TryDecode(observed, out aborted, out _) ||
                    aborted.Phase != DurableCompositeOperationPhase.Aborted ||
                    !SameRootIdentity(root, aborted))
                    return Result(DurableCompositeResultCode.FailedClosed,
                        SafeReason(reason, "composite.aborted.persist-failed"), root);
            }
            foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
            {
                DurableCompositeEndpointClaim claim = root.ClaimFor(endpoint);
                endpoints.TryReleaseClaim(claim, out _);
            }
            _factory.NotifyOutstandingChanged(aborted.ActorIdentity);
            return Result(DurableCompositeResultCode.Aborted, "composite.aborted", aborted);
        }

        private bool TryReadExpected(
            DurableCompositeOperationReference operation,
            out IDurableCompositeJournalStore journal,
            out CompositeRootRecord root,
            out byte[] current,
            out DurableCompositeOperationResult failure)
        {
            journal = null;
            root = null;
            current = Array.Empty<byte>();
            failure = null;
            if (!ValidReferenceOwner(operation))
            {
                failure = Fail(DurableCompositeResultCode.FailedClosed,
                    "composite.owner.invalid");
                return false;
            }
            if (!_factory.TryGetJournal(out journal))
            {
                failure = Fail(DurableCompositeResultCode.NotReady,
                    "composite.journal.not-ready");
                return false;
            }
            DurableCompositeJournalReadState state = journal.Read(
                operation.OperationIdText, out current, out string reason);
            if (state == DurableCompositeJournalReadState.Absent)
            {
                failure = Fail(DurableCompositeResultCode.NotFound,
                    "composite.operation.not-found");
                return false;
            }
            string decodeReason = string.Empty;
            if (state != DurableCompositeJournalReadState.Present ||
                !DurableCompositeCodec.TryDecode(current, out root, out decodeReason))
            {
                failure = Fail(DurableCompositeResultCode.FailedClosed,
                    SafeReason(
                        state == DurableCompositeJournalReadState.Present ? decodeReason : reason,
                        "composite.journal.unavailable"));
                return false;
            }
            if (!root.Matches(operation))
            {
                failure = Fail(DurableCompositeResultCode.ReplayConflict,
                    "composite.replay-conflict");
                return false;
            }
            return true;
        }

        private bool TryEnter(
            out IDisposable gate,
            out DurableCompositeOperationResult failure)
        {
            gate = null;
            failure = null;
            if (!_factory.IsOwnerActive(_owner) ||
                !_registry.IsModuleRegistrationActive(_owner))
            {
                failure = Fail(DurableCompositeResultCode.FailedClosed,
                    "composite.owner.lease-inactive");
                return false;
            }
            if (!RunicMutationGate.TryEnter(MutationOwner, out gate))
            {
                failure = Fail(DurableCompositeResultCode.MutationBusy,
                    "composite.mutation-busy");
                return false;
            }
            return true;
        }

        private bool ValidIntentOwner(DurableCompositeOperationIntent intent) =>
            intent != null && ReferenceEquals(intent.OwnerModule, _owner) &&
            string.Equals(intent.EndpointDomainId, _domainId, StringComparison.Ordinal) &&
            _registry.IsModuleRegistrationActive(intent.OwnerModule);

        private bool ValidReferenceOwner(DurableCompositeOperationReference operation) =>
            operation != null && ReferenceEquals(operation.OwnerModule, _owner) &&
            _registry.IsModuleRegistrationActive(operation.OwnerModule);

        private static bool EndpointsUsePrefix(
            IReadOnlyList<DurableCompositeEndpointIntent> endpoints,
            string prefix)
        {
            foreach (DurableCompositeEndpointIntent endpoint in endpoints)
                if (!endpoint.StableEndpointId.Value.StartsWith(prefix, StringComparison.Ordinal) ||
                    endpoint.StableEndpointId.Value.Length == prefix.Length)
                    return false;
            return true;
        }

        private static bool TryReadAuthority(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim expectedClaim,
            bool allowAbsentClaim,
            out DurableCompositeEndpointSnapshot snapshot,
            out DurableCompositeOperationResult failure)
        {
            if (!TryReadSnapshot(
                    endpoints, endpoint.StableEndpointId, out snapshot, out failure)) return false;
            if (!AuthorityReady(
                    endpoints, root, endpoint, snapshot, out string authorityReason))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.AuthorityChanged,
                    SafeReason(authorityReason, "composite.endpoint.authority-changed"),
                    null);
                return false;
            }
            if (!string.Equals(snapshot.Fingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal) ||
                !string.Equals(
                    snapshot.SemanticRevision,
                    endpoint.PreparedSemanticRevision,
                    StringComparison.Ordinal))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.EvidenceConflict,
                    "composite.endpoint.before-conflict",
                    null);
                return false;
            }
            if (snapshot.Claim == null ? !allowAbsentClaim : !expectedClaim.MatchesExact(snapshot.Claim))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.EvidenceConflict,
                    "composite.endpoint.claim-conflict",
                    null);
                return false;
            }
            return true;
        }

        private static bool TryReadCommitAuthority(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim expectedClaim,
            bool allowAbsentClaim,
            out DurableCompositeEndpointSnapshot snapshot,
            out DurableCompositeOperationResult failure)
        {
            if (!TryReadSnapshot(
                    endpoints, endpoint.StableEndpointId, out snapshot, out failure)) return false;
            if (!AuthorityReady(endpoints, root, endpoint, snapshot))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.AuthorityChanged,
                    "composite.endpoint.authority-changed",
                    null);
                return false;
            }
            if (!IsExactBefore(endpoint, snapshot) &&
                !IsExactPreparedAfter(
                    endpoints, root, endpoint, expectedClaim, snapshot, out string stagedReason))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.EvidenceConflict,
                    SafeReason(stagedReason, "composite.endpoint.before-conflict"),
                    null);
                return false;
            }
            if (snapshot.Claim == null ? !allowAbsentClaim :
                !expectedClaim.MatchesExact(snapshot.Claim))
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.EvidenceConflict,
                    "composite.endpoint.claim-conflict",
                    null);
                return false;
            }
            return true;
        }

        private static bool IsExactBefore(
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointSnapshot snapshot) =>
            endpoint != null && snapshot != null &&
            string.Equals(
                snapshot.Fingerprint,
                endpoint.BeforeFingerprint,
                StringComparison.Ordinal) &&
            string.Equals(
                snapshot.SemanticRevision,
                endpoint.PreparedSemanticRevision,
                StringComparison.Ordinal);

        private static bool IsExactPreparedAfter(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim claim,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode)
        {
            failureCode = "composite.endpoint.prepared-after-unsupported";
            if (!(endpoints is IDurableCompositePreparedAfterEndpointStore staged) ||
                root == null || endpoint == null || claim == null || snapshot == null ||
                !string.Equals(
                    snapshot.Fingerprint,
                    endpoint.AfterFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    snapshot.SemanticRevision,
                    endpoint.AfterSemanticRevision,
                    StringComparison.Ordinal)) return false;
            try
            {
                return staged.IsExactPreparedAfter(
                    root.ToRollbackContext(),
                    endpoint,
                    claim,
                    snapshot,
                    out failureCode);
            }
            catch
            {
                failureCode = "composite.endpoint.prepared-after-exception";
                return false;
            }
        }

        private static bool TryReadSnapshot(
            IDurableCompositeEndpointStore endpoints,
            EndpointId endpoint,
            out DurableCompositeEndpointSnapshot snapshot,
            out DurableCompositeOperationResult failure)
        {
            snapshot = null;
            failure = null;
            DurableCompositeEndpointReadState state;
            string reason;
            try { state = endpoints.Read(endpoint, out snapshot, out reason); }
            catch
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.FailedClosed,
                    "composite.endpoint.read-exception",
                    null);
                return false;
            }
            if (state != DurableCompositeEndpointReadState.Ready || snapshot == null)
            {
                failure = new DurableCompositeOperationResult(
                    state == DurableCompositeEndpointReadState.Missing
                        ? DurableCompositeResultCode.NotReady
                        : DurableCompositeResultCode.FailedClosed,
                    SafeReason(reason, state == DurableCompositeEndpointReadState.Missing
                        ? "composite.endpoint.missing"
                        : "composite.endpoint.unavailable"),
                    null);
                return false;
            }
            return true;
        }

        private static bool AuthorityReady(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointSnapshot snapshot) =>
            AuthorityReady(endpoints, root, endpoint, snapshot, out _);

        private static bool AuthorityReady(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode)
        {
            failureCode = "composite.endpoint.authority-changed";
            if (endpoints == null || root == null || endpoint == null || snapshot == null ||
                snapshot.StableEndpointId != endpoint.StableEndpointId ||
                !snapshot.StableIdentityCurrent || !snapshot.Synchronized ||
                snapshot.DestroyPending) return false;
            if (snapshot.ServerOwned)
            {
                failureCode = "composite.endpoint.server-authority-current";
                return true;
            }
            if (!(endpoints is IDurableCompositeDelegatedAuthorityEndpointStore delegated))
                return false;
            try
            {
                return delegated.IsExactDelegatedMutationAuthorityCurrent(
                    root.ToRollbackContext(),
                    root.CommitSequence,
                    endpoint,
                    snapshot,
                    out failureCode);
            }
            catch
            {
                failureCode = "composite.endpoint.delegated-authority-exception";
                return false;
            }
        }

        private static bool TryBindOperation(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            out DurableCompositeOperationResult failure)
        {
            failure = null;
            if (!(endpoints is IDurableCompositeOperationBoundEndpointStore bound))
                return true;
            try
            {
                DurableCompositeOperationBindState state = bound.TryBindOperation(
                    root.ToRollbackContext(), root.Endpoints, out string reason);
                if (!Enum.IsDefined(typeof(DurableCompositeOperationBindState), state))
                    state = DurableCompositeOperationBindState.FailedClosed;
                if (state == DurableCompositeOperationBindState.Bound) return true;
                failure = new DurableCompositeOperationResult(
                    state == DurableCompositeOperationBindState.NotReady
                        ? DurableCompositeResultCode.NotReady
                        : DurableCompositeResultCode.FailedClosed,
                    SafeReason(
                        reason,
                        state == DurableCompositeOperationBindState.NotReady
                            ? "composite.endpoint.binding-not-ready"
                            : "composite.endpoint.binding-failed"),
                    null);
                return false;
            }
            catch
            {
                failure = new DurableCompositeOperationResult(
                    DurableCompositeResultCode.FailedClosed,
                    "composite.endpoint.binding-exception",
                    null);
                return false;
            }
        }

        private static DurableCompositeDeferredClaimState TryAcquireOrResumeClaim(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim claim,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode)
        {
            failureCode = "composite.claim.acquire-failed";
            if (endpoints == null || root == null || endpoint == null || claim == null ||
                snapshot == null || snapshot.StableEndpointId != endpoint.StableEndpointId)
                return DurableCompositeDeferredClaimState.FailedClosed;
            if (snapshot.Claim != null)
            {
                bool exact = claim.MatchesExact(snapshot.Claim);
                failureCode = exact
                    ? "composite.claim.replay"
                    : "composite.claim.conflict";
                return exact
                    ? DurableCompositeDeferredClaimState.Acquired
                    : DurableCompositeDeferredClaimState.FailedClosed;
            }
            try
            {
                if (endpoints is IDurableCompositeDeferredClaimEndpointStore deferred)
                {
                    DurableCompositeDeferredClaimState state =
                        deferred.TryBeginOrResumeAcquireClaim(
                            root.ToRollbackContext(),
                            endpoint,
                            claim,
                            snapshot,
                            out failureCode);
                    return Enum.IsDefined(typeof(DurableCompositeDeferredClaimState), state)
                        ? state
                        : DurableCompositeDeferredClaimState.FailedClosed;
                }
                return endpoints.TryAcquireClaim(claim, out failureCode)
                    ? DurableCompositeDeferredClaimState.Acquired
                    : DurableCompositeDeferredClaimState.FailedClosed;
            }
            catch
            {
                failureCode = "composite.claim.acquire-exception";
                return DurableCompositeDeferredClaimState.FailedClosed;
            }
        }

        private static DurableCompositeDeferredApplyState TryApplyAfter(
            IDurableCompositeEndpointStore endpoints,
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim claim,
            DurableCompositeEndpointSnapshot snapshot,
            out string failureCode)
        {
            failureCode = "composite.endpoint.apply-failed";
            try
            {
                if (endpoints is IDurableCompositeDeferredEndpointStore deferred)
                    return deferred.TryBeginOrResumeApplyAfter(
                        root.ToMutationContext(), endpoint, claim, snapshot, out failureCode);
                return endpoints.TryApplyAfter(
                        root.ToMutationContext(), endpoint, claim, out failureCode)
                    ? DurableCompositeDeferredApplyState.Applied
                    : DurableCompositeDeferredApplyState.FailedClosed;
            }
            catch
            {
                failureCode = "composite.endpoint.apply-exception";
                return DurableCompositeDeferredApplyState.FailedClosed;
            }
        }

        private static bool MarkerExactlyMatches(
            CompositeRootRecord root,
            DurableCompositeEndpointSnapshot snapshot) =>
            root != null && snapshot != null &&
            string.Equals(
                snapshot.AppliedWorldEpoch,
                root.OperationToken.WorldEpoch,
                StringComparison.Ordinal) &&
            snapshot.AppliedCommitSequence == root.CommitSequence;

        private static DurableCompositeEndpointReceipt ExpectedCommittedReceipt(
            CompositeRootRecord root,
            DurableCompositeEndpointIntent endpoint) =>
            new DurableCompositeEndpointReceipt(
                endpoint.StableEndpointId,
                endpoint.AfterFingerprint,
                endpoint.AfterSemanticRevision,
                root.OperationToken.WorldEpoch,
                root.CommitSequence);

        private static bool MarkerMatchesPredecessor(
            CompositeRootRecord root,
            EndpointId endpoint,
            DurableCompositeEndpointSnapshot snapshot,
            long checkpoint)
        {
            DurableCompositeEndpointPredecessor predecessor = root.Predecessors
                .FirstOrDefault(value => value.EndpointId == endpoint);
            if (predecessor != null)
                return string.Equals(
                           snapshot.AppliedWorldEpoch,
                           root.OperationToken.WorldEpoch,
                           StringComparison.Ordinal) &&
                       snapshot.AppliedCommitSequence == predecessor.CommitSequence;
            if (snapshot.AppliedCommitSequence == 0)
                return string.IsNullOrEmpty(snapshot.AppliedWorldEpoch);
            return string.Equals(
                       snapshot.AppliedWorldEpoch,
                       root.OperationToken.WorldEpoch,
                       StringComparison.Ordinal) &&
                   snapshot.AppliedCommitSequence <= checkpoint;
        }

        private static bool IsSupersededByLaterAppliedMarker(
            IReadOnlyList<CompositeRootRecord> history,
            int start,
            EndpointId endpoint,
            DurableCompositeEndpointSnapshot snapshot)
        {
            if (snapshot == null || snapshot.AppliedCommitSequence < 1) return false;
            for (int index = start; index < history.Count; index++)
                if (history[index].CommitSequence == snapshot.AppliedCommitSequence &&
                    string.Equals(
                        history[index].OperationToken.WorldEpoch,
                        snapshot.AppliedWorldEpoch,
                        StringComparison.Ordinal) &&
                    history[index].Endpoints.Any(candidate =>
                        candidate.StableEndpointId == endpoint))
                        return true;
            return false;
        }

        private static bool SameRootIdentity(
            CompositeRootRecord left,
            CompositeRootRecord right) =>
            left != null && right != null && left.OperationId == right.OperationId &&
            string.Equals(left.OwnerModuleId, right.OwnerModuleId, StringComparison.Ordinal) &&
            string.Equals(
                left.OperationToken.CanonicalValue,
                right.OperationToken.CanonicalValue,
                StringComparison.Ordinal) &&
            CompositeValidation.SameIdentity(left.ActorIdentity, right.ActorIdentity) &&
            string.Equals(left.RequestHash, right.RequestHash, StringComparison.Ordinal) &&
            string.Equals(left.IntentHash, right.IntentHash, StringComparison.Ordinal);

        private DurableCompositeOperationResult Result(
            DurableCompositeResultCode code,
            string reason,
            CompositeRootRecord root)
        {
            DurableCompositeOperationSnapshot snapshot = null;
            try { snapshot = root?.ToSnapshot(_owner); }
            catch { }
            return new DurableCompositeOperationResult(code, reason, snapshot);
        }

        private static DurableCompositeOperationResult AttachSnapshot(
            DurableCompositeOperationResult failure,
            CompositeRootRecord requested) => failure;

        private static DurableCompositeOperationResult Fail(
            DurableCompositeResultCode code,
            string reason) => new DurableCompositeOperationResult(code, reason, null);

        private static DurableCompositeTokenIssueResult TokenFailure(string reason) =>
            new DurableCompositeTokenIssueResult(
                DurableCompositeTokenIssueCode.FailedClosed,
                SafeReason(reason, "composite.token.failed"),
                null);

        private static DurableCompositeTokenCancellationResult TokenCancellationFailure(
            string reason) =>
            new DurableCompositeTokenCancellationResult(
                DurableCompositeTokenCancellationCode.FailedClosed,
                SafeReason(reason, "composite.token.cancel-failed"));

        private static string SafeReason(string value, string fallback)
        {
            try { return RunicIdentifier.Require(value, nameof(value)); }
            catch { return fallback; }
        }

        private enum ExistingAction
        {
            Commit,
            Abort,
            Recover,
            Status
        }
    }
}
