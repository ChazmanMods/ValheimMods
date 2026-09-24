using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace Runic.Compatibility
{
    // Compiled into both mods: no hard dependency on RunicStorage or ItemDrawers.
    internal static class ModdedContainerCompatibility
    {
        internal const string Section = "Modded Containers";
        internal const string DefaultIds = "piece_drawer";
        private static readonly ConditionalWeakTable<Inventory, Container> Drawers = new ConditionalWeakTable<Inventory, Container>();
        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static bool IsDrawer(Container container) => container != null &&
            container.GetType().FullName == "DrawerContainer" &&
            container.GetType().Assembly.GetName().Name == "itemdrawers";

        internal static bool Listed(Container container, string ids)
        {
            var view = container != null ? container.GetComponent<ZNetView>() : null;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return false;
            // DrawerContainer renames its live GameObject, so use the persisted prefab hash.
            foreach (string entry in (ids ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = entry.Trim();
                if (id.Length > 0 && id != "*" && id.GetStableHashCode() == zdo.GetPrefab()) return true;
            }
            return false;
        }

        internal static string PullIds(string fallback)
        {
            if (Chainloader.PluginInfos.TryGetValue("chazman.RunicStorage", out var plugin) &&
                plugin.Instance != null && plugin.Instance.Config.TryGetEntry<string>(Section, "PullPrefabIds", out var entry))
                return entry.Value;
            return fallback;
        }

        internal static bool TryPreview(Container container, out Inventory preview)
        {
            preview = null;
            if (!IsDrawer(container)) return false;
            var view = container.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            try
            {
                string payload = view.GetZDO().GetString("items", "");
                if (payload.Length > 16384) return false;
                preview = new Inventory("ItemDrawers preview", null, 1, 1);
                if (payload.Length == 0) return true;
                var package = new ZPackage(payload);
                if (package.ReadInt() != 0) return false;
                string id = package.ReadString();
                if (id.Length == 0) return package.GetPos() == package.Size();
                int count = package.ReadInt();
                if (count < 0 || package.GetPos() != package.Size()) return false;
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(id) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) return false;
                var item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_gridPos = Vector2i.zero;
                item.m_stack = count;
                preview.GetAllItems().Add(item);
                return payload == view.GetZDO().GetString("items", "");
            }
            catch { preview = null; return false; }
        }

        internal static bool TryRefresh(Container container, out Inventory inventory)
        {
            inventory = null;
            if (!IsDrawer(container)) return false;
            var view = container.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner() || view.GetZDO().GetOwner() != ZNet.GetUID()) return false;
            try
            {
                if (!TryPreview(container, out var expected)) return false;
                var type = container.GetType();
                var revision = type.GetField("_lastRevision", Declared);
                var load = type.GetMethod("Load", Declared);
                var quantity = type.GetField("_quantity", Declared);
                if (revision == null || load == null || quantity == null) return false;
                int expectedCount = expected.GetAllItems().Count == 0 ? 0 : expected.GetAllItems()[0].m_stack;
                inventory = container.GetInventory();
                if (inventory != null && SameContents(expected, inventory) &&
                    (int)quantity.GetValue(container) == expectedCount)
                {
                    Drawers.GetValue(inventory, _ => container);
                    return true;
                }
                revision.SetValue(container, null);
                load.Invoke(container, Array.Empty<object>());
                inventory = container.GetInventory();
                if (inventory == null || !SameContents(expected, inventory) ||
                    !TryPreview(container, out var after) || !SameContents(expected, after)) return false;
                if ((int)quantity.GetValue(container) != expectedCount) return false;
                Drawers.GetValue(inventory, _ => container);
                return true;
            }
            catch { inventory = null; return false; }
        }

        private static bool SameContents(Inventory left, Inventory right)
        {
            var a = left.GetAllItems(); var b = right.GetAllItems();
            return a.Count == b.Count && (a.Count == 0 || a.Count == 1 &&
                a[0].m_dropPrefab == b[0].m_dropPrefab && a[0].m_stack == b[0].m_stack);
        }

        internal static ItemDrop.ItemData TransferTemplate(Inventory source, ItemDrop.ItemData item) =>
            Drawers.TryGetValue(source, out _) ? NativeTemplate(item) : item;

        internal static bool IsDrawerInventory(Inventory inventory) => Drawers.TryGetValue(inventory, out _);

        internal static ItemDrop.ItemData NativeTemplate(ItemDrop.ItemData item)
        {
            var drop = item?.m_dropPrefab != null ? item.m_dropPrefab.GetComponent<ItemDrop>() : null;
            if (drop == null) throw new InvalidOperationException("Drawer item prefab is unavailable.");
            var copy = item.Clone();
            copy.m_shared = drop.m_itemData.m_shared;
            return copy;
        }
    }
}
