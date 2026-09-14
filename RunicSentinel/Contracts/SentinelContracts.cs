using System;
using System.Collections.Generic;

namespace RunicSentinel.Contracts
{
    public static class SentinelCapabilityIds
    {
        public const string ProtocolVersion = "3.0";
        public const string Admission = "security.admission";
        public const string Attestation = "security.attest";
        public const string Evidence = "security.evidence";
        public const string Roles = "security.roles";
        public const string Enforcement = "security.enforcement";
        public const string RuntimeIntegrity = "security.runtime-integrity";

        public static IReadOnlyList<string> Published { get; } = Array.AsReadOnly(new[]
        {
            Admission,
            Attestation,
            Enforcement,
            Evidence,
            Roles,
            RuntimeIntegrity
        });
    }
}
