using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicStorage.Runtime
{
    internal static class StorageContainerAuthority
    {
        internal static Func<bool> CaptureAuthority(Container container)
        {
            if (container == null) return () => true;
            ZNetView view = ValheimContainerIdentity.NetworkView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            Inventory inventory = container.GetInventory();
            var revision = zdo?.OwnerRevision;
            RunicAutomation.MutationGate.Current?.Track(zdo?.m_uid);
            return () => container != null && view != null && view.IsValid() && view.IsOwner() &&
                ReferenceEquals(view.GetZDO(), zdo) && zdo != null && zdo.OwnerRevision == revision &&
                ReferenceEquals(container.GetInventory(), inventory) && !RunicAutomation.ContainerAuthority.Blocked(container);
        }
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

        private static readonly FieldInfo LastRevisionField =
            AccessTools.Field(typeof(Container), "m_lastRevision") ??
            throw new MissingFieldException(typeof(Container).FullName, "m_lastRevision");

        // Refresh only this candidate before matching or checking capacity. Native Load
        // reads the already received ZDO payload; no ownership request or network wait.
        internal static bool TryReadCurrentInventory(Container container, out Inventory inventory)
        {
            inventory = null;
            ZNetView view = ValheimContainerIdentity.NetworkView(container);
            if (container == null || view == null || !view.IsValid() || view.GetZDO() == null ||
                (bool)LoadingField.GetValue(container)) return false;
            try
            {
                if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(container))
                    return Runic.Compatibility.ModdedContainerCompatibility.TryRefresh(container, out inventory);
                // Preserve an initialized local owner's live inventory. The MaxValue
                // sentinel identifies a chest that has not performed its first Load/Save.
                if (!view.IsOwner() || (uint)LastRevisionField.GetValue(container) == uint.MaxValue)
                    LoadMethod.Invoke(container, Array.Empty<object>());
                inventory = container.GetInventory();
                return inventory != null;
            }
            catch { inventory = null; return false; }
        }

        internal static bool TryGetSynchronizedServerInventory(
            Container container,
            out Inventory inventory) =>
            TryGetOwnedInventory(container, allowInUse: false, out inventory);

        internal static bool TryClaimWritableInventory(
            Container container,
            bool allowCurrentUse)
        {
            if (ZNet.instance == null || container == null || RunicAutomation.ContainerAuthority.Blocked(container) || !container.isActiveAndEnabled ||
                (bool)LoadingField.GetValue(container) ||
                container.m_wagon != null && container.m_wagon.InUse())
                return false;

            ZNetView view = ValheimContainerIdentity.NetworkView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return false;

            bool persistedInUse = zdo.GetInt(ZDOVars.s_inUse, 0) != 0;
            if (!allowCurrentUse && (container.IsInUse() || persistedInUse)) return false;
            if (allowCurrentUse && persistedInUse && !view.IsOwner()) return false;

            Player actor = Player.m_localPlayer;
            if (actor == null || !RunicAutomation.ContainerAuthority.TryAcquire(container, "storage",
                    RunicAutomation.ContainerAuthority.PlayerContext(actor), actor.GetPlayerID(), allowCurrentUse)) return false;
            return view.IsOwner() && zdo.GetOwner() == ZNet.GetUID();
        }

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

        internal static bool TryGetOwnedInventory(
            Container container,
            bool allowInUse,
            out Inventory inventory, bool existingOwnerOnly = false)
        {
            inventory = null;
            if (!RunicAutomation.ContainerAuthority.Writable(container, allowInUse)) return false;
            if (!existingOwnerOnly && !TryClaimWritableInventory(container, allowInUse)) return false;

            ZNetView view = ValheimContainerIdentity.NetworkView(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
            if (zdo == null || zdo.GetOwner() != ZNet.GetUID()) return false;
            if (allowInUse && zdo.GetInt(ZDOVars.s_inUse, 0) == 0) return false;

            try
            {
                if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(container))
                    return Runic.Compatibility.ModdedContainerCompatibility.TryRefresh(container, out inventory);
                if (existingOwnerOnly) LoadMethod.Invoke(container, Array.Empty<object>());
                inventory = container.GetInventory();
                if (inventory == null) return false;
                return true;
            }
            catch
            {
                inventory = null;
                return false;
            }
        }
    }
}
