using System;
using System.Linq;
using HarmonyLib;

namespace RunicCrafting.Integration
{
    internal static class ManualItemTransfer
    {
        private static readonly System.Reflection.MethodInfo FreeStack = AccessTools.Method(typeof(Inventory), "FindFreeStackItem");
        private static readonly System.Reflection.MethodInfo EmptySlot = AccessTools.Method(typeof(Inventory), "FindEmptySlot", new[] { typeof(bool) });
        private static readonly System.Reflection.MethodInfo TopFirst = AccessTools.Method(typeof(Inventory), "TopFirst", new[] { typeof(ItemDrop.ItemData) });

        internal static bool TryMoveOne(Inventory source, Inventory destination, string resource,
            Func<bool> eligible, Func<ItemDrop.ItemData, bool> permitted, out string reason, Func<bool> stillWritable = null)
        {
            reason = "source-unavailable";
            if (source == null || destination == null || ReferenceEquals(source, destination) ||
                eligible == null || permitted == null || !eligible()) return false;
            var item = source.GetAllItems().FirstOrDefault(i =>
                ValheimReflection.IsUsableRequirementItem(i, resource) && i.m_stack > 0 && permitted(i));
            if (item == null) { reason = "no-unprotected-match"; return false; }
            var template = Runic.Compatibility.ModdedContainerCompatibility.TransferTemplate(source, item);
            if (!destination.CanAddItem(template, 1)) { reason = "backpack-space-required"; return false; }
            var stack = (ItemDrop.ItemData)FreeStack.Invoke(destination,
                new object[] { template.m_shared.m_name, template.m_quality, (float)template.m_worldLevel });
            var slot = stack != null ? stack.m_gridPos : (Vector2i)EmptySlot.Invoke(destination,
                new object[] { (bool)TopFirst.Invoke(destination, new object[] { template }) });
            if (slot.x < 0) { reason = "backpack-space-required"; return false; }
            if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(source))
            {
                var copy = template.Clone(); copy.m_stack = 1;
                if (!destination.AddItem(copy)) return false;
                item.m_stack--;
                ValheimReflection.NotifyInventoryChanged(source);
            }
            else
            {
                int before = item.m_stack;
                destination.MoveItemToThis(source, item, 1, slot.x, slot.y);
                if (before - (source.ContainsItem(item) ? item.m_stack : 0) != 1) return false;
            }
            reason = "ok";
            return true;
        }
    }
}
