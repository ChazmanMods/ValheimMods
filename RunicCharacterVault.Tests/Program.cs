using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BepInEx;
using HarmonyLib;
using RunicCharacterVault;

namespace RunicCharacterVault.Tests
{
    internal static class Program
    {
        private static int failures;
        private static int passes;

        private static int Main()
        {
            Run("admission matrix", AdmissionMatrix);
            Run("existing character enrollment and account limits", ExistingEnrollment);
            Run("import has no starter items and saves once on spawn", ImportedStartingItems);
            Run("enrollment preserves bytes and refuses overwrite", EnrollmentStorage);
            Run("real character transport and vault round trip", RealCharacterRoundTrip);
            Run("save acknowledgement requires verified admitted permitted session", SaveGate);
            Run("vault path segments neutralize traversal characters", SafeSegments);
            Run("valid chunked transfer round trips", TransferRoundTrip);
            Run("corrupt transfer is rejected", CorruptTransferRejected);
            Run("backup retention is bounded", BackupRetentionIsBounded);
            Run("Valheim 1.0 Harmony targets exist", HarmonyTargetsExist);
            Run("all Harmony patch declarations resolve for Valheim 1.0", HarmonyPatchDeclarationsResolve);
            Run("save status clones a live Valheim font template", SaveStatusUsesLiveFontTemplate);
            Run("plugin metadata is release coherent", PluginMetadata);
            Run("Thunderstore root files are valid", PackageFiles);

            System.Console.WriteLine();
            System.Console.WriteLine(failures == 0
                ? $"PASS: {passes}/{passes + failures} Runic Character Vault tests"
                : $"FAIL: {failures}/{passes + failures} Runic Character Vault tests failed");
            return failures == 0 ? 0 : 1;
        }

        private static void AdmissionMatrix()
        {
            Equal(CharacterAdmission.ExistingProfile,
                CharacterAdmissionPolicy.Decide(true, false, false, true, false));
            Equal(CharacterAdmission.RejectUnregisteredProfile,
                CharacterAdmissionPolicy.Decide(false, false, false, false, true));
            Equal(CharacterAdmission.RejectAdditionalCharacter,
                CharacterAdmissionPolicy.Decide(false, true, false, true, true));
            Equal(CharacterAdmission.RejectConcurrentEnrollment,
                CharacterAdmissionPolicy.Decide(false, true, true, false, false));
            Equal(CharacterAdmission.NewEnrollment,
                CharacterAdmissionPolicy.Decide(false, true, false, false, true));
        }

        private static void SaveGate()
        {
            ServerProfileSessionState session = new ServerProfileSessionState();
            False(SaveAcknowledgementPolicy.CanAcknowledge(session));
            session.Verified = true;
            session.Admitted = true;
            False(SaveAcknowledgementPolicy.CanAcknowledge(session));
            session.RecordPermission(true);
            True(SaveAcknowledgementPolicy.CanAcknowledge(session));
        }

        private static void ExistingEnrollment()
        {
            Equal(CharacterAdmission.NewEnrollment,
                CharacterAdmissionPolicy.Decide(false, false, false, false, true, true));
            Equal(CharacterAdmission.RejectUnregisteredProfile,
                CharacterAdmissionPolicy.Decide(false, false, false, false, true, false));
            Equal(CharacterAdmission.RejectAdditionalCharacter,
                CharacterAdmissionPolicy.Decide(false, false, false, true, true, true));
            Equal(CharacterAdmission.RejectConcurrentEnrollment,
                CharacterAdmissionPolicy.Decide(false, false, true, false, false, true));
            Equal(CharacterAdmission.ExistingProfile,
                CharacterAdmissionPolicy.Decide(true, false, false, true, false, true));
            CharacterAdmissionEvaluator evaluator = new CharacterAdmissionEvaluator(new OccupiedCatalog());
            Equal(CharacterAdmission.RejectAdditionalCharacter,
                evaluator.Decide(false, "account", false, false, true, true));
            Equal(CharacterAdmission.NewEnrollment,
                evaluator.Decide(false, "account", false, true, true, true));
        }

