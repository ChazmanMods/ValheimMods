using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunicSentinel.Core;

namespace RunicSentinel.Forge
{
    internal static class Program
    {
        private const int MaximumInputBytes = 1024 * 1024;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "keygen") return Keygen(args[1]);
                if (args.Length == 1 && args[0] == "selftest") return SelfTest();
                if (args.Length == 4 && args[0] == "compile")
                    return Compile(args[1], args[2], args[3]);
                if (args.Length == 5 && args[0] == "verify")
                    return Verify(args[1], args[2], args[3], args[4]);
                Usage();
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("ERROR " + exception.Message);
                return 1;
            }
        }

        private static int Keygen(string outputDirectory)
        {
            string root = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(root);
            string privatePath = Path.Combine(root, "RunicSentinel.private.pem");
            string publicPath = Path.Combine(root, "RunicSentinel.policy.pub");
            if (File.Exists(privatePath) || File.Exists(publicPath))
                throw new IOException("Refusing to overwrite an existing Sentinel key.");
            using RSA rsa = RSA.Create(3072);
            RSAParameters parameters = rsa.ExportParameters(false);
            string publicText = "RUNIC-RSA-PUBLIC/1\nmodulus=" +
                                Convert.ToBase64String(parameters.Modulus) +
                                "\nexponent=" + Convert.ToBase64String(parameters.Exponent) + "\n";
            WriteExclusive(privatePath, Encoding.ASCII.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));
            WriteExclusive(publicPath, Encoding.ASCII.GetBytes(publicText));
            string pin = Sha256(Encoding.ASCII.GetBytes(publicText));
            WriteExclusive(
                Path.Combine(root, "RunicSentinel.public-key-pin.txt"),
                Encoding.ASCII.GetBytes(pin + "\n"));
            Console.WriteLine("Created a new RSA-3072 signing key.");
            Console.WriteLine("PRIVATE (keep offline): " + privatePath);
            Console.WriteLine("PUBLIC: " + publicPath);
            Console.WriteLine("TrustedPublicKeySha256 = " + pin);
            return 0;
        }

        private static int SelfTest()
        {
            string temporaryRoot = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "RunicSentinelForge-" + Guid.NewGuid().ToString("N")));
            string expectedRoot = Path.GetFullPath(Path.GetTempPath());
            if (!temporaryRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The self-test staging path is outside the system temporary directory.");
            string keys = Path.Combine(temporaryRoot, "keys");
            string output = Path.Combine(temporaryRoot, "output");
            string rules = Path.Combine(temporaryRoot, "rules.json");
            try
            {
                Directory.CreateDirectory(temporaryRoot);
                File.WriteAllText(
                    rules,
                    "{\"profile\":\"selftest\",\"sequence\":1,\"issued\":1," +
                    "\"expires\":0,\"unknownMods\":\"Forbidden\",\"requiredMods\":[]," +
                    "\"optionalMods\":[],\"grayListMods\":[],\"forbiddenMods\":[]," +
                    "\"modules\":[],\"administrators\":[],\"bannedUsers\":[]}",
                    new UTF8Encoding(false));
                if (Keygen(keys) != 0 ||
                    Compile(rules, Path.Combine(keys, "RunicSentinel.private.pem"), output) != 0)
                    throw new InvalidOperationException("Forge self-test setup failed.");
                string pin = File.ReadAllText(
                    Path.Combine(keys, "RunicSentinel.public-key-pin.txt"),
                    Encoding.ASCII).Trim();
                if (Verify(
                        Path.Combine(output, "RunicSentinel.policy"),
                        Path.Combine(output, "RunicSentinel.policy.sig"),
                        Path.Combine(output, "RunicSentinel.policy.pub"),
                        pin) != 0)
                    throw new InvalidOperationException("Forge self-test verification failed.");
                Console.WriteLine("SELFTEST PASS: standalone key generation, signing, and verification.");
                return 0;
            }
            finally
            {
                try { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }
                catch { }
            }
        }

        private static int Compile(string rulesPath, string privateKeyPath, string outputDirectory)
        {
            byte[] jsonBytes = ReadBounded(rulesPath, MaximumInputBytes);
            using JsonDocument document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            string canonical = BuildPolicy(document.RootElement);
            byte[] payload = new UTF8Encoding(false, true).GetBytes(canonical);
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(Encoding.ASCII.GetString(ReadBounded(privateKeyPath, 64 * 1024)));
            if (rsa.KeySize != 3072) throw new InvalidDataException("The private key is not RSA-3072.");
            byte[] signature = rsa.SignData(
                payload,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            RSAParameters publicParameters = rsa.ExportParameters(false);
            string publicText = "RUNIC-RSA-PUBLIC/1\nmodulus=" +
                                Convert.ToBase64String(publicParameters.Modulus) +
                                "\nexponent=" + Convert.ToBase64String(publicParameters.Exponent) + "\n";
            string root = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(root);
            AtomicWrite(Path.Combine(root, "RunicSentinel.policy"), payload);
            AtomicWrite(
                Path.Combine(root, "RunicSentinel.policy.sig"),
                Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n"));
            AtomicWrite(
                Path.Combine(root, "RunicSentinel.policy.pub"),
                Encoding.ASCII.GetBytes(publicText));
            string pin = Sha256(Encoding.ASCII.GetBytes(publicText));
            Console.WriteLine("Compiled and signed RUNIC-SENTINEL/3 passport.");
            Console.WriteLine("Policy digest: " + Sha256(payload));
            Console.WriteLine("TrustedPublicKeySha256 = " + pin);
            Console.WriteLine("Copy only .policy, .policy.sig, and .policy.pub to the profile/server.");
            return 0;
        }

        private static int Verify(
            string policyPath,
            string signaturePath,
            string publicKeyPath,
            string expectedPin)
        {
            byte[] payload = ReadBounded(policyPath, SentinelPolicy.MaximumBytes);
            byte[] signatureFile = ReadBounded(
                signaturePath,
                SentinelPolicy.MaximumSignatureFileBytes);
            byte[] publicFile = ReadBounded(publicKeyPath, PinnedRsaPublicKey.MaximumFileBytes);
            if (!PinnedRsaPublicKey.TryParse(
                    publicFile,
                    expectedPin,
                    out PinnedRsaPublicKey key,
                    out string failure) ||
                !SentinelPolicy.TryDecodeSignatureFile(signatureFile, out byte[] signature, out failure) ||
                !SentinelPolicy.TryParseAndVerify(payload, signature, key, out SentinelPolicy policy, out failure))
                throw new InvalidDataException("Passport verification failed: " + failure);
            Console.WriteLine("Verified passport " + policy.Profile +
                              " sequence " + policy.Sequence.ToString(CultureInfo.InvariantCulture) + ".");
            Console.WriteLine(policy.Rules.Count + " plugin rules, " + policy.Modules.Count +
                              " module rules, " + policy.Administrators.Count + " administrators, " +
                              policy.BannedUsers.Count + " banned identities.");
            return 0;
        }

        private static string BuildPolicy(JsonElement root)
        {
            RequireObject(root, "rules");
            string profile = Atom(RequiredString(root, "profile"), 64, "profile");
            long sequence = RequiredPositiveInt64(root, "sequence");
            long issued = OptionalNonNegativeInt64(root, "issued", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            long expires = OptionalNonNegativeInt64(root, "expires", 0L);
            if (expires != 0L && expires <= issued)
                throw new InvalidDataException("expires must be zero or greater than issued.");
            string unknown = OptionalString(root, "unknownMods", "Forbidden");
            if (unknown != "Forbidden" && unknown != "Quarantined" && unknown != "Unmanaged")
                throw new InvalidDataException("unknownMods must be Forbidden, Quarantined, or Unmanaged.");

            var plugins = new SortedDictionary<string, PluginRule>(StringComparer.Ordinal);
            AddPlugins(root, "requiredMods", "Required", plugins);
            AddPlugins(root, "optionalMods", "ApprovedOptional", plugins);
            AddPlugins(root, "grayListMods", "Unmanaged", plugins);
            AddPlugins(root, "forbiddenMods", "Forbidden", plugins);

            var builder = new StringBuilder();
            builder.Append("RUNIC-SENTINEL/3\n")
                .Append("profile=").Append(profile).Append('\n')
                .Append("sequence=").Append(sequence.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("issued=").Append(issued.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("expires=").Append(expires.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("unknown=").Append(unknown).Append('\n')
                .Append("unknown-capability=Forbidden\n");
            foreach (PluginRule value in plugins.Values)
                builder.Append("rule=").Append(value.Classification).Append('|')
                    .Append(value.Id).Append('|').Append(value.Version).Append('|')
                    .Append(value.Sha256).Append('\n');

            foreach (ModuleRule value in ReadModules(root).OrderBy(value => value.Id, StringComparer.Ordinal))
                builder.Append("module=").Append(value.Scope).Append('|')
                    .Append(value.Id).Append('|').Append(value.Version).Append('|')
                    .Append(value.Protocol.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(string.Join(",", value.Capabilities)).Append('\n');
            foreach (Identity value in ReadIdentities(root, "administrators"))
                builder.Append("role=").Append(value.Authority).Append('|')
                    .Append(Uri.EscapeDataString(value.Subject)).Append('\n');
            foreach (Identity value in ReadIdentities(root, "bannedUsers"))
                builder.Append("ban=").Append(value.Authority).Append('|')
                    .Append(Uri.EscapeDataString(value.Subject)).Append('\n');
            return builder.ToString();
        }

        private static void AddPlugins(
            JsonElement root,
            string property,
            string classification,
            IDictionary<string, PluginRule> target)
        {
            if (!root.TryGetProperty(property, out JsonElement values)) return;
            if (values.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(property + " must be an array.");
            foreach (JsonElement value in values.EnumerateArray())
            {
                RequireObject(value, property);
                string id = Atom(RequiredString(value, "id"), 128, property + ".id");
                string version = RequiredString(value, "version");
                string hash = RequiredString(value, "sha256");
                if (!SentinelPolicy.CanonicalVersionOrWildcard(version) ||
                    !SentinelPolicy.CanonicalHashOrWildcard(hash))
                    throw new InvalidDataException(property + " contains an invalid version or hash.");
                if (target.ContainsKey(id))
                    throw new InvalidDataException("Plugin appears in more than one list: " + id);
                target.Add(id, new PluginRule(classification, id, version, hash));
            }
        }

        private static IEnumerable<ModuleRule> ReadModules(JsonElement root)
        {
            if (!root.TryGetProperty("modules", out JsonElement values)) yield break;
            if (values.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("modules must be an array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement value in values.EnumerateArray())
            {
                RequireObject(value, "modules");
                string scope = RequiredString(value, "scope");
                if (scope != "Both" && scope != "Client" && scope != "Server")
                    throw new InvalidDataException("Module scope must be Both, Client, or Server.");
                string id = Atom(RequiredString(value, "id"), 128, "module.id");
                if (!seen.Add(id)) throw new InvalidDataException("Duplicate module: " + id);
                string version = RequiredString(value, "version");
                if (!SentinelPolicy.CanonicalVersionOrWildcard(version))
                    throw new InvalidDataException("Invalid module version.");
                int protocol = value.GetProperty("protocol").GetInt32();
                if (protocol <= 0) throw new InvalidDataException("Module protocol must be positive.");
                string[] capabilities = value.TryGetProperty("capabilities", out JsonElement caps)
                    ? caps.EnumerateArray().Select(item => Atom(item.GetString(), 128, "capability"))
                        .OrderBy(item => item, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                if (capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Length)
                    throw new InvalidDataException("Duplicate module capability.");
                yield return new ModuleRule(scope, id, version, protocol, capabilities);
            }
        }

        private static IEnumerable<Identity> ReadIdentities(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out JsonElement values)) yield break;
            if (values.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(property + " must be an array.");
            var result = new List<Identity>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement value in values.EnumerateArray())
            {
                string authority = Atom(RequiredString(value, "authority"), 64, property + ".authority");
                if (authority.Any(character => character >= 'A' && character <= 'Z'))
                    throw new InvalidDataException("Identity authority must be lowercase.");
                string subject = RequiredString(value, "subject");
                if (subject.Length > 256 || subject.Any(char.IsControl))
                    throw new InvalidDataException("Identity subject is invalid.");
                string key = authority + ":" + Uri.EscapeDataString(subject);
                if (!seen.Add(key)) throw new InvalidDataException("Duplicate identity in " + property + ".");
                result.Add(new Identity(authority, subject));
            }
            foreach (Identity value in result.OrderBy(
                         item => item.Authority + ":" + Uri.EscapeDataString(item.Subject),
                         StringComparer.Ordinal))
                yield return value;
        }

        private static byte[] ReadBounded(string path, int maximum)
        {
            var info = new FileInfo(Path.GetFullPath(path));
            if (!info.Exists || info.Length <= 0 || info.Length > maximum)
                throw new InvalidDataException("Input file is missing or exceeds its bound: " + path);
            return File.ReadAllBytes(info.FullName);
        }

        private static void WriteExclusive(string path, byte[] bytes)
        {
            using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string full = Path.GetFullPath(path);
            string temporary = full + ".tmp";
            using (FileStream stream = new FileStream(
                       temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporary, full, true);
        }

        private static string Sha256(byte[] value) =>
            Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        private static void RequireObject(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(name + " must be a JSON object.");
        }
        private static string RequiredString(JsonElement value, string name) =>
            value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(property.GetString())
                ? property.GetString()
                : throw new InvalidDataException("Missing string: " + name);
        private static string OptionalString(JsonElement value, string name, string fallback) =>
            value.TryGetProperty(name, out JsonElement property) ? property.GetString() : fallback;
        private static long RequiredPositiveInt64(JsonElement value, string name)
        {
            long result = value.GetProperty(name).GetInt64();
            return result > 0 ? result : throw new InvalidDataException(name + " must be positive.");
        }
        private static long OptionalNonNegativeInt64(JsonElement value, string name, long fallback)
        {
            long result = value.TryGetProperty(name, out JsonElement property) ? property.GetInt64() : fallback;
            return result >= 0 ? result : throw new InvalidDataException(name + " cannot be negative.");
        }
        private static string Atom(string value, int maximum, string name) =>
            SentinelPolicy.CanonicalAtom(value, 1, maximum)
                ? value
                : throw new InvalidDataException(name + " is not a canonical identifier.");
        private static void Usage()
        {
            Console.WriteLine("RunicSentinel.Forge selftest");
            Console.WriteLine("RunicSentinel.Forge keygen <offline-key-directory>");
            Console.WriteLine("RunicSentinel.Forge compile <rules.json> <private.pem> <output-directory>");
            Console.WriteLine("RunicSentinel.Forge verify <policy> <signature> <public-key> <public-key-pin>");
        }

        private sealed record PluginRule(string Classification, string Id, string Version, string Sha256);
        private sealed record ModuleRule(string Scope, string Id, string Version, int Protocol, string[] Capabilities);
        private sealed record Identity(string Authority, string Subject);
    }
}
