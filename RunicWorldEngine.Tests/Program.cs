using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using RunicWorldEngine.Contracts;
using RunicWorldEngine.Core;

namespace RunicWorldEngine.Tests
{
    internal static partial class Program
    {
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("byte rates seed, wrap and reject resets", ByteRatesAreSafe),
                ("independent payload meters count both directions without resetting game statistics", PayloadMetersAreIndependent),
                ("send-window pressure respects transport estimates and unavailable data", SendWindowEstimatesAreSafe),
                ("health warnings sustain, cool down and recover", WarningsAreBounded),
                ("ownership observation is bounded and distinguishes transitions", TransfersAreBounded),
                ("client and dedicated player-cap consumers are fully audited", CapacityTargetsAreAudited),
                ("changed cap methods are rejected", ChangedCapacityIsRejected),
                ("missing cap methods are rejected", MissingCapacityIsRejected),
                ("unexpected nested limit is rejected", ExtraCapacityIsRejected),
                ("transport host slots and cap bounds are consistent", CapacityBoundsAreCorrect),
                ("capacity rewrites preserve unrelated constants and control-flow metadata", CapacityRewritesAreExact),
                ("capacity rewrites reject ambiguous or missing matches without partial mutation", CapacityRewritesRejectAmbiguity),
                ("client, Windows server and Linux diagnostic contracts match", HealthContractsAreExact),
                ("audited raw method IL matches the independent PE reader", RawMethodCodeIsExact),
                ("health runtime does not reset statistics or scan world objects", HealthIsReadOnly),
                ("observatory captures and resets interval rates", ObservatoryRatesAreIntervals),
                ("observatory clamps invalid aggregate inputs", ObservatoryInputsAreClamped),
                ("observatory sequence saturates instead of wrapping", SequenceNeverWraps),
                ("observatory duration values stay finite", ObservatoryDurationsAreFinite),
                ("installed Valheim observatory targets are exact", InstalledTargetsAreExact),
                ("runtime uses constant-time aggregate reads", RuntimeUsesAggregateReads),
                ("load completion cannot bypass the sampling ceiling", SamplingCeilingCannotBeForced),
                ("timing finalizers preserve installed exceptions", TimingFinalizersPreserveExceptions),
                ("world engine has no destructive world path", RuntimeHasNoDestructiveWorldPath),
                ("save smoothing stays on the Unity thread and is time bounded", SaveSmoothingIsSafe),
                ("disabled mode is startup inert", DisabledModeIsInert),
                ("release identity and standalone surface align", ReleaseSurfaceIsAligned),
                ("documentation and configuration describe observability and save smoothing", DocumentationIsAligned),
                ("icon is an exact 256 by 256 PNG", IconIsExact)
            };
            int failed = 0;
            foreach ((string name, Action run) in tests)
            {
                try
                {
                    run();
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void ObservatoryRatesAreIntervals()
        {
            var counter = new ObservatoryCounter();
            counter.MarkCreated();
            counter.MarkCreated();
            counter.MarkDestroyed();
            ZdoObservatorySnapshot first = counter.Capture(100L, 20, 3, 4, 5);
            Equal(2, first.CreatedSincePreviousSample);
            Equal(1, first.DestroyedSincePreviousSample);
            Equal(20, first.TotalObjects);
            ZdoObservatorySnapshot second = counter.Capture(200L, 21, 2, 1, 2);
            Equal(0, second.CreatedSincePreviousSample);
            Equal(0, second.DestroyedSincePreviousSample);
            Equal(first.Sequence + 1L, second.Sequence);
        }

        private static void ObservatoryInputsAreClamped()
        {
            var counter = new ObservatoryCounter();
            ZdoObservatorySnapshot value = counter.Capture(-1L, -2, -3, -4, -5);
            Equal(0L, value.CapturedUnixMilliseconds);
            Equal(0, value.TotalObjects);
            Equal(0, value.ConnectedPeers);
            Equal(0, value.SentLastSecond);
            Equal(0, value.ReceivedLastSecond);
            counter.Reset();
            Equal(0L, counter.Current.Sequence);
        }

        private static void SequenceNeverWraps()
        {
            var counter = new ObservatoryCounter();
            typeof(ObservatoryCounter).GetField("_sequence",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(counter, long.MaxValue);
            Equal(long.MaxValue, counter.Capture(1L, 0, 0, 0, 0).Sequence);
            Equal(long.MaxValue, counter.Capture(2L, 0, 0, 0, 0).Sequence);
        }

        private static void ObservatoryDurationsAreFinite()
        {
            var counter = new ObservatoryCounter();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            counter.RecordSaveDuration(start);
            counter.RecordLoadDuration(start);
            ZdoObservatorySnapshot value = counter.Capture(1L, 0, 0, 0, 0);
            True(value.LastSaveMilliseconds >= 0d && !double.IsNaN(value.LastSaveMilliseconds));
            True(value.LastLoadMilliseconds >= 0d && !double.IsInfinity(value.LastLoadMilliseconds));
        }

        private static void InstalledTargetsAreExact()
        {
            string game = Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ??
                          @"E:\SteamLibrary\steamapps\common\Valheim";
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(game, "valheim_Data", "Managed", "assembly_valheim.dll"));
            TypeDefinition manager = assembly.MainModule.Types.Single(type => type.FullName == "ZDOMan");
            Method(manager, "CreateNewZDO", "ZDOID", "UnityEngine.Vector3", "System.Int32");
            Method(manager, "HandleDestroyedZDO", "ZDOID");
            Method(manager, "UpdateStats", "System.Single");
            Method(manager, "SaveChunks", "System.String", "FileHelpers/FileSource");
            Method(manager, "LoadChunks", "System.String", "FileHelpers/FileSource", "Version/World");
            Method(manager, "Load", "System.IO.BinaryReader", "Version/World");
        }

        private static void RuntimeUsesAggregateReads()
        {
            string source = Read("RunicWorldEngine", "Integration", "ObservatoryRuntime.cs");
            foreach (string token in new[]
                     {
                         "objects.Count", "collection.Count", "timestamp < _nextCaptureTimestamp",
                         "timestamp + Stopwatch.Frequency", "typeof(Dictionary<ZDOID, ZDO>)",
                         "GetGenericTypeDefinition() == typeof(List<>)"
                     })
                Contains(source, token);
            foreach (string forbidden in new[] { "foreach (ZDO", "GetSaveClone", "FindSectorObjects" })
                False(source.Contains(forbidden, StringComparison.Ordinal));
        }

        private static void SamplingCeilingCannotBeForced()
        {
            string source = Read("RunicWorldEngine", "Integration", "ObservatoryRuntime.cs");
            Contains(source, "internal static void Capture(ZDOMan manager)");
            Contains(source, "if (timestamp < _nextCaptureTimestamp) return;");
            False(source.Contains("force", StringComparison.OrdinalIgnoreCase));
        }

        private static void TimingFinalizersPreserveExceptions()
        {
            string source = Read("RunicWorldEngine", "Integration", "HarmonyPatches.cs");
            Equal(3, Count(source, "return __exception;"));
            Equal(3, Count(source, "private static Exception Finalizer"));
        }

        private static void RuntimeHasNoDestructiveWorldPath()
        {
            string all = string.Join("\n", Directory.GetFiles(
                    PathOf("RunicWorldEngine"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) &&
                               !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
            foreach (string forbidden in new[]
                     {
                         ".DestroyZDO(", ".ForceSendZDO(", ".SetOwner(", "new ZDO(",
                         "ZDOExtraData.Set", "RunicRegistry", "RegisterModule", "CapabilityId"
                     })
                False(all.Contains(forbidden, StringComparison.Ordinal));
            Contains(all, "Unknown data is preserved");
        }

        private static void SaveSmoothingIsSafe()
        {
            string source = Read("RunicWorldEngine", "Integration", "SaveSmoothingRuntime.cs");
            foreach (string token in new[]
                     {
                         "if (!(WorldEngineConfig.SmoothWorldSaves?.Value ?? true) || sync) return true;",
                         "MaximumSaveDeferralSeconds", "SaveWorldMethod.Invoke", "thread.IsAlive",
                         "Time.unscaledDeltaTime", "_pending = false"
                     })
                Contains(source, token);
            foreach (string forbidden in new[]
                     {
                         "Task.Run", "new Thread", "ThreadPool", "GetSaveClone", "ZDOExtraData"
                     })
                False(source.Contains(forbidden, StringComparison.Ordinal));
        }

        private static void DisabledModeIsInert()
        {
            string plugin = Read("RunicWorldEngine", "Plugin.cs");
            int enabled = plugin.IndexOf("if (!(WorldEngineConfig.Enabled?.Value ?? true))", StringComparison.Ordinal);
            int harmony = plugin.IndexOf("_harmony = new Harmony(Guid)", StringComparison.Ordinal);
            int verify = plugin.IndexOf("ObservatoryRuntime.Verify()", StringComparison.Ordinal);
            True(enabled >= 0 && enabled < harmony && enabled < verify);
            False(plugin.Contains("RegisterModule", StringComparison.Ordinal));
        }

        private static void ReleaseSurfaceIsAligned()
        {
            string plugin = Read("RunicWorldEngine", "Plugin.cs");
            Contains(plugin, "public const string Version = \"1.2.0\"");
            string project = Read("RunicWorldEngine", "RunicWorldEngine.csproj");
            False(project.Contains("ProjectReference", StringComparison.Ordinal));
            False(project.Contains("BoundedOwnershipRegistry", StringComparison.Ordinal));
            False(File.Exists(PathOf("RunicWorldEngine", "Core", "BoundedOwnershipRegistry.cs")));

            using JsonDocument manifest = JsonDocument.Parse(Read("RunicWorldEngine", "manifest.json"));
            Equal("RunicWorldEngine", manifest.RootElement.GetProperty("name").GetString());
            Equal("1.2.0", manifest.RootElement.GetProperty("version_number").GetString());
            string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            Sequence(new[] { "denikson-BepInExPack_Valheim-5.4.2350" }, dependencies);

            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(BuiltPlugin());
            Equal(new Version(1, 2, 0, 0), assembly.Name.Version);
            False(assembly.MainModule.AssemblyReferences.Any(reference =>
                reference.Name.StartsWith("Runic", StringComparison.OrdinalIgnoreCase)));
            False(assembly.MainModule.Types.Any(type =>
                type.FullName.Contains("Registry", StringComparison.OrdinalIgnoreCase) ||
                type.FullName.Contains("Capability", StringComparison.OrdinalIgnoreCase) ||
                type.FullName.Contains("Protocol", StringComparison.OrdinalIgnoreCase) ||
                type.FullName.Contains("Ownership", StringComparison.OrdinalIgnoreCase)));
        }

        private static void DocumentationIsAligned()
        {
            string readme = Read("RunicWorldEngine", "README.md");
            foreach (string token in new[]
                     {
                         "unknown third-party data is always preserved", "one-second ceiling",
                         "failed sample is discarded", "Save smoothing", "Synchronous shutdown saves",
                         "never touches Unity objects", "worker thread"
                     })
                Contains(readme, token);
            foreach (string token in new[] { "runicworld_status", "five sites", "six on a dedicated server", "PlayFab", "Send-window pressure", "Authentication and character-vault checks remain in effect" })
                Contains(readme, token);
            string config = Read("RunicWorldEngine", "RunicWorldEngine.cfg.example");
            Contains(config, "Enabled = true");
            Contains(config, "LogPeriodicSummary = false");
            Contains(config, "SummaryIntervalSeconds = 30");
            Contains(config, "[Save Smoothing]");
            Contains(config, "FrameBudgetMilliseconds = 24");
            Contains(config, "MaximumDeferralSeconds = 5");
            False(config.Contains("Core module", StringComparison.Ordinal));
        }

        private static void IconIsExact()
        {
            byte[] png = File.ReadAllBytes(PathOf("RunicWorldEngine", "icon.png"));
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            True(png.Length > 24 && png.Take(8).SequenceEqual(signature));
            Equal(256, BigEndianInt32(png, 16));
            Equal(256, BigEndianInt32(png, 20));
        }

        private static int BigEndianInt32(byte[] value, int offset) =>
            value[offset] << 24 | value[offset + 1] << 16 |
            value[offset + 2] << 8 | value[offset + 3];

        private static MethodDefinition Method(TypeDefinition type, string name, params string[] parameters)
        {
            MethodDefinition[] matches = type.Methods.Where(method =>
                method.Name == name && method.Parameters.Select(value => value.ParameterType.FullName)
                    .SequenceEqual(parameters)).ToArray();
            Equal(1, matches.Length);
            return matches[0];
        }

        private static int Count(string value, string token)
        {
            int count = 0;
            for (int index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0;
                 index += token.Length)
                count++;
            return count;
        }

        private static string Read(params string[] path) => File.ReadAllText(PathOf(path));
        private static string BuiltPlugin() => PathOf(
            "RunicWorldEngine", "bin", "Release", "netstandard2.1", "RunicWorldEngine.dll");
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
    }
}
