using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
using RunicSentinel.Contracts;

namespace RunicSentinel.Core
{
    internal static class AttestationPolicy
    {
        internal const int MaximumPlugins = 512;
        internal const int MaximumRelations = 64;

        internal static bool TryCanonicalize(IEnumerable<AttestedPlugin> source,
            out IReadOnlyList<AttestedPlugin> plugins, out string canonical, out string failure)
        {
            plugins = Array.Empty<AttestedPlugin>(); canonical = string.Empty; failure = string.Empty;
            if (source == null) { failure = "MissingAttestation"; return false; }
            var inspected = new List<AttestedPlugin>();
            foreach (AttestedPlugin value in source)
            {
                if (inspected.Count >= MaximumPlugins) { failure = "PluginCap"; return false; }
                inspected.Add(value);
            }
            AttestedPlugin[] values = inspected.ToArray();
            Array.Sort(values, (a, b) => string.CompareOrdinal(a?.Id, b?.Id));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var builder = new StringBuilder(Math.Min(65536, 128 + values.Length * 96));
            builder.Append("RUNIC-ATTESTATION/1\n");
            foreach (AttestedPlugin value in values)
            {
                if (value == null || !SentinelPolicy.CanonicalPluginId(value.Id) ||
                    !SentinelPolicy.CanonicalAtom(value.Version, 1, 64) ||
                    !SentinelPolicy.IsLowerHex(value.Sha256, 64) || !seen.Add(value.Id) ||
                    value.Dependencies.Count > MaximumRelations || value.Capabilities.Count > MaximumRelations)
                { failure = "PluginEvidence"; return false; }
                string[] dependencies = CanonicalRelations(value.Dependencies, out bool validDependencies);
                string[] capabilities = CanonicalRelations(value.Capabilities, out bool validCapabilities);
                if (!validDependencies || !validCapabilities) { failure = "PluginRelations"; return false; }
                builder.Append(value.Id).Append('|').Append(value.Version).Append('|').Append(value.Sha256).Append('|')
                    .Append(string.Join(",", dependencies)).Append('|').Append(string.Join(",", capabilities)).Append('\n');
                if (builder.Length > SentinelPolicy.MaximumBytes) { failure = "AttestationSize"; return false; }
            }
            plugins = values;
            canonical = builder.ToString();
            return true;
        }

        internal static string Digest(string canonical)
        {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical ?? string.Empty)));
        }

        internal static bool TryComputeNonceBinding(
            string nonceHex,
            string snapshotDigest,
            out string response)
        {
            response = string.Empty;
            if (!SentinelPolicy.IsLowerHex(nonceHex, 64) || !SentinelPolicy.IsLowerHex(snapshotDigest, 64)) return false;
            using var sha = SHA256.Create();
            response = Hex(sha.ComputeHash(Encoding.ASCII.GetBytes(
                "RUNIC-NONCE-BINDING/2\n" + nonceHex + "\n" + snapshotDigest)));
            return true;
        }

        internal static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static string[] CanonicalRelations(IReadOnlyList<string> values, out bool valid)
        {
            valid = true;
            string[] result = values.ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            string previous = null;
            foreach (string value in result)
            {
                if (!SentinelPolicy.CanonicalAtom(value, 1, 128) || value == previous) { valid = false; return Array.Empty<string>(); }
                previous = value;
            }
            return result;
        }
    }

    internal static class AdmissionPolicy
    {
        internal static AdmissionDecision Evaluate(SentinelPolicy policy, AttestationSnapshot snapshot, string role)
        {
            if (policy == null || snapshot == null) return new AdmissionDecision(
                AdmissionDisposition.Unavailable,
                0L,
                string.Empty,
                new[] { new AdmissionFinding("PolicyUnavailable", string.Empty, "No verified server policy is active.", FindingConfidence.High) });
            if (!AttestationPolicy.TryCanonicalize(snapshot.Plugins,
                    out IReadOnlyList<AttestedPlugin> validated,
                    out string canonical,
                    out string validationFailure) ||
                !string.Equals(AttestationPolicy.Digest(canonical), snapshot.Digest, StringComparison.Ordinal))
            {
                return new AdmissionDecision(
                    AdmissionDisposition.Deny,
                    policy.Sequence,
                    policy.Profile,
                    new[] { new AdmissionFinding("InvalidAttestation", string.Empty,
                        validationFailure.Length == 0 ? "DigestMismatch" : validationFailure,
                        FindingConfidence.Conclusive) });
            }
            var findings = new List<AdmissionFinding>();
            var byId = validated.ToDictionary(value => value.Id, StringComparer.Ordinal);
            AdmissionDisposition disposition = AdmissionDisposition.Allow;
            foreach (SentinelPolicyRule rule in policy.Rules)
            {
                bool present = byId.TryGetValue(rule.Id, out AttestedPlugin plugin);
                if (rule.Classification == PluginClassification.Required && !present)
                { Add("RequiredMissing", rule.Id, FindingConfidence.Conclusive, AdmissionDisposition.Deny); continue; }
                if (!present) continue;
                if (rule.Classification == PluginClassification.Forbidden)
                { Add("ForbiddenPresent", rule.Id, FindingConfidence.Conclusive, AdmissionDisposition.Deny); continue; }
                if (rule.Classification == PluginClassification.ServerOnly)
                { Add("ServerOnlyOnClient", rule.Id, FindingConfidence.Conclusive, AdmissionDisposition.Deny); continue; }
                if (rule.Classification == PluginClassification.AdministratorOnly && !string.Equals(role, "administrator", StringComparison.Ordinal))
                { Add("AdministratorOnly", rule.Id, FindingConfidence.Conclusive, AdmissionDisposition.Deny); continue; }
                bool version = rule.Version == "*" || rule.Version == plugin.Version;
                bool hash = rule.Sha256 == "*" || rule.Sha256 == plugin.Sha256;
                if (!version || !hash)
                {
                    AdmissionDisposition mismatch = rule.Classification == PluginClassification.Required
                        ? AdmissionDisposition.Deny : AdmissionDisposition.Quarantine;
                    Add("PluginMismatch", rule.Id, FindingConfidence.VeryHigh, mismatch);
                }
                if (rule.Classification == PluginClassification.Quarantined)
                    Add("PolicyQuarantine", rule.Id, FindingConfidence.High, AdmissionDisposition.Quarantine);
            }
            var known = new HashSet<string>(policy.Rules.Select(value => value.Id), StringComparer.Ordinal);
            foreach (AttestedPlugin plugin in validated)
            {
                if (known.Contains(plugin.Id)) continue;
                if (policy.Unknown == PluginClassification.Forbidden)
                    Add("UnknownForbidden", plugin.Id, FindingConfidence.High, AdmissionDisposition.Deny);
                else if (policy.Unknown == PluginClassification.Quarantined)
                    Add("UnknownQuarantine", plugin.Id, FindingConfidence.Moderate, AdmissionDisposition.Quarantine);
            }
            return new AdmissionDecision(
                disposition,
                policy.Sequence,
                policy.Profile,
                findings);

            void Add(string rule, string id, FindingConfidence confidence, AdmissionDisposition next)
            {
                findings.Add(new AdmissionFinding(rule, id, rule, confidence));
                if (next == AdmissionDisposition.Deny || disposition == AdmissionDisposition.Allow) disposition = next;
            }
        }
    }
}
