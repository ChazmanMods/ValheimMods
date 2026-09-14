using System;

namespace RunicDisplayStands
{
    /// <summary>
    /// Uses Valheim's own inventory format so every ItemData field supported by the
    /// current game (including mod-owned custom data) survives a trip through a stand.
    /// </summary>
    internal static class ItemDataSerializer
    {
        internal static byte[] Serialize(ItemDrop.ItemData item)
        {
            if (item == null) return Array.Empty<byte>();

            var inventory = new Inventory("RunicDisplayStands", null, 1, 1);
            var stored = item.Clone();
            stored.m_stack = 1;
            stored.m_gridPos = new Vector2i(0, 0);
            inventory.GetAllItems().Add(stored);

            var package = new ZPackage();
            inventory.Save(package);
            return package.GetArray();
        }

        internal static ItemDrop.ItemData Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            try
            {
                var inventory = new Inventory("RunicDisplayStands", null, 1, 1);
                inventory.Load(new ZPackage(bytes));
                var items = inventory.GetAllItems();
                if (items.Count != 1 || items[0].m_stack != 1)
                    throw new InvalidOperationException("Invalid saved stand inventory.");

                var item = items[0];
                item.m_stack = 1;
                return item;
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning(
                    $"Could not restore complete stand item data; stand left unchanged. {error.Message}");
                throw;
            }
        }
    }
}
