using System;
using System.Collections.Generic;
using System.Globalization;
using RunicSentinel.Core;

namespace RunicSentinel.Contracts
{
    public enum PluginClassification
    {
        Unknown = 0,
        Required = 1,
        ApprovedOptional = 2,
        ServerOnly = 3,
        Forbidden = 4,
        Unmanaged = 5,
        AdministratorOnly = 6,
        Quarantined = 7
    }

    public enum AdmissionDisposition
    {
        Unavailable = 0,
        Allow = 1,
        Quarantine = 2,
        Deny = 3
    }

    public enum FindingConfidence
    {
        Informational = 0,
        Low = 1,
        Moderate = 2,
        High = 3,
        VeryHigh = 4,
        Conclusive = 5
    }

    public enum EnforcementAction
    {
        Log = 0,
        Validate = 1,
        Cancel = 2,
        Warn = 3,
        Disconnect = 4,
        Quarantine = 5,
        Ban = 6
    }

    public sealed class AttestedPlugin
    {
        public const int MaximumRelations = 64;
        public const int MaximumInspectedRelations = 256;

        public AttestedPlugin(
            string id,
            string version,
            string sha256,
            IEnumerable<string> dependencies,
            IEnumerable<string> capabilities)
        {
            Id = SecurityContractValidation.RequireAtom(id, 1, 128, nameof(id));
            Version = SecurityContractValidation.RequireAtom(version, 1, 64, nameof(version));
            Sha256 = SecurityContractValidation.RequireLowerHex(sha256, 64, nameof(sha256));
            Dependencies = SecurityContractValidation.CopyAtoms(
                dependencies,
                MaximumRelations,
                MaximumInspectedRelations,
                nameof(dependencies));
            Capabilities = SecurityContractValidation.CopyAtoms(
                capabilities,
                MaximumRelations,
                MaximumInspectedRelations,
                nameof(capabilities));
        }

        public string Id { get; }
        public string Version { get; }
        public string Sha256 { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public IReadOnlyList<string> Capabilities { get; }
    }

    public sealed class AttestationSnapshot
    {
        public const int MaximumPlugins = 512;

        public AttestationSnapshot(
            string digest,
            IEnumerable<AttestedPlugin> plugins,
            long capturedUnixSeconds)
        {
            Digest = SecurityContractValidation.RequireLowerHex(digest, 64, nameof(digest));
            if (capturedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(capturedUnixSeconds));
            var copy = new List<AttestedPlugin>();
            if (plugins != null)
                foreach (AttestedPlugin plugin in plugins)
                {
                    if (copy.Count >= MaximumPlugins)
                        throw new ArgumentOutOfRangeException(nameof(plugins));
                    copy.Add(plugin ?? throw new ArgumentException("Attested plugins cannot be null.", nameof(plugins)));
                }
            Plugins = copy.AsReadOnly();
            CapturedUnixSeconds = capturedUnixSeconds;
        }

        public string Digest { get; }
        public IReadOnlyList<AttestedPlugin> Plugins { get; }
        public long CapturedUnixSeconds { get; }
    }

    public sealed class AdmissionFinding
    {
        public AdmissionFinding(
            string rule,
            string pluginId,
            string detail,
            FindingConfidence confidence)
        {
            Rule = SecurityContractValidation.RequireSafeText(rule, 1, 128, nameof(rule));
            PluginId = string.IsNullOrEmpty(pluginId)
                ? string.Empty
                : SecurityContractValidation.RequireAtom(pluginId, 1, 128, nameof(pluginId));
            Detail = SecurityContractValidation.RequireSafeText(detail, 1, 512, nameof(detail));
            if (!SecurityContractValidation.IsConfidence(confidence))
                throw new ArgumentOutOfRangeException(nameof(confidence));
            Confidence = confidence;
        }

        public string Rule { get; }
        public string PluginId { get; }
        public string Detail { get; }
        public FindingConfidence Confidence { get; }
    }