        private sealed class OccupiedCatalog : ICharacterProfileCatalog
        {
            public bool HasProfile(string accountId) => true;
        }

        private static void ImportedStartingItems()
        {
            var configured = new[] { new StartingItem("Hammer", 1) };
            Equal(1, StartingItemGrantPolicy.ForEnrollment(true, configured).Count);
            var imported = StartingItemGrantPolicy.ForEnrollment(false, configured);
            Equal(0, imported.Count);
            ClientSaveLifecycle lifecycle = new ClientSaveLifecycle();
            lifecycle.BeginEnrollment();
            int saves = 0;
            int grants = 0;
            True(StartingItemGrantPolicy.ApplyEnrollment<object>(lifecycle, true, imported,
                _ => new object(), (_, __) => { grants++; return true; },
                () => saves++, _ => throw new Exception("Unexpected grant")));
            False(StartingItemGrantPolicy.ApplyEnrollment<object>(lifecycle, true, imported,
                _ => new object(), (_, __) => { grants++; return true; },
                () => saves++, _ => throw new Exception("Unexpected grant")));
            Equal(0, grants);
            Equal(1, saves);
            True(lifecycle.CanUpload);
        }

        private static void EnrollmentStorage()
        {
            WithStorage((storage, root) =>
            {
                byte[] original = Enumerable.Range(0, 90000).Select(i => (byte)(i % 251)).ToArray();
                storage.CommitEnrollment("account", "Drakvaldr", original);
                True(storage.TryRead("account", "Drakvaldr", out byte[] stored));
                SequenceEqual(original, stored);
                string backup = Path.Combine(root, "enrollment-backups", "account_Drakvaldr.fch");
                SequenceEqual(original, File.ReadAllBytes(backup));
                Throws<IOException>(() => storage.CommitEnrollment("account", "Drakvaldr", new byte[] { 9 }));
                Throws<IOException>(() => storage.CommitEnrollment("account", "drakvaldr", new byte[] { 9 }));
                storage.Commit("account", "Drakvaldr", new byte[] { 8, 7 });
                SequenceEqual(original, File.ReadAllBytes(backup));
                True(storage.TryRead("account", "Drakvaldr", out byte[] updated));
                SequenceEqual(new byte[] { 8, 7 }, updated);
            });
        }

        private static void RealCharacterRoundTrip()
        {
            string fixture = Environment.GetEnvironmentVariable("RUNIC_CHARACTER_FIXTURE");
            if (string.IsNullOrWhiteSpace(fixture))
                throw new InvalidOperationException("RUNIC_CHARACTER_FIXTURE must identify a backup .fch for the migration regression.");
            byte[] original = File.ReadAllBytes(fixture);
            True(original.Length > 0);
            string hash = VaultStorage.Hash(original);
            var begin = ProfileTransferProtocol.Begin("existing-character", original.Length, hash);
            begin.SetPos(0);
            var transfer = IncomingTransfer.Create(begin, original.Length);
            for (int offset = 0; offset < original.Length; offset += ProfileTransferProtocol.ChunkSize)
            {
                var chunk = ProfileTransferProtocol.Chunk("existing-character", original, offset);
                chunk.SetPos(0);
                transfer.Add(chunk);
            }
            byte[] received = transfer.Complete("existing-character");
            WithStorage((storage, root) =>
            {
                storage.CommitEnrollment("fixture-account", "fixture-character", received);
                True(storage.TryRead("fixture-account", "fixture-character", out byte[] stored));
                SequenceEqual(original, stored);
                Equal(hash, VaultStorage.Hash(stored));
            });
            Equal(hash, VaultStorage.Hash(File.ReadAllBytes(fixture)));
        }

        private static void WithStorage(Action<VaultStorage, string> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "RunicEnrollmentTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { test(new VaultStorage(root), root); }
            finally { Directory.Delete(root, true); }
        }

        private static void SafeSegments()
        {
            Equal(".._.._evil_name_", VaultStorage.SafeSegment("../../evil|name?"));
            False(VaultStorage.SafeSegment("player/name").Contains("/"));
        }

