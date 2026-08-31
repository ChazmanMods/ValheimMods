using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Mono.Cecil;
using RunicVelocity.Contracts;

namespace RunicVelocity.Core
{
    internal sealed class PluginManifestScanner
    {
        private const string PluginAttribute = "BepInEx.BepInPlugin";
        private const string DependencyAttribute = "BepInEx.BepInDependency";

        internal PluginManifestSnapshot Scan(
            string root,
            string cachePath,
            bool allowWarmCache,
            int configuredMaximumFiles,
            CancellationToken cancellationToken = default)
        {
            int maximum = Math.Max(1, Math.Min(ManifestCachePolicy.MaximumFiles, configuredMaximumFiles));
            Dictionary<string, PluginManifestEntry> cached;
            if (!allowWarmCache || !ManifestCacheCodec.TryRead(cachePath, out cached))
                cached = new Dictionary<string, PluginManifestEntry>(ManifestCachePolicy.PathComparer);

            bool truncated;
            List<string> paths = EnumerateDlls(root, maximum, cancellationToken, out truncated);
            var results = new List<PluginManifestEntry>(paths.Count);
            int hashed = 0;
            int reused = 0;
            bool metadataPartial = false;
            long admittedBytes = 0L;
            long boot = DateTime.UtcNow.Ticks;
            var hashBuffer = new byte[131072];

            foreach (string fullPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                string relative;
                try
                {
                    info = new FileInfo(fullPath);
                    relative = RelativePath(root, fullPath);
                }
                catch
                {
                    truncated = true;
                    continue;
                }
                if (!ManifestCachePolicy.IsSafeRelativePath(relative) || !info.Exists ||
                    !ManifestCachePolicy.CanAdmitScanBytes(admittedBytes, info.Length))
                {
                    truncated = true;
                    continue;
                }
                admittedBytes += info.Length;

                if (cached.TryGetValue(relative, out PluginManifestEntry prior) &&
                    ManifestCachePolicy.CanReuse(prior, info.Length, info.LastWriteTimeUtc.Ticks))
                {
                    results.Add(new PluginManifestEntry(
                        relative, prior.Length, prior.LastWriteUtcTicks, prior.Sha256,
                        prior.PluginId, prior.PluginVersion, prior.Dependencies,
                        prior.Classification, boot));
                    reused++;
                    continue;
                }

                long observedLength = info.Length;
                long observedModified = info.LastWriteTimeUtc.Ticks;
                bool inspectMetadata = observedLength <= ManifestCachePolicy.MaximumMetadataFileBytes;
                if (!TryReadEvidence(
                        fullPath,
                        observedLength,
                        hashBuffer,
                        inspectMetadata,
                        cancellationToken,
                        out string sha,
                        out string pluginId,
                        out string version,
                        out string[] dependencies,
                        out bool metadataComplete))
                {
                    truncated = true;
                    continue;
                }
                if (!metadataComplete) metadataPartial = true;
                info.Refresh();
                if (!info.Exists || info.Length != observedLength ||
                    info.LastWriteTimeUtc.Ticks != observedModified)
                {
                    truncated = true;
                    continue;
                }
                results.Add(new PluginManifestEntry(
                    relative, observedLength, observedModified, sha,
                    pluginId, version, dependencies,
                    ManifestCachePolicy.Classification(pluginId, relative), boot));
                hashed++;
            }

            results.Sort((left, right) =>
            {
                int comparison = ManifestCachePolicy.PathComparer.Compare(
                    left.RelativePath,
                    right.RelativePath);
                return comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
            });
            cancellationToken.ThrowIfCancellationRequested();
            bool cacheWritten = ManifestCacheCodec.TryWrite(
                cachePath,
                results,
                cancellationToken);
            string status = Status(truncated, metadataPartial, cacheWritten, hashed, reused);
            return new PluginManifestSnapshot(results, hashed, reused, truncated, status);
        }

        private static string Status(
            bool truncated,
            bool metadataPartial,
            bool cacheWritten,
            int hashed,
            int reused)
        {
            if (truncated) return "bounded-partial";
            if (!cacheWritten) return "manifest-cache-write-failed";
            if (metadataPartial) return "hash-complete-metadata-partial";
            if (reused > 0) return hashed > 0 ? "mixed-fresh-and-warm" : "warm-cache-reused";
            return "freshly-hashed";
        }

