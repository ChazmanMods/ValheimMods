using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicAutomation.Operations
{
    // One authoritative process owns this directory. Records are retained across reconnects/restarts.
    // Do not prune receipts on a timer: a disconnected actor may still retry the original request.
    internal sealed class FileOperationJournal : IOperationJournal, ICompactOperationJournal, IDisposable
    {
        private const int Schema = 2, MaximumFile = 64 * 1024 * 1024;
        private readonly string directory, world;
        private readonly FileStream ownership;
        private readonly Dictionary<string, OperationRecord> records = new Dictionary<string, OperationRecord>();
        public FileOperationJournal(string directory, string world)
        {
            this.directory = Path.GetFullPath(directory); this.world = world;
            Directory.CreateDirectory(this.directory);
            ownership = new FileStream(Path.Combine(this.directory, "authority.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            try
            {
                foreach (string path in Directory.EnumerateFiles(this.directory, "*.operation"))
                {
                    OperationRecord record = Decode(ReadBounded(path));
                    if (record.Request.World != world || Path.GetFileNameWithoutExtension(path) != record.Request.Key)
                        throw new InvalidDataException("Operation journal identity mismatch");
                    records.Add(record.Request.Key, record);
                }
            }
            catch { ownership.Dispose(); throw; }
        }
        public OperationRecord Find(string key) => records.TryGetValue(key, out var record) ? record : null;
        public IEnumerable<OperationRecord> Pending() => records.Values.Where(x => x.Result.Status == OperationStatus.RecoveryPending).ToArray();
        public void ForgetTerminal(string key)
        {
            if (!records.TryGetValue(key, out var record)) return;
            if (record.Result.Status == OperationStatus.RecoveryPending) throw new InvalidOperationException("Cannot discard a recovery record");
            File.Delete(Path.Combine(directory, key + ".operation")); records.Remove(key);
        }
        public void Save(OperationRecord record)
        {
            if (record.Request.World != world) throw new InvalidOperationException("Wrong world");
            if (records.TryGetValue(record.Request.Key, out var old))
            {
                if (old.Request.Fingerprint != record.Request.Fingerprint) throw new InvalidOperationException("Reused request id");
                if (old.Result.Status != OperationStatus.RecoveryPending)
                {
                    if (!OwnerOperationQueue.Same(Encode(old), Encode(record))) throw new InvalidOperationException("Terminal receipt is immutable");
                    return;
                }
            }
            string destination = Path.Combine(directory, record.Request.Key + ".operation");
            DurableReplace(destination, Encode(record));
            records[record.Request.Key] = record;
        }
        internal static void DurableReplace(string destination, byte[] data)
        {
            // Publish a complete file atomically after flushing the managed stream buffer.
            // This deliberately uses normal OS write caching, not per-operation fsync:
            // reconnect/process restart can read completed writes, while an OS crash or
            // power loss can lose or reorder recent journal and endpoint updates.
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.None))
                    stream.Write(data, 0, data.Length);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static byte[] ReadBounded(string path)
        {
            if (new FileInfo(path).Length > MaximumFile) throw new InvalidDataException("Journal record too large");
            return File.ReadAllBytes(path);
        }
        private static byte[] Encode(OperationRecord record)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
                {
                    writer.Write(Schema);
                    writer.Write(record.Plan.Native);
                    writer.Write(record.Request.World); writer.Write(record.Request.Actor);
                    writer.Write(record.Request.Id); writer.Write(record.Request.Kind); Write(writer, record.Request.Payload);
                    writer.Write((int)record.Result.Status); writer.Write(record.Result.Reason); Write(writer, record.Result.Payload);
                    Write(writer, record.Plan.Response); writer.Write(record.Plan.Changes.Count);
                    foreach (var change in record.Plan.Changes)
                    { writer.Write(change.Endpoint); Write(writer, change.Before); Write(writer, change.After); }
                }
                if (output.Length > MaximumFile - 32) throw new InvalidDataException("Journal record too large");
                using (var sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(output.ToArray());
                    output.Write(digest, 0, digest.Length);
                }
                return output.ToArray();
            }
        }
        private static OperationRecord Decode(byte[] data)
        {
            if (data.Length < 36) throw new InvalidDataException("Truncated journal");
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(data, 0, data.Length - 32).SequenceEqual(data.Skip(data.Length - 32)))
                    throw new InvalidDataException("Journal checksum mismatch");
            using (var input = new MemoryStream(data, 0, data.Length - 32, false))
            using (var reader = new BinaryReader(input, Encoding.UTF8))
            {
                int schema = reader.ReadInt32();
                if (schema < 1 || schema > Schema) throw new InvalidDataException("Unsupported journal schema");
                bool native = schema >= 2 && reader.ReadBoolean();
                var request = new OperationRequest(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadString(), Read(reader, 65536));
                var status = (OperationStatus)reader.ReadInt32();
                if (status != OperationStatus.Committed && status != OperationStatus.Denied && status != OperationStatus.RecoveryPending)
                    throw new InvalidDataException("Invalid receipt status");
                string reason = reader.ReadString();
                if (reason.Length > 4096) throw new InvalidDataException("Invalid receipt reason");
                var result = new OperationResult(status, reason, Read(reader, 65536));
                byte[] response = Read(reader, 65536);
                int count = reader.ReadInt32();
                if (count < 0 || count > 128) throw new InvalidDataException("Invalid endpoint count");
                var changes = new List<EndpointChange>();
                for (int n = 0; n < count; n++)
                    changes.Add(new EndpointChange(reader.ReadString(), Read(reader, 4 * 1024 * 1024), Read(reader, 4 * 1024 * 1024)));
                if (input.Position != input.Length) throw new InvalidDataException("Trailing journal data");
                return new OperationRecord(request, new OperationPlan(changes, response, native), result);
            }
        }
        private static void Write(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
        private static byte[] Read(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum) throw new InvalidDataException("Invalid payload length");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
        public void Dispose() => ownership.Dispose();
    }
}