        private static void TransferRoundTrip()
        {
            byte[] data = Enumerable.Range(0, ProfileTransferProtocol.ChunkSize + 17)
                .Select(index => (byte)(index % 251)).ToArray();
            string id = Guid.NewGuid().ToString("N");
            ZPackage begin = ProfileTransferProtocol.Begin(
                id, data.Length, VaultStorage.Hash(data));
            begin.SetPos(0);
            IncomingTransfer incoming = IncomingTransfer.Create(begin, data.Length);
            ZPackage first = ProfileTransferProtocol.Chunk(id, data, 0);
            first.SetPos(0);
            incoming.Add(first);
            ZPackage second = ProfileTransferProtocol.Chunk(
                id, data, ProfileTransferProtocol.ChunkSize);
            second.SetPos(0);
            incoming.Add(second);
            SequenceEqual(data, incoming.Complete(id));
        }

        private static void CorruptTransferRejected()
        {
            byte[] data = { 1, 2, 3, 4 };
            string id = "corrupt";
            ZPackage begin = ProfileTransferProtocol.Begin(
                id, data.Length, new string('0', 64));
            begin.SetPos(0);
            IncomingTransfer incoming = IncomingTransfer.Create(begin, 64);
            ZPackage chunk = ProfileTransferProtocol.Chunk(id, data, 0);
            chunk.SetPos(0);
            incoming.Add(chunk);
            Throws<InvalidDataException>(() => incoming.Complete(id));
        }

        private static void BackupRetentionIsBounded()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "RunicCharacterVaultTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                DateTime start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                for (int index = 0; index < 30; index++)
                {
                    DateTime stamp = start.AddDays(index);
                    string name = "account_character_" +
                        stamp.ToString("yyyyMMdd'T'HHmmssfffffff'Z'") + ".fch";
                    File.WriteAllBytes(Path.Combine(directory, name), new byte[] { (byte)index });
                }
                BackupRetention.Apply(directory, "account_character");
                int retained = Directory.GetFiles(directory, "*.fch").Length;
                True(retained <= 15);
                True(retained >= 5);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void HarmonyTargetsExist()
        {
            string[] znet = { "SaveWorldAndPlayerProfiles", "Start", "OnNewConnection",
                "SendPeerInfo", "RPC_PeerInfo", "IsAllowed", "Disconnect", "OnDestroy",
                "InternalKick", "Save" };
            foreach (string method in znet) HasMethod(typeof(ZNet), method);
            foreach (string method in new[] { "Logout", "ContinueLogout", "OnDestroy", "SpawnPlayer" })
                HasMethod(typeof(Game), method);
            HasMethod(typeof(PlayerProfile), "SavePlayerToDisk");
            HasMethod(typeof(Player), "OnSpawned");
            HasMethod(typeof(Minimap), "Start");
        }

        private static void PluginMetadata()
        {
            Assembly assembly = typeof(CharacterVaultPlugin).Assembly;
            Equal(new System.Version(1, 0, 2, 0), assembly.GetName().Version);
            BepInPlugin attribute = typeof(CharacterVaultPlugin)
                .GetCustomAttribute<BepInPlugin>();
            Equal("chazman.RunicCharacterVault", attribute.GUID);
            Equal("1.0.2", attribute.Version.ToString());
        }

        private static void SaveStatusUsesLiveFontTemplate()
        {
            string source = File.ReadAllText(Path.Combine(
                FindProjectRoot(), "CharacterSaveStatusDisplay.cs"));
            True(source.Contains("Object.Instantiate(", StringComparison.Ordinal));
            True(source.Contains("minimap.m_biomeNameSmall.gameObject", StringComparison.Ordinal));
            False(source.Contains("AddComponent<TextMeshProUGUI>", StringComparison.Ordinal));
            True(source.Contains("_label.font == null", StringComparison.Ordinal));
        }