    public sealed class AdmissionDecision
    {
        public AdmissionDecision(
            AdmissionDisposition disposition,
            long policySequence,
            string policyProfile,
            IEnumerable<AdmissionFinding> findings)
        {
            if (!SecurityContractValidation.IsDisposition(disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition));
            if (policySequence < 0L) throw new ArgumentOutOfRangeException(nameof(policySequence));
            Disposition = disposition;
            PolicySequence = policySequence;
            PolicyProfile = string.IsNullOrEmpty(policyProfile)
                ? string.Empty
                : SecurityContractValidation.RequireAtom(
                    policyProfile,
                    1,
                    64,
                    nameof(policyProfile));
            var copy = new List<AdmissionFinding>();
            if (findings != null)
                foreach (AdmissionFinding finding in findings)
                {
                    if (copy.Count >= 4096) throw new ArgumentOutOfRangeException(nameof(findings));
                    copy.Add(finding ?? throw new ArgumentException("Findings cannot contain null.", nameof(findings)));
                }
            Findings = copy.AsReadOnly();
        }

        public AdmissionDisposition Disposition { get; }
        public long PolicySequence { get; }
        public string PolicyProfile { get; }
        public IReadOnlyList<AdmissionFinding> Findings { get; }
    }

    /// <summary>
    /// Immutable in-process review evidence. Detail must contain a bounded reason, never raw chat,
    /// secrets, filesystem paths, or authentication tokens.
    /// </summary>
    public sealed class SecurityEvidence
    {
        public SecurityEvidence(
            long sequence,
            long unixSeconds,
            string providerModuleId,
            string actor,
            string rule,
            string correlationId,
            FindingConfidence confidence,
            EnforcementAction requestedAction,
            EnforcementAction effectiveAction,
            long policySequence,
            string detail)
        {
            if (sequence <= 0L) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (unixSeconds < 0L) throw new ArgumentOutOfRangeException(nameof(unixSeconds));
            ProviderModuleId = RunicIdentifier.Require(providerModuleId, nameof(providerModuleId));
            Actor = SecurityContractValidation.RequireSafeText(actor, 1, 128, nameof(actor));
            Rule = SecurityContractValidation.RequireSafeText(rule, 1, 128, nameof(rule));
            CorrelationId = SecurityContractValidation.RequireSafeText(
                correlationId,
                1,
                128,
                nameof(correlationId));
            Detail = SecurityContractValidation.RequireSafeText(detail, 1, 512, nameof(detail));
            if (!SecurityContractValidation.IsConfidence(confidence))
                throw new ArgumentOutOfRangeException(nameof(confidence));
            if (!SecurityContractValidation.IsAction(requestedAction))
                throw new ArgumentOutOfRangeException(nameof(requestedAction));
            if (!SecurityContractValidation.IsAction(effectiveAction))
                throw new ArgumentOutOfRangeException(nameof(effectiveAction));
            if (policySequence < 0L) throw new ArgumentOutOfRangeException(nameof(policySequence));
            Sequence = sequence;
            UnixSeconds = unixSeconds;
            Confidence = confidence;
            RequestedAction = requestedAction;
            EffectiveAction = effectiveAction;
            PolicySequence = policySequence;
        }

        public long Sequence { get; }
        public long UnixSeconds { get; }
        public string ProviderModuleId { get; }
        public string Actor { get; }
        public string Rule { get; }
        public string CorrelationId { get; }
        public FindingConfidence Confidence { get; }
        public EnforcementAction RequestedAction { get; }
        /// <summary>
        /// Policy-limited recommendation recorded by the ledger; this does not mean an action was
        /// executed and grants no enforcement authority.
        /// </summary>
        public EnforcementAction EffectiveAction { get; }
        public long PolicySequence { get; }
        public string Detail { get; }
    }

    public sealed class EvidenceProviderStatus
    {
        public EvidenceProviderStatus(
            string providerModuleId,
            bool active,
            int bufferedEntries,
            long acceptedEntries,
            long droppedEntries)
        {
            ProviderModuleId = RunicIdentifier.Require(providerModuleId, nameof(providerModuleId));
            if (bufferedEntries < 0 || acceptedEntries < 0L || droppedEntries < 0L)
                throw new ArgumentOutOfRangeException(nameof(bufferedEntries));
            Active = active;
            BufferedEntries = bufferedEntries;
            AcceptedEntries = acceptedEntries;
            DroppedEntries = droppedEntries;
        }

        public string ProviderModuleId { get; }
        public bool Active { get; }
        public int BufferedEntries { get; }
        public long AcceptedEntries { get; }
        public long DroppedEntries { get; }
    }

