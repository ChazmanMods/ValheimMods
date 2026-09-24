using System;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private const string CapConfig = "# unchanged header\n[General]\nEnabled = true\n\n[Player Capacity]\n# exact comment\nEnabled = false\nMaximumPlayers = 10\n\n[Server Health]\nEnabled = true\nWarnings = true\n";
        private static void CapacityRequestIsStrict()
        {
            string revision = new string('A', 64);
            foreach (int players in new[] { 2, 10, 20, 64 })
            foreach (bool enabled in new[] { false, true })
            {
                byte[] bytes = SentinelCapacityProtocol.Encode(revision, enabled, players);
                True(bytes.Length <= 128);
                True(SentinelCapacityProtocol.TryDecode(bytes, out string r, out bool e, out int p));
                Equal(revision, r); Equal(enabled, e); Equal(players, p);
                False(SentinelCapacityProtocol.TryDecode(bytes.Concat(new byte[] { 10 }).ToArray(), out _, out _, out _));
            }
            foreach (int invalid in new[] { int.MinValue, 0, 1, 65, int.MaxValue })
                Throws<ArgumentException>(() => SentinelCapacityProtocol.Encode(revision, true, invalid));
            foreach (string invalid in new[] { "", new string('a', 64), new string('G', 64), new string('0', 63), "../config" })
                Throws<ArgumentException>(() => SentinelCapacityProtocol.Encode(invalid, true, 10));
            foreach (string count in new[] { "01", "02", "1", "65", "-5", " 10", "10 ", "1.5", "10\nadmin" })
                False(SentinelCapacityProtocol.TryDecode(Encoding.UTF8.GetBytes("RUNIC-SENTINEL-CAPACITY/1\n" + revision + "\n1\n" + count + "\n"), out _, out _, out _));
            False(SentinelCapacityProtocol.TryDecode(new byte[129], out _, out _, out _));
            False(SentinelCapacityProtocol.TryDecode(new byte[] { 0xff }, out _, out _, out _));
        }
        private static void CapacityStatusIsCompatible()
        {
            var input = new SentinelAdminDocument { Sequence = 7, Profile = "keep-policy", Administrators = "steam|123", CapacitySupported = true, CapacityAvailable = true, CapacitySavedEnabled = true, CapacitySavedPlayers = "20", CapacityActivePlayers = "10", CapacityRestartRequired = true, CapacityRevision = new string('A', 64), CapacityStatus = "vanilla", CapacityVersion = "1.2.0", CapacityCurrentPlayers = "3" };
            True(SentinelAdminProtocol.TryDecode(SentinelAdminProtocol.Encode(input), out var output));
            Equal(input.Profile, output.Profile); Equal(input.Administrators, output.Administrators);
            Equal(input.CapacityRevision, output.CapacityRevision); Equal("20", output.CapacitySavedPlayers);
            True(output.CapacityRestartRequired && output.CapacityAvailable && output.CapacitySupported && output.CapacitySavedEnabled);
            string legacy = string.Join("\n", Encoding.UTF8.GetString(SentinelAdminProtocol.Encode(input)).Split('\n').Where(line => !line.StartsWith("cap-", StringComparison.Ordinal)));
            True(SentinelAdminProtocol.TryDecode(Encoding.UTF8.GetBytes(legacy), out output));
            False(output.CapacitySupported); False(output.CapacityAvailable);
            Equal(input.Profile, output.Profile); Equal(7L, output.Sequence);
        }
        private static void CapacityEditsAreScoped()
        {
            foreach (bool bom in new[] { false, true })
            foreach (string newline in new[] { "\n", "\r\n" })
            {
                string original = (bom ? "\ufeff" : "") + CapConfig.Replace("\n", newline);
                var parsed = SentinelCapacitySettings.Parse(Encoding.UTF8.GetBytes(original));
                False(parsed.Enabled); Equal(10, parsed.Players);
                string edited = Encoding.UTF8.GetString(parsed.Edit(true, 20));
                string expected = original.Replace("Enabled = false", "Enabled = true").Replace("MaximumPlayers = 10", "MaximumPlayers = 20");
                Equal(expected, edited);
                var after = SentinelCapacitySettings.Parse(Encoding.UTF8.GetBytes(edited));
                True(after.Enabled); Equal(20, after.Players); False(after.Revision == parsed.Revision);
                Throws<ArgumentOutOfRangeException>(() => parsed.Edit(true, 65));
            }
        }
        private static void CapacityConfigsRejectAmbiguity()
        {
            foreach (string invalid in new[] { "", CapConfig.Replace("[Player Capacity]", "[Other]"), CapConfig + "[Player Capacity]\n", CapConfig.Replace("Enabled = false", "Enabled = false\nEnabled = true"), CapConfig.Replace("MaximumPlayers = 10", "MaximumPlayers = 10\nMaximumPlayers = 20"), CapConfig.Replace("MaximumPlayers = 10", "MaximumPlayers = 999"), CapConfig.Replace("Enabled = false", "Enabled = maybe"), CapConfig.Replace("MaximumPlayers = 10", "") })
                Throws<InvalidDataException>(() => SentinelCapacitySettings.Parse(Encoding.UTF8.GetBytes(invalid)));
            Throws<InvalidDataException>(() => SentinelCapacitySettings.Parse(new byte[SentinelCapacitySettings.MaximumConfigBytes + 1]));
        }
        private static void CapacityPersistenceIsSafe()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RunicSentinelCapTests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "chazman.RunicWorldEngine.cfg");
                byte[] original = Encoding.UTF8.GetBytes(CapConfig);
                File.WriteAllBytes(path, original);
                string revision = SentinelCapacitySettings.Read(path).Revision;
                SentinelCapacitySettings.Save(path, revision, true, 20);
                Sequence(original, File.ReadAllBytes(path + ".sentinel-cap.bak"));
                Equal(20, SentinelCapacitySettings.Read(path).Players);
                byte[] beforeStale = File.ReadAllBytes(path);
                Throws<InvalidOperationException>(() => SentinelCapacitySettings.Save(path, revision, false, 30));
                Sequence(beforeStale, File.ReadAllBytes(path));
                Throws<ArgumentOutOfRangeException>(() => SentinelCapacitySettings.Save(path, SentinelCapacitySettings.Read(path).Revision, true, 65));
                Sequence(beforeStale, File.ReadAllBytes(path));
                SentinelCapacitySettings.Save(path, SentinelCapacitySettings.Read(path).Revision, false, 12);
                Sequence(beforeStale, File.ReadAllBytes(path + ".sentinel-cap.bak"));
                var final = SentinelCapacitySettings.Read(path); False(final.Enabled); Equal(12, final.Players);
                Equal(2, Directory.GetFiles(root).Length); // live + one backup; no leaked temporary files.
            }
            finally
            {
                if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "RunicSentinelCapTests-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture directory.");
                Directory.Delete(root, true);
            }
        }
        private static void CapacityAuthorityIsServerOwned()
        {
            string control = Read("RunicSentinel", "Runtime", "SentinelAdminControl.cs");
            string execute = control.Substring(control.IndexOf("private void Execute", StringComparison.Ordinal));
            True(execute.IndexOf("_runtime.IsBanned", StringComparison.Ordinal) < execute.IndexOf("SentinelCapacityBridge.Save", StringComparison.Ordinal));
            True(execute.IndexOf("SentinelAdministratorRules.Allows", StringComparison.Ordinal) < execute.IndexOf("SentinelCapacityBridge.Save", StringComparison.Ordinal));
            Contains(control, "Submit(\"capacity\", bytes, new Pending");
            Contains(control, "TryExecuteLocal(\"capacity\", bytes");
            string bridge = Read("RunicSentinel", "Runtime", "SentinelCapacityBridge.cs");
            Contains(bridge, "!ZNet.instance.IsServer()"); Contains(bridge, "config.ConfigFilePath");
            Contains(bridge, "config.SaveOnConfigSet = false");
            foreach (string forbidden in new[] { "Process.Start", ".Initialize(", "MaximumPlayers.Value", ".Disconnect(", "Config.Reload(", "File.WriteAllText" }) False(bridge.Contains(forbidden));
            string panel = ReadLocalizedPanel();
            Contains(panel, "\"Server Cap\""); Contains(panel, "Save for Next Restart"); Contains(panel, "!_document.CapacitySupported");
            Contains(panel, "CapacityRestartRequired"); Contains(panel, "!_requesting && valid && changed");
        }
        private static void CapacityAdapterMatchesWorldEngine()
        {
            string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            using var engine = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "RunicWorldEngine", "bin", "Release", "netstandard2.1", "RunicWorldEngine.dll"));
            Equal(1, engine.Name.Version.Major); Equal(2, engine.Name.Version.Minor);
            var plugin = engine.MainModule.Types.Single(t => t.FullName == "RunicWorldEngine.Plugin");
            True(plugin.Fields.Any(f => f.Name == "_harmony"));
            var runtime = engine.MainModule.Types.Single(t => t.FullName == "RunicWorldEngine.Integration.CapacityRuntime");
            Equal("System.Boolean", runtime.Fields.Single(f => f.Name == "_requested").FieldType.FullName);
            Equal("System.Boolean", runtime.Properties.Single(p => p.Name == "ValidatedOverride").PropertyType.FullName);
            Equal("System.Int32", runtime.Properties.Single(p => p.Name == "PlayerLimit").PropertyType.FullName);
            Equal("System.String", runtime.Properties.Single(p => p.Name == "Status").PropertyType.FullName);
        }
    }
}
