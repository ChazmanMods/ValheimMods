using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using Runic.Foundation.Core;
using RunicSentinel.Contracts;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelRuntime :
        ISentinelAttestationService,
        ISentinelAdmissionService,
        ISentinelNetworkProfileSource,
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
        private AttestationSnapshot _snapshot;
        private SentinelPolicy _policy;
        private SentinelNetworkProfile _networkProfile;
        private SentinelNetworkCompatibility _network;
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
                _policy = null;
                _networkProfile = null;
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

        public bool TryGetCurrent(out AttestationSnapshot snapshot, out string status)
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

        public AdmissionDecision Evaluate(AttestationSnapshot snapshot, string role)
        {
            lock (_gate)
                return AdmissionPolicy.Evaluate(_policy, snapshot, role ?? string.Empty);
        }

        internal void AttachNetwork(SentinelRemoteAdmissionMode mode)
        {
            var network = new SentinelNetworkCompatibility(
                this,
                Evidence,
                mode);
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

        internal void TickNetwork()
        {
            SentinelNetworkCompatibility network;
            lock (_gate) network = _network;
            network?.Tick();
        }

        public bool TryGetNetworkProfile(out SentinelNetworkProfile profile)
        {
            lock (_gate)
            {
                profile = _networkProfile;
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
                _policy = null;
                _networkProfile = null;
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
                var plugins = new List<AttestedPlugin>(descriptors.Length);
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
                        fileEvidence = new FileEvidence(hash);
                        evidenceByPath.Add(fullPath, fileEvidence);
                    }
                    plugins.Add(new AttestedPlugin(
                        descriptor.Id,
                        descriptor.Version,
                        fileEvidence.Sha256,
                        descriptor.Dependencies,
                        descriptor.Capabilities));
                }

                if (!AttestationPolicy.TryCanonicalize(
                        plugins,
                        out IReadOnlyList<AttestedPlugin> canonical,
                        out string text,
                        out string failure))
                    throw new InvalidDataException(failure);
                var snapshot = new AttestationSnapshot(
                    AttestationPolicy.Digest(text),
                    canonical,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                SentinelPolicy policy = TryLoadPolicy(inputs, token, out string policyStatus);
                Publish(generation, token, snapshot, policy, policyStatus);
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
            AttestationSnapshot snapshot,
            SentinelPolicy policy,
            string policyStatus)
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
                if (policy == null)
                {
                    _networkProfile = null;
                }
                else
                {
                    AdmissionDecision localAdmission = AdmissionPolicy.Evaluate(
                        policy,
                        snapshot,
                        "player");
                    _networkProfile = new SentinelNetworkProfile(
                        snapshot.Digest,
                        snapshot.CapturedUnixSeconds,
                        policy.PayloadDigest,
                        policy.Sequence,
                        policy.Profile,
                        localAdmission.Disposition);
                }
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
                _networkProfile = null;
                _status = "FailedLocalSnapshot:" + failure;
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
                Resolve(root, SentinelConfig.PolicyFile?.Value),
                Resolve(root, SentinelConfig.SignatureFile?.Value),
                Resolve(root, SentinelConfig.PublicKeyFile?.Value),
                SentinelConfig.TrustedPublicKeySha256?.Value ?? string.Empty);
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
            internal FileEvidence(string sha256) { Sha256 = sha256; }
            internal string Sha256 { get; }
        }

        private sealed class WorkerInputs
        {
            internal WorkerInputs(
                string policyPath,
                string signaturePath,
                string publicKeyPath,
                string pinnedPublicKeySha256)
            {
                PolicyPath = policyPath;
                SignaturePath = signaturePath;
                PublicKeyPath = publicKeyPath;
                PinnedPublicKeySha256 = pinnedPublicKeySha256;
            }

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