        private static List<string> EnumerateDlls(
            string root,
            int maximum,
            CancellationToken cancellationToken,
            out bool truncated)
        {
            truncated = false;
            var files = new List<string>(maximum);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                truncated = true;
                return files;
            }
            var pending = new Queue<string>();
            pending.Enqueue(Path.GetFullPath(root));
            int directories = 0;
            int scheduledDirectories = 1;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++directories > ManifestCachePolicy.MaximumDirectories)
                {
                    truncated = true;
                    break;
                }
                string directory = pending.Dequeue();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (files.Count >= maximum)
                        {
                            truncated = true;
                            break;
                        }
                        files.Add(file);
                    }
                    if (files.Count >= maximum) break;
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (scheduledDirectories >= ManifestCachePolicy.MaximumDirectories)
                        {
                            truncated = true;
                            break;
                        }
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Enqueue(child);
                            scheduledDirectories++;
                        }
                    }
                }
                catch
                {
                    truncated = true;
                }
            }
            return files;
        }

        private static string RelativePath(string root, string fullPath)
        {
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(fullPath);
            if (!candidate.StartsWith(basePath, ManifestCachePolicy.PathComparison)) return string.Empty;
            return candidate.Substring(basePath.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static bool TryReadEvidence(
            string path,
            long expectedLength,
            byte[] buffer,
            bool inspectMetadata,
            CancellationToken cancellationToken,
            out string sha,
            out string pluginId,
            out string version,
            out string[] dependencies,
            out bool metadataComplete)
        {
            sha = string.Empty;
            pluginId = string.Empty;
            version = string.Empty;
            dependencies = Array.Empty<string>();
            metadataComplete = false;
            try
            {
                if (expectedLength < 0L || expectedLength > ManifestCachePolicy.MaximumFileBytes ||
                    buffer == null || buffer.Length == 0) return false;
                using (var algorithm = SHA256.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                {
                    if (stream.Length != expectedLength) return false;
                    long remaining = expectedLength;
                    while (remaining > 0L)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int requested = (int)Math.Min((long)buffer.Length, remaining);
                        int read = stream.Read(buffer, 0, requested);
                        if (read <= 0) return false;
                        algorithm.TransformBlock(buffer, 0, read, buffer, 0);
                        remaining -= read;
                    }
                    if (stream.ReadByte() != -1 || stream.Length != expectedLength) return false;
                    algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha = BitConverter.ToString(algorithm.Hash).Replace("-", string.Empty);
                    if (!ManifestCachePolicy.IsSha256(sha)) return false;

                    if (inspectMetadata)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        stream.Position = 0L;
                        metadataComplete = ReadMetadata(
                            stream,
                            cancellationToken,
                            out pluginId,
                            out version,
                            out dependencies);
                    }
                    return true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }

        private static bool ReadMetadata(
            Stream stream,
            CancellationToken cancellationToken,
            out string pluginId,
            out string version,
            out string[] dependencies)
        {
            pluginId = string.Empty;
            version = string.Empty;
            dependencies = Array.Empty<string>();
            try
            {
                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
                {
                    ReadingMode = ReadingMode.Deferred,
                    ReadSymbols = false,
                    InMemory = false
                }))
                {
                    var dependencySet = new SortedSet<string>(StringComparer.Ordinal);
                    var pending = new Queue<TypeDefinition>();
                    foreach (TypeDefinition topLevel in assembly.MainModule.Types)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (pending.Count >= 65536)
                            throw new InvalidDataException("Metadata pending-type bound exceeded.");
                        pending.Enqueue(topLevel);
                    }
                    int typeCount = 0;
                    int attributeCount = 0;
                    while (pending.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++typeCount > 65536) throw new InvalidDataException("Metadata type bound exceeded.");
                        TypeDefinition type = pending.Dequeue();
                        foreach (TypeDefinition nested in type.NestedTypes)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (pending.Count >= 65536)
                                throw new InvalidDataException("Metadata pending-type bound exceeded.");
                            pending.Enqueue(nested);
                        }
                        foreach (CustomAttribute attribute in type.CustomAttributes)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++attributeCount > 262144)
                                throw new InvalidDataException("Metadata attribute bound exceeded.");
                            if (attribute.AttributeType.FullName == PluginAttribute &&
                                attribute.ConstructorArguments.Count >= 3 && string.IsNullOrEmpty(pluginId))
                            {
                                pluginId = BoundedExact(attribute.ConstructorArguments[0].Value as string);
                                version = BoundedExact(attribute.ConstructorArguments[2].Value as string);
                            }
                            else if (attribute.AttributeType.FullName == DependencyAttribute &&
                                     attribute.ConstructorArguments.Count >= 1)
                            {
                                string value = BoundedExact(attribute.ConstructorArguments[0].Value as string);
                                if (!string.IsNullOrEmpty(value) && !dependencySet.Contains(value))
                                {
                                    if (dependencySet.Count >= 64)
                                        throw new InvalidDataException("Metadata dependency bound exceeded.");
                                    dependencySet.Add(value);
                                }
                            }
                        }
                    }
                    dependencies = dependencySet.ToArray();
                    return true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                pluginId = string.Empty;
                version = string.Empty;
                dependencies = Array.Empty<string>();
                return false;
            }
        }

        private static string BoundedExact(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 256) throw new InvalidDataException("Metadata identity bound exceeded.");
            return value;
        }
    }
}
