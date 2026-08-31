using System;
using System.Collections.Generic;

namespace RunicSafety.Api
{
    public sealed class CompatibilityIdentity
    {
        public CompatibilityIdentity(
            string moduleId,
            string semanticVersion,
            string protocolVersion,
            string gameVersion,
            string topologyHash,
            string synchronizedRulesHash)
        {
            ModuleId = moduleId ?? string.Empty;
            SemanticVersion = semanticVersion ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
            GameVersion = gameVersion ?? string.Empty;
            TopologyHash = topologyHash ?? string.Empty;
            SynchronizedRulesHash = synchronizedRulesHash ?? string.Empty;
        }

        public string ModuleId { get; }
        public string SemanticVersion { get; }
        public string ProtocolVersion { get; }
        public string GameVersion { get; }
        public string TopologyHash { get; }
        public string SynchronizedRulesHash { get; }
    }

    public enum CompatibilityOutcome
    {
        Compatible = 0,
        BlockedInvalidIdentity = 1,
        BlockedGameVersion = 2,
        BlockedProtocol = 3,
        BlockedTopology = 4,
        BlockedSynchronizedRules = 5,
        BlockedKnownCombination = 6
    }

    public sealed class KnownUnsafeCombination
    {
        public KnownUnsafeCombination(
            string localModuleId,
            string localVersion,
            string remoteModuleId,
            string remoteVersion,
            string remediationCode)
        {
            LocalModuleId = localModuleId ?? string.Empty;
            LocalVersion = localVersion ?? string.Empty;
            RemoteModuleId = remoteModuleId ?? string.Empty;
            RemoteVersion = remoteVersion ?? string.Empty;
            RemediationCode = remediationCode ?? string.Empty;
        }

        public string LocalModuleId { get; }
        public string LocalVersion { get; }
        public string RemoteModuleId { get; }
        public string RemoteVersion { get; }
        public string RemediationCode { get; }
    }

    public sealed class CompatibilityDecision
    {
        internal CompatibilityDecision(
            CompatibilityOutcome outcome,
            string remediationCode,
            string correlationId)
        {
            Outcome = outcome;
            RemediationCode = remediationCode ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
        }

        public CompatibilityOutcome Outcome { get; }
        public string RemediationCode { get; }
        public string CorrelationId { get; }
        public bool MayEnter => Outcome == CompatibilityOutcome.Compatible;
    }

    public interface ICompatibilityGate
    {
        CompatibilityDecision Evaluate(
            CompatibilityIdentity local,
            CompatibilityIdentity remote,
            IEnumerable<KnownUnsafeCombination> knownUnsafe = null);
        bool RemoteAdmissionHookAvailable { get; }
    }
}
