using RunicInteraction.Core;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class TransferGestureRuntime
    {
        private static bool _lockWarning;

        internal static bool TryHandleAltClick(InventoryGrid grid, UIInputHandler handler)
        {
            if (!FeatureOn() || !grid || !handler) return false;
            if (ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl) ||
                ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift) ||
                !(ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt)))
                return false;

            InventoryGui gui = InventoryGui.instance;
            Container container = ValheimAccess.GetCurrentContainer(gui);
            if (!container || ValheimAccess.HasDrag(gui)) return false;
            Vector2i position = ValheimAccess.GetButtonPosition(grid, handler.gameObject);
            Inventory inventory = grid.GetInventory();
            ItemDrop.ItemData item = inventory?.GetItemAt(position.x, position.y);
            if (item == null || grid.m_onSelected == null) return false;
            if (!CanMove(container, item, gui, notify: true)) return true;

            grid.m_onSelected(grid, item, position, InventoryGrid.Modifier.Move);
            return true;
        }

        internal static bool GuardVanillaMove(
            InventoryGrid grid,
            ItemDrop.ItemData item,
            InventoryGrid.Modifier modifier)
        {
            if (modifier != InventoryGrid.Modifier.Move || !FeatureOn()) return true;
            InventoryGui gui = InventoryGui.instance;
            Container container = ValheimAccess.GetCurrentContainer(gui);
            if (!container) return true; // Keep vanilla Ctrl/controller drop outside containers.
            return CanMove(container, item, gui, notify: true);
        }

        internal static void OnConfigurationChanged() => _lockWarning = false;

        private static bool CanMove(
            Container container,
            ItemDrop.ItemData item,
            InventoryGui gui,
            bool notify)
        {
            Player player = Player.m_localPlayer;
            long playerId = player ? player.GetPlayerID() : 0L;
            bool permission = player && ValheimAccess.ContainerAllows(container, playerId);
            bool ward = player && PrivateArea.CheckAccess(container.transform.position, 0f, flash: false);
            bool baseAllowed = TransferGesturePolicy.MayTransfer(
                FeatureOn(),
                container,
                container && container.IsOwner(),
                permission,
                ward,
                item != null,
                item != null && item.m_shared.m_questItem,
                ValheimAccess.HasDrag(gui),
                itemProtectionAllowsTransfer: true);

            TransferItemProtection protection = TransferItemProtection.NotApplicable;
            if (baseAllowed)
            {
                protection = ItemProtectionQueryAdapter.Resolve(item);
            }

            bool allowed = baseAllowed && ItemProtectionQueryAdapter.AllowsTransfer(protection);
            if (allowed) return true;

            if (protection == TransferItemProtection.Unknown && !_lockWarning)
            {
                _lockWarning = true;
                Diagnostics.Warn(
                    "A transfer was denied because inventory.item-locks was advertised but its " +
                    "typed protection query was missing, incompatible, indeterminate, or faulted.");
            }
            if (notify && player)
            {
                if (!ward) player.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                else if (!permission) player.Message(MessageHud.MessageType.Center, "$msg_cantopen");
                else if (!ItemProtectionQueryAdapter.AllowsTransfer(protection))
                    player.Message(MessageHud.MessageType.Center, "$msg_blocked");
            }
            return false;
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.TransferGestures.Value;
    }
}
