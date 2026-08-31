using System;
using System.Collections.Generic;

namespace RunicSentinel.Contracts
{
    public static class SentinelCapabilityIds
    {
        public const string ProtocolVersion = "2.0";
        public const string Admission = "security.admission";
        public const string Attestation = "security.attest";
        public const string Evidence = "security.evidence";

        public static IReadOnlyList<string> Published { get; } = Array.AsReadOnly(new[]
        {
            Admission,
            Attestation,
            Evidence
        });
    }
}
