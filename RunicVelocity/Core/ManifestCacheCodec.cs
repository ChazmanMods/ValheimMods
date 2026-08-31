using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using RunicVelocity.Contracts;

namespace RunicVelocity.Core
{
    internal static class ManifestCacheCodec
    {
        private const int Magic = 0x52564C43; // RVLC
        private const int Schema = 2;
        private const int MaximumDependencies = 64;
        private const int MaximumIdentityCharacters = 256;
        private const int MaximumClassificationCharacters = 64;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static bool TryRead(string path, out Dictionary<string, PluginManifestEntry> entries)
        {
            entries = new Dictionary<string, PluginManifestEntry>(ManifestCachePolicy.PathComparer);
            try
            {
                if (!TryReadStableBounded(path, out byte[] file)) return false;
                byte[] payload;
                using (var envelope = new MemoryStream(file, false))
                using (var reader = new BinaryReader(envelope, Encoding.UTF8, false))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != Schema) return false;
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength <= 0 || payloadLength > ManifestCachePolicy.MaximumCacheBytes - 44)
                        return false;
                    payload = reader.ReadBytes(payloadLength);
                    byte[] expected = reader.ReadBytes(32);
                    if (payload.Length != payloadLength || expected.Length != 32 ||
                        envelope.Position != envelope.Length) return false;
                    using (SHA256 hash = SHA256.Create())
                        if (!ConstantTimeEquals(expected, hash.ComputeHash(payload))) return false;
                }

                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
                {
                    int count = reader.ReadInt32();
                    if (count < 0 || count > ManifestCachePolicy.MaximumFiles) return false;
                    for (int index = 0; index < count; index++)
                    {
                        string relative = ReadBounded(reader, ManifestCachePolicy.MaximumPathCharacters);
                        long length = reader.ReadInt64();
                        long modified = reader.ReadInt64();
                        string sha = ReadBounded(reader, 64);
                        string pluginId = ReadBounded(reader, MaximumIdentityCharacters);
                        string version = ReadBounded(reader, MaximumIdentityCharacters);
                        string classification = ReadBounded(reader, MaximumClassificationCharacters);
                        long lastManifestScan = reader.ReadInt64();
                        int dependencyCount = reader.ReadInt32();
                        if (dependencyCount < 0 || dependencyCount > MaximumDependencies) return false;
                        var dependencies = new string[dependencyCount];
                        for (int dependency = 0; dependency < dependencyCount; dependency++)
                            dependencies[dependency] = ReadBounded(reader, MaximumIdentityCharacters);
                        if (!ManifestCachePolicy.IsSafeRelativePath(relative) ||
                            length < 0L || modified < 0L || !ManifestCachePolicy.IsSha256(sha) ||
                            entries.ContainsKey(relative)) return false;
                        entries.Add(relative, new PluginManifestEntry(
                            relative, length, modified, sha, pluginId, version,
                            dependencies, classification, lastManifestScan));
                    }
                    if (stream.Position != stream.Length) return false;
                    return true;
                }
            }
            catch
            {
                entries.Clear();
                return false;
            }
        }

        private static bool TryReadStableBounded(string path, out byte[] bytes)
        {
            bytes = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0L ||
                    info.Length > ManifestCachePolicy.MaximumCacheBytes ||
                    info.Length > int.MaxValue) return false;
                long length = info.Length;
                DateTime modified = info.LastWriteTimeUtc;
                bytes = new byte[(int)length];
                using (var stream = new FileStream(
                           path, FileMode.Open, FileAccess.Read, FileShare.Read,
                           Math.Max(1, Math.Min(65536, bytes.Length)), FileOptions.SequentialScan))
                {
                    if (stream.Length != length) { bytes = null; return false; }
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) { bytes = null; return false; }
                        offset += read;
                    }
                    if (stream.ReadByte() != -1 || stream.Length != length)
                    { bytes = null; return false; }
                }
                info.Refresh();
                if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != modified)
                { bytes = null; return false; }
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        internal static bool TryWrite(
            string path,
            IReadOnlyList<PluginManifestEntry> entries,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || entries == null ||
                entries.Count > ManifestCachePolicy.MaximumFiles) return false;
            string pending = path + ".pending";
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] payload;
                var paths = new HashSet<string>(ManifestCachePolicy.PathComparer);
                using (var memory = new MemoryStream())
                using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
                {
                    writer.Write(entries.Count);
                    for (int index = 0; index < entries.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PluginManifestEntry entry = entries[index];
                        if (!Validate(entry) || !paths.Add(entry.RelativePath)) return false;
                        WriteBounded(writer, entry.RelativePath, ManifestCachePolicy.MaximumPathCharacters);
                        writer.Write(entry.Length);
                        writer.Write(entry.LastWriteUtcTicks);
                        WriteBounded(writer, entry.Sha256, 64);
                        WriteBounded(writer, entry.PluginId, MaximumIdentityCharacters);
                        WriteBounded(writer, entry.PluginVersion, MaximumIdentityCharacters);
                        WriteBounded(writer, entry.Classification, MaximumClassificationCharacters);
                        writer.Write(entry.LastManifestScanUtcTicks);
                        writer.Write(entry.Dependencies.Count);
                        for (int dependency = 0; dependency < entry.Dependencies.Count; dependency++)
                            WriteBounded(writer, entry.Dependencies[dependency], MaximumIdentityCharacters);
                    }
                    writer.Flush();
                    if (memory.Length > ManifestCachePolicy.MaximumCacheBytes - 44) return false;
                    payload = memory.ToArray();
                }

                byte[] file;
                using (var hash = SHA256.Create())
                using (var memory = new MemoryStream())
                using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Schema);
                    writer.Write(payload.Length);
                    writer.Write(payload);
                    writer.Write(hash.ComputeHash(payload));
                    writer.Flush();
                    if (memory.Length > ManifestCachePolicy.MaximumCacheBytes) return false;
                    file = memory.ToArray();
                }

                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory)) return false;
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int offset = 0;
                    while (offset < file.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(65536, file.Length - offset);
                        stream.Write(file, offset, count);
                        offset += count;
                    }
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                return true;
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(pending)) File.Delete(pending); } catch { }
                throw;
            }
            catch
            {
                try { if (File.Exists(pending)) File.Delete(pending); } catch { }
                return false;
            }
        }

        private static bool Validate(PluginManifestEntry entry)
        {
            if (entry == null || !ManifestCachePolicy.IsSafeRelativePath(entry.RelativePath) ||
                entry.Length < 0L || entry.LastWriteUtcTicks < 0L ||
                !ManifestCachePolicy.IsSha256(entry.Sha256) ||
                entry.PluginId.Length > MaximumIdentityCharacters ||
                entry.PluginVersion.Length > MaximumIdentityCharacters ||
                entry.Classification.Length > MaximumClassificationCharacters ||
                entry.Dependencies.Count > MaximumDependencies) return false;
            for (int index = 0; index < entry.Dependencies.Count; index++)
                if ((entry.Dependencies[index] ?? string.Empty).Length > MaximumIdentityCharacters) return false;
            return true;
        }

        private static string ReadBounded(BinaryReader reader, int maximum)
        {
            if (reader == null || maximum < 0)
                throw new InvalidDataException("Manifest string bound is invalid.");
            int maximumBytes = checked(maximum * 4);
            int byteCount = Read7BitEncodedInt(reader, maximumBytes);
            Stream stream = reader.BaseStream;
            if (stream.CanSeek && (byteCount < 0 || byteCount > stream.Length - stream.Position))
                throw new EndOfStreamException("Manifest string exceeds the remaining payload.");
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new EndOfStreamException("Manifest string ended early.");
            string value = StrictUtf8.GetString(bytes);
            if (value.Length > maximum) throw new InvalidDataException("Manifest field exceeded its bound.");
            return value;
        }

        private static void WriteBounded(BinaryWriter writer, string value, int maximum)
        {
            value = value ?? string.Empty;
            if (value.Length > maximum) throw new InvalidDataException("Manifest field exceeded its bound.");
            byte[] bytes = StrictUtf8.GetBytes(value);
            if (bytes.Length > checked(maximum * 4))
                throw new InvalidDataException("Manifest field exceeded its UTF-8 bound.");
            Write7BitEncodedInt(writer, bytes.Length);
            writer.Write(bytes);
        }

        private static int Read7BitEncodedInt(BinaryReader reader, int maximum)
        {
            uint value = 0U;
            for (int index = 0; index < 5; index++)
            {
                byte next = reader.ReadByte();
                if (index == 4 && (next & 0xF0) != 0)
                    throw new InvalidDataException("Manifest string length is not canonical.");
                value |= (uint)(next & 0x7F) << (index * 7);
                if ((next & 0x80) == 0)
                {
                    if (index > 0 && next == 0)
                        throw new InvalidDataException("Manifest string length is not canonical.");
                    if (value > maximum)
                        throw new InvalidDataException("Manifest string byte length exceeded its bound.");
                    return (int)value;
                }
            }
            throw new InvalidDataException("Manifest string length is not canonical.");
        }

        private static void Write7BitEncodedInt(BinaryWriter writer, int value)
        {
            uint remaining = checked((uint)value);
            while (remaining >= 0x80U)
            {
                writer.Write((byte)((remaining & 0x7FU) | 0x80U));
                remaining >>= 7;
            }
            writer.Write((byte)remaining);
        }

        private static bool ConstantTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
