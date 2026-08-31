using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicInventory.Api;

namespace RunicInventory.Integration
{
    internal sealed class ItemMutationEvidence
    {
        internal ItemMutationEvidence(ItemDrop.ItemData item, Vector2i coordinate, string fingerprint)
        {
            Item = item;
            Coordinate = coordinate;
            Fingerprint = fingerprint;
        }

        internal ItemDrop.ItemData Item { get; }
        internal Vector2i Coordinate { get; }
        internal string Fingerprint { get; }
    }

    internal static class InventoryEvidence
    {
        internal const int MaximumCustomEntriesPerItem = 64;
        internal const int MaximumCustomStringCharacters = 4096;
        internal const int MaximumAggregateCustomCharacters = 65536;
        internal const int MaximumSerializedBytes = 1048576;

        internal static bool TryCaptureMutation(
            Inventory inventory,
            out IReadOnlyList<ItemMutationEvidence> evidence,
            out string reasonCode)
        {
            evidence = Array.Empty<ItemMutationEvidence>();
            if (inventory == null)
            {
                reasonCode = "evidence.inventory-null";
                return false;
            }
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            if (items == null || items.Count > InventoryTopologySnapshot.MaximumNativeSlots)
            {
                reasonCode = "evidence.item-bound-exceeded";
                return false;
            }
            var coordinates = new HashSet<int>();
            var result = new List<ItemMutationEvidence>(items.Count);
            int aggregateCustom = 0;
            foreach (ItemDrop.ItemData item in items)
            {
                if (item == null || item.m_gridPos.x < 0 || item.m_gridPos.x >= inventory.GetWidth() ||
                    item.m_gridPos.y < 0 || item.m_gridPos.y >= inventory.GetHeight() ||
                    !coordinates.Add(item.m_gridPos.y * inventory.GetWidth() + item.m_gridPos.x) ||
                    !TryFingerprint(item, includePosition: false, ref aggregateCustom, out string fingerprint))
                {
                    reasonCode = "evidence.item-invalid";
                    return false;
                }
                result.Add(new ItemMutationEvidence(item, item.m_gridPos, fingerprint));
            }
            evidence = result.AsReadOnly();
            reasonCode = "ok";
            return true;
        }

        internal static bool VerifyUnchangedExceptPosition(
            Inventory inventory,
            IReadOnlyList<ItemMutationEvidence> before,
            out string reasonCode)
        {
            if (inventory == null || before == null || inventory.GetAllItems().Count != before.Count)
            {
                reasonCode = "evidence.item-count-changed";
                return false;
            }
            var live = new HashSet<ItemDrop.ItemData>(ReferenceComparer<ItemDrop.ItemData>.Instance);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) live.Add(item);
            var coordinates = new HashSet<int>();
            int aggregateCustom = 0;
            foreach (ItemMutationEvidence record in before)
            {
                ItemDrop.ItemData item = record.Item;
                if (!live.Contains(item) || item.m_gridPos.x < 0 || item.m_gridPos.x >= inventory.GetWidth() ||
                    item.m_gridPos.y < 0 || item.m_gridPos.y >= inventory.GetHeight() ||
                    !coordinates.Add(item.m_gridPos.y * inventory.GetWidth() + item.m_gridPos.x) ||
                    !TryFingerprint(item, false, ref aggregateCustom, out string fingerprint) ||
                    !string.Equals(fingerprint, record.Fingerprint, StringComparison.Ordinal))
                {
                    reasonCode = "evidence.metadata-or-membership-changed";
                    return false;
                }
            }
            reasonCode = "ok";
            return true;
        }

        internal static void RestorePositions(IReadOnlyList<ItemMutationEvidence> evidence)
        {
            if (evidence == null) return;
            foreach (ItemMutationEvidence record in evidence)
                if (record?.Item != null) record.Item.m_gridPos = record.Coordinate;
        }

        internal static bool TryFingerprint(ItemDrop.ItemData item, bool includePosition, out string fingerprint)
        {
            int aggregate = 0;
            return TryFingerprint(item, includePosition, ref aggregate, out fingerprint);
        }

        internal static bool TryDeterministicSave(Inventory inventory, out string reasonCode)
        {
            try
            {
                var first = new ZPackage();
                inventory.Save(first);
                byte[] left = first.GetArray();
                if (left == null || left.Length > MaximumSerializedBytes)
                {
                    reasonCode = "serialization.payload-bound";
                    return false;
                }
                var second = new ZPackage();
                inventory.Save(second);
                byte[] right = second.GetArray();
                if (right == null || right.Length != left.Length)
                {
                    reasonCode = "serialization.nondeterministic";
                    return false;
                }
                int difference = 0;
                for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
                reasonCode = difference == 0 ? "ok" : "serialization.nondeterministic";
                return difference == 0;
            }
            catch (Exception)
            {
                reasonCode = "serialization.failed";
                return false;
            }
        }

        internal static string HashTopology(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var text = new StringBuilder(64);
                foreach (byte item in hash) text.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static bool TryFingerprint(
            ItemDrop.ItemData item,
            bool includePosition,
            ref int aggregateCustom,
            out string fingerprint)
        {
            fingerprint = string.Empty;
            if (item == null || item.m_shared == null || item.m_stack <= 0) return false;
            string prefab = ValheimContracts.PrefabId(item);
            string sharedName = item.m_shared.m_name ?? string.Empty;
            string crafterName = item.m_crafterName ?? string.Empty;
            if (prefab.Length == 0 || sharedName.Length > 256 || crafterName.Length > 256) return false;
            var builder = new StringBuilder(512);
            Append(builder, prefab);
            Append(builder, sharedName);
            builder.Append('|').Append((int)item.m_shared.m_itemType)
                .Append('|').Append(item.m_stack)
                .Append('|').Append(BitConverter.SingleToInt32Bits(item.m_durability))
                .Append('|').Append(item.m_equipped ? 1 : 0)
                .Append('|').Append(item.m_quality)
                .Append('|').Append(item.m_variant)
                .Append('|').Append(item.m_crafterID)
                .Append('|').Append(item.m_worldLevel)
                .Append('|').Append(item.m_pickedUp ? 1 : 0);
            Append(builder, crafterName);
            if (includePosition) builder.Append('|').Append(item.m_gridPos.x).Append('|').Append(item.m_gridPos.y);

            int customCount = item.m_customData?.Count ?? 0;
            if (customCount > MaximumCustomEntriesPerItem) return false;
            var keys = new List<string>(customCount);
            if (item.m_customData != null)
            {
                foreach (KeyValuePair<string, string> pair in item.m_customData)
                {
                    string key = pair.Key ?? string.Empty;
                    string value = pair.Value ?? string.Empty;
                    if (key.Length > MaximumCustomStringCharacters || value.Length > MaximumCustomStringCharacters)
                        return false;
                    aggregateCustom += key.Length + value.Length;
                    if (aggregateCustom > MaximumAggregateCustomCharacters) return false;
                    keys.Add(key);
                }
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    Append(builder, key);
                    Append(builder, item.m_customData[key] ?? string.Empty);
                }
            }
            fingerprint = HashTopology(builder.ToString());
            return true;
        }

        private static void Append(StringBuilder builder, string value) =>
            builder.Append('|').Append(value.Length).Append(':').Append(value);

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
