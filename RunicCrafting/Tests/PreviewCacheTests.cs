using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using RunicCrafting.Domain;
using RunicCrafting.Integration;

namespace RunicCrafting.Tests
{
    internal static class PreviewCacheTests
    {
        private static bool Get(PreviewSnapshotCache<int, object> cache, int key, object identity,
            byte[] bytes, out object value, int width = 8, int height = 4, string name = "Chest", int level = 0) =>
            cache.TryGet(key, identity, bytes, width, height, name, level, out value);

        private static void Put(PreviewSnapshotCache<int, object> cache, int key, object identity,
            byte[] bytes, object value) => cache.Store(key, identity, bytes, 8, 4, "Chest", 0, value);

        internal static void UnchangedPayloadIsReusedAcrossThousandsOfQueries()
        {
            var cache = new PreviewSnapshotCache<int, object>();
            var identities = Enumerable.Range(0, 19).Select(_ => new object()).ToArray();
            var bytes = new byte[] { 1, 2, 3, 4 };
            int builds = 0, hits = 0;
            for (int query = 0; query < 4000; query++)
                for (int chest = 0; chest < identities.Length; chest++)
                {
                    if (Get(cache, chest, identities[chest], (byte[])bytes.Clone(), out _)) hits++;
                    else { Put(cache, chest, identities[chest], bytes, new object()); builds++; }
                }
            TestAssert.Equal(19, builds);
            TestAssert.Equal(75981, hits);
            TestAssert.Equal(19, cache.Count);
        }

        internal static void PayloadChangesInvalidateOnlyAffectedChest()
        {
            var cache = new PreviewSnapshotCache<int, object>();
            var identity = new object();
            byte[] bytes = { 1, 2, 3 };
            var value = new object();
            Put(cache, 1, identity, bytes, value);
            Put(cache, 2, identity, bytes, value);
            bytes[1] = 4; // Same array object, changed bytes: must miss.
            TestAssert.False(Get(cache, 1, identity, bytes, out _));
            TestAssert.True(Get(cache, 2, identity, new byte[] { 1, 2, 3 }, out object found));
            TestAssert.True(ReferenceEquals(value, found));
            Put(cache, 1, identity, bytes, new object());
            TestAssert.False(Get(cache, 1, identity, new byte[] { 1, 2, 3 }, out _));
        }

        internal static void IdentityDimensionsAndWorldLevelInvalidate()
        {
            var cache = new PreviewSnapshotCache<int, object>();
            var identity = new object();
            var bytes = new byte[] { 1 };
            Action store = () => Put(cache, 1, identity, bytes, new object());
            store(); TestAssert.False(Get(cache, 1, new object(), bytes, out _));
            store(); TestAssert.False(Get(cache, 1, identity, bytes, out _, width: 9));
            store(); TestAssert.False(Get(cache, 1, identity, bytes, out _, height: 5));
            store(); TestAssert.False(Get(cache, 1, identity, bytes, out _, name: "Other"));
            store(); TestAssert.False(Get(cache, 1, identity, bytes, out _, level: 1));
        }

        internal static void SessionPlayerAndDatabaseChangesClearEvidence()
        {
            var cache = new PreviewSnapshotCache<int, object>();
            object network = new object(), player = new object(), database = new object();
            var identity = new object();
            var bytes = new byte[] { 1 };
            cache.SetContext(network, player, database);
            Put(cache, 1, identity, bytes, new object());
            cache.SetContext(network, player, database);
            TestAssert.Equal(1, cache.Count);
            network = new object(); cache.SetContext(network, player, database);
            TestAssert.Equal(0, cache.Count);
            Put(cache, 1, identity, bytes, new object());
            player = new object(); cache.SetContext(network, player, database);
            TestAssert.Equal(0, cache.Count);
            Put(cache, 1, identity, bytes, new object());
            database = new object(); cache.SetContext(network, player, database);
            TestAssert.Equal(0, cache.Count);
            Put(cache, 1, identity, bytes, new object());
            cache.SetContext(null, null, null);
            TestAssert.Equal(0, cache.PayloadBytes);
        }

        internal static void CacheHasLruEntryAndByteBounds()
        {
            var cache = new PreviewSnapshotCache<int, object>(2, 8);
            var identity = new object();
            var bytes = new byte[] { 1, 2, 3 };
            Put(cache, 1, identity, bytes, new object());
            Put(cache, 2, identity, bytes, new object());
            TestAssert.True(Get(cache, 1, identity, bytes, out _));
            Put(cache, 3, identity, bytes, new object());
            TestAssert.False(Get(cache, 2, identity, bytes, out _));
            TestAssert.True(Get(cache, 1, identity, bytes, out _));
            Put(cache, 4, identity, new byte[7], new object());
            TestAssert.Equal(1, cache.Count);
            TestAssert.Equal(7, cache.PayloadBytes);
            Put(cache, 5, identity, new byte[9], new object());
            TestAssert.Equal(1, cache.Count);
            cache.Clear();
            TestAssert.Equal(0, cache.PayloadBytes);
            var large = new PreviewSnapshotCache<int, object>();
            Put(large, 1, identity, new byte[262145], new object());
            TestAssert.Equal(0, large.Count);
        }

