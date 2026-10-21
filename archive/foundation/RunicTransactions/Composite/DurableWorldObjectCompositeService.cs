using System;
using System.Collections.Generic;
using Runic.Foundation.Core;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    public sealed class DurableWorldObjectCompositeService :
        IDurableWorldObjectCompositeService,
        IDisposable
    {
        private readonly object _sync = new object();
        private readonly RunicRegistry _registry;
        private readonly IDurableCompositeOperationCoordinatorFactory _factory;
        private readonly ModuleRegistration _transactionsModule;
        private readonly WorldObjectEndpointStore _endpointStore;
        private readonly IDisposable _domainRegistration;
        private bool _disposed;

        public DurableWorldObjectCompositeService(
            RunicRegistry registry,
            IDurableCompositeOperationCoordinatorFactory factory,
            ModuleRegistration transactionsModule)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _transactionsModule = transactionsModule ??
                throw new ArgumentNullException(nameof(transactionsModule));
            if (!_registry.IsModuleRegistrationActive(_transactionsModule))
                throw new InvalidOperationException(
                    "The Transactions module lease is not active.");
            _endpointStore = new WorldObjectEndpointStore(_registry);
            _domainRegistration = _factory.RegisterEndpointDomain(
                _transactionsModule,
                DurableWorldObjectDomain.Descriptor,
                _endpointStore);
        }

        public IDisposable RegisterMutationProvider(
            ModuleRegistration providerModule,
            DurableWorldObjectMutationProviderDescriptor descriptor,
            IDurableWorldObjectMutationProvider provider)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                return _endpointStore.Register(providerModule, descriptor, provider);
            }
        }

        public IDurableCompositeOperationCoordinator CreateCoordinator(
            ModuleRegistration ownerModule)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                return _factory.Create(ownerModule, DurableWorldObjectDomain.DomainId);
            }
        }

        public DurableCompositeOperationIntent CreateOperationIntent(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            Runic.Foundation.Persistence.RpcPeerIdentity actorIdentity,
            string requestHash,
            byte[] exactIntent,
            IEnumerable<IDurableWorldObjectEndpointIntent> endpoints,
            DurableCompositeReconciliationRequirement reconciliationRequirement = null)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                if (ownerModule == null || !_registry.IsModuleRegistrationActive(ownerModule))
                    throw new InvalidOperationException(
                        "The world-object operation owner lease is not active.");
                if (operationToken == null) throw new ArgumentNullException(nameof(operationToken));
                var actor = CompositeValidation.RequireBackendIdentity(actorIdentity);
                if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
                var typed = new List<IDurableWorldObjectEndpointIntent>();
                var ordinals = new HashSet<int>();
                foreach (IDurableWorldObjectEndpointIntent endpoint in endpoints)
                {
                    if (typed.Count == DurableCompositeLimits.MaximumEndpoints)
                        throw new ArgumentOutOfRangeException(nameof(endpoints));
                    if (endpoint == null ||
                        !string.Equals(
                            endpoint.OperationToken.CanonicalValue,
                            operationToken.CanonicalValue,
                            StringComparison.Ordinal) ||
                        !CompositeValidation.SameIdentity(endpoint.ActorIdentity, actor) ||
                        !ordinals.Add(endpoint.EndpointOrdinal))
                        throw new ArgumentException(
                            "Typed endpoints must bind the exact token, actor, and unique ordinal.",
                            nameof(endpoints));
                    typed.Add(endpoint);
                }
                return new DurableCompositeOperationIntent(
                    ownerModule,
                    operationToken,
                    actor,
                    DurableWorldObjectDomain.DomainId,
                    requestHash,
                    exactIntent,
                    typed.ConvertAll(value => value.ToCompositeEndpointIntent()),
                    reconciliationRequirement,
                    DurableCompositeLimits.MaximumEndpoints);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _endpointStore.Dispose();
                _domainRegistration.Dispose();
            }
        }

        private sealed class WorldObjectEndpointStore :
            IDurableCompositeEndpointStore,
            IDurableCompositeOperationBoundEndpointStore,
            IDurableCompositeDeferredClaimEndpointStore,
            IDurableCompositeCompensatingEndpointStore,
            IDurableCompositePreparedAfterEndpointStore,
            IDurableCompositeDelegatedAuthorityEndpointStore,
            IDurableCompositeDeferredEndpointStore,
            IDisposable
        {
            private readonly object _sync = new object();
            private readonly RunicRegistry _registry;
            private readonly Dictionary<string, ProviderLease> _providers =
                new Dictionary<string, ProviderLease>(StringComparer.Ordinal);
            private readonly Dictionary<EndpointId, DurableCompositeEndpointClaim> _claims =
                new Dictionary<EndpointId, DurableCompositeEndpointClaim>();
            private readonly Dictionary<EndpointId, EndpointBinding> _bindings =
                new Dictionary<EndpointId, EndpointBinding>();
            private bool _disposed;

            internal WorldObjectEndpointStore(RunicRegistry registry)
            {
                _registry = registry;
            }

            internal IDisposable Register(
                ModuleRegistration module,
                DurableWorldObjectMutationProviderDescriptor descriptor,
                IDurableWorldObjectMutationProvider provider)
            {
                if (module == null) throw new ArgumentNullException(nameof(module));
                if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
                if (provider == null) throw new ArgumentNullException(nameof(provider));
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                    if (!_registry.IsModuleRegistrationActive(module))
                        throw new InvalidOperationException(
                            "The world-object provider lease is not active.");
                    if (_providers.TryGetValue(
                            descriptor.MutationKind, out ProviderLease current) && current.Active)
                        throw new InvalidOperationException(
                            "The world-object mutation kind already has an active provider.");
                    var lease = new ProviderLease(this, module, descriptor, provider);
                    _providers[descriptor.MutationKind] = lease;
                    return lease;
                }
            }

            public DurableCompositeEndpointReadState Read(
                EndpointId stableEndpointId,
                out DurableCompositeEndpointSnapshot snapshot,
                out string failureCode)
            {
                snapshot = null;
                failureCode = "composite.world-object.unavailable";
                if (!DurableWorldObjectCreationId.TryParseEndpoint(
                        stableEndpointId, out DurableWorldObjectCreationId creation) ||
                    !TryProvider(
                        creation.MutationKind,
                        0,
                        out IDurableWorldObjectMutationProvider provider,
                        out _))
                    return DurableCompositeEndpointReadState.Missing;
                try
                {
                    DurableWorldObjectReadState state = provider.Read(
                        creation,
                        out DurableWorldObjectAuthorityEvidence evidence,
                        out string reason);
                    bool usedUnenrolledTarget = false;
                    EndpointBinding binding = null;
                    lock (_sync) _bindings.TryGetValue(stableEndpointId, out binding);
                    if (state == DurableWorldObjectReadState.Absent &&
                        evidence != null &&
                        evidence.AppliedCommitSequence == 0 &&
                        binding != null &&
                        binding.Operation.Phase == DurableCompositeOperationPhase.Claiming &&
                        binding.Intent.RequiresIdentityEnrollment)
                    {
                        if (!(provider is IDurableWorldObjectIdentityEnrollmentProvider enrollment))
                        {
                            failureCode = "composite.world-object.enrollment-provider-required";
                            return DurableCompositeEndpointReadState.Unavailable;
                        }
                        state = enrollment.ReadExactUnenrolledTarget(
                            binding.Operation,
                            binding.Intent,
                            out evidence,
                            out reason);
                        usedUnenrolledTarget = true;
                    }
                    if (state == DurableWorldObjectReadState.Duplicate ||
                        state == DurableWorldObjectReadState.Corrupt)
                    {
                        failureCode = SafeReason(
                            reason,
                            state == DurableWorldObjectReadState.Duplicate
                                ? "composite.world-object.duplicate"
                                : "composite.world-object.corrupt");
                        return DurableCompositeEndpointReadState.Corrupt;
                    }
                    if (state == DurableWorldObjectReadState.Unavailable || evidence == null ||
                        evidence.State != state)
                    {
                        failureCode = SafeReason(
                            reason, "composite.world-object.unavailable");
                        return DurableCompositeEndpointReadState.Unavailable;
                    }
                    if (usedUnenrolledTarget && binding != null &&
                        binding.Intent.RequiresIdentityEnrollment &&
                        state == DurableWorldObjectReadState.Present &&
                        (!string.Equals(
                             evidence.Fingerprint,
                             binding.Intent.BeforeFingerprint,
                             StringComparison.Ordinal) ||
                         !string.Equals(
                             evidence.SemanticRevision,
                             binding.Intent.BeforeSemanticRevision,
                             StringComparison.Ordinal)))
                    {
                        failureCode = "composite.world-object.enrollment-evidence-conflict";
                        return DurableCompositeEndpointReadState.Corrupt;
                    }
                    DurableCompositeEndpointClaim claim;
                    lock (_sync) _claims.TryGetValue(stableEndpointId, out claim);
                    snapshot = new DurableCompositeEndpointSnapshot(
                        stableEndpointId,
                        evidence.Fingerprint,
                        evidence.SemanticRevision,
                        evidence.StableIdentityCurrent,
                        evidence.Synchronized,
                        evidence.ServerOwned,
                        evidence.DestroyPending,
                        claim,
                        evidence.AppliedWorldEpoch,
                        evidence.AppliedCommitSequence);
                    failureCode = "composite.world-object.ready";
                    return DurableCompositeEndpointReadState.Ready;
                }
                catch
                {
                    failureCode = "composite.world-object.read-exception";
                    return DurableCompositeEndpointReadState.Unavailable;
                }
            }

            public DurableCompositeOperationBindState TryBindOperation(
                DurableCompositeRollbackContext operation,
                IReadOnlyList<DurableCompositeEndpointIntent> endpoints,
                out string failureCode)
            {
                failureCode = "composite.world-object.binding-invalid";
                if (operation == null || endpoints == null || endpoints.Count < 1 ||
                    endpoints.Count > DurableCompositeLimits.MaximumEndpoints ||
                    !string.Equals(
                        operation.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    operation.EndpointDomainSchemaVersion != DurableWorldObjectDomain.SchemaVersion)
                    return DurableCompositeOperationBindState.FailedClosed;

                var additions = new List<EndpointBinding>();
                foreach (DurableCompositeEndpointIntent endpoint in endpoints)
                {
                    if (!TryDecodeOperationEndpoint(
                            operation,
                            endpoint,
                            out IDurableWorldObjectEndpointIntent intent))
                        return DurableCompositeOperationBindState.FailedClosed;
                    if (!TryProvider(
                            intent.Provider.MutationKind,
                            intent.Provider.SchemaVersion,
                            out IDurableWorldObjectMutationProvider provider,
                            out _))
                    {
                        failureCode = "composite.world-object.binding-provider-missing";
                        return DurableCompositeOperationBindState.NotReady;
                    }
                    if (!intent.RequiresIdentityEnrollment) continue;
                    if (!(provider is IDurableWorldObjectIdentityEnrollmentProvider))
                    {
                        failureCode = "composite.world-object.enrollment-provider-required";
                        return DurableCompositeOperationBindState.FailedClosed;
                    }
                    additions.Add(new EndpointBinding(operation, endpoint, intent));
                }

                lock (_sync)
                {
                    if (_disposed)
                    {
                        failureCode = "composite.world-object.binding-disposed";
                        return DurableCompositeOperationBindState.NotReady;
                    }
                    int newCount = 0;
                    foreach (EndpointBinding addition in additions)
                    {
                        if (_bindings.TryGetValue(
                                addition.Endpoint.StableEndpointId,
                                out EndpointBinding current))
                        {
                            if (!current.Matches(addition))
                            {
                                failureCode = "composite.world-object.binding-conflict";
                                return DurableCompositeOperationBindState.FailedClosed;
                            }
                        }
                        else newCount++;
                    }
                    long maximumBindings =
                        (long)DurableCompositeLimits.MaximumUnresolvedOperations *
                        DurableCompositeLimits.MaximumEndpoints;
                    if ((long)_bindings.Count + newCount > maximumBindings)
                    {
                        failureCode = "composite.world-object.binding-capacity";
                        return DurableCompositeOperationBindState.NotReady;
                    }
                    foreach (EndpointBinding addition in additions)
                        _bindings[addition.Endpoint.StableEndpointId] = addition;
                    failureCode = "composite.world-object.binding-ready";
                    return DurableCompositeOperationBindState.Bound;
                }
            }

            public DurableCompositeDeferredClaimState TryBeginOrResumeAcquireClaim(
                DurableCompositeRollbackContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                DurableCompositeEndpointSnapshot authoritativeSnapshot,
                out string failureCode)
            {
                failureCode = "composite.world-object.claim-invalid";
                if (!TryDecodeOperationEndpoint(
                        operation,
                        endpoint,
                        out IDurableWorldObjectEndpointIntent intent) ||
                    exactClaim == null || authoritativeSnapshot == null ||
                    authoritativeSnapshot.StableEndpointId != endpoint.StableEndpointId ||
                    authoritativeSnapshot.Claim != null ||
                    !string.Equals(
                        exactClaim.OwnerModuleId,
                        operation.OwnerModuleId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        exactClaim.EndpointDomainId,
                        operation.EndpointDomainId,
                        StringComparison.Ordinal) ||
                    exactClaim.OperationId != operation.OperationId ||
                    !CompositeValidation.SameIdentity(
                        exactClaim.ActorIdentity,
                        operation.ActorIdentity) ||
                    !string.Equals(
                        exactClaim.IntentHash,
                        operation.IntentHash,
                        StringComparison.Ordinal) ||
                    exactClaim.StableEndpointId != endpoint.StableEndpointId ||
                    !string.Equals(
                        exactClaim.BeforeFingerprint,
                        endpoint.BeforeFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        exactClaim.PreparedSemanticRevision,
                        endpoint.PreparedSemanticRevision,
                        StringComparison.Ordinal) ||
                    !TryProvider(
                        intent.Provider.MutationKind,
                        intent.Provider.SchemaVersion,
                        out IDurableWorldObjectMutationProvider provider,
                        out _))
                    return DurableCompositeDeferredClaimState.FailedClosed;

                if (!intent.RequiresIdentityEnrollment)
                {
                    if (!ExactClaimAuthority(
                            operation,
                            intent,
                            provider,
                            authoritativeSnapshot,
                            out failureCode))
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    return TryAcquireClaim(exactClaim, out failureCode)
                        ? DurableCompositeDeferredClaimState.Acquired
                        : DurableCompositeDeferredClaimState.FailedClosed;
                }

                EndpointBinding binding;
                lock (_sync) _bindings.TryGetValue(endpoint.StableEndpointId, out binding);
                if (binding == null ||
                    !binding.Matches(new EndpointBinding(operation, endpoint, intent)) ||
                    !(provider is IDurableWorldObjectIdentityEnrollmentProvider enrollment))
                {
                    failureCode = "composite.world-object.enrollment-binding-missing";
                    return DurableCompositeDeferredClaimState.FailedClosed;
                }

                try
                {
                    DurableWorldObjectReadState taggedState = provider.Read(
                        intent.ObjectId,
                        out DurableWorldObjectAuthorityEvidence tagged,
                        out string reason);
                    if (taggedState == DurableWorldObjectReadState.Duplicate ||
                        taggedState == DurableWorldObjectReadState.Corrupt)
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-tag-conflict");
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }
                    if (ExactEnrollmentBefore(intent, tagged) ||
                        operation.Phase != DurableCompositeOperationPhase.Claiming &&
                        ExactEnrollmentPreparedAfter(intent, tagged))
                    {
                        return TryAcquireClaim(exactClaim, out failureCode)
                            ? DurableCompositeDeferredClaimState.Acquired
                            : DurableCompositeDeferredClaimState.FailedClosed;
                    }
                    if (operation.Phase != DurableCompositeOperationPhase.Claiming)
                    {
                        failureCode = "composite.world-object.enrollment-readback-conflict";
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }
                    if (taggedState != DurableWorldObjectReadState.Absent || tagged == null ||
                        tagged.AppliedCommitSequence != 0)
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-tag-unavailable");
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }

                    DurableWorldObjectReadState targetState =
                        enrollment.ReadExactUnenrolledTarget(
                            operation,
                            intent,
                            out DurableWorldObjectAuthorityEvidence target,
                            out reason);
                    if (targetState != DurableWorldObjectReadState.Present ||
                        !ExactEnrollmentBefore(intent, target) ||
                        !EvidenceMatchesSnapshot(target, authoritativeSnapshot))
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-target-conflict");
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }

                    DurableWorldObjectIdentityEnrollmentState enrollmentState =
                        enrollment.TryBeginOrResumeIdentityEnrollment(
                            operation, intent, target, out reason);
                    if (!Enum.IsDefined(
                            typeof(DurableWorldObjectIdentityEnrollmentState),
                            enrollmentState) ||
                        enrollmentState == DurableWorldObjectIdentityEnrollmentState.FailedClosed)
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-failed");
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }
                    if (enrollmentState == DurableWorldObjectIdentityEnrollmentState.Pending)
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-pending");
                        return DurableCompositeDeferredClaimState.Pending;
                    }

                    taggedState = provider.Read(intent.ObjectId, out tagged, out reason);
                    if (taggedState != DurableWorldObjectReadState.Present ||
                        !ExactEnrollmentBefore(intent, tagged))
                    {
                        failureCode = SafeReason(
                            reason,
                            "composite.world-object.enrollment-readback-failed");
                        return DurableCompositeDeferredClaimState.FailedClosed;
                    }
                    return TryAcquireClaim(exactClaim, out failureCode)
                        ? DurableCompositeDeferredClaimState.Acquired
                        : DurableCompositeDeferredClaimState.FailedClosed;
                }
                catch
                {
                    failureCode = "composite.world-object.enrollment-exception";
                    return DurableCompositeDeferredClaimState.FailedClosed;
                }
            }

            public bool TryAcquireClaim(
                DurableCompositeEndpointClaim claim,
                out string failureCode)
            {
                failureCode = "composite.world-object.claim-invalid";
                if (claim == null ||
                    !string.Equals(
                        claim.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    !DurableWorldObjectCreationId.TryParseEndpoint(
                        claim.StableEndpointId, out DurableWorldObjectCreationId creation) ||
                    !TryProvider(creation.MutationKind, 0, out _, out _)) return false;
                lock (_sync)
                {
                    if (_disposed) return false;
                    if (_claims.TryGetValue(
                            claim.StableEndpointId,
                            out DurableCompositeEndpointClaim existing))
                    {
                        bool exact = claim.MatchesExact(existing);
                        failureCode = exact
                            ? "composite.world-object.claim-replay"
                            : "composite.world-object.claim-conflict";
                        return exact;
                    }
                    if (_claims.Count >= DurableCompositeLimits.MaximumUnresolvedOperations *
                                         DurableCompositeLimits.MaximumEndpoints)
                    {
                        failureCode = "composite.world-object.claim-capacity";
                        return false;
                    }
                    _claims.Add(claim.StableEndpointId, claim);
                    failureCode = "composite.world-object.claimed";
                    return true;
                }
            }

            public bool TryApplyAfter(
                DurableCompositeMutationContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                out string failureCode)
            {
                failureCode = "composite.world-object.apply-invalid";
                if (!TryDecodeBound(
                        operation,
                        endpoint,
                        exactClaim,
                        out IDurableWorldObjectEndpointIntent intent,
                        out IDurableWorldObjectMutationProvider provider)) return false;
                try
                {
                    if (!provider.TryApplyDesiredState(operation, intent, out string reason))
                    {
                        failureCode = SafeReason(
                            reason, "composite.world-object.apply-failed");
                        return false;
                    }
                    DurableWorldObjectReadState state = provider.Read(
                        intent.ObjectId,
                        out DurableWorldObjectAuthorityEvidence readback,
                        out reason);
                    DurableWorldObjectReadState expectedState = intent.TransitionKind ==
                            DurableWorldObjectTransitionKind.RemoveToAbsence
                        ? DurableWorldObjectReadState.Absent
                        : DurableWorldObjectReadState.Present;
                    if (state != expectedState || readback == null ||
                        !string.Equals(
                            readback.Fingerprint,
                            endpoint.AfterFingerprint,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            readback.SemanticRevision,
                            endpoint.AfterSemanticRevision,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            readback.AppliedWorldEpoch,
                            operation.WorldEpoch,
                            StringComparison.Ordinal) ||
                        readback.AppliedCommitSequence != operation.CommitSequence ||
                        !readback.StableIdentityCurrent || !readback.Synchronized ||
                        !readback.ServerOwned || readback.DestroyPending)
                    {
                        failureCode = "composite.world-object.after-readback-failed";
                        return false;
                    }
                    failureCode = "composite.world-object.applied";
                    return true;
                }
                catch
                {
                    failureCode = "composite.world-object.apply-exception";
                    return false;
                }
            }

            public DurableCompositeDeferredApplyState TryBeginOrResumeApplyAfter(
                DurableCompositeMutationContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                DurableCompositeEndpointSnapshot authoritativeSnapshot,
                out string failureCode)
            {
                failureCode = "composite.world-object.apply-invalid";
                if (authoritativeSnapshot == null ||
                    authoritativeSnapshot.StableEndpointId != endpoint?.StableEndpointId ||
                    !TryDecodeBound(
                        operation,
                        endpoint,
                        exactClaim,
                        out IDurableWorldObjectEndpointIntent intent,
                        out IDurableWorldObjectMutationProvider provider))
                    return DurableCompositeDeferredApplyState.FailedClosed;

                if (!(provider is IDurableWorldObjectDeferredMutationProvider deferred))
                    return TryApplyAfter(operation, endpoint, exactClaim, out failureCode)
                        ? DurableCompositeDeferredApplyState.Applied
                        : DurableCompositeDeferredApplyState.FailedClosed;
                try
                {
                    DurableCompositeDeferredApplyState state =
                        deferred.TryBeginOrResumeDesiredState(
                            operation, intent, out string reason);
                    if (!Enum.IsDefined(typeof(DurableCompositeDeferredApplyState), state))
                    {
                        failureCode = "composite.world-object.deferred-state-invalid";
                        return DurableCompositeDeferredApplyState.FailedClosed;
                    }
                    failureCode = SafeReason(
                        reason,
                        state == DurableCompositeDeferredApplyState.Pending
                            ? "composite.world-object.owner-apply-pending"
                            : state == DurableCompositeDeferredApplyState.Applied
                                ? "composite.world-object.owner-apply-observed"
                                : "composite.world-object.owner-apply-failed");
                    return state;
                }
                catch
                {
                    failureCode = "composite.world-object.owner-apply-exception";
                    return DurableCompositeDeferredApplyState.FailedClosed;
                }
            }

            public bool IsExactDelegatedMutationAuthorityCurrent(
                DurableCompositeRollbackContext operation,
                long expectedCommitSequence,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointSnapshot snapshot,
                out string failureCode)
            {
                failureCode = "composite.world-object.owner-authority-invalid";
                if (operation == null || endpoint == null || snapshot == null ||
                    expectedCommitSequence < 0) return false;
                if (!string.Equals(
                        operation.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    operation.EndpointDomainSchemaVersion != DurableWorldObjectDomain.SchemaVersion)
                {
                    failureCode = "composite.world-object.owner-domain-mismatch";
                    return false;
                }
                if (!DurableWorldObjectCodec.TryDecode(
                        endpoint.ExactMutation, out IDurableWorldObjectEndpointIntent intent))
                {
                    failureCode = "composite.world-object.owner-intent-invalid";
                    return false;
                }
                if (intent.ObjectId.EndpointId != endpoint.StableEndpointId ||
                    snapshot.StableEndpointId != endpoint.StableEndpointId ||
                    !string.Equals(
                        intent.OperationToken.CanonicalValue,
                        operation.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(
                        intent.ActorIdentity, operation.ActorIdentity) ||
                    !string.Equals(
                        endpoint.BeforeFingerprint,
                        intent.BeforeFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.PreparedSemanticRevision,
                        intent.BeforeSemanticRevision,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.AfterFingerprint,
                        intent.AfterFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.AfterSemanticRevision,
                        intent.AfterSemanticRevision,
                        StringComparison.Ordinal))
                {
                    failureCode = "composite.world-object.owner-intent-mismatch";
                    return false;
                }
                if (!TryProvider(
                        intent.Provider.MutationKind,
                        intent.Provider.SchemaVersion,
                        out IDurableWorldObjectMutationProvider provider,
                        out _))
                {
                    failureCode = "composite.world-object.owner-provider-missing";
                    return false;
                }
                try
                {
                    DurableWorldObjectReadState state = provider.Read(
                        intent.ObjectId,
                        out DurableWorldObjectAuthorityEvidence evidence,
                        out string readReason);
                    if (evidence == null || evidence.State != state ||
                        !EvidenceMatchesSnapshot(evidence, snapshot))
                    {
                        failureCode = SafeReason(
                            readReason, "composite.world-object.owner-authority-stale");
                        return false;
                    }

                    if (!(provider is IDurableWorldObjectDeferredMutationProvider delegated))
                    {
                        failureCode = "composite.world-object.owner-provider-required";
                        return false;
                    }

                    // Once the exact desired state carries this root's durable marker, a client
                    // disconnect cannot prevent the server from publishing the terminal receipt.
                    if (expectedCommitSequence > 0 &&
                        string.Equals(
                            evidence.Fingerprint,
                            endpoint.AfterFingerprint,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            evidence.SemanticRevision,
                            endpoint.AfterSemanticRevision,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            evidence.AppliedWorldEpoch,
                            operation.OperationToken.WorldEpoch,
                            StringComparison.Ordinal) &&
                        evidence.AppliedCommitSequence == expectedCommitSequence)
                    {
                        failureCode = "composite.world-object.owner-apply-observed";
                        return true;
                    }
                    bool current = delegated.IsExactCurrentOwnerMutationAuthority(
                        operation,
                        expectedCommitSequence,
                        intent,
                        evidence,
                        out string authorityReason);
                    failureCode = SafeReason(
                        authorityReason,
                        current
                            ? "composite.world-object.owner-authority-current"
                            : "composite.world-object.owner-authority-stale");
                    return current;
                }
                catch
                {
                    failureCode = "composite.world-object.owner-authority-exception";
                    return false;
                }
            }

            public bool TryRestoreBefore(
                DurableCompositeRollbackContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                out string failureCode)
            {
                failureCode = "composite.world-object.compensation-invalid";
                if (operation == null || endpoint == null || exactClaim == null ||
                    !DurableWorldObjectCodec.TryDecode(
                        endpoint.ExactMutation, out IDurableWorldObjectEndpointIntent intent) ||
                    operation.OperationId != intent.OperationToken.OperationId ||
                    !string.Equals(
                        operation.OperationToken.CanonicalValue,
                        intent.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(
                        operation.ActorIdentity,
                        intent.ActorIdentity) ||
                    !exactClaim.MatchesExact(new DurableCompositeEndpointClaim(
                        exactClaim.OwnerModuleId,
                        exactClaim.EndpointDomainId,
                        exactClaim.OperationId,
                        exactClaim.ActorIdentity,
                        exactClaim.IntentHash,
                        endpoint)) ||
                    !TryProvider(
                        intent.Provider.MutationKind,
                        intent.Provider.SchemaVersion,
                        out IDurableWorldObjectMutationProvider provider,
                        out _)) return false;
                try
                {
                    if (!provider.TryCompensateToBefore(operation, intent, out string reason))
                    {
                        failureCode = SafeReason(
                            reason, "composite.world-object.compensation-failed");
                        return false;
                    }
                    DurableWorldObjectReadState state = provider.Read(
                        intent.ObjectId,
                        out DurableWorldObjectAuthorityEvidence evidence,
                        out reason);
                    DurableWorldObjectReadState expectedState = intent.TransitionKind ==
                            DurableWorldObjectTransitionKind.CreateFromAbsence
                        ? DurableWorldObjectReadState.Absent
                        : DurableWorldObjectReadState.Present;
                    if (state != expectedState || evidence == null ||
                        !string.Equals(
                            evidence.Fingerprint,
                            endpoint.BeforeFingerprint,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            evidence.SemanticRevision,
                            endpoint.PreparedSemanticRevision,
                            StringComparison.Ordinal))
                    {
                        failureCode = "composite.world-object.compensation-readback-failed";
                        return false;
                    }
                    failureCode = "composite.world-object.compensated";
                    return true;
                }
                catch
                {
                    failureCode = "composite.world-object.compensation-exception";
                    return false;
                }
            }

            public bool IsExactPreparedAfter(
                DurableCompositeRollbackContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                DurableCompositeEndpointSnapshot snapshot,
                out string failureCode)
            {
                failureCode = "composite.world-object.staged-invalid";
                if (operation == null || endpoint == null || exactClaim == null || snapshot == null ||
                    !string.Equals(
                        operation.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    operation.EndpointDomainSchemaVersion != DurableWorldObjectDomain.SchemaVersion ||
                    operation.OperationId != exactClaim.OperationId ||
                    !CompositeValidation.SameIdentity(
                        operation.ActorIdentity, exactClaim.ActorIdentity) ||
                    !string.Equals(
                        operation.IntentHash, exactClaim.IntentHash, StringComparison.Ordinal) ||
                    endpoint.StableEndpointId != exactClaim.StableEndpointId ||
                    snapshot.StableEndpointId != endpoint.StableEndpointId ||
                    snapshot.Claim != null && !exactClaim.MatchesExact(snapshot.Claim) ||
                    !string.Equals(
                        snapshot.Fingerprint, endpoint.AfterFingerprint, StringComparison.Ordinal) ||
                    !string.Equals(
                        snapshot.SemanticRevision,
                        endpoint.AfterSemanticRevision,
                        StringComparison.Ordinal) ||
                    !DurableWorldObjectCodec.TryDecode(
                        endpoint.ExactMutation, out IDurableWorldObjectEndpointIntent intent) ||
                    intent.ObjectId.EndpointId != endpoint.StableEndpointId ||
                    !string.Equals(
                        intent.OperationToken.CanonicalValue,
                        operation.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(
                        intent.ActorIdentity, operation.ActorIdentity) ||
                    !TryProvider(
                        intent.Provider.MutationKind,
                        intent.Provider.SchemaVersion,
                        out _,
                        out _)) return false;
                failureCode = "composite.world-object.staged-exact";
                return true;
            }

            public bool TryReleaseClaim(
                DurableCompositeEndpointClaim exactClaim,
                out string failureCode)
            {
                failureCode = "composite.world-object.release-invalid";
                if (exactClaim == null) return false;
                lock (_sync)
                {
                    if (_disposed) return false;
                    if (!_claims.TryGetValue(
                            exactClaim.StableEndpointId,
                            out DurableCompositeEndpointClaim current))
                    {
                        RemoveBindingLocked(exactClaim);
                        failureCode = "composite.world-object.release-replay";
                        return true;
                    }
                    if (!exactClaim.MatchesExact(current))
                    {
                        failureCode = "composite.world-object.claim-conflict";
                        return false;
                    }
                    _claims.Remove(exactClaim.StableEndpointId);
                    RemoveBindingLocked(exactClaim);
                    failureCode = "composite.world-object.released";
                    return true;
                }
            }

            private bool TryDecodeBound(
                DurableCompositeMutationContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim claim,
                out IDurableWorldObjectEndpointIntent intent,
                out IDurableWorldObjectMutationProvider provider)
            {
                intent = null;
                provider = null;
                if (operation == null || endpoint == null || claim == null ||
                    !string.Equals(
                        operation.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    operation.EndpointDomainSchemaVersion != DurableWorldObjectDomain.SchemaVersion ||
                    operation.OperationId != claim.OperationId ||
                    !CompositeValidation.SameIdentity(
                        operation.ActorIdentity, claim.ActorIdentity) ||
                    !string.Equals(operation.IntentHash, claim.IntentHash, StringComparison.Ordinal) ||
                    endpoint.StableEndpointId != claim.StableEndpointId ||
                    !DurableWorldObjectCodec.TryDecode(
                        endpoint.ExactMutation, out intent) ||
                    intent.ObjectId.EndpointId != endpoint.StableEndpointId ||
                    !string.Equals(
                        intent.OperationToken.CanonicalValue,
                        operation.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(
                        intent.ActorIdentity, operation.ActorIdentity) ||
                    !string.Equals(
                        endpoint.AfterFingerprint,
                        intent.AfterFingerprint,
                        StringComparison.Ordinal) ||
                    !TryProvider(
                        intent.Provider.MutationKind,
                        intent.Provider.SchemaVersion,
                        out provider,
                        out _))
                {
                    intent = null;
                    provider = null;
                    return false;
                }
                lock (_sync)
                    return _claims.TryGetValue(
                               endpoint.StableEndpointId,
                               out DurableCompositeEndpointClaim current) &&
                           claim.MatchesExact(current);
            }

            private bool TryDecodeOperationEndpoint(
                DurableCompositeRollbackContext operation,
                DurableCompositeEndpointIntent endpoint,
                out IDurableWorldObjectEndpointIntent intent)
            {
                intent = null;
                if (operation == null || endpoint == null ||
                    !string.Equals(
                        operation.EndpointDomainId,
                        DurableWorldObjectDomain.DomainId,
                        StringComparison.Ordinal) ||
                    operation.EndpointDomainSchemaVersion != DurableWorldObjectDomain.SchemaVersion ||
                    !DurableWorldObjectCodec.TryDecode(endpoint.ExactMutation, out intent) ||
                    intent.ObjectId.EndpointId != endpoint.StableEndpointId ||
                    !string.Equals(
                        intent.OperationToken.CanonicalValue,
                        operation.OperationToken.CanonicalValue,
                        StringComparison.Ordinal) ||
                    !CompositeValidation.SameIdentity(
                        intent.ActorIdentity, operation.ActorIdentity) ||
                    !string.Equals(
                        endpoint.BeforeFingerprint,
                        intent.BeforeFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.PreparedSemanticRevision,
                        intent.BeforeSemanticRevision,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.AfterFingerprint,
                        intent.AfterFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        endpoint.AfterSemanticRevision,
                        intent.AfterSemanticRevision,
                        StringComparison.Ordinal))
                {
                    intent = null;
                    return false;
                }
                return true;
            }

            private static bool ExactEnrollmentBefore(
                IDurableWorldObjectEndpointIntent intent,
                DurableWorldObjectAuthorityEvidence evidence) =>
                intent != null && evidence != null &&
                evidence.State == DurableWorldObjectReadState.Present &&
                evidence.StableIdentityCurrent &&
                evidence.Synchronized &&
                !evidence.DestroyPending &&
                string.Equals(
                    evidence.Fingerprint,
                    intent.BeforeFingerprint,
                    StringComparison.Ordinal) &&
                string.Equals(
                    evidence.SemanticRevision,
                    intent.BeforeSemanticRevision,
                    StringComparison.Ordinal);

            private static bool ExactEnrollmentPreparedAfter(
                IDurableWorldObjectEndpointIntent intent,
                DurableWorldObjectAuthorityEvidence evidence) =>
                intent != null && evidence != null &&
                evidence.State == (intent.TransitionKind ==
                        DurableWorldObjectTransitionKind.RemoveToAbsence
                    ? DurableWorldObjectReadState.Absent
                    : DurableWorldObjectReadState.Present) &&
                evidence.StableIdentityCurrent &&
                evidence.Synchronized &&
                !evidence.DestroyPending &&
                evidence.AppliedCommitSequence == 0 &&
                string.Equals(
                    evidence.Fingerprint,
                    intent.AfterFingerprint,
                    StringComparison.Ordinal) &&
                string.Equals(
                    evidence.SemanticRevision,
                    intent.AfterSemanticRevision,
                    StringComparison.Ordinal);

            private static bool ExactClaimAuthority(
                DurableCompositeRollbackContext operation,
                IDurableWorldObjectEndpointIntent intent,
                IDurableWorldObjectMutationProvider provider,
                DurableCompositeEndpointSnapshot snapshot,
                out string failureCode)
            {
                failureCode = "composite.world-object.claim-authority-stale";
                if (operation == null || intent == null || provider == null || snapshot == null ||
                    snapshot.StableEndpointId != intent.ObjectId.EndpointId ||
                    !snapshot.StableIdentityCurrent || !snapshot.Synchronized ||
                    snapshot.DestroyPending) return false;
                bool exactBefore = string.Equals(
                        snapshot.Fingerprint,
                        intent.BeforeFingerprint,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        snapshot.SemanticRevision,
                        intent.BeforeSemanticRevision,
                        StringComparison.Ordinal);
                bool exactPreparedAfter = operation.Phase !=
                        DurableCompositeOperationPhase.Claiming &&
                    snapshot.AppliedCommitSequence == 0 &&
                    string.Equals(
                        snapshot.Fingerprint,
                        intent.AfterFingerprint,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        snapshot.SemanticRevision,
                        intent.AfterSemanticRevision,
                        StringComparison.Ordinal);
                if (!exactBefore && !exactPreparedAfter) return false;
                if (snapshot.ServerOwned)
                {
                    failureCode = "composite.world-object.claim-server-authority-current";
                    return true;
                }
                if (!(provider is IDurableWorldObjectDeferredMutationProvider delegated))
                    return false;
                DurableWorldObjectReadState state = provider.Read(
                    intent.ObjectId,
                    out DurableWorldObjectAuthorityEvidence evidence,
                    out string reason);
                if (state != DurableWorldObjectReadState.Present ||
                    !EvidenceMatchesSnapshot(evidence, snapshot))
                {
                    failureCode = SafeReason(
                        reason,
                        "composite.world-object.claim-authority-readback-failed");
                    return false;
                }
                bool current = delegated.IsExactCurrentOwnerMutationAuthority(
                    operation, 0, intent, evidence, out reason);
                failureCode = SafeReason(
                    reason,
                    current
                        ? "composite.world-object.claim-owner-authority-current"
                        : "composite.world-object.claim-owner-authority-stale");
                return current;
            }

            private void RemoveBindingLocked(DurableCompositeEndpointClaim claim)
            {
                if (claim == null || !_bindings.TryGetValue(
                        claim.StableEndpointId, out EndpointBinding binding)) return;
                if (binding.Operation.OperationId == claim.OperationId &&
                    string.Equals(
                        binding.Operation.OwnerModuleId,
                        claim.OwnerModuleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        binding.Operation.IntentHash,
                        claim.IntentHash,
                        StringComparison.Ordinal) &&
                    CompositeValidation.SameIdentity(
                        binding.Operation.ActorIdentity,
                        claim.ActorIdentity))
                    _bindings.Remove(claim.StableEndpointId);
            }

            private bool TryProvider(
                string kind,
                int requiredSchema,
                out IDurableWorldObjectMutationProvider provider,
                out DurableWorldObjectMutationProviderDescriptor descriptor)
            {
                lock (_sync)
                {
                    provider = null;
                    descriptor = null;
                    if (_disposed || !_providers.TryGetValue(kind, out ProviderLease lease) ||
                        !lease.Active || !_registry.IsModuleRegistrationActive(lease.Module) ||
                        requiredSchema > 0 && lease.Descriptor.SchemaVersion != requiredSchema)
                        return false;
                    provider = lease.Provider;
                    descriptor = lease.Descriptor;
                    return true;
                }
            }

            private static bool EvidenceMatchesSnapshot(
                DurableWorldObjectAuthorityEvidence evidence,
                DurableCompositeEndpointSnapshot snapshot) =>
                evidence != null && snapshot != null &&
                string.Equals(
                    evidence.Fingerprint, snapshot.Fingerprint, StringComparison.Ordinal) &&
                string.Equals(
                    evidence.SemanticRevision,
                    snapshot.SemanticRevision,
                    StringComparison.Ordinal) &&
                evidence.StableIdentityCurrent == snapshot.StableIdentityCurrent &&
                evidence.Synchronized == snapshot.Synchronized &&
                evidence.ServerOwned == snapshot.ServerOwned &&
                evidence.DestroyPending == snapshot.DestroyPending &&
                string.Equals(
                    evidence.AppliedWorldEpoch,
                    snapshot.AppliedWorldEpoch,
                    StringComparison.Ordinal) &&
                evidence.AppliedCommitSequence == snapshot.AppliedCommitSequence;

            internal void Unregister(ProviderLease lease)
            {
                lock (_sync)
                    if (_providers.TryGetValue(
                            lease.Descriptor.MutationKind,
                            out ProviderLease current) && ReferenceEquals(current, lease))
                        _providers.Remove(lease.Descriptor.MutationKind);
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    foreach (ProviderLease lease in _providers.Values) lease.Deactivate();
                    _providers.Clear();
                    _claims.Clear();
                    _bindings.Clear();
                }
            }

            private static string SafeReason(string value, string fallback)
            {
                try { return RunicIdentifier.Require(value, nameof(value)); }
                catch { return fallback; }
            }

            private sealed class EndpointBinding
            {
                internal EndpointBinding(
                    DurableCompositeRollbackContext operation,
                    DurableCompositeEndpointIntent endpoint,
                    IDurableWorldObjectEndpointIntent intent)
                {
                    Operation = operation;
                    Endpoint = endpoint;
                    Intent = intent;
                }

                internal DurableCompositeRollbackContext Operation { get; }
                internal DurableCompositeEndpointIntent Endpoint { get; }
                internal IDurableWorldObjectEndpointIntent Intent { get; }

                internal bool Matches(EndpointBinding other) =>
                    other != null &&
                    Operation.OperationId == other.Operation.OperationId &&
                    string.Equals(
                        Operation.OwnerModuleId,
                        other.Operation.OwnerModuleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        Operation.IntentHash,
                        other.Operation.IntentHash,
                        StringComparison.Ordinal) &&
                    CompositeValidation.SameIdentity(
                        Operation.ActorIdentity,
                        other.Operation.ActorIdentity) &&
                    Endpoint.StableEndpointId == other.Endpoint.StableEndpointId &&
                    ExactBytesEqual(Endpoint.ExactMutation, other.Endpoint.ExactMutation);
            }

            private static bool ExactBytesEqual(byte[] left, byte[] right)
            {
                if (left == null || right == null || left.Length != right.Length) return false;
                int difference = 0;
                for (int index = 0; index < left.Length; index++)
                    difference |= left[index] ^ right[index];
                return difference == 0;
            }

            internal sealed class ProviderLease : IDisposable
            {
                private readonly WorldObjectEndpointStore _owner;
                private bool _active = true;

                internal ProviderLease(
                    WorldObjectEndpointStore owner,
                    ModuleRegistration module,
                    DurableWorldObjectMutationProviderDescriptor descriptor,
                    IDurableWorldObjectMutationProvider provider)
                {
                    _owner = owner;
                    Module = module;
                    Descriptor = descriptor;
                    Provider = provider;
                }

                internal ModuleRegistration Module { get; }
                internal DurableWorldObjectMutationProviderDescriptor Descriptor { get; }
                internal IDurableWorldObjectMutationProvider Provider { get; }
                internal bool Active => _active;

                public void Dispose()
                {
                    if (!_active) return;
                    _active = false;
                    _owner.Unregister(this);
                }

                internal void Deactivate() => _active = false;
            }
        }
    }
}
