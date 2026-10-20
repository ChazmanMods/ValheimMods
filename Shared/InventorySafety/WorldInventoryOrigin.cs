using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // A world file may predate the first ledger enrollment. Derive an identity from its
    // exact persisted record while loading, before any future world-save clone is made.
    // A crash before that first save derives the same identity on the next load.
    internal static class WorldInventoryOrigin
    {
        internal const string Key = "runic.inventory.world-origin.v2";
        private static readonly System.Reflection.FieldInfo Reader = AccessTools.Field(typeof(ZPackage), "m_reader");
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.origins.v2", () => new Origins().Routes());
        internal static string Ensure(ZDO zdo)
        {
            string origin = zdo.GetString(Key, "");
            if (origin.Length == 0) { origin = Guid.NewGuid().ToString("N"); zdo.Set(Key, origin); }
            return origin;
        }
        internal static void Register(string prefix, Func<byte[], Tuple<string, int, Vector3>> identity, Action<ZDO, string, byte[]> restore, Action<string, byte[]> recoverMissing = null) =>
            ((Action<string, Func<byte[], Tuple<string, int, Vector3>>, Action<ZDO, string, byte[]>, Action<string, byte[]>>)Routes["register"])(prefix, identity, restore, recoverMissing);
        internal static void Tick() => ((Action)Routes["tick"])();
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static long ReadPosition(ZPackage package) => ((BinaryReader)Reader.GetValue(package)).BaseStream.Position;
        internal static void Loaded(ZDO zdo, ZPackage package, long start)
        {
            if (!InventoryOperationNetwork.Installed || ZNet.instance == null || !ZNet.instance.IsServer() || zdo.GetString(Key, "").Length != 0) return;
            var prefab = ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
            if (prefab == null || (prefab.GetComponent<Container>() == null && prefab.GetComponent<CraftingStation>() == null &&
                prefab.GetComponent<Smelter>() == null && prefab.GetComponent<CookingStation>() == null && prefab.GetComponent<Fermenter>() == null &&
                prefab.GetComponent<Fireplace>() == null && prefab.GetComponent<Beehive>() == null && prefab.GetComponent<ItemDrop>() == null)) return;
            long end = ReadPosition(package);
            var stream = (Reader.GetValue(package) as BinaryReader)?.BaseStream;
            if (stream == null || start < 0 || end <= start || end > stream.Length)
                throw new InvalidDataException("Cannot identify the persisted inventory world record");
            // Native loaded packages can wrap a non-publicly-visible MemoryStream.
            // Read only this record and restore the parser position, without requiring
            // GetBuffer/TryGetBuffer or copying the entire world chunk.
            byte[] record = new byte[checked((int)(end - start))];
            long position = stream.Position;
            try
            {
                stream.Position = start;
                int offset = 0;
                while (offset < record.Length)
                {
                    int count = stream.Read(record, offset, record.Length - offset);
                    if (count == 0) throw new EndOfStreamException("Truncated persisted inventory record");
                    offset += count;
                }
            }
            finally { stream.Position = position; }
            using var sha = SHA256.Create();
            // Chunked saves can encode position relative to the chunk. Include the
            // decoded absolute position so identical records in different chunks differ.
            var identity = new ZPackage(); identity.Write(zdo.GetPosition()); identity.Write(record);
            string origin = BitConverter.ToString(sha.ComputeHash(identity.GetArray())).Replace("-", "").ToLowerInvariant();
            zdo.Set(Key, origin);
        }
        private sealed class Origins
        {
            private readonly Dictionary<string, Tuple<Func<byte[], Tuple<string, int, Vector3>>, Action<ZDO, string, byte[]>, Action<string, byte[]>>> handlers = new();
            private readonly HashSet<string> restored = new();
            internal Dictionary<string, Delegate> Routes() => new()
            {
                ["register"] = new Action<string, Func<byte[], Tuple<string, int, Vector3>>, Action<ZDO, string, byte[]>, Action<string, byte[]>>((prefix, identity, restore, missing) => handlers[prefix] = Tuple.Create(identity, restore, missing)),
                ["clear"] = new Action(() => restored.Clear()),
                ["tick"] = new Action(() => {
                    if (!InventoryOperationNetwork.Ready || !SharedOperationHost.Active || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                    foreach (var handler in handlers.Where(h => !restored.Contains(h.Key)).ToArray())
                    {
                        var records = SharedOperationHost.Enumerate(handler.Key).Select(r => Tuple.Create(r, handler.Value.Item1(FileOperationEndpoints.Inventory(r.Item2)))).ToArray();
                        var counts = records.GroupBy(r => r.Item2.Item1).ToDictionary(g => g.Key, g => g.Count());
                        foreach (var entry in records)
                        {
                            var identity = entry.Item2;
                            if (string.IsNullOrEmpty(identity.Item1)) continue;
                            // Multiple durable records with the same origin are ambiguous too.
                            if (counts[identity.Item1] != 1) continue;
                            var matches = NetworkInventoryAccess.Nearby(identity.Item3, 1f)
                                .Where(z => z.GetPrefab() == identity.Item2 && z.GetString(Key, "") == identity.Item1).Take(2).ToArray();
                            if (matches.Length == 1) handler.Value.Item2(matches[0], entry.Item1.Item1, entry.Item1.Item2);
                            // A newly built object can be enrolled before its first world
                            // save. Recover its durable contents through normal retirement;
                            // never recreate a building and refund construction materials.
                            else if (matches.Length == 0 && Guid.TryParseExact(identity.Item1, "N", out _))
                                handler.Value.Item3?.Invoke(entry.Item1.Item1, entry.Item1.Item2);
                        }
                        restored.Add(handler.Key);
                    }
                })
            };
        }
    }
    [HarmonyPatch(typeof(ZDO), "Load")]
    internal static class CapturePersistedInventoryOrigin
    {
        private static void Prefix(ZPackage pkg, out long __state) => __state = WorldInventoryOrigin.ReadPosition(pkg);
        private static void Postfix(ZDO __instance, ZPackage pkg, long __state) => WorldInventoryOrigin.Loaded(__instance, pkg, __state);
    }
}
