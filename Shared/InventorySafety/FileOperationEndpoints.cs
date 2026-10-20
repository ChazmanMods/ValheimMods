using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicAutomation.Operations
{
    // Durable Runic metadata lives here. Active chest contents come from LiveChestWorld;
    // these files must never replace a live native inventory.
    internal sealed class FileOperationEndpoints : IOperationEndpoints
    {
        private readonly string directory, world;
        private readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        internal Action<string, byte[]> Changed = (_, __) => { };
        internal Func<string, bool> NativeEndpoint = _ => false;
        internal Func<string, byte[]> NativeRead = _ => null;
        public FileOperationEndpoints(string directory, string world)
        {
            this.directory = Path.GetFullPath(directory);
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            Directory.CreateDirectory(this.directory);
        }
        public byte[] Import(string endpoint, byte[] inventory)
        {
            byte[] existing = Read(endpoint);
            if (existing != null) return existing;
            byte[] image = Image(world, endpoint, 0, inventory);
            FileOperationJournal.DurableReplace(PathFor(endpoint), image);
            cache[endpoint] = image; return (byte[])image.Clone();
        }
        public byte[] Read(string endpoint)
        {
            if (NativeEndpoint(endpoint)) return NativeRead(endpoint);
            return ReadStored(endpoint);
        }
        internal byte[] ReadStored(string endpoint)
        {
            if (cache.TryGetValue(endpoint, out byte[] existing)) return (byte[])existing.Clone();
            string path = PathFor(endpoint);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Endpoint too large");
            byte[] image = File.ReadAllBytes(path);
            Parse(image, out string imageWorld, out string imageEndpoint, out _, out _);
            if (world != imageWorld || endpoint != imageEndpoint) throw new InvalidDataException("Endpoint identity mismatch");
            cache[endpoint] = image; return (byte[])image.Clone();
        }
        public bool CompareExchange(string endpoint, byte[] expected, byte[] replacement)
        {
            if (!OwnerOperationQueue.Same(Read(endpoint), expected)) return false;
            Parse(expected, out string oldWorld, out string oldEndpoint, out long oldRevision, out _);
            Parse(replacement, out string newWorld, out string newEndpoint, out long newRevision, out _);
            if (oldWorld != world || newWorld != world || oldEndpoint != endpoint || newEndpoint != endpoint ||
                oldRevision == long.MaxValue || newRevision != oldRevision + 1)
                throw new InvalidDataException("Invalid endpoint revision or identity");
            FileOperationJournal.DurableReplace(PathFor(endpoint), replacement);
            cache[endpoint] = (byte[])replacement.Clone();
            Changed(endpoint, (byte[])replacement.Clone());
            return true;
        }
        internal Tuple<string, byte[]>[] Enumerate(string prefix)
        {
            var found = new List<Tuple<string, byte[]>>();
            foreach (string path in Directory.EnumerateFiles(directory, "*.inventory"))
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Endpoint too large");
                byte[] image = File.ReadAllBytes(path);
                Parse(image, out string imageWorld, out string endpoint, out _, out _);
                if (imageWorld != world || !string.Equals(path, PathFor(endpoint), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Endpoint identity mismatch");
                if (!endpoint.StartsWith(prefix, StringComparison.Ordinal)) continue;
                cache[endpoint] = image; found.Add(Tuple.Create(endpoint, (byte[])image.Clone()));
            }
            return found.ToArray();
        }
        public static byte[] Next(byte[] image, byte[] inventory)
        {
            Parse(image, out string world, out string endpoint, out long revision, out _);
            return Image(world, endpoint, checked(revision + 1), inventory);
        }
        internal static byte[] Transient(string world, string endpoint, long revision, byte[] inventory) =>
            Image(world, endpoint, revision, inventory);
        public static byte[] Inventory(byte[] image)
        { Parse(image, out _, out _, out _, out byte[] inventory); return inventory; }
        public static long Revision(byte[] image)
        { Parse(image, out _, out _, out long revision, out _); return revision; }
        private string PathFor(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 256) throw new ArgumentException("Invalid endpoint");
            return Path.Combine(directory, OperationRequest.Hash(world + "\n" + endpoint) + ".inventory");
        }
        private static byte[] Image(string world, string endpoint, long revision, byte[] inventory)
        {
            if (inventory == null || inventory.Length > 4 * 1024 * 1024 - 4096) throw new ArgumentException("Invalid inventory size");
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                {
                    writer.Write(1); writer.Write(world); writer.Write(endpoint); writer.Write(revision);
                    writer.Write(inventory.Length); writer.Write(inventory);
                }
                using (var sha = SHA256.Create())
                { byte[] hash = sha.ComputeHash(buffer.ToArray()); buffer.Write(hash, 0, hash.Length); }
                return buffer.ToArray();
            }
        }
        private static void Parse(byte[] image, out string world, out string endpoint, out long revision, out byte[] inventory)
        {
            if (image == null || image.Length < 48 || image.Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid endpoint image");
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(image, 0, image.Length - 32).SequenceEqual(image.Skip(image.Length - 32)))
                    throw new InvalidDataException("Endpoint checksum mismatch");
            using (var buffer = new MemoryStream(image, 0, image.Length - 32, false))
            using (var reader = new BinaryReader(buffer, Encoding.UTF8))
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported endpoint schema");
                world = reader.ReadString(); endpoint = reader.ReadString(); revision = reader.ReadInt64();
                if (world.Length > 256 || endpoint.Length > 256 || revision < 0) throw new InvalidDataException("Invalid endpoint identity");
                int count = reader.ReadInt32();
                if (count < 0 || count != buffer.Length - buffer.Position) throw new InvalidDataException("Invalid endpoint payload");
                inventory = reader.ReadBytes(count);
            }
        }
    }
}
