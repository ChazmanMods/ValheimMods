using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using RunicSentinel.Admission;
using RunicSentinel.Core;
using Local = RunicSentinel.Contracts;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelRuntime :
        Local.ISentinelAttestationService,
        Local.ISentinelAdmissionService,
        IDisposable
    {
        private const long MaximumPluginBytes = 512L * 1024L * 1024L;
        private const long MaximumTotalPluginBytes = 4L * 1024L * 1024L * 1024L;
        private static readonly StringComparer PathComparer =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private readonly object _gate = new object();
        private CancellationTokenSource _cancel;
        private Local.AttestationSnapshot _snapshot;
        private AdmissionClientProfile _lastRemoteProfile;
        private SentinelPolicy _policy;
        private SentinelNetworkCompatibility _network;
        private string _lastAdmissionFailure = string.Empty;
        private IntegrityFileStamp[] _integrityFiles = Array.Empty<IntegrityFileStamp>();
        private bool _integrityCompromised;
        private string _integrityReason = "snapshot-unavailable";
        private long _nextIntegrityCheckUtcTicks;
        private string _status = "NotStarted";
        private long _generation;
        private long _highestPolicySequence;
        private string _highestPolicyDigest = string.Empty;
        private bool _disposed;

        internal SentinelRuntime()
        {
            Evidence = new EvidenceLedger();
        }

        internal EvidenceLedger Evidence { get; }
        public bool ProvidesClientAuthenticityProof => false;
        public bool PolicyReady { get { lock (_gate) return _policy != null; } }
        public bool AuthoritativeTransportReady
        {
            get
            {
                lock (_gate) return _network != null && _network.IsActive;
            }
        }
        public long PolicySequence { get { lock (_gate) return _policy?.Sequence ?? 0L; } }
        public string PolicyProfile { get { lock (_gate) return _policy?.Profile ?? string.Empty; } }

        internal bool TryGetVerifiedPolicy(out SentinelPolicy policy)
        {
            lock (_gate)
            {
                policy = PolicyCurrentLocked() ? _policy : null;
                return policy != null;
            }
        }

        internal void Start(string configDirectory)
        {
            CancellationTokenSource previous;
            CancellationTokenSource current;
            long generation;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SentinelRuntime));
                if (_generation == long.MaxValue)
                    throw new InvalidOperationException("Sentinel worker generation space is exhausted.");
                generation = ++_generation;
                previous = _cancel;
                current = new CancellationTokenSource();
                _cancel = current;
                _snapshot = null;
                _lastRemoteProfile = null;
                _policy = null;
                _integrityFiles = Array.Empty<IntegrityFileStamp>();
                _integrityCompromised = false;
                _integrityReason = "snapshot-pending";
                _nextIntegrityCheckUtcTicks = 0L;
                _status = "PendingLocalSnapshot";
                Evidence.SetPolicySequence(0L);
            }
            CancelAndDispose(previous);

            PluginDescriptor[] descriptors;
            WorkerInputs inputs;
            try
            {
                descriptors = CaptureDescriptors();
                inputs = CaptureInputs(configDirectory);
            }
            catch (Exception exception)
            {
                PublishFailure(generation, current.Token, exception.GetType().Name);
                CancelAndDispose(current);
                throw;
            }
            CancellationToken token = current.Token;
            Task.Run(
                () => Build(generation, descriptors, inputs, token),
                token);
        }

        public bool TryGetCurrent(out Local.AttestationSnapshot snapshot, out string status)
        {
            lock (_gate)
            {
                snapshot = _snapshot;
                status = _status;
                return snapshot != null;
            }
        }

        public bool TryComputeNonceBinding(
            string nonceHex,
            out string bindingHex,
            out string status)
        {
            lock (_gate)
            {
                status = _status + ":UnauthenticatedNonceBinding";
                if (_snapshot == null)
                {
                    bindingHex = string.Empty;
                    return false;
                }
                return AttestationPolicy.TryComputeNonceBinding(
                    nonceHex,
                    _snapshot.Digest,
                    out bindingHex);
            }
        }

        public Local.AdmissionDecision Evaluate(Local.AttestationSnapshot snapshot, string role)
        {
            lock (_gate)
                return AdmissionPolicy.Evaluate(_policy, snapshot, role ?? string.Empty);
        }

        internal void AttachNetwork(SentinelRemoteAdmissionMode mode)
        {
            var network = new SentinelNetworkCompatibility(this, Evidence, mode);
            lock (_gate)
            {
                if (_disposed || _network != null)
                {
                    network.Dispose();
                    if (_disposed) throw new ObjectDisposedException(nameof(SentinelRuntime));
                    throw new InvalidOperationException("Sentinel network compatibility is already attached.");
                }
                _network = network;
            }
        }

        internal SentinelRemoteAdmissionMode EffectiveRemoteAdmissionMode
        {
            get
            {
                SentinelNetworkCompatibility network;
                lock (_gate) network = _network;
                return network?.Mode ?? SentinelRemoteAdmissionMode.Disabled;
            }
        }

        internal void TickNetwork()
        {
            _network?.Tick();
        }

        internal bool IsAdministrator(string authority, string subject)
        {
            lock (_gate)
                return PolicyCurrentLocked() && _policy.Administrators.Any(role =>
                    string.Equals(role.Authority, authority, StringComparison.Ordinal) &&
                    string.Equals(role.Subject, subject, StringComparison.Ordinal));
        }

        internal bool IsBanned(string authority, string subject)
        {
            lock (_gate)
                return PolicyCurrentLocked() && _policy.BannedUsers.Any(role =>
                    string.Equals(role.Authority, authority, StringComparison.Ordinal) &&
                    string.Equals(role.Subject, subject, StringComparison.Ordinal));
        }

        internal SentinelIntegritySnapshot GetIntegritySnapshot()
        {
            lock (_gate)
            {
                SentinelIntegrityState state = _integrityCompromised
                    ? SentinelIntegrityState.Compromised
                    : _snapshot == null
                    ? SentinelIntegrityState.Unavailable
                    : _policy == null
                        ? SentinelIntegrityState.MonitorOnly
                        : SentinelIntegrityState.Ready;
                return new SentinelIntegritySnapshot(
                    state,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    _integrityReason,
                    _policy?.PayloadDigest ?? string.Empty);
            }
        }

        internal void TickIntegrity()
        {
            IntegrityFileStamp[] files;
            long now = DateTime.UtcNow.Ticks;
            lock (_gate)
            {
                if (_disposed || _integrityCompromised || now < _nextIntegrityCheckUtcTicks) return;
                int seconds = Math.Max(
                    5,
                    Math.Min(300, SentinelConfig.IntegrityCheckSeconds?.Value ?? 15));
                _nextIntegrityCheckUtcTicks = now + TimeSpan.FromSeconds(seconds).Ticks;
                if (_policy != null && _policy.ExpiresUnixSeconds != 0L &&
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _policy.ExpiresUnixSeconds)
                {
                    _integrityCompromised = true;
                    _integrityReason = "passport-expired";
                    _lastAdmissionFailure = "sentinel-passport-expired";
                    return;
                }
                files = _integrityFiles;
            }
            for (int index = 0; index < files.Length; index++)
            {
                IntegrityFileStamp expected = files[index];
                var current = new FileInfo(expected.Path);
                if (!current.Exists || current.Length != expected.Length ||
                    current.LastWriteTimeUtc.Ticks != expected.LastWriteUtcTicks)
                {
                    lock (_gate)
                    {
                        _integrityCompromised = true;
                        _integrityReason = expected.PolicyAsset
                            ? "passport-file-changed"
                            : "plugin-file-changed";
                        _lastAdmissionFailure = "sentinel-runtime-integrity-changed";
                    }
                    return;
                }
            }
        }

        private bool PolicyCurrentLocked()
        {
            if (_policy == null || _integrityCompromised) return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return _policy.IssuedUnixSeconds <= now + 300L &&
                   (_policy.ExpiresUnixSeconds == 0L || now < _policy.ExpiresUnixSeconds);
        }

        internal void RecordAdmissionFailure(string reason)
        {
            if (string.IsNullOrEmpty(reason) || reason.Length > 128 ||
                !RunicIdentifier.IsValid(reason)) reason = "sentinel-admission-denied";
            lock (_gate) _lastAdmissionFailure = reason;
        }

        internal string LastAdmissionFailure
        {
            get { lock (_gate) return _lastAdmissionFailure; }
        }

        internal bool TryGetTransitionFingerprint(out string fingerprint)
        {
            lock (_gate)
            {
                if (_snapshot == null || _policy == null || _integrityCompromised)
                {
                    fingerprint = string.Empty;
                    return false;
                }
                using SHA256 sha = SHA256.Create();
                fingerprint = SentinelPolicy.Hex(sha.ComputeHash(Encoding.ASCII.GetBytes(
                    "RUNIC-TRANSITION/1\n" + _policy.PayloadDigest + "\n" + _snapshot.Digest + "\n")));
                return true;
            }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        internal bool TryGetAdmissionClientProfile(out AdmissionClientProfile profile)
        {
            Local.AttestationSnapshot snapshot;
            lock (_gate)
            {
                snapshot = !_disposed && !_integrityCompromised ? _snapshot : null;
            }
            if (snapshot == null)
            {
                profile = null;
                return false;
            }
            var plugins = snapshot.Plugins.Select(value =>
                new AdmissionPluginEvidence(value.Id, value.Version, value.Sha256));
            return AdmissionProfileCanonicalizer.TryCreate(
                plugins,
                snapshot.CapturedUnixSeconds,
                out profile,
                out _);
        }
#endif

        internal Local.AdmissionDecision EvaluateAdmissionClientProfile(
            SentinelPolicy policy,
            AdmissionClientProfile profile,
            string role)
        {
            if (policy == null || profile == null)
                return AdmissionPolicy.Evaluate(null, null, role ?? string.Empty);
            var plugins = profile.Plugins.Select(value => new Local.AttestedPlugin(
                value.Id,
                value.Version,
                value.Sha256,
                Array.Empty<string>(),
                Array.Empty<string>())).ToArray();
            if (!AttestationPolicy.TryCanonicalize(
                    plugins,
                    out IReadOnlyList<Local.AttestedPlugin> canonical,
                    out string text,
                    out _))
                return AdmissionPolicy.Evaluate(policy, null, role ?? string.Empty);
            var snapshot = new Local.AttestationSnapshot(
                AttestationPolicy.Digest(text),
                canonical,
                profile.CapturedUnixSeconds);
            return AdmissionPolicy.Evaluate(policy, snapshot, role ?? string.Empty);
        }

        internal void ObserveRemoteAdmissionProfile(AdmissionClientProfile profile)
        {
            if (profile == null) return;
            lock (_gate)
            {
                if (!_disposed) _lastRemoteProfile = profile;
            }
        }

        internal bool TryGetLastRemoteAdmissionProfile(out AdmissionClientProfile profile)
        {
            lock (_gate)
            {
                profile = _lastRemoteProfile;
                return profile != null;
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancel;
            SentinelNetworkCompatibility network;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_generation < long.MaxValue) _generation++;
                cancel = _cancel;
                _cancel = null;
                _snapshot = null;
                _lastRemoteProfile = null;
                _policy = null;
                _integrityFiles = Array.Empty<IntegrityFileStamp>();
                _integrityCompromised = false;
                _integrityReason = "disposed";
                network = _network;
                _network = null;
                _status = "Disposed";
                Evidence.SetPolicySequence(0L);
            }
            try { network?.Dispose(); }
            catch (Exception) { }
            CancelAndDispose(cancel);
        }

        private void Build(
            long generation,
            PluginDescriptor[] descriptors,
            WorkerInputs inputs,
            CancellationToken token)
        {
            try
            {
                var plugins = new List<Local.AttestedPlugin>(descriptors.Length);
                var evidenceByPath = new Dictionary<string, FileEvidence>(PathComparer);
                long totalBytes = 0L;
                var hashBuffer = new byte[65536];
                for (int index = 0; index < descriptors.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    PluginDescriptor descriptor = descriptors[index];
                    string fullPath = Path.GetFullPath(descriptor.Path);
                    if (!evidenceByPath.TryGetValue(fullPath, out FileEvidence fileEvidence))
                    {
                        var info = new FileInfo(fullPath);
                        if (!info.Exists || info.Length <= 0L || info.Length > MaximumPluginBytes ||
                            totalBytes > MaximumTotalPluginBytes - info.Length)
                            throw new InvalidDataException("Plugin file bound failed.");
                        long expectedLength = info.Length;
                        DateTime expectedWrite = info.LastWriteTimeUtc;
                        totalBytes += expectedLength;
                        string hash = HashStablePlugin(
                            fullPath,
                            expectedLength,
                            hashBuffer,
                            token);
                        info.Refresh();
                        if (!info.Exists || info.Length != expectedLength ||
                            info.LastWriteTimeUtc != expectedWrite)
                            throw new IOException("Plugin changed during local snapshot hashing.");
                        fileEvidence = new FileEvidence(
                            hash,
                            expectedLength,
                            expectedWrite.Ticks);
                        evidenceByPath.Add(fullPath, fileEvidence);
                    }
                    plugins.Add(new Local.AttestedPlugin(
                        descriptor.Id,
                        descriptor.Version,
                        fileEvidence.Sha256,
                        descriptor.Dependencies,
                        descriptor.Capabilities));
                }

                if (!AttestationPolicy.TryCanonicalize(
                        plugins,
                        out IReadOnlyList<Local.AttestedPlugin> canonical,
                        out string text,
                        out string failure))
                    throw new InvalidDataException(failure);
                var snapshot = new Local.AttestationSnapshot(
                    AttestationPolicy.Digest(text),
                    canonical,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                SentinelPolicy policy = TryLoadPolicy(inputs, token, out string policyStatus);
                SentinelDraftExporter.TryWrite(
                    inputs.ConfigRoot,
                    snapshot);
                Publish(
                    generation,
                    token,
                    snapshot,
                    policy,
                    policyStatus,
                    CaptureIntegrityStamps(evidenceByPath, inputs, policy != null));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                PublishFailure(generation, token, exception.GetType().Name);
            }
        }

        private void Publish(
            long generation,
            CancellationToken token,
            Local.AttestationSnapshot snapshot,
            SentinelPolicy policy,
            string policyStatus,
            IntegrityFileStamp[] integrityFiles)
        {
            lock (_gate)
            {
                if (_disposed || token.IsCancellationRequested || generation != _generation) return;
                if (policy != null)
                {
                    if (policy.Sequence < _highestPolicySequence ||
                        policy.Sequence == _highestPolicySequence &&
                        _highestPolicySequence > 0L &&
                        !string.Equals(
                            policy.PayloadDigest,
                            _highestPolicyDigest,
                            StringComparison.Ordinal))
                    {
                        policyStatus = "PolicyRollbackOrEquivocation";
                        policy = null;
                    }
                    else if (policy.Sequence > _highestPolicySequence)
                    {
                        _highestPolicySequence = policy.Sequence;
                        _highestPolicyDigest = policy.PayloadDigest;
                    }
                }
                _snapshot = snapshot;
                _policy = policy;
                _integrityFiles = integrityFiles ?? Array.Empty<IntegrityFileStamp>();
                _integrityCompromised = false;
                _integrityReason = policy == null ? "passport-unavailable" : "verified";
                _status = policy == null
                    ? "ReadyLocalSnapshot:MonitorOnly:" + policyStatus
                    : "ReadyLocalSnapshot:VerifiedRsaPolicy";
                Evidence.SetPolicySequence(policy?.Sequence ?? 0L);
            }
        }

        private void PublishFailure(
            long generation,
            CancellationToken token,
            string failure)
        {
            lock (_gate)
            {
                if (_disposed || token.IsCancellationRequested || generation != _generation) return;
                _snapshot = null;
                _policy = null;
                _status = "FailedLocalSnapshot:" + failure;
                _integrityFiles = Array.Empty<IntegrityFileStamp>();
                _integrityCompromised = true;
                _integrityReason = "snapshot-failed";
                Evidence.SetPolicySequence(0L);
            }
        }

        private static SentinelPolicy TryLoadPolicy(
            WorkerInputs inputs,
            CancellationToken token,
            out string status)
        {
            status = "PolicyFilesUnavailable";
            if (!TryReadStableBounded(
                    inputs.PolicyPath,
                    1L,
                    SentinelPolicy.MaximumBytes,
                    token,
                    out byte[] payload))
            { status = "PolicySizeOrChanged"; return null; }
            if (!TryReadStableBounded(
                    inputs.SignaturePath,
                    1L,
                    SentinelPolicy.MaximumSignatureFileBytes,
                    token,
                    out byte[] signatureFile))
            { status = "SignatureSizeOrChanged"; return null; }
            if (!TryReadStableBounded(
                    inputs.PublicKeyPath,
                    1L,
                    PinnedRsaPublicKey.MaximumFileBytes,
                    token,
                    out byte[] publicKeyFile))
            { status = "PublicKeySizeOrChanged"; return null; }
            if (!PinnedRsaPublicKey.TryParse(
                    publicKeyFile,
                    inputs.PinnedPublicKeySha256,
                    out PinnedRsaPublicKey publicKey,
                    out status)) return null;
            if (!SentinelPolicy.TryDecodeSignatureFile(
                    signatureFile,
                    out byte[] signature,
                    out status)) return null;
            if (!SentinelPolicy.TryParseAndVerify(
                    payload,
                    signature,
                    publicKey,
                    out SentinelPolicy policy,
                    out status)) return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (policy.IssuedUnixSeconds > now + 300L)
            { status = "PolicyNotYetValid"; return null; }
            if (policy.ExpiresUnixSeconds != 0L && now >= policy.ExpiresUnixSeconds)
            { status = "PolicyExpired"; return null; }
            status = "Verified";
            return policy;
        }

        private static string HashStablePlugin(
            string path,
            long expectedLength,
            byte[] buffer,
            CancellationToken token)
        {
            if (expectedLength <= 0L || expectedLength > MaximumPluginBytes ||
                buffer == null || buffer.Length == 0)
                throw new InvalidDataException("Plugin file bound failed.");
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       buffer.Length,
                       FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                if (stream.Length != expectedLength)
                    throw new IOException("Plugin changed before local snapshot hashing.");
                long remaining = expectedLength;
                while (remaining > 0L)
                {
                    token.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)buffer.Length, remaining);
                    int read = stream.Read(buffer, 0, requested);
                    if (read <= 0)
                        throw new EndOfStreamException("Plugin ended during local snapshot hashing.");
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    remaining -= read;
                }
                if (stream.ReadByte() != -1 || stream.Length != expectedLength)
                    throw new IOException("Plugin grew during local snapshot hashing.");
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return AttestationPolicy.Hex(sha.Hash);
            }
        }

        private static bool TryReadStableBounded(
            string path,
            long minimumBytes,
            long maximumBytes,
            CancellationToken token,
            out byte[] bytes)
        {
            bytes = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var before = new FileInfo(path);
                if (!before.Exists || before.Length < minimumBytes ||
                    before.Length > maximumBytes || before.Length > int.MaxValue) return false;
                long expectedLength = before.Length;
                DateTime expectedWrite = before.LastWriteTimeUtc;
                bytes = new byte[(int)expectedLength];
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           Math.Max(1, Math.Min(65536, bytes.Length)),
                           FileOptions.SequentialScan))
                {
                    if (stream.Length != expectedLength) { bytes = null; return false; }
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) { bytes = null; return false; }
                        offset += read;
                    }
                    if (stream.ReadByte() != -1 || stream.Length != expectedLength)
                    { bytes = null; return false; }
                }
                before.Refresh();
                if (!before.Exists || before.Length != expectedLength ||
                    before.LastWriteTimeUtc != expectedWrite)
                { bytes = null; return false; }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private static PluginDescriptor[] CaptureDescriptors()
        {
            var result = new List<PluginDescriptor>();
            if (Chainloader.PluginInfos.Count > AttestationPolicy.MaximumPlugins)
                throw new InvalidDataException("Plugin cap exceeded.");
            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (result.Count >= AttestationPolicy.MaximumPlugins)
                    throw new InvalidDataException("Plugin cap exceeded.");
                PluginInfo value = pair.Value;
                string path = value.Location;
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidDataException("Plugin path unavailable.");
                string version = value.Metadata.Version.ToString();
                if (!SentinelPolicy.CanonicalPluginId(pair.Key) ||
                    !SentinelPolicy.CanonicalAtom(version, 1, 64))
                    throw new InvalidDataException("Plugin identity evidence is invalid.");
                string[] dependencies = value.Dependencies
                    .Select(dependency => dependency.DependencyGUID)
                    .Take(AttestationPolicy.MaximumRelations + 1)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
                if (dependencies.Length > AttestationPolicy.MaximumRelations)
                    throw new InvalidDataException("Plugin dependency cap exceeded.");
                result.Add(new PluginDescriptor(
                    pair.Key,
                    version,
                    Path.GetFullPath(path),
                    dependencies,
                    Array.Empty<string>()));
            }
            return result.ToArray();
        }

        private static WorkerInputs CaptureInputs(string configDirectory)
        {
            string root = Path.GetFullPath(configDirectory ?? string.Empty);
            return new WorkerInputs(
                root,
                Resolve(root, SentinelConfig.PolicyFile?.Value),
                Resolve(root, SentinelConfig.SignatureFile?.Value),
                Resolve(root, SentinelConfig.PublicKeyFile?.Value),
                SentinelConfig.TrustedPublicKeySha256?.Value ?? string.Empty);
        }

        private static IntegrityFileStamp[] CaptureIntegrityStamps(
            IReadOnlyDictionary<string, FileEvidence> pluginFiles,
            WorkerInputs inputs,
            bool includePolicyAssets)
        {
            var result = new List<IntegrityFileStamp>(pluginFiles.Count + 3);
            foreach (KeyValuePair<string, FileEvidence> pair in pluginFiles
                         .OrderBy(value => value.Key, PathComparer))
                result.Add(new IntegrityFileStamp(
                    pair.Key,
                    pair.Value.Length,
                    pair.Value.LastWriteUtcTicks,
                    false));
            if (includePolicyAssets)
            {
                AddPolicy(inputs.PolicyPath);
                AddPolicy(inputs.SignaturePath);
                AddPolicy(inputs.PublicKeyPath);
            }
            return result.ToArray();

            void AddPolicy(string path)
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new IOException("Verified passport asset disappeared.");
                result.Add(new IntegrityFileStamp(
                    info.FullName,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    true));
            }
        }

        private static string Resolve(string root, string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return string.Empty;
            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(root, configured));
        }

        private static void CancelAndDispose(CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try { cancellation.Cancel(); } catch { }
            try { cancellation.Dispose(); } catch { }
        }

        private sealed class FileEvidence
        {
            internal FileEvidence(string sha256, long length, long lastWriteUtcTicks)
            {
                Sha256 = sha256;
                Length = length;
                LastWriteUtcTicks = lastWriteUtcTicks;
            }
            internal string Sha256 { get; }
            internal long Length { get; }
            internal long LastWriteUtcTicks { get; }
        }

        private sealed class IntegrityFileStamp
        {
            internal IntegrityFileStamp(
                string path,
                long length,
                long lastWriteUtcTicks,
                bool policyAsset)
            {
                Path = path;
                Length = length;
                LastWriteUtcTicks = lastWriteUtcTicks;
                PolicyAsset = policyAsset;
            }
            internal string Path { get; }
            internal long Length { get; }
            internal long LastWriteUtcTicks { get; }
            internal bool PolicyAsset { get; }
        }

        private sealed class WorkerInputs
        {
            internal WorkerInputs(
                string configRoot,
                string policyPath,
                string signaturePath,
                string publicKeyPath,
                string pinnedPublicKeySha256)
            {
                ConfigRoot = configRoot;
                PolicyPath = policyPath;
                SignaturePath = signaturePath;
                PublicKeyPath = publicKeyPath;
                PinnedPublicKeySha256 = pinnedPublicKeySha256;
            }

            internal string ConfigRoot { get; }
            internal string PolicyPath { get; }
            internal string SignaturePath { get; }
            internal string PublicKeyPath { get; }
            internal string PinnedPublicKeySha256 { get; }
        }

        private sealed class PluginDescriptor
        {
            internal PluginDescriptor(
                string id,
                string version,
                string path,
                string[] dependencies,
                string[] capabilities)
            {
                Id = id;
                Version = version;
                Path = path;
                Dependencies = dependencies;
                Capabilities = capabilities;
            }

            internal string Id { get; }
            internal string Version { get; }
            internal string Path { get; }
            internal string[] Dependencies { get; }
            internal string[] Capabilities { get; }
        }
    }
}
