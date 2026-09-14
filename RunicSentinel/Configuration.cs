using System;
using BepInEx.Configuration;
using RunicSentinel.Core;
#if !RUNIC_SENTINEL_SERVER_ONLY
using UnityEngine;
#endif

namespace RunicSentinel
{
    internal static class SentinelConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UseServerAdminList;
        internal static ConfigEntry<string> PolicyFile;
        internal static ConfigEntry<string> SignatureFile;
        internal static ConfigEntry<string> PublicKeyFile;
        internal static ConfigEntry<string> TrustedPublicKeySha256;
        internal static ConfigEntry<string> RemoteAdmissionPolicy;
        internal static ConfigEntry<int> IntegrityCheckSeconds;
        internal static ConfigEntry<bool> BackupBeforeTransitions;
        internal static ConfigEntry<int> VeryHighDisconnectCount;
        internal static ConfigEntry<int> HighDisconnectCount;
        internal static ConfigEntry<int> EnforcementWindowSeconds;
#if !RUNIC_SENTINEL_SERVER_ONLY
        internal static ConfigEntry<KeyboardShortcut> AdminPanelKey;
#endif
        internal static event Action Changed;
        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Enable bounded Sentinel local-snapshot, RSA policy, and evidence services. Sampled at startup; false registers nothing and starts no worker.");
            UseServerAdminList = config.Bind("Administrator Access", "UseServerAdminList", true,
                "Allow server-verified adminlist.txt administrators and the local listen host to use Sentinel administration and first-time F3 setup. Steam raw, Steam_, and V_ IDs are supported. Signed Sentinel roles remain independent. Only the server setting is authoritative.");
            PolicyFile = config.Bind("Policy", "ManifestFile", "RunicSentinel.policy",
                "Canonical RUNIC-SENTINEL/3 policy path, relative to BepInEx/config unless absolute.");
            SignatureFile = config.Bind("Policy", "SignatureFile", "RunicSentinel.policy.sig",
                "Canonical Base64 detached RSA-3072/SHA-256 PKCS#1 v1.5 signature path.");
            PublicKeyFile = config.Bind("Policy", "PublicKeyFile", "RunicSentinel.policy.pub",
                "Canonical RUNIC-RSA-PUBLIC/1 verification public key path. The optional F3 workflow keeps its private key in a separate server-only directory.");
            TrustedPublicKeySha256 = config.Bind("Policy", "TrustedPublicKeySha256", string.Empty,
                "Required lowercase SHA-256 of the exact canonical public-key file. Empty or mismatched pins keep Sentinel monitor-only.");
            RemoteAdmissionPolicy = config.Bind("Remote Admission", "Policy", "Optional",
                "Sampled at startup; changing it requires a restart. Required withholds native " +
                "admission and denies a " +
                "missing, stale, malformed, or signed-policy-incompatible client report after a " +
                "bounded grace period. Optional records evidence without delaying or disconnecting. " +
                "Disabled does not register the direct Sentinel admission protocol.");
            IntegrityCheckSeconds = config.Bind("Runtime Integrity", "CheckIntervalSeconds", 15,
                "Metadata-check loaded plugin DLLs and active signed-passport files at this interval. " +
                "A detected runtime change denies new strict admissions until restart. Range 5-300 seconds.");
            BackupBeforeTransitions = config.Bind("Transition Safety", "BackupWorldBeforeProfileChange", true,
                "Before a server loads an existing world with a different signed policy or plugin snapshot, " +
                "require a verified Runic Safety backup of the world database and metadata.");
            VeryHighDisconnectCount = config.Bind("Automatic Enforcement", "VeryHighFindingsBeforeDisconnect", 2,
                new ConfigDescription("Disconnect after this many very-high-confidence violations in the enforcement window.",
                    new AcceptableValueRange<int>(1, 10)));
            HighDisconnectCount = config.Bind("Automatic Enforcement", "HighFindingsBeforeDisconnect", 3,
                new ConfigDescription("Disconnect after this many high-confidence violations in the enforcement window.",
                    new AcceptableValueRange<int>(1, 20)));
            EnforcementWindowSeconds = config.Bind("Automatic Enforcement", "FindingWindowSeconds", 60,
                new ConfigDescription("Rolling violation window used by graduated automatic enforcement.",
                    new AcceptableValueRange<int>(10, 600)));
#if !RUNIC_SENTINEL_SERVER_ONLY
            AdminPanelKey = config.Bind("Administrator Panel", "OpenPanel", new KeyboardShortcut(KeyCode.F3),
                "Open the server-authorized Runic Sentinel administrator panel. Non-administrators are denied by the server.");
#endif
            PolicyFile.SettingChanged += Notify;
            SignatureFile.SettingChanged += Notify;
            PublicKeyFile.SettingChanged += Notify;
            TrustedPublicKeySha256.SettingChanged += Notify;
#if !RUNIC_SENTINEL_SERVER_ONLY
            AdminPanelKey.SettingChanged += Notify;
#endif
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
