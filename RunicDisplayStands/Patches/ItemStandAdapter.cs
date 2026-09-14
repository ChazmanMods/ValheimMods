namespace RunicDisplayStands
{
    /// <summary>
    /// Everything that touches ItemStand's private internals lives here so that if a game
    /// update renames a field, this is the only file you need to fix.
    ///
    /// Valheim 1.0 stores the placed item as its stable prefab hash. The legacy string read is
    /// retained solely to migrate stands written by pre-1.0 RunicDisplayStands builds.
    /// </summary>
    internal static class ItemStandAdapter
    {
        private static readonly System.Reflection.MethodInfo GetOrientationMethod =
            HarmonyLib.AccessTools.Method(typeof(ItemStand), "GetOrientation");

        public static ItemDrop.ItemData GetPlacedItem(ItemStand stand)
        {
            var nview = stand.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return null;

            var zdo = nview.GetZDO();
            int prefabHash = AttachedItemIdentity.Resolve(
                stand.GetAttachedItem(),
                nview.IsOwner(),
                () => zdo.GetString(ZDOVars.s_item, string.Empty),
                legacyName => legacyName.GetStableHashCode(),
                migratedHash =>
                {
                    zdo.Set(ZDOVars.s_item, migratedHash);
                    zdo.RemoveString(ZDOVars.s_item);
                });
            if (prefabHash == 0) return null;

            var stored = ItemDataSerializer.Deserialize(
                zdo.GetByteArray(ItemDataKey, null));
            if (stored != null)
            {
                if (stored.m_dropPrefab == null || stored.m_dropPrefab.name.GetStableHashCode() != prefabHash)
                    throw new System.InvalidOperationException("Saved stand data does not match the displayed item; stand left unchanged.");
                return stored;
            }

            var prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
            if (prefab == null) throw new System.InvalidOperationException("The displayed item prefab is unavailable.");

            var drop = prefab.GetComponent<ItemDrop>();
            var data = drop.m_itemData.Clone();
            ItemDrop.LoadFromZDO(data, zdo);
            data.m_stack = 1;
            return data;
        }

        internal static StandWriteBatch PrepareWrite(ItemStand stand, ItemDrop.ItemData item)
        {
            if (item != null && item.m_stack != 1)
                throw new System.InvalidOperationException("An item stand holds one item, not a stack.");
            var batch = new StandWriteBatch(stand.GetComponent<ZNetView>());
            batch.Item(item);
            return batch;
        }

        internal static void PublishVisual(ItemStand stand, ItemDrop.ItemData item)
        {
            stand.GetComponent<ZNetView>().InvokeRPC(ZNetView.Everybody, "SetVisualItem",
                item == null ? 0 : item.m_dropPrefab.name.GetStableHashCode(),
                item?.m_variant ?? 0, item?.m_quality ?? 1, GetOrientation(stand));
        }

        private static int GetOrientation(ItemStand stand) =>
            (int)GetOrientationMethod.Invoke(stand, null);

        private static readonly int ItemDataKey =
            "RunicDisplayStands_itemdata".GetStableHashCode();

    }
}
