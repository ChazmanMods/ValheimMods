using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RunicAutomation.Operations
{
    // Drawer quantities are Int32, whereas native inventory stacks serialize as UInt16.
    // Keep assignment and capacity in an explicit envelope, including assigned empty drawers.
    internal static class ManagedDrawerInventory
    {
        internal const int Schema = -184723901;
        private static ConditionalWeakTable<Inventory, Tuple<string, int>> Shapes => SharedState.Get("operations.drawer-shapes.v1", () => new ConditionalWeakTable<Inventory, Tuple<string, int>>());
        internal static bool IsDrawer(Container container) => container != null && container.GetType().FullName == "DrawerContainer" && container.GetType().Assembly.GetName().Name == "itemdrawers";
        internal static bool IsDrawer(Inventory inventory) => inventory != null && Shapes.TryGetValue(inventory, out _);
        internal static bool Listed(ZDO zdo, string ids) => (ids ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(id => id.Trim().Length > 0 && id.Trim() != "*" && id.Trim().GetStableHashCode() == zdo.GetPrefab());
        internal static string Assignment(Inventory inventory) => Shapes.TryGetValue(inventory, out var shape) ? shape.Item1 : "";
        internal static int Maximum => (int)(AccessTools.Property(Type.GetType("itemdrawers.ItemDrawerPlugin, itemdrawers", false), "MaxItems")?.GetValue(null) ?? 0);
        internal static Inventory Create(string assignment, int count, int capacity)
        {
            if (capacity < 1 || count < 0 || count > capacity || assignment.Length > 128 || (assignment.Length == 0 && count != 0))
                throw new InvalidDataException("Invalid drawer quantity or capacity");
            var inventory = new Inventory("Runic drawer", null, 1, 1); Shapes.Add(inventory, Tuple.Create(assignment, capacity));
            if (count > 0)
            {
                var prefab = ObjectDB.instance.GetItemPrefab(assignment); var drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
                if (drop == null) throw new InvalidDataException("Drawer assignment is unavailable");
                var item = drop.m_itemData.Clone(); item.m_dropPrefab = prefab; item.m_stack = count; item.m_gridPos = Vector2i.zero;
                item.m_shared = (ItemDrop.ItemData.SharedData)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(item.m_shared, null);
                item.m_shared.m_maxStackSize = capacity; inventory.GetAllItems().Add(item);
            }
            return inventory;
        }
        internal static byte[] ReadNative(ZDO zdo)
        {
            string raw = zdo.GetString("items", ""), assignment = ""; int count = 0;
            if (raw.Length > 16384) throw new InvalidDataException("Drawer snapshot too large");
            if (raw.Length > 0)
            {
                var packet = new ZPackage(raw); if (packet.ReadInt() != 0) throw new InvalidDataException("Unknown drawer schema");
                assignment = packet.ReadString(); if (assignment.Length > 0) count = packet.ReadInt(); OperationPacket.End(packet);
            }
            return Save(Create(assignment, count, Maximum));
        }
        internal static string NativePayload(Inventory inventory)
        {
            Validate(inventory); var packet = new ZPackage(); packet.Write(0); packet.Write(Assignment(inventory));
            if (Assignment(inventory).Length > 0) packet.Write(inventory.GetAllItems().Sum(i => i.m_stack));
            return packet.GetBase64();
        }
        internal static byte[] Save(Inventory inventory)
        {
            Validate(inventory); var shape = Shapes.GetValue(inventory, _ => throw new InvalidOperationException());
            var packet = new ZPackage(); packet.Write(Schema); packet.Write(shape.Item1); packet.Write(shape.Item2);
            packet.Write(inventory.GetAllItems().Sum(i => i.m_stack)); return packet.GetArray();
        }
        internal static Inventory Load(byte[] bytes)
        {
            var packet = new ZPackage(bytes); if (packet.ReadInt() != Schema) throw new InvalidDataException("Unknown drawer envelope");
            string assignment = packet.ReadString(); int capacity = packet.ReadInt(), count = packet.ReadInt(); OperationPacket.End(packet);
            return Create(assignment, count, capacity);
        }
        internal static void CopyShape(Inventory source, Inventory target)
        { if (Shapes.TryGetValue(source, out var shape)) { Shapes.Remove(target); Shapes.Add(target, shape); } }
        internal static bool Representable(ItemDrop.ItemData item)
        {
            var canonical = item?.m_dropPrefab?.GetComponent<ItemDrop>()?.m_itemData;
            return canonical != null && canonical.m_shared.m_maxStackSize > 1 && !item.m_cheated && !item.m_shared.m_questItem &&
                item.m_quality == canonical.m_quality && item.m_variant == canonical.m_variant && item.m_worldLevel == canonical.m_worldLevel &&
                item.m_crafterID == canonical.m_crafterID && (item.m_crafterID == 0 || (item.m_crafterName ?? "") == (canonical.m_crafterName ?? "")) &&
                (item.m_customData?.Count ?? 0) == (canonical.m_customData?.Count ?? 0) &&
                (item.m_customData == null || item.m_customData.All(p => canonical.m_customData != null && canonical.m_customData.TryGetValue(p.Key, out var value) && value == p.Value));
        }
        internal static int CapacityFor(Inventory inventory, ItemDrop.ItemData item)
        {
            var shape = Shapes.GetValue(inventory, _ => throw new InvalidOperationException());
            if (!Representable(item) || (shape.Item1.Length > 0 && shape.Item1 != item.m_dropPrefab.name)) return 0;
            return shape.Item2 - inventory.GetAllItems().Sum(i => i.m_stack);
        }
        internal static bool Add(Inventory inventory, ItemDrop.ItemData item)
        {
            if (item == null || item.m_stack < 1 || CapacityFor(inventory, item) < item.m_stack) return false;
            var shape = Shapes.GetValue(inventory, _ => throw new InvalidOperationException());
            int total = inventory.GetAllItems().Sum(i => i.m_stack) + item.m_stack;
            var next = Create(item.m_dropPrefab.name, total, shape.Item2);
            inventory.GetAllItems().Clear(); inventory.GetAllItems().AddRange(next.GetAllItems()); CopyShape(next, inventory); return true;
        }
        internal static void Validate(Inventory inventory)
        {
            var shape = Shapes.GetValue(inventory, _ => throw new InvalidOperationException()); var items = inventory.GetAllItems();
            if (items.Count > 1 || (items.Count == 1 && (items[0].m_stack < 1 || items[0].m_stack > shape.Item2 ||
                items[0].m_gridPos != Vector2i.zero || items[0].m_dropPrefab?.name != shape.Item1 || !Representable(items[0]))))
                throw new InvalidDataException("Drawer contents cannot be represented without loss");
        }
    }
}
