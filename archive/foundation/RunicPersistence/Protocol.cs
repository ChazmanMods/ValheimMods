using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runic.Foundation.Core;

namespace Runic.Foundation.Persistence
{
    public sealed class ModuleProtocolState
    {
        public const int MaximumCapabilities = 64;

        public ModuleProtocolState(string moduleId, string semanticVersion, int protocolVersion, IEnumerable<string> capabilities)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException("Module ID is required.", nameof(moduleId));
            }

            if (protocolVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            }

            ModuleId = RunicIdentifier.Require(moduleId, nameof(moduleId));
            SemanticVersion = Runic.Foundation.Core.SemanticVersion.Parse(semanticVersion).ToString();
            ProtocolVersion = protocolVersion;
            string[] normalizedCapabilities = (capabilities ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => RunicIdentifier.Require(value, nameof(capabilities)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (normalizedCapabilities.Length > MaximumCapabilities)
                throw new ArgumentOutOfRangeException(nameof(capabilities));
            Capabilities = Array.AsReadOnly(normalizedCapabilities);
        }

        public string ModuleId { get; }

        public string SemanticVersion { get; }

        public int ProtocolVersion { get; }

        public IReadOnlyList<string> Capabilities { get; }
    }

    public sealed class ProtocolHello
    {
        public const int MaximumModules = 64;
        public const int MaximumNonceLength = 128;

        public ProtocolHello(
            string nonce,
            IEnumerable<ModuleProtocolState> modules,
            IEnumerable<RpcHandshakeClaim> claims = null)
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException("Handshake nonce is required.", nameof(nonce));
            }
            if (nonce.Length > MaximumNonceLength)
                throw new ArgumentOutOfRangeException(nameof(nonce));
            if (char.IsWhiteSpace(nonce[0]) || char.IsWhiteSpace(nonce[nonce.Length - 1]))
                throw new ArgumentException(
                    "Handshake nonce must not have leading or trailing whitespace.",
                    nameof(nonce));

