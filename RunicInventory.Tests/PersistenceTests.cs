using System;
using System.Collections.Generic;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class PersistenceTests
    {
        internal static void Register()
        {
            TestRunner.Run("lock persistence round-trips deterministically", RoundTrips);
            TestRunner.Run("owning-client profile package round-trips the exact lock payload", OwningClientPackageRoundTrips);
            TestRunner.Run("lock persistence sorts facts through a fixed bitset", InputOrderDoesNotMatter);
            TestRunner.Run("lock persistence detects single-byte corruption", CorruptionRejected);
            TestRunner.Run("lock persistence rejects malformed schema and hex", MalformedRejected);
            TestRunner.Run("lock persistence rejects duplicate and out-of-bounds coordinates", InvalidLocksRejected);
            TestRunner.Run("all 128 native locks remain under the persistence cap", FullTopologyIsBounded);
            TestRunner.Run("million-character persistence input fails before split", OversizeFailsEarly);
            TestRunner.Run("persisted topology dimensions cannot silently migrate", DimensionsRemainExact);
            TestRunner.Run("persistence contains no item blobs or item metadata", NoItemPayload);
        }

        private static void RoundTrips()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var locks = new[] { new InventorySlotCoordinate(7, 3), new InventorySlotCoordinate(0, 0), new InventorySlotCoordinate(2, 1) };
            TestAssert.True(TopologyPersistenceCodec.TryEncode(layout, locks, out string payload, out string code));
            TestAssert.Equal("ok", code);
            TestAssert.True(TopologyPersistenceCodec.TryDecode(payload, out PersistedTopologyState state, out code));
            TestAssert.Equal("ok", code);
            TestAssert.True(state.IsLocked(7, 3));
            TestAssert.True(state.IsLocked(0, 0));
            TestAssert.True(state.IsLocked(2, 1));
            TestAssert.False(state.IsLocked(1, 1));
            TestAssert.True(TopologyPersistenceCodec.TryEncode(layout, state.LockedSlots(), out string second, out _));
            TestAssert.Equal(payload, second);
        }

        private static void OwningClientPackageRoundTrips()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var locks = new[] { new InventorySlotCoordinate(0, 3), new InventorySlotCoordinate(7, 3) };
            TestAssert.True(TopologyPersistenceCodec.TryEncode(layout, locks, out string payload, out _));

            var savedProfile = new ZPackage();
            savedProfile.Write(TopologyPersistenceCodec.MetadataKey);
            savedProfile.Write(payload);
            var loadedProfile = new ZPackage(savedProfile.GetArray());
            TestAssert.Equal(TopologyPersistenceCodec.MetadataKey, loadedProfile.ReadString());
            string restored = loadedProfile.ReadString();

            TestAssert.Equal(payload, restored);
            TestAssert.True(TopologyPersistenceCodec.TryDecode(restored, out PersistedTopologyState state, out string code));
            TestAssert.Equal("ok", code);
            TestAssert.True(state.IsLocked(0, 3));
            TestAssert.True(state.IsLocked(7, 3));
        }

        private static void InputOrderDoesNotMatter()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var left = new[] { new InventorySlotCoordinate(5, 3), new InventorySlotCoordinate(1, 1) };
            var right = new[] { left[1], left[0] };
            TopologyPersistenceCodec.TryEncode(layout, left, out string a, out _);
            TopologyPersistenceCodec.TryEncode(layout, right, out string b, out _);
            TestAssert.Equal(a, b);
        }

        private static void CorruptionRejected()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            TopologyPersistenceCodec.TryEncode(layout, Array.Empty<InventorySlotCoordinate>(), out string payload, out _);
            int index = payload.IndexOf('|') + 1;
            char replacement = payload[index] == '8' ? '7' : '8';
            string corrupt = payload.Substring(0, index) + replacement + payload.Substring(index + 1);
            TestAssert.False(TopologyPersistenceCodec.TryDecode(corrupt, out _, out string code));
            TestAssert.True(code == "persistence.dimensions-invalid" || code == "persistence.digest-invalid");
        }

        private static void MalformedRejected()
        {
            foreach (string payload in new[] { "", "2|8|4|00000000|bad", "1|8|4|GGGGGGGG|" + new string('0', 64), "1|8|4" })
                TestAssert.False(TopologyPersistenceCodec.TryDecode(payload, out _, out _));
        }

        private static void InvalidLocksRejected()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var duplicate = new[] { new InventorySlotCoordinate(1, 1), new InventorySlotCoordinate(1, 1) };
            TestAssert.False(TopologyPersistenceCodec.TryEncode(layout, duplicate, out _, out string duplicateCode));
            TestAssert.Equal("persistence.lock-duplicate", duplicateCode);
            var outOfBounds = new[] { new InventorySlotCoordinate(8, 1) };
            TestAssert.False(TopologyPersistenceCodec.TryEncode(layout, outOfBounds, out _, out string boundCode));
            TestAssert.Equal("persistence.lock-out-of-bounds", boundCode);
        }

        private static void FullTopologyIsBounded()
        {
            TopologyLayout.TryCreate(8, 16, out TopologyLayout layout, out _);
            var locks = new List<InventorySlotCoordinate>();
            for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++) locks.Add(new InventorySlotCoordinate(x, y));
            TestAssert.True(TopologyPersistenceCodec.TryEncode(layout, locks, out string payload, out _));
            TestAssert.True(payload.Length <= TopologyPersistenceCodec.MaximumPayloadCharacters);
            TestAssert.True(TopologyPersistenceCodec.TryDecode(payload, out PersistedTopologyState state, out _));
            TestAssert.Equal(128, state.LockedSlots().Count);
        }

        private static void OversizeFailsEarly()
        {
            TestAssert.False(TopologyPersistenceCodec.TryDecode(new string('x', 1000000), out _, out string code));
            TestAssert.Equal("persistence.payload-too-large", code);
        }

        private static void DimensionsRemainExact()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout four, out _);
            TopologyPersistenceCodec.TryEncode(four, Array.Empty<InventorySlotCoordinate>(), out string payload, out _);
            TopologyPersistenceCodec.TryDecode(payload, out PersistedTopologyState state, out _);
            TestAssert.Equal(4, state.Height);
            TopologyLayout.TryCreate(8, 5, out TopologyLayout five, out _);
            TestAssert.NotEqual(state.Height, five.Height);
        }

        private static void NoItemPayload()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            TopologyPersistenceCodec.TryEncode(layout, new[] { new InventorySlotCoordinate(0, 0) }, out string payload, out _);
            TestAssert.False(payload.Contains("m_customData", StringComparison.Ordinal));
            TestAssert.False(payload.Contains("ItemData", StringComparison.Ordinal));
            TestAssert.False(payload.Contains("prefab", StringComparison.OrdinalIgnoreCase));
        }
    }
}
