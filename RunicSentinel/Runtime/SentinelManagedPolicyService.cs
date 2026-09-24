using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using RunicSentinel.Admission;
using RunicSentinel.Contracts;
using RunicSentinel.Core;
using Local = RunicSentinel.Contracts;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelManagedPolicyService
    {
        private const string PrivateHeader = "RUNIC-RSA-PRIVATE/1";
        private readonly object _gate = new object();
        private readonly SentinelRuntime _runtime;
        private readonly ManualLogSource _log;
        private readonly string _configRoot;
        private readonly string _privatePath;
        private readonly Func<string, string> _backup;

        internal SentinelManagedPolicyService(
            SentinelRuntime runtime,
            ManualLogSource log,
            string configRoot,
            Func<string, string> backup)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _log = log;
            _configRoot = Path.GetFullPath(configRoot);
            _privatePath = Path.Combine(
                _configRoot,
                "RunicSentinel",
                "server-private",
                "RunicSentinel.private.key");
            _backup = backup;
        }

        internal bool HasManagedKey => File.Exists(_privatePath);

        internal bool CanInitialize
        {
            get
            {
                lock (_gate)
                {
                    try
                    {
                        bool hasTrustMaterial = PathExists(_privatePath) ||
                            !string.IsNullOrEmpty(SentinelConfig.TrustedPublicKeySha256?.Value) ||
                            PathExists(Resolve(SentinelConfig.PolicyFile?.Value)) ||
                            PathExists(Resolve(SentinelConfig.SignatureFile?.Value)) ||
                            PathExists(Resolve(SentinelConfig.PublicKeyFile?.Value));
                        return SentinelAdministratorRules.CanInitialize(true, hasTrustMaterial,
                            _runtime.TryGetCurrent(out _, out _), false);
                    }
                    catch { return false; }
                }
            }
        }

        private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        internal string InitializeFromServerAdministrator(string authority, string subject)
        {
            lock (_gate)
            {
                if (!CanInitialize)
                    throw new InvalidOperationException("Setup unavailable: an existing policy/key must be preserved, or the server snapshot is not ready. Refresh the panel.");
                return Bootstrap(authority, subject);
            }
        }

        internal SentinelAdminDocument CreateDocument(string status = "Ready")
        {
            var document = new SentinelAdminDocument
            {
                Status = status,
                AdmissionMode = _runtime.EffectiveRemoteAdmissionMode.ToString(),
                IntegritySeconds = (SentinelConfig.IntegrityCheckSeconds?.Value ?? 15)
                    .ToString(CultureInfo.InvariantCulture),
                VeryHighThreshold = (SentinelConfig.VeryHighDisconnectCount?.Value ?? 2)
                    .ToString(CultureInfo.InvariantCulture),
                HighThreshold = (SentinelConfig.HighDisconnectCount?.Value ?? 3)
                    .ToString(CultureInfo.InvariantCulture),
                EnforcementWindowSeconds = (SentinelConfig.EnforcementWindowSeconds?.Value ?? 60)
                    .ToString(CultureInfo.InvariantCulture),
                BackupTransitions = SentinelConfig.BackupBeforeTransitions?.Value ?? true,
                ManagedSigningKey = HasManagedKey,
                Integrity = _runtime.GetIntegritySnapshot().State + ":" +
                            _runtime.GetIntegritySnapshot().ReasonCode,
                LastDenial = _runtime.LastAdmissionFailure
            };
            if (_runtime.TryGetVerifiedPolicy(out SentinelPolicy policy))
            {
                document.Sequence = policy.Sequence;
                document.Profile = policy.Profile;
                document.ExpiresUnixSeconds = policy.ExpiresUnixSeconds.ToString(CultureInfo.InvariantCulture);
                document.UnknownMods = policy.Unknown.ToString();
                document.RequiredMods = PluginLines(policy, Local.PluginClassification.Required);
                document.OptionalMods = PluginLines(policy, Local.PluginClassification.ApprovedOptional);
                document.GrayListMods = PluginLines(policy, Local.PluginClassification.Unmanaged);
                document.ForbiddenMods = PluginLines(policy, Local.PluginClassification.Forbidden);
                document.Administrators = IdentityLines(policy.Administrators);
                document.BannedUsers = IdentityLines(policy.BannedUsers);
                document.Modules = "Standalone Sentinel transport; no Runic Core or Runic Persistence dependency.";
                document.SigningKeyPin = SentinelConfig.TrustedPublicKeySha256?.Value ?? string.Empty;
            }
            string serverProfile;
            if (_runtime.TryGetCurrent(out Local.AttestationSnapshot snapshot, out string snapshotStatus))
                serverProfile = string.Join("\n", snapshot.Plugins.Select(plugin =>
                    plugin.Id + "|" + plugin.Version + "|" + plugin.Sha256));
            else serverProfile = "Snapshot unavailable: " + snapshotStatus;
            string clientProfile = _runtime.TryGetLastRemoteAdmissionProfile(
                    out AdmissionClientProfile remote)
                ? string.Join("\n", remote.Plugins.Select(plugin =>
                    plugin.Id + "|" + plugin.Version + "|" + plugin.Sha256))
                : "No client report observed in this process lifetime.";
            document.DetectedProfile = "SERVER PROFILE (not a client allowlist)\n" +
                                       serverProfile +
                                       "\n\nMOST RECENT CLIENT REPORT\n" +
                                       clientProfile;
            document.NamedMods = SentinelModChoices.Installed();
            return document;
        }

        internal string Bootstrap(string authority, string subject)
        {
            lock (_gate)
            {
                if (!CanonicalAuthority(authority) || !CanonicalSubject(subject))
                    throw new InvalidDataException("bootstrap-identity-invalid");
                if (File.Exists(_privatePath))
                    throw new InvalidOperationException("managed-signing-key-already-exists");
                if (!_runtime.TryGetCurrent(out Local.AttestationSnapshot snapshot, out _))
                    throw new InvalidOperationException("sentinel-snapshot-not-ready");

                RSAParameters privateParameters;
                using (RSA rsa = CreateManagedRsa3072())
                {
                    privateParameters = rsa.ExportParameters(true);
                }
                SentinelAdminDocument draft = _runtime.TryGetVerifiedPolicy(out SentinelPolicy existing)
                    ? CreateDocument("Bootstrap")
                    : DefaultDocument(snapshot);
                var identities = ParseIdentities(draft.Administrators, "administrators");
                identities[authority + ":" + Uri.EscapeDataString(subject)] =
                    new SentinelAdministratorRole(authority, subject);
                draft.Administrators = IdentityLines(identities.Values);
                string result = ApplyCore(draft, null, privateParameters, true);
                Directory.CreateDirectory(Path.GetDirectoryName(_privatePath));
                try { WriteExclusive(_privatePath, EncodePrivate(privateParameters)); }
                catch
                {
                    throw new IOException("managed-key-persistence-failed-after-policy-signing");
                }
                return result + global::Runic.Localization.RunicText.Get("text_f9d60b74f4ef") + authority + ":" + subject + ".";
            }
        }

        internal string Apply(SentinelAdminDocument draft, string callerAuthority, string callerSubject)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            lock (_gate)
            {
                if (!File.Exists(_privatePath))
                    throw new InvalidOperationException("server-managed-signing-key-required");
                if (!_runtime.TryGetVerifiedPolicy(out SentinelPolicy current))
                    throw new InvalidOperationException("verified-policy-required");
                if (draft.Sequence != current.Sequence)
                    throw new InvalidOperationException("policy-sequence-stale");
                RSAParameters privateParameters = DecodePrivate(File.ReadAllBytes(_privatePath));
                return ApplyCore(
                    draft,
                    callerAuthority + ":" + Uri.EscapeDataString(callerSubject),
                    privateParameters,
                    false);
            }
        }

        private string ApplyCore(
            SentinelAdminDocument draft,
            string callerKey,
            RSAParameters privateParameters,
            bool changingTrustRoot)
        {
            ValidateSettings(draft);
            if (!string.Equals(
                    draft.AdmissionMode,
                    _runtime.EffectiveRemoteAdmissionMode.ToString(),
                    StringComparison.Ordinal))
                throw new InvalidDataException("admission-mode-restart-required");
            long sequence = _runtime.TryGetVerifiedPolicy(out SentinelPolicy current)
                ? checked(current.Sequence + 1L)
                : 1L;
            long issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] payload = BuildPolicy(draft, sequence, issued, callerKey);
            byte[] signature;
            byte[] publicBytes;
            string pin;
            using (RSA rsa = ImportManagedRsa3072(privateParameters))
            {
                signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                publicBytes = EncodePublic(rsa.ExportParameters(false));
                pin = Sha256(publicBytes);
            }
            if (!PinnedRsaPublicKey.TryParse(publicBytes, pin, out PinnedRsaPublicKey publicKey, out string failure) ||
                !SentinelPolicy.TryParseAndVerify(
                    payload, signature, publicKey, out SentinelPolicy verified, out failure))
                throw new InvalidDataException("generated-policy-invalid-" + failure);
            if (verified.Administrators.Count == 0)
                throw new InvalidDataException("at-least-one-administrator-required");

            string configuredPin = SentinelConfig.TrustedPublicKeySha256?.Value ?? string.Empty;
            if (!changingTrustRoot && !SentinelPolicy.FixedTimeHexEquals(configuredPin, pin))
                throw new InvalidOperationException("managed-key-does-not-match-active-trust-root");

            string backup = _backup?.Invoke("runic-sentinel-admin-policy-apply") ?? "no-world-loaded";
            ArchiveCurrent(sequence);
            AtomicWrite(Resolve(SentinelConfig.PolicyFile?.Value), payload);
            AtomicWrite(
                Resolve(SentinelConfig.SignatureFile?.Value),
                Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n"));
            AtomicWrite(Resolve(SentinelConfig.PublicKeyFile?.Value), publicBytes);
            if (SentinelConfig.TrustedPublicKeySha256 != null &&
                !string.Equals(SentinelConfig.TrustedPublicKeySha256.Value, pin, StringComparison.Ordinal))
                SentinelConfig.TrustedPublicKeySha256.Value = pin;
            ApplySettings(draft);
            _runtime.Start(_configRoot);
            _log?.LogWarning(
                "Raven's Gate administrator applied signed policy sequence " + sequence +
                "; backup=" + backup + ". Connected clients must receive the public passport " +
                "before their next strict admission.");
            return global::Runic.Localization.RunicText.Get("text_14a979707579") + sequence + global::Runic.Localization.RunicText.Get("text_417e665008f9") + backup +
                   global::Runic.Localization.RunicText.Get("text_0b98e93c05cc") + pin +
                   global::Runic.Localization.RunicText.Get("text_cd66dd6181a5") +
                   _runtime.EffectiveRemoteAdmissionMode.ToString() + ".";
        }

        /// <summary>
        /// Unity's Mono provider can ignore RSA.KeySize after RSA.Create() and leave the object at
        /// its default size. Try the portable factory first, then the Mono/Windows CSP constructor
        /// that accepts the requested size at creation. Every candidate is validated from exported
        /// public parameters before it can become a managed signing key.
        /// </summary>
        internal static RSA CreateManagedRsa3072()
        {
            RSA candidate = null;
            try
            {
                candidate = RSA.Create();
                candidate.KeySize = 3072;
                if (IsExactRsa3072(candidate, true)) return candidate;
            }
            catch { }
            candidate?.Dispose();

            try
            {
                var provider = new RSACryptoServiceProvider(3072)
                {
                    PersistKeyInCsp = false
                };
                candidate = provider;
                if (IsExactRsa3072(candidate, true)) return candidate;
            }
            catch { }
            candidate?.Dispose();
            throw new CryptographicException("rsa-3072-unavailable");
        }

        internal static RSA ImportManagedRsa3072(RSAParameters parameters)
        {
            RSA candidate = null;
            try
            {
                candidate = RSA.Create();
                candidate.ImportParameters(parameters);
                if (IsExactRsa3072(candidate, true)) return candidate;
            }
            catch { }
            candidate?.Dispose();

            try
            {
                var provider = new RSACryptoServiceProvider
                {
                    PersistKeyInCsp = false
                };
                candidate = provider;
                candidate.ImportParameters(parameters);
                if (IsExactRsa3072(candidate, true)) return candidate;
            }
            catch { }
            candidate?.Dispose();
            throw new CryptographicException("managed-key-not-rsa-3072");
        }

        private static bool IsExactRsa3072(RSA rsa, bool requirePrivate)
        {
            if (rsa == null || rsa.KeySize != 3072) return false;
            try
            {
                RSAParameters parameters = rsa.ExportParameters(requirePrivate);
                return parameters.Modulus != null && parameters.Modulus.Length == 384 &&
                       parameters.Exponent != null && parameters.Exponent.Length == 3 &&
                       parameters.Exponent[0] == 1 && parameters.Exponent[1] == 0 &&
                       parameters.Exponent[2] == 1 &&
                       (!requirePrivate || parameters.D != null && parameters.D.Length > 0);
            }
            catch { return false; }
        }

        private byte[] BuildPolicy(
            SentinelAdminDocument draft,
            long sequence,
            long issued,
            string callerKey)
        {
            if (!SentinelPolicy.CanonicalAtom(draft.Profile, 1, 64))
                throw new InvalidDataException("profile-invalid");
            if (!long.TryParse(draft.ExpiresUnixSeconds, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long expires) || expires < 0L ||
                expires != 0L && expires <= issued)
                throw new InvalidDataException("expiration-must-be-zero-or-future-unix-time");
            if (draft.UnknownMods != "Forbidden" && draft.UnknownMods != "Quarantined" &&
                draft.UnknownMods != "Unmanaged")
                throw new InvalidDataException("unknown-mod-policy-invalid");

            var plugins = new SortedDictionary<string, Rule>(StringComparer.Ordinal);
            AddRules(draft.RequiredMods, "Required", plugins);
            AddRules(draft.OptionalMods, "ApprovedOptional", plugins);
            AddRules(draft.GrayListMods, "Unmanaged", plugins);
            AddRules(draft.ForbiddenMods, "Forbidden", plugins);
            SortedDictionary<string, SentinelAdministratorRole> admins =
                ParseIdentities(draft.Administrators, "administrators");
            SortedDictionary<string, SentinelAdministratorRole> bans =
                ParseIdentities(draft.BannedUsers, "banned-users");
            if (admins.Keys.Any(bans.ContainsKey))
                throw new InvalidDataException("identity-cannot-be-admin-and-banned");
            if (admins.Count == 0) throw new InvalidDataException("at-least-one-administrator-required");
            if (callerKey != null && !admins.ContainsKey(callerKey) && admins.Count < 1)
                throw new InvalidDataException("last-administrator-cannot-be-removed");

            // Sentinel is standalone. Legacy module claims are intentionally removed when the
            // server writes the next passport so Core/Persistence can never be reintroduced here.
            IReadOnlyList<SentinelModuleRule> modules = Array.Empty<SentinelModuleRule>();
            var builder = new StringBuilder(4096);
            builder.Append("RUNIC-SENTINEL/3\nprofile=").Append(draft.Profile)
                .Append("\nsequence=").Append(sequence.ToString(CultureInfo.InvariantCulture))
                .Append("\nissued=").Append(issued.ToString(CultureInfo.InvariantCulture))
                .Append("\nexpires=").Append(expires.ToString(CultureInfo.InvariantCulture))
                .Append("\nunknown=").Append(draft.UnknownMods)
                .Append("\nunknown-capability=Forbidden\n");
            foreach (Rule rule in plugins.Values)
                builder.Append("rule=").Append(rule.Classification).Append('|')
                    .Append(rule.Id).Append('|').Append(rule.Version).Append('|')
                    .Append(rule.Hash).Append('\n');
            foreach (SentinelModuleRule module in modules.OrderBy(value => value.Id, StringComparer.Ordinal))
                builder.Append("module=").Append(module.Scope).Append('|').Append(module.Id).Append('|')
                    .Append(module.Version).Append('|').Append(module.Protocol.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(string.Join(",", module.Capabilities)).Append('\n');
            foreach (SentinelAdministratorRole role in admins.Values)
                builder.Append("role=").Append(role.Authority).Append('|')
                    .Append(Uri.EscapeDataString(role.Subject)).Append('\n');
            foreach (SentinelAdministratorRole role in bans.Values)
                builder.Append("ban=").Append(role.Authority).Append('|')
                    .Append(Uri.EscapeDataString(role.Subject)).Append('\n');
            byte[] result = new UTF8Encoding(false, true).GetBytes(builder.ToString());
            if (result.Length > SentinelPolicy.MaximumBytes || result.Length > SentinelAdminProtocol.MaximumWireBytes)
                throw new InvalidDataException("policy-exceeds-admin-panel-bound");
            return result;
        }

        private SentinelAdminDocument DefaultDocument(Local.AttestationSnapshot snapshot)
        {
            var value = new SentinelAdminDocument
            {
                Profile = "runic-suite",
                Sequence = 0L,
                ExpiresUnixSeconds = "0",
                // Bootstrap is intentionally monitor-only and does not mistake the server's own
                // plugin set for a client allowlist. Review a reported client profile before
                // switching admission to Required and unknown plugins to Forbidden.
                UnknownMods = "Unmanaged",
                RequiredMods = string.Empty,
                Modules = string.Empty,
                AdmissionMode = SentinelConfig.RemoteAdmissionPolicy?.Value ?? "Optional",
                IntegritySeconds = "15",
                VeryHighThreshold = "2",
                HighThreshold = "3",
                EnforcementWindowSeconds = "60",
                BackupTransitions = true
            };
            return value;
        }

        private static void AddRules(
            string text,
            string classification,
            IDictionary<string, Rule> target)
        {
            foreach (string line in Lines(text))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 3 || !SentinelPolicy.CanonicalPluginId(fields[0]) ||
                    !SentinelPolicy.CanonicalVersionOrWildcard(fields[1]) ||
                    !SentinelPolicy.CanonicalHashOrWildcard(fields[2]))
                    throw new InvalidDataException("plugin-rule-invalid-" + line);
                if (target.ContainsKey(fields[0]))
                    throw new InvalidDataException("plugin-listed-more-than-once-" + fields[0]);
                target.Add(fields[0], new Rule(classification, fields[0], fields[1], fields[2]));
            }
        }

        private static SortedDictionary<string, SentinelAdministratorRole> ParseIdentities(
            string text,
            string label)
        {
            var result = new SortedDictionary<string, SentinelAdministratorRole>(StringComparer.Ordinal);
            foreach (string line in Lines(text))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 2 || !CanonicalAuthority(fields[0]) || !CanonicalSubject(fields[1]))
                    throw new InvalidDataException(label + "-identity-invalid-" + line);
                string key = fields[0] + ":" + Uri.EscapeDataString(fields[1]);
                if (result.ContainsKey(key)) throw new InvalidDataException(label + "-duplicate-" + key);
                result.Add(key, new SentinelAdministratorRole(fields[0], fields[1]));
            }
            return result;
        }

        private void ApplySettings(SentinelAdminDocument value)
        {
            int integrity = BoundedInt(value.IntegritySeconds, 5, 300, "integrity-seconds");
            int veryHigh = BoundedInt(value.VeryHighThreshold, 1, 10, "very-high-threshold");
            int high = BoundedInt(value.HighThreshold, 1, 20, "high-threshold");
            int window = BoundedInt(value.EnforcementWindowSeconds, 10, 600, "enforcement-window");
            if (value.AdmissionMode != "Disabled" && value.AdmissionMode != "Optional" &&
                value.AdmissionMode != "Required")
                throw new InvalidDataException("admission-mode-invalid");
            Set(SentinelConfig.IntegrityCheckSeconds, integrity);
            Set(SentinelConfig.VeryHighDisconnectCount, veryHigh);
            Set(SentinelConfig.HighDisconnectCount, high);
            Set(SentinelConfig.EnforcementWindowSeconds, window);
            Set(SentinelConfig.BackupBeforeTransitions, value.BackupTransitions);
        }

        private static void ValidateSettings(SentinelAdminDocument value)
        {
            BoundedInt(value.IntegritySeconds, 5, 300, "integrity-seconds");
            BoundedInt(value.VeryHighThreshold, 1, 10, "very-high-threshold");
            BoundedInt(value.HighThreshold, 1, 20, "high-threshold");
            BoundedInt(value.EnforcementWindowSeconds, 10, 600, "enforcement-window");
            if (value.AdmissionMode != "Disabled" && value.AdmissionMode != "Optional" &&
                value.AdmissionMode != "Required")
                throw new InvalidDataException("admission-mode-invalid");
        }

        private void ArchiveCurrent(long nextSequence)
        {
            string root = Path.Combine(_configRoot, "RunicSentinel", "policy-history",
                "before-sequence-" + nextSequence.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(root);
            CopyIfPresent(Resolve(SentinelConfig.PolicyFile?.Value), Path.Combine(root, "RunicSentinel.policy"));
            CopyIfPresent(Resolve(SentinelConfig.SignatureFile?.Value), Path.Combine(root, "RunicSentinel.policy.sig"));
            CopyIfPresent(Resolve(SentinelConfig.PublicKeyFile?.Value), Path.Combine(root, "RunicSentinel.policy.pub"));
        }

        private string Resolve(string configured) => Path.IsPathRooted(configured ?? string.Empty)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(_configRoot, configured ?? string.Empty));

        private static void AtomicWrite(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".admin.tmp";
            using (var stream = new FileStream(
                       temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                       65536, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void WriteExclusive(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                       path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static byte[] EncodePrivate(RSAParameters value)
        {
            string Text(string name, byte[] bytes) => name + "=" + Convert.ToBase64String(bytes) + "\n";
            return Encoding.ASCII.GetBytes(PrivateHeader + "\n" +
                Text("modulus", value.Modulus) + Text("exponent", value.Exponent) + Text("d", value.D) +
                Text("p", value.P) + Text("q", value.Q) + Text("dp", value.DP) +
                Text("dq", value.DQ) + Text("inverseq", value.InverseQ));
        }

        private static RSAParameters DecodePrivate(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 64 * 1024)
                throw new InvalidDataException("managed-key-size-invalid");
            string text = Encoding.ASCII.GetString(bytes);
            string[] lines = text.Split('\n');
            if (lines.Length != 10 || lines[0] != PrivateHeader || lines[9].Length != 0)
                throw new InvalidDataException("managed-key-format-invalid");
            var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int index = 1; index < 9; index++)
            {
                int split = lines[index].IndexOf('=');
                if (split <= 0 || values.ContainsKey(lines[index].Substring(0, split)))
                    throw new InvalidDataException("managed-key-field-invalid");
                try { values.Add(lines[index].Substring(0, split),
                    Convert.FromBase64String(lines[index].Substring(split + 1))); }
                catch { throw new InvalidDataException("managed-key-base64-invalid"); }
            }
            byte[] Get(string key) => values.TryGetValue(key, out byte[] value) && value.Length > 0
                ? value : throw new InvalidDataException("managed-key-field-missing-" + key);
            return new RSAParameters
            {
                Modulus = Get("modulus"), Exponent = Get("exponent"), D = Get("d"),
                P = Get("p"), Q = Get("q"), DP = Get("dp"), DQ = Get("dq"),
                InverseQ = Get("inverseq")
            };
        }

        private static byte[] EncodePublic(RSAParameters value) => Encoding.ASCII.GetBytes(
            "RUNIC-RSA-PUBLIC/1\nmodulus=" + Convert.ToBase64String(value.Modulus) +
            "\nexponent=" + Convert.ToBase64String(value.Exponent) + "\n");
        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return SentinelPolicy.Hex(sha.ComputeHash(bytes));
        }
        private static string PluginLines(SentinelPolicy policy, Local.PluginClassification kind) =>
            string.Join("\n", policy.Rules.Where(value => value.Classification == kind)
                .Select(value => value.Id + "|" + value.Version + "|" + value.Sha256));
        private static string IdentityLines(IEnumerable<SentinelAdministratorRole> values) =>
            string.Join("\n", values.OrderBy(value => value.CanonicalKey, StringComparer.Ordinal)
                .Select(value => value.Authority + "|" + value.Subject));
        private static string ModuleLines(IEnumerable<SentinelModuleRule> values) =>
            string.Join("\n", values.OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.Scope + "|" + value.Id + "|" + value.Version + "|" +
                    value.Protocol + "|" + string.Join(",", value.Capabilities)));
        private static IEnumerable<string> Lines(string value) =>
            (value ?? string.Empty).Replace("\r", string.Empty).Split('\n')
                .Select(line => line.Trim()).Where(line => line.Length > 0);
        private static bool CanonicalAuthority(string value) =>
            SentinelPolicy.CanonicalAtom(value, 1, 64) && value.All(character =>
                !(character >= 'A' && character <= 'Z'));
        private static bool CanonicalSubject(string value) => value != null && value.Length > 0 &&
            value.Length <= 256 && !value.Any(char.IsControl) &&
            !char.IsWhiteSpace(value[0]) && !char.IsWhiteSpace(value[value.Length - 1]);
        private static int BoundedInt(string value, int minimum, int maximum, string label) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
            parsed >= minimum && parsed <= maximum
                ? parsed : throw new InvalidDataException(label + "-invalid");
        private static void Set<T>(ConfigEntry<T> entry, T value)
        {
            if (entry != null && !EqualityComparer<T>.Default.Equals(entry.Value, value)) entry.Value = value;
        }
        private static void CopyIfPresent(string source, string destination)
        {
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination, false);
        }
        private sealed class Rule
        {
            internal Rule(string classification, string id, string version, string hash)
            { Classification = classification; Id = id; Version = version; Hash = hash; }
            internal string Classification { get; }
            internal string Id { get; }
            internal string Version { get; }
            internal string Hash { get; }
        }
    }
}