    public sealed class EvidenceReadSnapshot
    {
        public EvidenceReadSnapshot(
            long newestSequence,
            long policySequence,
            IEnumerable<SecurityEvidence> entries,
            IEnumerable<EvidenceProviderStatus> providers)
        {
            if (newestSequence < 0L || policySequence < 0L)
                throw new ArgumentOutOfRangeException(nameof(newestSequence));
            NewestSequence = newestSequence;
            PolicySequence = policySequence;
            Entries = Copy(entries, 256, nameof(entries));
            Providers = Copy(providers, 32, nameof(providers));
        }

        public long NewestSequence { get; }
        public long PolicySequence { get; }
        public IReadOnlyList<SecurityEvidence> Entries { get; }
        public IReadOnlyList<EvidenceProviderStatus> Providers { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, int maximum, string name)
            where T : class
        {
            var result = new List<T>();
            if (source != null)
                foreach (T value in source)
                {
                    if (result.Count >= maximum) throw new ArgumentOutOfRangeException(name);
                    result.Add(value ?? throw new ArgumentException("Snapshot values cannot be null.", name));
                }
            return result.AsReadOnly();
        }
    }

    public interface ISentinelAttestationService
    {
        bool ProvidesClientAuthenticityProof { get; }
        bool TryGetCurrent(out AttestationSnapshot snapshot, out string status);
        bool TryComputeNonceBinding(
            string nonceHex,
            out string bindingHex,
            out string status);
    }

    public interface ISentinelAdmissionService
    {
        bool PolicyReady { get; }
        bool AuthoritativeTransportReady { get; }
        long PolicySequence { get; }
        string PolicyProfile { get; }
        AdmissionDecision Evaluate(AttestationSnapshot snapshot, string role);
    }

    public interface ISentinelEvidenceSink
    {
        bool TryAppend(
            string actor,
            string rule,
            string correlationId,
            FindingConfidence confidence,
            EnforcementAction requestedAction,
            string detail,
            out SecurityEvidence accepted);
    }

    public interface ISentinelEvidenceProviderLease : IDisposable
    {
        string ProviderModuleId { get; }
        bool IsActive { get; }
        ISentinelEvidenceSink Sink { get; }
    }

    public interface ISentinelEvidenceService
    {
        ISentinelEvidenceProviderLease RegisterProvider(string providerId);
        EvidenceReadSnapshot ReadAfter(long sequence, int maximum);
    }

    internal static class SecurityContractValidation
    {
        internal static string RequireAtom(string value, int minimum, int maximum, string name)
        {
            if (value == null || value.Length < minimum || value.Length > maximum)
                throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '.' || character == '-' || character == '_';
                if (!safe) throw new ArgumentException("Value is not a canonical atom.", name);
            }
            return value;
        }

        internal static string RequireLowerHex(string value, int length, string name)
        {
            if (value == null || value.Length != length)
                throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    throw new ArgumentException("Value is not canonical lowercase hexadecimal.", name);
            }
            return value;
        }

        internal static string RequireSafeText(string value, int minimum, int maximum, string name)
        {
            if (value == null || value.Length < minimum || value.Length > maximum)
                throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsControl(character) || category == UnicodeCategory.Format ||
                    category == UnicodeCategory.Surrogate)
                    throw new ArgumentException("Text contains unsafe control or formatting characters.", name);
            }
            return value;
        }

        internal static IReadOnlyList<string> CopyAtoms(
            IEnumerable<string> values,
            int maximumUnique,
            int maximumInspected,
            string name)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            int inspected = 0;
            if (values != null)
                foreach (string value in values)
                {
                    if (++inspected > maximumInspected)
                        throw new ArgumentOutOfRangeException(name);
                    string canonical = RequireAtom(value, 1, 128, name);
                    if (!result.Add(canonical)) throw new ArgumentException("Duplicate relation.", name);
                    if (result.Count > maximumUnique) throw new ArgumentOutOfRangeException(name);
                }
            return new List<string>(result).AsReadOnly();
        }

        internal static bool IsClassification(PluginClassification value) =>
            value >= PluginClassification.Required && value <= PluginClassification.Quarantined;
        internal static bool IsDisposition(AdmissionDisposition value) =>
            value >= AdmissionDisposition.Unavailable && value <= AdmissionDisposition.Deny;
        internal static bool IsConfidence(FindingConfidence value) =>
            value >= FindingConfidence.Informational && value <= FindingConfidence.Conclusive;
        internal static bool IsAction(EnforcementAction value) =>
            value >= EnforcementAction.Log && value <= EnforcementAction.Ban;
    }
}
