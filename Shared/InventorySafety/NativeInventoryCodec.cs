using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace RunicAutomation.Operations
{
    internal static class NativeInventoryCodec
    {
        // Native Save encodes the number of occupied item records as UInt16. Capacity is
        // separate: an empty modded slot is not an item record and needs no Runic size cap.
        internal const int MaximumItems = ushort.MaxValue;
        internal static bool ValidDimensions(int width, int height) => width > 0 && height > 0 && (long)width * height <= int.MaxValue;
        internal static byte[] Save(Inventory inventory)
        {
            if (ManagedDrawerInventory.IsDrawer(inventory)) return ManagedDrawerInventory.Save(inventory);
            if (inventory == null || inventory.GetAllItems().Count > MaximumItems)
                throw new InvalidDataException("Unsupported inventory size");
            var package = new ZPackage(); inventory.Save(package);
            byte[] bytes = package.GetArray();
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Inventory payload too large");
            // Stack-size mods may extend the native UInt16 field using item metadata.
            // Accept their actual serializer, but never let an unextended writer truncate
            // a large stack before an authoritative transaction commits it.
            if (inventory.GetAllItems().Any(item => item.m_stack > ushort.MaxValue))
                VerifyExtendedStackCounts(inventory, bytes);
            return bytes;
        }

        private static void VerifyExtendedStackCounts(Inventory inventory, byte[] bytes)
        {
            var package = new ZPackage(bytes);
            int version = package.ReadInt();
            if (version != 109 || package.ReadUShort() != inventory.GetAllItems().Count)
                throw new InvalidDataException("Extended stack serializer changed the inventory structure");
            foreach (var original in inventory.GetAllItems())
            {
                var restored = ItemDrop.ItemData.Load(package, (global::Version.Item)version);
                if (restored.itemData.m_stack != original.m_stack)
                    throw new InvalidDataException("Installed item serializer cannot preserve this stack quantity");
            }
            OperationPacket.End(package);
        }

        internal static Inventory Load(byte[] bytes, int width, int height, string name = "Runic operation")
        {
            if (bytes != null && bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == ManagedDrawerInventory.Schema) return ManagedDrawerInventory.Load(bytes);
            if (!ValidDimensions(width, height) ||
                bytes == null || bytes.Length > 1024 * 1024)
                throw new InvalidDataException("Unsupported inventory dimensions or payload");
            var inventory = new Inventory(name, null, width, height);
            // Use the game's loader, including installed mod patches, rather than rebuilding
            // item data from prefabs or requiring byte-identical normalization by Runic.
            inventory.Load(new ZPackage(bytes));
            Validate(inventory);
            return inventory;
        }

        internal static Inventory Clone(Inventory inventory)
        {
            var clone = new Inventory(inventory.GetName(), null, inventory.GetWidth(), inventory.GetHeight());
            foreach (var item in inventory.GetAllItems()) clone.GetAllItems().Add(item.Clone());
            ManagedDrawerInventory.CopyShape(inventory, clone);
            Validate(clone);
            return clone;
        }

        internal static void Validate(Inventory inventory)
        {
            if (ManagedDrawerInventory.IsDrawer(inventory)) { ManagedDrawerInventory.Validate(inventory); return; }
            var occupied = new HashSet<Vector2i>();
            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || item.m_dropPrefab == null || item.m_shared == null || item.m_stack <= 0 ||
                    item.m_gridPos.x < 0 || item.m_gridPos.y < 0 || item.m_gridPos.x >= inventory.GetWidth() ||
                    item.m_gridPos.y >= inventory.GetHeight() || !occupied.Add(item.m_gridPos))
                    throw new InvalidDataException("Invalid native inventory item: shape=" + inventory.GetWidth() + "x" + inventory.GetHeight() +
                        "; slot=" + item?.m_gridPos + "; stack=" + item?.m_stack + "; prefab=" + item?.m_dropPrefab?.name + "; quality=" + item?.m_quality);
            }
        }

        internal static string ItemIdentity(ItemDrop.ItemData item)
        {
            return OperationRequest.Hash(Convert.ToBase64String(InventoryMetadataComparison.ForItem(item)));
        }

        internal static byte[] ComparisonBytes(Inventory inventory) => ComparisonBytes(Save(inventory));

        internal static byte[] ComparisonBytes(byte[] bytes) => InventoryMetadataComparison.ForPayload(bytes);

        internal static string StackIdentity(ItemDrop.ItemData item) => ItemIdentity(item);

        // All operations occur on detached shadows. No callbacks can publish an intermediate source.
        internal static bool Transfer(Inventory source, Inventory destination, Vector2i slot, string identity, int amount)
        {
            var item = source.GetItemAt(slot.x, slot.y);
            if (item == null || amount < 1 || item.m_stack < amount || ItemIdentity(item) != identity) return false;
            var moved = item.Clone(); moved.m_stack = amount;
            if (!AddExact(destination, moved)) return false;
            if (!source.RemoveItem(item, amount)) throw new InvalidOperationException("Shadow transfer source changed");
            Validate(source); Validate(destination); return true;
        }

        internal static bool MoveSlot(Inventory inventory, Vector2i source, string identity, int amount,
            Vector2i target, string targetIdentity)
        {
            if (target.x < 0 || target.y < 0 || target.x >= inventory.GetWidth() || target.y >= inventory.GetHeight()) return false;
            var item = inventory.GetItemAt(source.x, source.y);
            var other = inventory.GetItemAt(target.x, target.y);
            if (item == null || amount < 1 || amount > item.m_stack || ItemIdentity(item) != identity ||
                (other == null ? "" : ItemIdentity(other)) != targetIdentity) return false;
            if (source == target) return true;
            if (other == null)
            {
                if (amount == item.m_stack) item.m_gridPos = target;
                else { var split = item.Clone(); split.m_stack = amount; split.m_gridPos = target;
                    inventory.GetAllItems().Add(split); item.m_stack -= amount; }
            }
            else if (identity == targetIdentity)
            {
                int moved = Math.Min(amount, other.m_shared.m_maxStackSize - other.m_stack);
                if (moved < 1) return false;
                other.m_stack += moved;
                if (moved == item.m_stack) inventory.GetAllItems().Remove(item); else item.m_stack -= moved;
            }
            else
            {
                if (amount != item.m_stack) return false;
                other.m_gridPos = source; item.m_gridPos = target;
            }
            Validate(inventory); return true;
        }

        internal static bool AddAll(Inventory destination, Inventory incoming)
        {
            foreach (var item in incoming.GetAllItems())
            {
                var copy = item.Clone(); copy.m_equipped = false;
                if (!AddExact(destination, copy)) return false;
            }
            Validate(destination); return true;
        }

        internal static int CapacityFor(Inventory destination, ItemDrop.ItemData item)
        {
            if (ManagedDrawerInventory.IsDrawer(destination)) return ManagedDrawerInventory.CapacityFor(destination, item);
            int maximum = item.m_shared.m_maxStackSize;
            if (maximum < 1) return 0;
            string identity = StackIdentity(item);
            long capacity = (long)(destination.GetWidth() * destination.GetHeight() - destination.GetAllItems().Count) * maximum;
            foreach (var existing in destination.GetAllItems())
                if (StackIdentity(existing) == identity) capacity += Math.Max(0, maximum - existing.m_stack);
            int low = 0, high = (int)Math.Min(int.MaxValue, capacity);
            while (low < high)
            {
                int middle = low + (int)(((long)high - low + 1) / 2);
                if (destination.CanAddItem(item, middle)) low = middle; else high = middle - 1;
            }
            return low;
        }

        internal static bool AddExact(Inventory destination, ItemDrop.ItemData item)
        {
            if (ManagedDrawerInventory.IsDrawer(destination)) return ManagedDrawerInventory.Add(destination, item);
            if (item == null || item.m_stack < 1 || CapacityFor(destination, item) < item.m_stack) return false;
            // Use the native slot-transfer method. Automatic AddItem(name) stacking can
            // discard custom data by merging a distinct modded item into an ordinary stack.
            // Slot insertion still executes the installed Inventory patches and callbacks.
            var moved = item.Clone(); string identity = StackIdentity(item);
            var insert = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
            foreach (var stack in destination.GetAllItems().Where(i => StackIdentity(i) == identity && i.IsSameType(item)).ToArray())
            {
                int amount = Math.Min(moved.m_stack, Math.Max(0, stack.m_shared.m_maxStackSize - stack.m_stack));
                if (amount > 0 && !(bool)insert.Invoke(destination, new object[] { moved, amount, stack.m_gridPos.x, stack.m_gridPos.y, false })) return false;
                if (moved.m_stack == 0) return true;
            }
            while (moved.m_stack > 0)
            {
                bool top = (bool)AccessTools.Method(typeof(Inventory), "TopFirst").Invoke(destination, new object[] { moved });
                var slot = (Vector2i)AccessTools.Method(typeof(Inventory), "FindEmptySlot").Invoke(destination, new object[] { top });
                if (slot.x < 0 || !(bool)insert.Invoke(destination, new object[] { moved, Math.Min(moved.m_stack, Math.Max(1, moved.m_shared.m_maxStackSize)), slot.x, slot.y, false })) return false;
            }
            return true;
        }
    }
}
