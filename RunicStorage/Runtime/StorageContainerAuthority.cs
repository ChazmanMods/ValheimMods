using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicStorage.Runtime
{
    internal static class StorageContainerAuthority
    {
        internal const string ContainerLoadMethodName = "Load";
        internal const string ContainerLoadingFieldName = "m_loading";

        private static readonly MethodInfo LoadMethod =
            AccessTools.Method(typeof(Container), ContainerLoadMethodName) ??
            throw new MissingMethodException(typeof(Container).FullName, ContainerLoadMethodName);
        private static readonly FieldInfo LoadingField =
            AccessTools.Field(typeof(Container), ContainerLoadingFieldName) ??
            throw new MissingFieldException(typeof(Container).FullName, ContainerLoadingFieldName);
        private static readonly FieldInfo CurrentContainerField =
            AccessTools.Field(typeof(InventoryGui), "m_currentContainer") ??
            throw new MissingFieldException(typeof(InventoryGui).FullName, "m_currentContainer");
        private static readonly FieldInfo DragItemField =
            AccessTools.Field(typeof(InventoryGui), "m_dragItem") ??
            throw new MissingFieldException(typeof(InventoryGui).FullName, "m_dragItem");

        internal static bool TryGetSynchronizedServerInventory(
            Container container,
            out Inventory inventory) =>
            TryGetOwnedInventory(container, allowInUse: false, out inventory);

        internal static bool TryGetExactOpenedServerOwnerInventory(
            Container container,
            Player player,
            out Inventory inventory) =>
            TryGetExactOpenedLocalOwnerInventory(container, player, out inventory);

        internal static bool TryGetExactOpenedLocalOwnerInventory(
            Container container,
            Player player,
            out Inventory inventory)
        {
            inventory = null;
            if (container == null || player == null ||
                !ReferenceEquals(player, Player.m_localPlayer) || !player.IsOwner() ||
                InventoryGui.instance == null ||
                !ReferenceEquals(CurrentContainerField.GetValue(InventoryGui.instance), container) ||
                DragItemField.GetValue(InventoryGui.instance) != null ||
                !container.isActiveAndEnabled || !container.IsInUse() ||
                container.m_wagon != null && container.m_wagon.InUse())
                return false;
            return TryGetOwnedInventory(container, allowInUse: true, out inventory);
        }

        private static bool TryGetOwnedInventory(
            Container container,
            bool allowInUse,
            out Inventory inventory)
        {
            inventory = null;
            if (ZNet.instance == null || container == null || !container.isActiveAndEnabled ||
                !container.IsOwner() || (!allowInUse &&
                (container.IsInUse() || container.m_wagon != null && container.m_wagon.InUse())) ||
                (bool)LoadingField.GetValue(container))
                return false;

            ZNetView view = ValheimContainerIdentity.NetworkView(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
            if (zdo == null || zdo.GetOwner() != ZNet.GetUID()) return false;
            if (allowInUse && !zdo.GetBool(ZDOVars.s_inUse, false)) return false;

            try
            {
                LoadMethod.Invoke(container, Array.Empty<object>());
                inventory = container.GetInventory();
                if (inventory == null) return false;
                string persisted = zdo.GetString(ZDOVars.s_items, string.Empty);
                return string.IsNullOrEmpty(persisted)
                    ? inventory.GetAllItems().Count == 0
                    : string.Equals(
                        ValheimContainerService.SaveInventory(inventory).GetBase64(),
                        persisted,
                        StringComparison.Ordinal);
            }
            catch
            {
                inventory = null;
                return false;
            }
        }
    }
}