        private static void HarmonyPatchDeclarationsResolve()
        {
            Type[] patchTypes = typeof(CharacterVaultPlugin).Assembly.GetTypes()
                .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                .ToArray();
            True(patchTypes.Length > 0);
            foreach (Type patchType in patchTypes)
            {
                HarmonyPatch[] declarations = patchType
                    .GetCustomAttributes(typeof(HarmonyPatch), false)
                    .Cast<HarmonyPatch>().ToArray();
                Type declaringType = declarations.Select(declaration =>
                    declaration.info.declaringType).FirstOrDefault(type => type != null);
                string methodName = declarations.Select(declaration =>
                    declaration.info.methodName).FirstOrDefault(name =>
                    !string.IsNullOrWhiteSpace(name));
                if (declaringType != null && !string.IsNullOrWhiteSpace(methodName))
                {
                    HasMethod(declaringType, methodName);
                    continue;
                }
                if (declaringType != null && declarations.Any(declaration =>
                    declaration.info.methodType == MethodType.Constructor))
                {
                    True(declaringType.GetConstructors(BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic).Length > 0);
                    continue;
                }

                MethodInfo[] attributedMethods = patchType.GetMethods(BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic).Where(method =>
                    method.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0).ToArray();
                if (attributedMethods.Length > 0)
                {
                    foreach (MethodInfo method in attributedMethods)
                    {
                        HarmonyPatch[] methodDeclarations = method
                            .GetCustomAttributes(typeof(HarmonyPatch), false)
                            .Cast<HarmonyPatch>().ToArray();
                        Type methodType = methodDeclarations.Select(declaration =>
                            declaration.info.declaringType).FirstOrDefault(type => type != null);
                        string methodTarget = methodDeclarations.Select(declaration =>
                            declaration.info.methodName).FirstOrDefault(name =>
                            !string.IsNullOrWhiteSpace(name));
                        True(methodType != null && !string.IsNullOrWhiteSpace(methodTarget));
                        HasMethod(methodType, methodTarget);
                    }
                    continue;
                }

                if (!patchType.GetMethods(BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic).Any(method =>
                    method.Name == "TargetMethod" || method.Name == "TargetMethods"))
                {
                    throw new InvalidOperationException(
                        "Patch target declaration was not resolved: " + patchType.FullName);
                }
            }
        }

        private static void PackageFiles()
        {
            string root = FindProjectRoot();
            string manifestPath = Path.Combine(root, "manifest.json");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Equal("RunicCharacterVault", manifest.RootElement.GetProperty("name").GetString());
            Equal("1.0.2", manifest.RootElement.GetProperty("version_number").GetString());
            string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                .EnumerateArray().Select(item => item.GetString()).ToArray();
            SequenceEqual(new[] { "denikson-BepInExPack_Valheim-5.4.2350" }, dependencies);
            string icon = Path.Combine(root, "icon.png");
            byte[] png = File.ReadAllBytes(icon);
            Equal(256, ReadBigEndian(png, 16));
            Equal(256, ReadBigEndian(png, 20));
            foreach (string required in new[] { "README.md", "CHANGELOG.md", "LICENSE", "NOTICE.md" })
                True(File.Exists(Path.Combine(root, required)));
        }

        private static string FindProjectRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "RunicCharacterVault", "manifest.json");
                if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
                string direct = Path.Combine(current.FullName, "manifest.json");
                if (File.Exists(direct)) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("RunicCharacterVault project root was not found.");
        }

        private static int ReadBigEndian(byte[] data, int offset) =>
            data[offset] << 24 | data[offset + 1] << 16 |
            data[offset + 2] << 8 | data[offset + 3];

        private static void HasMethod(Type type, string name)
        {
            True(type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic).Any(method => method.Name == name));
        }

        private static void Run(string name, Action test)
        {
            try { test(); passes++; System.Console.WriteLine("PASS " + name); }
            catch (Exception exception)
            { failures++; System.Console.Error.WriteLine("FAIL " + name); System.Console.Error.WriteLine("     " + exception); }
        }

        private static void True(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void False(bool value) => True(!value);

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}; got {actual}.");
        }

        private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException("Sequences differ.");
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
