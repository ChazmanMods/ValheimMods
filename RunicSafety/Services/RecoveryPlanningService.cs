using System;
using System.Collections.Generic;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class RecoveryPlanningService : IRecoveryPlanningService
    {
        private const int MaximumProviders = 8;
        private readonly object _sync = new object();
        private readonly List<ProviderEntry> _providers = new List<ProviderEntry>();
        private readonly ISafetyDiagnosticService _diagnostics;
        private long _token;

        public RecoveryPlanningService(ISafetyDiagnosticService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public bool HasTopologyProvider
        {
            get { lock (_sync) return _providers.Count != 0; }
        }

        public IDisposable RegisterTopologyProvider(IInventoryTopologyProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.ProviderId) || provider.ProviderId.Length > 96)
                throw new ArgumentException("A bounded topology provider ID is required.", nameof(provider));
            lock (_sync)
            {
                if (_providers.Count >= MaximumProviders)
                    throw new InvalidOperationException("The topology provider limit is 8.");
                foreach (ProviderEntry existing in _providers)
                    if (string.Equals(existing.Provider.ProviderId, provider.ProviderId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Topology provider already registered: " + provider.ProviderId);
                long token = ++_token;
                _providers.Add(new ProviderEntry(provider, token));
                _providers.Sort((left, right) =>
                    StringComparer.Ordinal.Compare(left.Provider.ProviderId, right.Provider.ProviderId));
                return new Registration(this, token);
            }
        }

        public bool TryCaptureTopology(
            long playerId,
            out InventoryTopologySnapshot snapshot,
            out string failureCode)
        {
            ProviderEntry[] providers;
            lock (_sync) providers = _providers.ToArray();
            snapshot = null;
            failureCode = providers.Length == 0 ? "no-topology-provider" : string.Empty;
            foreach (ProviderEntry provider in providers)
            {
                try
                {
                    if (!provider.Provider.TryCapture(playerId, out InventoryTopologySnapshot candidate, out string failure))
                    {
                        failureCode = Bound(failure, "provider-declined");
                        continue;
                    }
                    if (candidate == null)
                    {
                        failureCode = "provider-returned-null";
                        continue;
                    }
                    snapshot = candidate;
                    failureCode = string.Empty;
                    return true;
                }
                catch (Exception)
                {
                    failureCode = "provider-threw";
                }
            }
            return false;
        }

        public RecoveryPlan Plan(RecoveryPlanningRequest request)
        {
            string correlation = _diagnostics.NewCorrelationId("recovery");
            if (request == null || request.VanillaWidth <= 0 || request.VanillaHeight <= 0 ||
                request.VanillaOccupiedSlots < 0)
                return Result(RecoveryPlanOutcome.Invalid, 0, 0, correlation, "invalid-request");

            int vanillaCapacity;
            try { vanillaCapacity = checked(request.VanillaWidth * request.VanillaHeight); }
            catch (OverflowException)
            {
                return Result(RecoveryPlanOutcome.Invalid, 0, 0, correlation, "capacity-overflow");
            }
            if (request.VanillaOccupiedSlots > vanillaCapacity)
                return Result(
                    RecoveryPlanOutcome.BlockedUnsupportedExternalTopology,
                    request.VanillaOccupiedSlots,
                    vanillaCapacity,
                    correlation,
                    "vanilla-capacity-insufficient");
            if (!request.VanillaSerializationVerified)
                return Result(
                    RecoveryPlanOutcome.BlockedSerializationFailure,
                    request.VanillaOccupiedSlots,
                    vanillaCapacity,
                    correlation,
                    "verify-vanilla-serialization");

            InventoryTopologySnapshot topology = request.Topology;
            if (topology == null)
                return Result(
                    RecoveryPlanOutcome.SafeVanillaTombstone,
                    request.VanillaOccupiedSlots,
                    vanillaCapacity,
                    correlation,
                    "none");
            if (!ValidTopology(topology) || !CompatibleProtocol(topology.ProtocolVersion))
                return Result(
                    RecoveryPlanOutcome.BlockedIncompatibleTopology,
                    Math.Max(request.VanillaOccupiedSlots, topology.OccupiedSlots),
                    Math.Max(vanillaCapacity, topology.RecoveryCapacitySlots),
                    correlation,
                    "update-inventory-topology-provider");
            if (!topology.SerializationVerified)
                return Result(
                    RecoveryPlanOutcome.BlockedSerializationFailure,
                    Math.Max(request.VanillaOccupiedSlots, topology.OccupiedSlots),
                    topology.RecoveryCapacitySlots,
                    correlation,
                    "verify-peer-topology-serialization");

            int required = Math.Max(request.VanillaOccupiedSlots, topology.OccupiedSlots);
            int planned = Math.Max(vanillaCapacity, topology.RecoveryCapacitySlots);
            if (planned < required)
                return Result(
                    RecoveryPlanOutcome.BlockedUnsupportedExternalTopology,
                    required,
                    planned,
                    correlation,
                    "unsupported-external-inventory-topology");
            return Result(
                RecoveryPlanOutcome.SafeExpandedTombstone,
                required,
                planned,
                correlation,
                "none");
        }

        private RecoveryPlan Result(
            RecoveryPlanOutcome outcome,
            int required,
            int planned,
            string correlation,
            string remediation)
        {
            _diagnostics.Record(
                correlation,
                "recovery-plan",
                outcome.ToString().ToLowerInvariant(),
                outcome == RecoveryPlanOutcome.SafeVanillaTombstone ||
                outcome == RecoveryPlanOutcome.SafeExpandedTombstone
                    ? SafetyDiagnosticSeverity.Information
                    : SafetyDiagnosticSeverity.Warning);
            return new RecoveryPlan(outcome, required, planned, correlation, remediation);
        }

        private static bool ValidTopology(InventoryTopologySnapshot snapshot) =>
            !string.IsNullOrWhiteSpace(snapshot.ProviderId) &&
            !string.IsNullOrWhiteSpace(snapshot.TopologyHash) &&
            snapshot.TotalSlots >= 0 && snapshot.OccupiedSlots >= 0 &&
            snapshot.OccupiedSlots <= snapshot.TotalSlots && snapshot.RecoveryCapacitySlots >= 0;

        private static bool CompatibleProtocol(string value)
        {
            return TryReadMajor(value, out int candidate) &&
                   TryReadMajor(Plugin.ProtocolVersion, out int local) &&
                   candidate == local;
        }

        private static bool TryReadMajor(string value, out int major)
        {
            major = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Trim().Split('.');
            return parts.Length >= 1 && int.TryParse(parts[0], out major) && major >= 0;
        }

        private void Unregister(long token)
        {
            lock (_sync) _providers.RemoveAll(entry => entry.Token == token);
        }

        private static string Bound(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length <= 64 ? trimmed : trimmed.Substring(0, 64);
        }

        private readonly struct ProviderEntry
        {
            internal ProviderEntry(IInventoryTopologyProvider provider, long token)
            {
                Provider = provider;
                Token = token;
            }
            internal IInventoryTopologyProvider Provider { get; }
            internal long Token { get; }
        }

        private sealed class Registration : IDisposable
        {
            private RecoveryPlanningService _owner;
            private readonly long _token;
            internal Registration(RecoveryPlanningService owner, long token)
            {
                _owner = owner;
                _token = token;
            }
            public void Dispose()
            {
                RecoveryPlanningService owner = _owner;
                _owner = null;
                owner?.Unregister(_token);
            }
        }
    }
}
