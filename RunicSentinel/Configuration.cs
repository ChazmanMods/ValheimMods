using System;
using BepInEx.Configuration;
using RunicSentinel.Core;

namespace RunicSentinel
{
    internal static class SentinelConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> PolicyFile;
        internal static ConfigEntry<string> SignatureFile;
        internal static ConfigEntry<string> PublicKeyFile;
        internal static ConfigEntry<string> TrustedPublicKeySha256;
        internal static ConfigEntry<string> RemoteAdmissionPolicy;
        internal static event Action Changed;
        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Enable bounded Sentinel local-snapshot, RSA policy, and evidence services. Sampled at startup; false registers nothing and starts no worker.");
            PolicyFile = config.Bind("Policy", "ManifestFile", "RunicSentinel.policy",
                "Canonical RUNIC-SENTINEL/2 policy path, relative to BepInEx/config unless absolute.");
            SignatureFile = config.Bind("Policy", "SignatureFile", "RunicSentinel.policy.sig",
                "Canonical Base64 detached RSA-3072/SHA-256 PKCS#1 v1.5 signature path.");
            PublicKeyFile = config.Bind("Policy", "PublicKeyFile", "RunicSentinel.policy.pub",
                "Canonical RUNIC-RSA-PUBLIC/1 verification-only public key path. No private key is loaded by Sentinel.");
            TrustedPublicKeySha256 = config.Bind("Policy", "TrustedPublicKeySha256", string.Empty,
                "Required lowercase SHA-256 of the exact canonical public-key file. Empty or mismatched pins keep Sentinel monitor-only.");
            RemoteAdmissionPolicy = config.Bind("Remote Admission", "Policy", "Optional",
                "Sampled at startup. Required denies missing, stale, malformed, or signed-policy-mismatched " +
                "self-reported claims by disconnecting the exact authenticated peer. Optional records " +
                "bounded evidence without disconnecting. Disabled creates no private RPC handlers.");
            PolicyFile.SettingChanged += Notify;
            SignatureFile.SettingChanged += Notify;
            PublicKeyFile.SettingChanged += Notify;
            TrustedPublicKeySha256.SettingChanged += Notify;
        }

        internal static SentinelRemoteAdmissionMode RemoteAdmissionMode =>
            string.Equals(RemoteAdmissionPolicy?.Value?.Trim(), "Disabled", StringComparison.OrdinalIgnoreCase)
                ? SentinelRemoteAdmissionMode.Disabled
                : string.Equals(RemoteAdmissionPolicy?.Value?.Trim(), "Required", StringComparison.OrdinalIgnoreCase)
                    ? SentinelRemoteAdmissionMode.Required
                    : SentinelRemoteAdmissionMode.Optional;

        private static void Notify(object sender, EventArgs args) => Changed?.Invoke();
    }
}
