using System;
using System.Collections.Generic;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class CompatibilityGate : ICompatibilityGate
    {
        private readonly ISafetyDiagnosticService _diagnostics;
        private Func<bool> _remoteAdmissionAvailable;

        public CompatibilityGate(ISafetyDiagnosticService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _remoteAdmissionAvailable = () => false;
        }

        public bool RemoteAdmissionHookAvailable
        {
            get
            {
                try { return _remoteAdmissionAvailable(); }
                catch (Exception) { return false; }
            }
        }

        internal void SetRemoteAdmissionAvailability(Func<bool> available) =>
            _remoteAdmissionAvailable = available ?? (() => false);

        public CompatibilityDecision Evaluate(
            CompatibilityIdentity local,
            CompatibilityIdentity remote,
            IEnumerable<KnownUnsafeCombination> knownUnsafe = null)
        {
            string correlation = _diagnostics.NewCorrelationId("compat");
            if (!Valid(local) || !Valid(remote))
                return Result(CompatibilityOutcome.BlockedInvalidIdentity,
                    "supply-complete-compatibility-metadata", correlation);
            if (!string.Equals(local.GameVersion, remote.GameVersion, StringComparison.Ordinal))
                return Result(CompatibilityOutcome.BlockedGameVersion,
                    "match-valheim-versions", correlation);

            if (!TryReadMajor(local.ProtocolVersion, out int localProtocol) ||
                !TryReadMajor(remote.ProtocolVersion, out int remoteProtocol))
                return Result(CompatibilityOutcome.BlockedProtocol,
                    "install-compatible-runic-safety-version", correlation);
            if (localProtocol != remoteProtocol)
                return Result(CompatibilityOutcome.BlockedProtocol,
                    "install-compatible-runic-safety-version", correlation);
            if (!string.Equals(local.TopologyHash, remote.TopologyHash, StringComparison.Ordinal))
                return Result(CompatibilityOutcome.BlockedTopology,
                    "match-inventory-slot-topology", correlation);
            if (!string.Equals(
                    local.SynchronizedRulesHash,
                    remote.SynchronizedRulesHash,
                    StringComparison.Ordinal))
                return Result(CompatibilityOutcome.BlockedSynchronizedRules,
                    "match-server-safety-rules", correlation);

            if (knownUnsafe != null)
            {
                int inspected = 0;
                foreach (KnownUnsafeCombination unsafeCombination in knownUnsafe)
                {
                    if (++inspected > 128)
                        return Result(CompatibilityOutcome.BlockedInvalidIdentity,
                            "reduce-known-combination-list", correlation);
                    if (unsafeCombination == null) continue;
                    bool forward = Matches(local, remote, unsafeCombination);
                    bool reverse = Matches(remote, local, unsafeCombination);
                    if (forward || reverse)
                        return Result(
                            CompatibilityOutcome.BlockedKnownCombination,
                            Bound(unsafeCombination.RemediationCode, "remove-known-unsafe-combination"),
                            correlation);
                }
            }
            return Result(CompatibilityOutcome.Compatible, "none", correlation);
        }

        private CompatibilityDecision Result(
            CompatibilityOutcome outcome,
            string remediation,
            string correlation)
        {
            _diagnostics.Record(
                correlation,
                "compatibility",
                outcome.ToString().ToLowerInvariant(),
                outcome == CompatibilityOutcome.Compatible
                    ? SafetyDiagnosticSeverity.Information
                    : SafetyDiagnosticSeverity.Warning);
            return new CompatibilityDecision(outcome, remediation, correlation);
        }

        private static bool Valid(CompatibilityIdentity identity)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.ModuleId) ||
                string.IsNullOrWhiteSpace(identity.SemanticVersion) ||
                string.IsNullOrWhiteSpace(identity.ProtocolVersion) ||
                string.IsNullOrWhiteSpace(identity.GameVersion) ||
                identity.ModuleId.Length > 96 || identity.TopologyHash.Length > 128 ||
                identity.SynchronizedRulesHash.Length > 128) return false;
            string stable = identity.SemanticVersion.Split(new[] { '-', '+' }, 2)[0];
            return Version.TryParse(stable, out _);
        }

        private static bool Matches(
            CompatibilityIdentity local,
            CompatibilityIdentity remote,
            KnownUnsafeCombination candidate) =>
            string.Equals(local.ModuleId, candidate.LocalModuleId, StringComparison.Ordinal) &&
            string.Equals(local.SemanticVersion, candidate.LocalVersion, StringComparison.Ordinal) &&
            string.Equals(remote.ModuleId, candidate.RemoteModuleId, StringComparison.Ordinal) &&
            string.Equals(remote.SemanticVersion, candidate.RemoteVersion, StringComparison.Ordinal);

        private static string Bound(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length <= 64 ? trimmed : trimmed.Substring(0, 64);
        }

        private static bool TryReadMajor(string value, out int major)
        {
            major = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Trim().Split('.');
            return parts.Length >= 1 && int.TryParse(parts[0], out major) && major >= 0;
        }
    }
}
