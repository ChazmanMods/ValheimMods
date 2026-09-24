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
                global::Runic.Localization.RunicText.Get("text_04f9fe75aee9"));
            UseServerAdminList = config.Bind("Administrator Access", "UseServerAdminList", true,
                global::Runic.Localization.RunicText.Get("text_99bb2f56596f"));
            PolicyFile = config.Bind("Policy", "ManifestFile", "RunicSentinel.policy",
                global::Runic.Localization.RunicText.Get("text_b7eed9044c43"));
            SignatureFile = config.Bind("Policy", "SignatureFile", "RunicSentinel.policy.sig",
                global::Runic.Localization.RunicText.Get("text_2e110bbd5688"));
            PublicKeyFile = config.Bind("Policy", "PublicKeyFile", "RunicSentinel.policy.pub",
                global::Runic.Localization.RunicText.Get("text_54e85eb611ce"));
            TrustedPublicKeySha256 = config.Bind("Policy", "TrustedPublicKeySha256", string.Empty,
                global::Runic.Localization.RunicText.Get("text_0ed34bbd06da"));
            RemoteAdmissionPolicy = config.Bind("Remote Admission", "Policy", "Optional",
                global::Runic.Localization.RunicText.Get("text_b602d235fa8e") +
                global::Runic.Localization.RunicText.Get("text_d66b3605dcac") +
                global::Runic.Localization.RunicText.Get("text_b101247a5c6d") +
                global::Runic.Localization.RunicText.Get("text_3e51c1dea7db") +
                global::Runic.Localization.RunicText.Get("text_183c9913fd6f"));
            IntegrityCheckSeconds = config.Bind("Runtime Integrity", "CheckIntervalSeconds", 15,
                global::Runic.Localization.RunicText.Get("text_8907827da911") +
                global::Runic.Localization.RunicText.Get("text_46c4c63703c2"));
            BackupBeforeTransitions = config.Bind("Transition Safety", "BackupWorldBeforeProfileChange", true,
                global::Runic.Localization.RunicText.Get("text_f0e8f6c49a3c") +
                global::Runic.Localization.RunicText.Get("text_b87330de459d"));
            VeryHighDisconnectCount = config.Bind("Automatic Enforcement", "VeryHighFindingsBeforeDisconnect", 2,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_308e67125b7e"),
                    new AcceptableValueRange<int>(1, 10)));
            HighDisconnectCount = config.Bind("Automatic Enforcement", "HighFindingsBeforeDisconnect", 3,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_a4d9b1405b2f"),
                    new AcceptableValueRange<int>(1, 20)));
            EnforcementWindowSeconds = config.Bind("Automatic Enforcement", "FindingWindowSeconds", 60,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_dff407e006ab"),
                    new AcceptableValueRange<int>(10, 600)));
#if !RUNIC_SENTINEL_SERVER_ONLY
            AdminPanelKey = config.Bind("Administrator Panel", "OpenPanel", new KeyboardShortcut(KeyCode.F3),
                global::Runic.Localization.RunicText.Get("text_629c5f55524d"));
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
