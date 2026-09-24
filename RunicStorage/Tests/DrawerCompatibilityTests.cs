using System;
using System.Linq;
using Runic.Compatibility;
using RunicStorage.Runtime;

namespace RunicStorage.Tests
{
    internal static class DrawerCompatibilityTests
    {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        internal static void ConfigAndSynchronization()
        {
            var drawer = DrawerFixture.Create(100);
            Check(ModdedContainerCompatibility.IsDrawer(drawer), "Recognize external type.");
            Check(ModdedContainerCompatibility.Listed(drawer, " other; piece_drawer,other"), "Exact trimmed prefab list.");
            Check(!ModdedContainerCompatibility.Listed(drawer, "piece_draw;*"), "No partial or wildcard match.");
            Check(!ModdedContainerCompatibility.Listed(drawer, ""), "Empty disables adapter.");
            Check(ModdedContainerCompatibility.TryRefresh(drawer, out var inventory), "Refresh custom payload.");
            var original = inventory.GetAllItems()[0];
            Check(ModdedContainerCompatibility.TryRefresh(drawer, out _) && ReferenceEquals(original, inventory.GetAllItems()[0]), "Repeated eligibility must retain live item references.");
            drawer.View.Data.Owner = 2;
            Check(!ModdedContainerCompatibility.TryRefresh(drawer, out _), "Remote owner denied.");
            Check(ModdedContainerCompatibility.TryPreview(drawer, out var preview) && preview.GetAllItems()[0].m_stack == 100, "Read-only remote preview.");
            drawer.View.Data.Owner = 1;
            var package = new ZPackage(); package.Write(0); package.Write("Wood"); package.Write(73);
            drawer.View.Data.Payload = package.GetBase64();
            Check(ModdedContainerCompatibility.TryRefresh(drawer, out inventory) && inventory.GetAllItems()[0].m_stack == 73, "Refresh stale live inventory.");
            drawer.View.Data.Payload = "invalid";
            Check(!ModdedContainerCompatibility.TryPreview(drawer, out _) && !ModdedContainerCompatibility.TryRefresh(drawer, out _), "Malformed data fails closed.");
            var plugin = new BepInEx.Bootstrap.PluginInfo();
            plugin.Instance.Config.PullIds = "";
            BepInEx.Bootstrap.Chainloader.PluginInfos["chazman.RunicStorage"] = plugin;
            Check(ModdedContainerCompatibility.PullIds("piece_drawer") == "", "Explicit Storage disable overrides Crafting fallback.");
            BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
            Check(ModdedContainerCompatibility.PullIds("piece_drawer") == "piece_drawer", "Standalone Crafting fallback.");
        }

        internal static void DepositAndRestockConserveItems()
        {
            var emptying = DrawerFixture.Create(5);
            var last = new Inventory("last", null, 1, 1);
            Check(ValheimContainerService.MoveUpTo(emptying.Inventory, last, emptying.Inventory.GetAllItems()[0], 5, sourceContainer: emptying) == 5, "Withdrawing the final stack must succeed.");
            Check(emptying.Inventory.GetAllItems().Count == 1 && emptying.Inventory.GetAllItems()[0].m_stack == 0, "Retain the assigned empty drawer item.");
            var drawer = DrawerFixture.Create(9980);
            var source = new Inventory("backpack", null, 3, 1);
            var wood = ObjectDB.instance.GetItemPrefab("Wood").Drop.m_itemData.Clone();
            wood.m_stack = 50; wood.m_pickedUp = true; source.GetAllItems().Add(wood);
            Check(ValheimContainerService.MoveUpTo(source, drawer.Inventory, wood, 50, destinationContainer: drawer) == 19, "Drawer capacity is 9999, not native 50.");
            Check(wood.m_stack == 31 && ModdedContainerCompatibility.TryPreview(drawer, out var preview) && preview.GetAllItems()[0].m_stack == 9999, "Deposit persisted exact accepted quantity.");
            Check(ValheimContainerService.MoveUpTo(source, drawer.Inventory, wood, 31, destinationContainer: drawer) == 0, "Full drawer rejects transfer.");
            var target = new Inventory("backpack", null, 3, 1);
            Check(ValheimContainerService.MoveUpTo(drawer.Inventory, target, drawer.Inventory.GetAllItems()[0], 120, sourceContainer: drawer) == 120, "Withdraw multiple native stacks.");
            Check(target.GetAllItems().Select(i => i.m_stack).SequenceEqual(new[] {50,50,20}), "Respect native stack limits.");
            Check(target.GetAllItems().All(i => i.m_shared.m_maxStackSize == 50), "Drawer shared data must not escape.");
            Check(ModdedContainerCompatibility.TryPreview(drawer, out preview) && preview.GetAllItems()[0].m_stack == 9879, "Withdrawal persisted.");
            var partial = new Inventory("backpack", null, 1, 1);
            var carried = wood.Clone(); carried.m_stack = 40; partial.GetAllItems().Add(carried);
            Check(ValheimContainerService.MoveUpTo(drawer.Inventory, partial, drawer.Inventory.GetAllItems()[0], 20, sourceContainer: drawer) == 10,
                "Restock fills a carried partial stack even though drawers do not persist the picked-up flag.");
            Check(carried.m_stack == 50 && carried.m_pickedUp, "Merge preserves the carried stack identity and pickup flag.");
        }

