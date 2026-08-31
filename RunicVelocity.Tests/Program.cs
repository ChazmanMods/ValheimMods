using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mono.Cecil;
using RunicVelocity.Contracts;
using RunicVelocity.Core;

namespace RunicVelocity.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("timeline is deduplicated and bounded", TimelineIsBounded),
                ("cache policy validates identity inputs", CachePolicyIsStrict),
                ("cache codec round-trips exact bounded metadata", CacheRoundTrips),
                ("cache codec rejects corrupt and oversized evidence", CacheRejectsBadEvidence),
                ("cache codec rejects a 10k workload before inspecting entries", CacheRejectsTenThousandImmediately),
                ("cache codec round-trips a bounded 1k workload", CacheHandlesOneThousand),
                ("cache decoder rejects oversized and noncanonical strings before allocation", CacheDecoderIsStrict),
                ("path identity follows the host platform", PathIdentityIsPlatformCorrect),
                ("scanner hashes once and reuses unchanged metadata", ScannerReusesUnchangedFiles),
                ("scanner rehashes changed files", ScannerRehashesChanges),
                ("scanner fails boundedly at its file cap", ScannerIsBounded),
                ("scanner total-byte admission is overflow safe", ScannerBytesAreBounded),
                ("cancelled scans publish no cache", CancelledScanPublishesNothing),
                ("cancelled cache writes publish no pending file", CancelledCacheWritePublishesNothing),
                ("missing scan roots are reported as bounded partial", MissingRootIsPartial),
                ("plugin metadata is read without loading code", MetadataIsReadOnly),
                ("runtime keeps Unity off its worker", RuntimeWorkerIsSafe),
                ("disabled mode is startup inert", DisabledModeIsInert),
                ("release identity and standalone surface align", ReleaseSurfaceAligns)
            };
            int failed = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); Console.WriteLine("PASS " + name); }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void TimelineIsBounded()
        {
            var timeline = new StartupTimeline(Stopwatch.GetTimestamp());
            True(timeline.Record("one"));
            False(timeline.Record("one"));
            for (int index = 1; index < StartupTimeline.MaximumStages; index++)
                True(timeline.Record("stage-" + index));
            False(timeline.Record("overflow"));
            Equal(StartupTimeline.MaximumStages, timeline.Current.Stages.Count);
        }

        private static void CachePolicyIsStrict()
        {
            var entry = Entry("plugin/a.dll", 10L, 20L, Sha('A'));
            True(ManifestCachePolicy.CanReuse(entry, 10L, 20L));
            False(ManifestCachePolicy.CanReuse(entry, 11L, 20L));
            False(ManifestCachePolicy.CanReuse(entry, 10L, 21L));
            True(ManifestCachePolicy.IsSafeRelativePath("folder/a.dll"));
            False(ManifestCachePolicy.IsSafeRelativePath("../a.dll"));
            False(ManifestCachePolicy.IsSafeRelativePath(Path.GetFullPath("a.dll")));
            False(ManifestCachePolicy.IsSha256(Sha('a')));
        }

        private static void CacheRoundTrips()
        {
            WithTemp(directory =>
            {
                string path = Path.Combine(directory, "cache.bin");
                var source = new[]
                {
                    new PluginManifestEntry("a.dll", 123, 456, Sha('B'), "chazman.RunicTest", "1.2.3",
                        new[] { "dep.a", "dep.b" }, "runic-plugin", 789)
                };
                True(ManifestCacheCodec.TryWrite(path, source));
                True(ManifestCacheCodec.TryRead(path, out Dictionary<string, PluginManifestEntry> loaded));
                PluginManifestEntry value = loaded["a.dll"];
                Equal(source[0].Sha256, value.Sha256);
                Equal(789L, value.LastManifestScanUtcTicks);
                Sequence(source[0].Dependencies, value.Dependencies);
                False(File.Exists(path + ".pending"));
                byte[] changed = File.ReadAllBytes(path);
                changed[changed.Length / 2] ^= 0x40;
                File.WriteAllBytes(path, changed);
                False(ManifestCacheCodec.TryRead(path, out _));
            });
        }

        private static void CacheDecoderIsStrict()
        {
            WithTemp(directory =>
            {
                string oversized = Path.Combine(directory, "oversized-string.bin");
                WriteRawCache(oversized, writer =>
                {
                    writer.Write(1);
                    Write7Bit(writer, 50_000_000);
                    writer.Write(new byte[128]);
                });
                long before = GC.GetAllocatedBytesForCurrentThread();
                False(ManifestCacheCodec.TryRead(oversized, out _));
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                True(allocated < 1_000_000L, "Oversized string caused an unbounded allocation: " + allocated);

                string overlong = Path.Combine(directory, "overlong-string.bin");
                WriteRawCache(overlong, writer =>
                {
                    writer.Write(1);
                    writer.Write((byte)0x80);
                    writer.Write((byte)0x00);
                });
                False(ManifestCacheCodec.TryRead(overlong, out _));

                string codec = Read("RunicVelocity", "Core", "ManifestCacheCodec.cs");
                False(codec.Contains("ReadString()", StringComparison.Ordinal));
                Contains(codec, "Read7BitEncodedInt(reader, maximumBytes)");
                Contains(codec, "index > 0 && next == 0");
            });
        }

        private static void CacheRejectsTenThousandImmediately()
        {
            WithTemp(directory =>
            {
                string cache = Path.Combine(directory, "cache.bin");
                False(ManifestCacheCodec.TryWrite(cache, new OversizedEntryList()));
                False(File.Exists(cache));
                False(File.Exists(cache + ".pending"));
            });
        }

        private static void CacheHandlesOneThousand()
        {
            WithTemp(directory =>
            {
                string cache = Path.Combine(directory, "cache.bin");
                PluginManifestEntry[] entries = Enumerable.Range(0, 1_000)
                    .Select(index => Entry(
                        "plugin/p" + index.ToString("D4") + ".dll",
                        index + 1L,
                        index + 1L,
                        Sha('E')))
                    .ToArray();
                True(ManifestCacheCodec.TryWrite(cache, entries));
                True(ManifestCacheCodec.TryRead(
                    cache,
                    out Dictionary<string, PluginManifestEntry> loaded));
                Equal(1_000, loaded.Count);
            });
        }

        private static void PathIdentityIsPlatformCorrect()
        {
            False(ManifestCachePolicy.PathComparerFor(false).Equals("A.dll", "a.dll"));
            True(ManifestCachePolicy.PathComparerFor(true).Equals("A.dll", "a.dll"));
            Equal(OperatingSystem.IsWindows(), ManifestCachePolicy.UsesCaseInsensitivePaths);
        }

        private static void CacheRejectsBadEvidence()
        {
            WithTemp(directory =>
            {
                string corrupt = Path.Combine(directory, "bad.bin");
                File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3, 4 });
                False(ManifestCacheCodec.TryRead(corrupt, out _));
                string huge = Path.Combine(directory, "huge.bin");
                using (var stream = new FileStream(huge, FileMode.Create, FileAccess.Write))
                    stream.SetLength(ManifestCachePolicy.MaximumCacheBytes + 1L);
                False(ManifestCacheCodec.TryRead(huge, out _));
                False(ManifestCacheCodec.TryWrite(Path.Combine(directory, "invalid.bin"),
                    new[] { Entry("../escape.dll", 1, 1, Sha('C')) }));
                string codec = Read("RunicVelocity", "Core", "ManifestCacheCodec.cs");
                Contains(codec, "TryReadStableBounded");
                Contains(codec, "stream.ReadByte() != -1");
                False(codec.Contains("File.ReadAllBytes", StringComparison.Ordinal));
            });
        }

        private static void ScannerReusesUnchangedFiles()
        {
            WithPluginCopy((directory, cache, plugin) =>
            {
                var scanner = new PluginManifestScanner();
                PluginManifestSnapshot first = scanner.Scan(directory, cache, true, 8);
                Equal(1, first.HashedFiles);
                Equal(0, first.ReusedFiles);
                Equal("freshly-hashed", first.Status);
                PluginManifestSnapshot second = scanner.Scan(directory, cache, true, 8);
                Equal(0, second.HashedFiles);
                Equal(1, second.ReusedFiles);
                Equal("warm-cache-reused", second.Status);
                Equal(first.Entries[0].Sha256, second.Entries[0].Sha256);
            });
        }

        private static void ScannerRehashesChanges()
        {
            WithPluginCopy((directory, cache, plugin) =>
            {
                var scanner = new PluginManifestScanner();
                scanner.Scan(directory, cache, true, 8);
                File.SetLastWriteTimeUtc(plugin, File.GetLastWriteTimeUtc(plugin).AddSeconds(2));
                PluginManifestSnapshot changed = scanner.Scan(directory, cache, true, 8);
                Equal(1, changed.HashedFiles);
                Equal(0, changed.ReusedFiles);
            });
        }

        private static void ScannerIsBounded()
        {
            WithPluginCopy((directory, cache, plugin) =>
            {
                File.Copy(plugin, Path.Combine(directory, "second.dll"));
                PluginManifestSnapshot result = new PluginManifestScanner().Scan(directory, cache, false, 1);
                Equal(1, result.Entries.Count);
                True(result.Truncated);
            });
        }

        private static void ScannerBytesAreBounded()
        {
            True(ManifestCachePolicy.CanAdmitScanBytes(0L, ManifestCachePolicy.MaximumFileBytes));
            True(ManifestCachePolicy.CanAdmitScanBytes(
                ManifestCachePolicy.MaximumScanBytes - 1L, 1L));
            False(ManifestCachePolicy.CanAdmitScanBytes(
                ManifestCachePolicy.MaximumScanBytes, 1L));
            False(ManifestCachePolicy.CanAdmitScanBytes(0L,
                ManifestCachePolicy.MaximumFileBytes + 1L));
            False(ManifestCachePolicy.CanAdmitScanBytes(long.MaxValue, long.MaxValue));
        }

        private static void CancelledScanPublishesNothing()
        {
            WithPluginCopy((directory, cache, plugin) =>
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Throws<OperationCanceledException>(() =>
                    new PluginManifestScanner().Scan(directory, cache, false, 8, cancellation.Token));
                False(File.Exists(cache));
            });
        }

        private static void CancelledCacheWritePublishesNothing()
        {
            WithTemp(directory =>
            {
                string cache = Path.Combine(directory, "cache.bin");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Throws<OperationCanceledException>(() =>
                    ManifestCacheCodec.TryWrite(
                        cache,
                        new[] { Entry("a.dll", 1L, 1L, Sha('D')) },
                        cancellation.Token));
                False(File.Exists(cache));
                False(File.Exists(cache + ".pending"));
            });
        }

        private static void MissingRootIsPartial()
        {
            WithTemp(directory =>
            {
                string missing = Path.Combine(directory, "not-created");
                PluginManifestSnapshot result = new PluginManifestScanner().Scan(
                    missing,
                    Path.Combine(directory, "cache.bin"),
                    false,
                    8);
                True(result.Truncated);
                Equal("bounded-partial", result.Status);
                Equal(0, result.Entries.Count);
            });
        }

        private static void MetadataIsReadOnly()
        {
            WithPluginCopy((directory, cache, plugin) =>
            {
                PluginManifestEntry entry = new PluginManifestScanner().Scan(directory, cache, false, 8).Entries.Single();
                Equal("chazman.RunicVelocity", entry.PluginId);
                Equal("1.0.0", entry.PluginVersion);
                Equal(0, entry.Dependencies.Count);
                Equal("runic-plugin", entry.Classification);
            });
        }

        private static void RuntimeWorkerIsSafe()
        {
            string runtime = Read("RunicVelocity", "Integration", "VelocityRuntime.cs");
            Contains(runtime, "() => scanner.Scan(root, cache, warm, maximum, cancellationToken)");
            string scanner = Read("RunicVelocity", "Core", "PluginManifestScanner.cs");
            False(scanner.Contains("UnityEngine", StringComparison.Ordinal));
            False(scanner.Contains("Assembly.Load", StringComparison.Ordinal));
            False(scanner.Contains("Resolve()", StringComparison.Ordinal));
            Contains(scanner, "MaximumDirectories");
            Contains(scanner, "CanAdmitScanBytes");
            Contains(scanner, "MaximumMetadataFileBytes");
            Contains(scanner, "TryReadEvidence(");
            Contains(scanner, "long remaining = expectedLength");
            Contains(scanner, "stream.ReadByte() != -1");
            False(scanner.Contains("FileShare.ReadWrite", StringComparison.Ordinal));
            Contains(scanner, "info.Refresh()");
            Contains(scanner, "scheduledDirectories");
            Contains(scanner, "typeCount > 65536");
            Contains(scanner, "var pending = new Queue<TypeDefinition>()");
            False(scanner.Contains(
                "new Queue<TypeDefinition>(assembly.MainModule.Types)",
                StringComparison.Ordinal));
            Contains(scanner, "foreach (TypeDefinition topLevel in assembly.MainModule.Types)");
            Contains(scanner, "if (pending.Count >= 65536)");
            Contains(scanner, "cancellationToken.ThrowIfCancellationRequested()");
            Contains(scanner, "stream.Position = 0L");
            Contains(scanner, "AssemblyDefinition.ReadAssembly(stream");
            False(scanner.Contains("AssemblyDefinition.ReadAssembly(path", StringComparison.Ordinal));
        }

        private static void DisabledModeIsInert()
        {
            string plugin = Read("RunicVelocity", "Plugin.cs");
            int enabledCheck = plugin.IndexOf("if (!(VelocityConfig.Enabled?.Value ?? true))", StringComparison.Ordinal);
            int runtimeCreate = plugin.IndexOf("_runtime = new VelocityRuntime()", StringComparison.Ordinal);
            True(enabledCheck >= 0 && enabledCheck < runtimeCreate);
            False(plugin.Contains("RegisterModule", StringComparison.Ordinal));
            False(plugin.Contains("service", StringComparison.OrdinalIgnoreCase));
            string runtime = Read("RunicVelocity", "Integration", "VelocityRuntime.cs");
            Contains(runtime, "if (!_enabled) return;");
        }

        private static void ReleaseSurfaceAligns()
        {
            string plugin = Read("RunicVelocity", "Plugin.cs");
            Contains(plugin, "public const string Version = \"1.0.0\"");
            string contracts = Read("RunicVelocity", "Contracts", "VelocityContracts.cs");
            foreach (string forbidden in new[]
                     {
                         "VelocityCapabilityIds", "ProtocolVersion", "IStartupTimelineService",
                         "IPluginManifestService", "IVelocityStatusService", "VelocityStatusSnapshot"
                     })
                False(contracts.Contains(forbidden, StringComparison.Ordinal));
            using JsonDocument manifest = JsonDocument.Parse(Read("RunicVelocity", "manifest.json"));
            Equal("RunicVelocity", manifest.RootElement.GetProperty("name").GetString());
            Equal("1.0.0", manifest.RootElement.GetProperty("version_number").GetString());
            string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            Sequence(new[] { "denikson-BepInExPack_Valheim-5.4.2333" }, dependencies);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(BuiltPlugin());
            Equal(new Version(1, 0, 0, 0), assembly.Name.Version);
            False(assembly.MainModule.AssemblyReferences.Any(reference =>
                reference.Name.StartsWith("Runic", StringComparison.OrdinalIgnoreCase)));
            False(assembly.MainModule.AssemblyReferences.Any(reference =>
                reference.Name.StartsWith("UnityEngine.UI", StringComparison.Ordinal)));
        }

        private static PluginManifestEntry Entry(string path, long length, long ticks, string sha) =>
            new PluginManifestEntry(path, length, ticks, sha, string.Empty, string.Empty,
                Array.Empty<string>(), "library");
        private static string Sha(char value) => new string(value, 64);

        private static void WriteRawCache(string path, Action<BinaryWriter> writePayload)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true))
            {
                writePayload(payloadWriter);
                payloadWriter.Flush();
                payload = payloadStream.ToArray();
            }
            using var hash = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
            writer.Write(0x52564C43);
            writer.Write(2);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Write(hash.ComputeHash(payload));
        }

        private static void Write7Bit(BinaryWriter writer, int value)
        {
            uint remaining = checked((uint)value);
            while (remaining >= 0x80U)
            {
                writer.Write((byte)((remaining & 0x7FU) | 0x80U));
                remaining >>= 7;
            }
            writer.Write((byte)remaining);
        }

        private static void WithPluginCopy(Action<string, string, string> action)
        {
            WithTemp(directory =>
            {
                string plugin = Path.Combine(directory, "RunicVelocity.dll");
                File.Copy(BuiltPlugin(), plugin);
                action(directory, Path.Combine(directory, "cache.bin"), plugin);
            });
        }

        private static string BuiltPlugin() => PathOf("RunicVelocity", "bin", "Release", "netstandard2.1", "RunicVelocity.dll");
        private static void WithTemp(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "runic-velocity-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(directory); }
            finally { try { Directory.Delete(directory, true); } catch { } }
        }
        private static string Read(params string[] path) => File.ReadAllText(PathOf(path));
        private static string PathOf(params string[] path)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return path.Aggregate(root, Path.Combine);
        }
        private static void Contains(string value, string token) =>
            True(value.Contains(token, StringComparison.Ordinal), "Missing token: " + token);
        private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
            True(expected.SequenceEqual(actual), "Sequences differ.");
        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }
        private static void True(bool value, string message = "Expected true.")
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private static void False(bool value) => True(!value, "Expected false.");
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        private sealed class OversizedEntryList : IReadOnlyList<PluginManifestEntry>
        {
            public int Count => 10_000;
            public PluginManifestEntry this[int index] =>
                throw new InvalidOperationException("Oversized input was inspected.");
            public IEnumerator<PluginManifestEntry> GetEnumerator() =>
                throw new InvalidOperationException("Oversized input was enumerated.");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