            Nonce = nonce;
            ModuleProtocolState[] moduleArray = (modules ?? Array.Empty<ModuleProtocolState>()).ToArray();
            if (moduleArray.Length > MaximumModules)
                throw new ArgumentOutOfRangeException(nameof(modules));
            if (moduleArray.Any(module => module == null))
                throw new ArgumentException("Protocol modules cannot contain null entries.", nameof(modules));
            if (moduleArray.Select(module => module.ModuleId).Distinct(StringComparer.Ordinal).Count() != moduleArray.Length)
                throw new ArgumentException("Protocol module identifiers must be unique.", nameof(modules));
            Modules = Array.AsReadOnly(moduleArray);
            Claims = new RpcHandshakeClaims(claims);
        }

        public string Nonce { get; }

        public IReadOnlyList<ModuleProtocolState> Modules { get; }
        public RpcHandshakeClaims Claims { get; }
    }

    public sealed class RpcHandshakeClaim
    {
        public const int MaximumKeyBytes = 128;
        public const int MaximumValueBytes = 512;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public RpcHandshakeClaim(string key, string value)
        {
            Key = RunicIdentifier.Require(key, nameof(key));
            Value = value ?? string.Empty;
            ValidateText(Key, MaximumKeyBytes, false, nameof(key));
            ValidateText(Value, MaximumValueBytes, true, nameof(value));
        }

        public string Key { get; }
        public string Value { get; }

        internal static int Utf8Count(string value)
        {
            try { return StrictUtf8.GetByteCount(value ?? string.Empty); }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("Handshake claim is not valid UTF-8 text.", exception);
            }
        }

        private static void ValidateText(string value, int maximumBytes, bool allowEmpty, string name)
        {
            if ((!allowEmpty && value.Length == 0) || Utf8Count(value) > maximumBytes)
                throw new ArgumentOutOfRangeException(name);
            if (value.Length > 0 &&
                (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])))
                throw new ArgumentException(
                    "Handshake claim text must not have leading or trailing whitespace.",
                    name);
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]))
                    throw new ArgumentException("Handshake claim contains a control character.", name);
        }
    }

    public sealed class RpcHandshakeClaims
    {
        public const int MaximumClaims = 64;
        public const int MaximumTotalBytes = 16 * 1024;
        private readonly IReadOnlyDictionary<string, string> _values;

        public RpcHandshakeClaims(IEnumerable<RpcHandshakeClaim> claims)
        {
            RpcHandshakeClaim[] values = (claims ?? Array.Empty<RpcHandshakeClaim>()).ToArray();
            if (values.Length > MaximumClaims) throw new ArgumentOutOfRangeException(nameof(claims));
            if (values.Any(value => value == null))
                throw new ArgumentException("Handshake claims cannot contain null entries.", nameof(claims));
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            int totalBytes = 0;
            foreach (RpcHandshakeClaim claim in values.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (map.ContainsKey(claim.Key))
                    throw new ArgumentException("Handshake claim keys must be unique.", nameof(claims));
                totalBytes = checked(totalBytes + RpcHandshakeClaim.Utf8Count(claim.Key) +
                                     RpcHandshakeClaim.Utf8Count(claim.Value));
                if (totalBytes > MaximumTotalBytes)
                    throw new ArgumentOutOfRangeException(nameof(claims));
                map.Add(claim.Key, claim.Value);
            }
            _values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(map);
        }

        public int Count => _values.Count;
        public IReadOnlyDictionary<string, string> Values => _values;
        public bool TryGetValue(string key, out string value) =>
            _values.TryGetValue(key ?? string.Empty, out value);
    }

    public sealed class RpcHandshakeClaimEvaluation
    {
        private RpcHandshakeClaimEvaluation(bool accepted, string reasonCode)
        {
            Accepted = accepted;
            ReasonCode = RunicIdentifier.Require(reasonCode, nameof(reasonCode));
        }

        public bool Accepted { get; }
        public string ReasonCode { get; }
        public static RpcHandshakeClaimEvaluation Allow() =>
            new RpcHandshakeClaimEvaluation(true, "claims-compatible");
        public static RpcHandshakeClaimEvaluation Deny(string reasonCode) =>
            new RpcHandshakeClaimEvaluation(false, reasonCode);
    }

    public delegate IEnumerable<RpcHandshakeClaim> RunicHandshakeClaimProvider();
    public delegate RpcHandshakeClaimEvaluation RunicHandshakeClaimEvaluator(RpcHandshakeClaims claims);

    /// <summary>
    /// Server-side pre-PeerInfo compatibility context. Identity is resolved from the exact direct
    /// session socket; RemoteHello claims cannot select or replace it. Session/nonce values bind
    /// an evaluation to this one handshake and are compatibility freshness, not client attestation.
    /// </summary>
    public sealed class RpcHandshakePeerContext
    {
        internal RpcHandshakePeerContext(
            RpcPeerIdentity identity,
            string sessionId,
            string localNonce,
            string remoteNonce,
            ProtocolHello remoteHello,
            bool receiverIsServer,
            bool connectionCurrent)
        {
            Identity = identity == null
                ? null
                : new RpcPeerIdentity(
                    identity.Authority, identity.SubjectId, identity.Assurance);
            SessionId = RunicIdentifier.Require(sessionId, nameof(sessionId));
            LocalNonce = RequireLowerHex(localNonce, nameof(localNonce));
            RemoteNonce = RequireLowerHex(remoteNonce, nameof(remoteNonce));
            RemoteHello = remoteHello ?? throw new ArgumentNullException(nameof(remoteHello));
            ReceiverIsServer = receiverIsServer;
            ConnectionCurrent = connectionCurrent;
        }

        public RpcPeerIdentity Identity { get; }
        public string SessionId { get; }
        public string LocalNonce { get; }
        public string RemoteNonce { get; }
        public ProtocolHello RemoteHello { get; }
        public bool ReceiverIsServer { get; }
        public bool ConnectionCurrent { get; }

        private static string RequireLowerHex(string value, string name)
        {
            string exact = value ?? string.Empty;
            if (exact.Length != RpcHandshakeCodec.NonceBytes * 2)
                throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < exact.Length; index++)
            {
                char current = exact[index];
                if (!((current >= '0' && current <= '9') ||
                      (current >= 'a' && current <= 'f')))
                    throw new ArgumentException("Handshake nonce is not canonical lower hex.", name);
            }
            return exact;
        }
    }

    public delegate RpcHandshakeClaimEvaluation RunicHandshakePeerEvaluator(
        RpcHandshakePeerContext context);

    public sealed class ProtocolRequirement
    {
        public ProtocolRequirement(string consumerModuleId, string capabilityId, int minimumProtocol, int maximumProtocol)
        {
            if (string.IsNullOrWhiteSpace(consumerModuleId) || string.IsNullOrWhiteSpace(capabilityId))
            {
                throw new ArgumentException("Consumer and capability IDs are required.");
            }

            if (minimumProtocol < 1 || maximumProtocol < minimumProtocol)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumProtocol));
            }

            ConsumerModuleId = RunicIdentifier.Require(consumerModuleId, nameof(consumerModuleId));
            CapabilityId = RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            MinimumProtocol = minimumProtocol;
            MaximumProtocol = maximumProtocol;
        }

        public string ConsumerModuleId { get; }

        public string CapabilityId { get; }

        public int MinimumProtocol { get; }

        public int MaximumProtocol { get; }
    }

    public sealed class IntegrationDecision
    {
        internal IntegrationDecision(ProtocolRequirement requirement, bool enabled, string providerModuleId, string reason)
        {
            Requirement = requirement;
            Enabled = enabled;
            ProviderModuleId = providerModuleId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public ProtocolRequirement Requirement { get; }

        public bool Enabled { get; }

        public string ProviderModuleId { get; }

        public string Reason { get; }
    }

    public sealed class NegotiationResult
    {
        internal NegotiationResult(IEnumerable<IntegrationDecision> decisions)
        {
            Decisions = Array.AsReadOnly(decisions.ToArray());
        }

        public IReadOnlyList<IntegrationDecision> Decisions { get; }

        public bool AllEnabled => Decisions.All(decision => decision.Enabled);
    }

    public static class ProtocolNegotiator
    {
        public const int MaximumRequirements = 256;

        public static NegotiationResult Negotiate(
            ProtocolHello local,
            ProtocolHello remote,
            IEnumerable<ProtocolRequirement> requirements)
        {
            if (local == null || remote == null)
            {
                throw new ArgumentNullException(local == null ? nameof(local) : nameof(remote));
            }

            ProtocolRequirement[] requirementArray =
                (requirements ?? Array.Empty<ProtocolRequirement>()).ToArray();
            if (requirementArray.Length > MaximumRequirements)
                throw new ArgumentOutOfRangeException(nameof(requirements));
            if (requirementArray.Any(requirement => requirement == null))
                throw new ArgumentException("Protocol requirements cannot contain null entries.", nameof(requirements));

            var decisions = new List<IntegrationDecision>(requirementArray.Length);
            foreach (ProtocolRequirement requirement in requirementArray)
            {
                ModuleProtocolState[] providers = remote.Modules
                    .Where(module => module.Capabilities.Contains(requirement.CapabilityId, StringComparer.Ordinal))
                    .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
                    .ToArray();

                if (providers.Length == 0)
                {
                    decisions.Add(new IntegrationDecision(
                        requirement,
                        false,
                        null,
                        $"Remote profile does not publish {requirement.CapabilityId}."));
                    continue;
                }

                ModuleProtocolState provider = providers.FirstOrDefault(candidate =>
                    candidate.ProtocolVersion >= requirement.MinimumProtocol &&
                    candidate.ProtocolVersion <= requirement.MaximumProtocol);
                bool compatible = provider != null;
                decisions.Add(new IntegrationDecision(
                    requirement,
                    compatible,
                    compatible ? provider.ModuleId : providers[0].ModuleId,
                    compatible
                        ? "Compatible."
                        : $"No provider protocol is inside supported range {requirement.MinimumProtocol}-{requirement.MaximumProtocol}."));
            }

            return new NegotiationResult(decisions);
        }
    }
}
