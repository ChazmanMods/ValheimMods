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
using RunicSentinel.Admission;

namespace RunicSentinelClient.Runtime
{
    internal sealed class ClientProfileCollector : IDisposable
    {
        private readonly object _gate = new object();
        private CancellationTokenSource _cancellation;
        private AdmissionClientProfile _profile;
        private string _status = "not-started";
        private bool _started;
        private bool _disposed;

        internal void Start()
        {
            ClientPluginFile[] files;
            CancellationToken token;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ClientProfileCollector));
                if (_started) return;
                _started = true;
                _status = "capturing-profile";
                _cancellation = new CancellationTokenSource();
                token = _cancellation.Token;
            }

            try
            {
                files = CaptureLoadedPlugins();
            }
            catch (Exception exception)
            {
                Publish(null, "profile-capture-failed-" + exception.GetType().Name, token);
                return;
            }

            Task.Run(() =>
            {
                if (ClientProfileBuilder.TryBuild(
                        files,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds,
                        token,
                        out AdmissionClientProfile profile,
                        out string failure))
                    Publish(profile, "ready", token);
                else if (!token.IsCancellationRequested)
                    Publish(null, failure, token);
            }, token);
        }

        internal bool TryGet(out AdmissionClientProfile profile, out string status)
        {
            lock (_gate)
            {
                profile = _profile;
                status = _status;
                return profile != null;
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                cancellation = _cancellation;
                _cancellation = null;
                _profile = null;
                _status = "disposed";
            }
            if (cancellation == null) return;
            try { cancellation.Cancel(); } catch { }
            try { cancellation.Dispose(); } catch { }
        }

        private void Publish(
            AdmissionClientProfile profile,
            string status,
            CancellationToken token)
        {
            lock (_gate)
            {
                if (_disposed || token.IsCancellationRequested) return;
                _profile = profile;
                _status = AdmissionValidation.IsReason(status)
                    ? status
                    : "profile-build-failed";
            }
        }

        private static ClientPluginFile[] CaptureLoadedPlugins()
        {
            if (Chainloader.PluginInfos.Count > AdmissionProtocolV2.MaximumPlugins)
                throw new InvalidDataException("Plugin cap exceeded.");
            var values = new List<ClientPluginFile>(Chainloader.PluginInfos.Count);
            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (values.Count >= AdmissionProtocolV2.MaximumPlugins)
                    throw new InvalidDataException("Plugin cap exceeded.");
                PluginInfo plugin = pair.Value;
                string location = plugin?.Location;
                string version = plugin?.Metadata?.Version?.ToString();
                values.Add(new ClientPluginFile(
                    pair.Key,
                    version,
                    Path.GetFullPath(location ?? string.Empty)));
            }
            return values.ToArray();
        }
    }

    internal sealed class ClientPluginFile
    {
        internal ClientPluginFile(string id, string version, string path)
        {
            Id = AdmissionValidation.RequireAtom(id, 1, 128, nameof(id));
            Version = AdmissionValidation.RequireAtom(version, 1, 64, nameof(version));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            Path = System.IO.Path.GetFullPath(path);
        }

        internal string Id { get; }
        internal string Version { get; }
        internal string Path { get; }
    }

    internal static class ClientProfileBuilder
    {
        internal const long MaximumPluginBytes = 512L * 1024L * 1024L;
        internal const long MaximumTotalPluginBytes = 4L * 1024L * 1024L * 1024L;

        private static readonly StringComparer PathComparer =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        internal static bool TryBuild(
            IEnumerable<ClientPluginFile> source,
            Func<long> unixTime,
            CancellationToken cancellation,
            out AdmissionClientProfile profile,
            out string failure)
        {
            profile = null;
            failure = string.Empty;
            try
            {
                if (source == null || unixTime == null)
                { failure = "profile-input-missing"; return false; }
                var descriptors = new List<ClientPluginFile>();
                foreach (ClientPluginFile value in source)
                {
                    if (value == null)
                    { failure = "profile-entry-null"; return false; }
                    if (descriptors.Count >= AdmissionProtocolV2.MaximumPlugins)
                    { failure = "profile-plugin-cap"; return false; }
                    descriptors.Add(value);
                }

                var evidence = new List<AdmissionPluginEvidence>(descriptors.Count);
                var hashes = new Dictionary<string, FileHash>(PathComparer);
                var buffer = new byte[65536];
                long totalBytes = 0L;
                foreach (ClientPluginFile descriptor in descriptors)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path = Path.GetFullPath(descriptor.Path);
                    if (!hashes.TryGetValue(path, out FileHash hash))
                    {
                        var before = new FileInfo(path);
                        if (!before.Exists || before.Length <= 0L ||
                            before.Length > MaximumPluginBytes ||
                            totalBytes > MaximumTotalPluginBytes - before.Length)
                        { failure = "profile-file-bound"; return false; }
                        long expectedLength = before.Length;
                        long expectedWriteTicks = before.LastWriteTimeUtc.Ticks;
                        totalBytes += expectedLength;
                        string digest = HashExact(path, expectedLength, buffer, cancellation);
                        before.Refresh();
                        if (!before.Exists || before.Length != expectedLength ||
                            before.LastWriteTimeUtc.Ticks != expectedWriteTicks)
                        { failure = "profile-file-changed"; return false; }
                        hash = new FileHash(digest, expectedLength, expectedWriteTicks);
                        hashes.Add(path, hash);
                    }
                    evidence.Add(new AdmissionPluginEvidence(
                        descriptor.Id,
                        descriptor.Version,
                        hash.Digest));
                }

                long captured = unixTime();
                if (captured < 0L)
                { failure = "profile-clock"; return false; }
                return AdmissionProfileCanonicalizer.TryCreate(
                    evidence, captured, out profile, out failure);
            }
            catch (OperationCanceledException)
            {
                failure = "profile-cancelled";
                return false;
            }
            catch (Exception)
            {
                failure = "profile-build-failed";
                return false;
            }
        }

        private static string HashExact(
            string path,
            long expectedLength,
            byte[] buffer,
            CancellationToken cancellation)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       buffer.Length,
                       FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                if (stream.Length != expectedLength)
                    throw new IOException("Plugin changed before hashing.");
                long remaining = expectedLength;
                while (remaining > 0L)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)buffer.Length, remaining);
                    int read = stream.Read(buffer, 0, requested);
                    if (read <= 0) throw new EndOfStreamException("Plugin ended during hashing.");
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    remaining -= read;
                }
                if (stream.ReadByte() != -1 || stream.Length != expectedLength)
                    throw new IOException("Plugin grew during hashing.");
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return AdmissionProfileCanonicalizer.Hex(sha.Hash);
            }
        }

        private sealed class FileHash
        {
            internal FileHash(string digest, long length, long writeTicks)
            {
                Digest = digest;
                Length = length;
                WriteTicks = writeTicks;
            }

            internal string Digest { get; }
            internal long Length { get; }
            internal long WriteTicks { get; }
        }
    }
}