        internal static void EmptyAndFailedSnapshotsCannotReuseOldValues()
        {
            var cache = new PreviewSnapshotCache<int, object>();
            var identity = new object();
            Put(cache, 1, identity, null, new object());
            TestAssert.True(Get(cache, 1, identity, Array.Empty<byte>(), out _));
            TestAssert.False(Get(cache, 1, identity, new byte[] { 1 }, out _));
            // A rejected decode is not stored. The previous empty result is already gone.
            TestAssert.False(Get(cache, 1, identity, null, out _));
            Put(cache, 1, identity, new byte[] { 1 }, null);
            TestAssert.Equal(0, cache.Count);
        }

        internal static void MaterialCountsAreImmutableFilteredAndSaturating()
        {
            var inventory = new Inventory("Chest", null, 8, 4);
            inventory.GetAllItems().AddRange(new[]
            {
                new ItemDrop.ItemData { Prefab = 1, m_stack = 7, m_worldLevel = 1 },
                new ItemDrop.ItemData { Prefab = 1, m_stack = 5, m_worldLevel = 2 },
                new ItemDrop.ItemData { Prefab = 1, m_stack = 100, m_worldLevel = 0 },
                new ItemDrop.ItemData { Prefab = 2, m_stack = int.MaxValue, m_worldLevel = 1 },
                new ItemDrop.ItemData { Prefab = 2, m_stack = 1, m_worldLevel = 1 },
                new ItemDrop.ItemData { Prefab = 3, m_stack = -3, m_worldLevel = 1 },
                null
            });
            var counts = new PreviewMaterialCounts(inventory, 1);
            TestAssert.Equal(12, counts.Count("1"));
            TestAssert.Equal(int.MaxValue, counts.Count("2"));
            TestAssert.Equal(0, counts.Count("3"));
            TestAssert.Equal(0, counts.Count("missing"));
            TestAssert.Equal(0, counts.Count(null));
            inventory.GetAllItems().Clear();
            TestAssert.Equal(12, counts.Count("1"));
        }

        internal static void PreviewCacheIsOutsideWritableAndPermissionPaths()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string query = File.ReadAllText(Path.Combine(root, "Integration", "ContainerQueryRuntime.cs"));
            int readBranch = query.IndexOf("if (!requireWritable)", query.IndexOf("if (!IsEligibleContainer", StringComparison.Ordinal), StringComparison.Ordinal);
            TestAssert.True(query.IndexOf("if (!IsEligibleContainer", StringComparison.Ordinal) < readBranch);
            string previewBranch = query.Substring(readBranch, query.IndexOf("result.Add(new ValheimMaterialSource(", readBranch, StringComparison.Ordinal) - readBranch);
            TestAssert.True(previewBranch.Contains("preview.Count(resource)", StringComparison.Ordinal));
            TestAssert.True(previewBranch.Contains("ReadOnlyMaterialSource", StringComparison.Ordinal));
            TestAssert.False(previewBranch.Contains("ClaimOwnership", StringComparison.Ordinal));
            string bridge = File.ReadAllText(Path.Combine(root, "Integration", "ValheimReflection.cs"));
            string reader = bridge.Substring(bridge.IndexOf("internal static bool TryReadContainerInventory", StringComparison.Ordinal));
            reader = reader.Substring(0, reader.IndexOf("internal static bool RefreshOwnedContainer", StringComparison.Ordinal));
            TestAssert.True(reader.IndexOf("PreviewCache.TryGet", StringComparison.Ordinal) < reader.IndexOf("preview.Load", StringComparison.Ordinal));
            TestAssert.True(reader.IndexOf("MatchesLoaded", StringComparison.Ordinal) < reader.IndexOf("PreviewCache.Store", StringComparison.Ordinal));
            TestAssert.True(reader.IndexOf("SamePayload(before, after)", StringComparison.Ordinal) < reader.IndexOf("PreviewCache.Store", StringComparison.Ordinal));
            TestAssert.False(reader.Contains("ClaimOwnership", StringComparison.Ordinal));
            string writer = bridge.Substring(bridge.IndexOf("internal static bool RefreshOwnedContainer", StringComparison.Ordinal));
            TestAssert.False(writer.Contains("PreviewCache", StringComparison.Ordinal));
            TestAssert.True(File.ReadAllText(Path.Combine(root, "Plugin.cs")).Contains("ValheimReflection.MaintainPreviewCacheContext();", StringComparison.Ordinal));
        }

        internal static void InstalledLoaderConfirmsReportedAllocationPath()
        {
            string install = Environment.GetEnvironmentVariable("VALHEIM_INSTALL");
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(install, "valheim_Data", "Managed", "assembly_valheim.dll"));
            TypeDefinition inventory = assembly.MainModule.Types.Single(t => t.Name == "Inventory");
            var add = inventory.Methods.Single(m => m.Name == "AddItem" && m.Parameters.Count == 14 && m.Parameters[0].ParameterType.FullName == "System.Int32");
            var calls = add.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
            TestAssert.True(calls.Any(m => m.Name == "Instantiate" && m.DeclaringType.FullName == "UnityEngine.Object"));
            TestAssert.True(calls.Any(m => m.Name == "Destroy" && m.DeclaringType.FullName == "UnityEngine.Object"));
            TestAssert.True(calls.Any(m => m.Name == "AddTempItem"));
            // Keep the full-fidelity loader: the native temporary path omits cheated metadata.
            var temporary = inventory.Methods.Single(m => m.Name == "AddTempItem");
            TestAssert.False(temporary.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Any(f => f.Name == "m_cheated"));
        }
    }
}