        internal static void MetadataAndRollback()
        {
            var drawer = DrawerFixture.Create(10);
            var source = new Inventory("backpack", null, 3, 1);
            var wood = ObjectDB.instance.GetItemPrefab("Wood").Drop.m_itemData.Clone();
            wood.m_stack = 8; wood.m_customData["enchanted"] = "yes"; source.GetAllItems().Add(wood);
            Check(ValheimContainerService.MoveUpTo(source, drawer.Inventory, wood, 8, out var rejection, destinationContainer: drawer) == 0 &&
                rejection == StorageMoveFailure.UnsupportedDrawerMetadata, "Custom metadata is distinguished from a full drawer.");
            wood.m_customData.Clear();
            var persist = drawer.Inventory.m_onChanged; int calls = 0;
            drawer.Inventory.m_onChanged = () => { persist(); if (++calls == 1) throw new Exception("injected"); };
            bool threw = false;
            try { ValheimContainerService.MoveUpTo(source, drawer.Inventory, wood, 8, destinationContainer: drawer); } catch { threw = true; }
            Check(threw && wood.m_stack == 8 && source.GetAllItems().Contains(wood), "Source rollback keeps original reference.");
            Check(ModdedContainerCompatibility.TryPreview(drawer, out var preview) && preview.GetAllItems()[0].m_stack == 10, "Drawer rollback restores persisted count.");
        }

        internal static void SpawnedItemsFollowNativeDrawerBehavior()
        {
            // Reported Greydwarf-eye stack: quality 1, variant/world 0, durability 100, no
            // custom data/crafter, picked-up and cheated flags set; drawer count 10.
            var drawer = DrawerFixture.Create(10);
            var canonical = ObjectDB.instance.GetItemPrefab("Wood").Drop.m_itemData;
            canonical.m_durability = 100;
            drawer.Inventory.GetAllItems()[0].m_durability = 100;
            var source = new Inventory("backpack", null, 1, 1);
            var item = canonical.Clone(); item.m_stack = 20; item.m_pickedUp = item.m_cheated = true;
            source.GetAllItems().Add(item);
            Check(ValheimContainerService.MoveUpTo(source, drawer.Inventory, item, 20, out var failure,
                destinationContainer: drawer) == 20 && failure == StorageMoveFailure.None,
                "Spawned stacks should deposit just as they do through ItemDrawers itself.");
            Check(source.GetAllItems().Count == 0 && ModdedContainerCompatibility.TryPreview(drawer, out var preview) &&
                preview.GetAllItems()[0].m_stack == 30, "Reported 10 + 20 drawer transfer persists 30.");
            Check(!drawer.Inventory.GetAllItems()[0].m_cheated,
                "Drawer type/count format follows its native treatment of the spawned flag.");
        }
    }
}
