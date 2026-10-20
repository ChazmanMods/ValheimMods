using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace RunicStorage.Runtime;

// Optional slot integrations are discovered when the action runs. They neither impose
// a plugin load order nor turn a hidden equipment/quick-slot row into ordinary storage.
internal static class StoragePlayerProtection
{
    private static int assemblyCount = -1;
    private static MethodInfo slotCell, visibleRows, fullHeight;
    private static bool warned;

    internal static bool IsReserved(Player player, ItemDrop.ItemData item)
    {
        if (player == null || item == null) return false;
        var inventory = player.GetInventory();
        if (inventory == null || !inventory.GetAllItems().Exists(candidate => ReferenceEquals(candidate, item))) return false;
        return IsReservedSlot(player, item.m_gridPos.x, item.m_gridPos.y);
    }

    internal static bool IsReservedSlot(Player player, int x, int row)
    {
        if (player == null) return false;
        var inventory = player.GetInventory();
        if (inventory == null) return false;
        var data = player.m_customData;
        if (data != null)
        {
            bool quiver = TryRow(data, "runic.inventory.betterarchery.v1", out int nativeRows);
            if (quiver && (row == nativeRows || row == nativeRows + 1)) return true;
            if (TryRow(data, "runic.inventory.extra-row.v1", out int roleRow) &&
                row == roleRow + (quiver && nativeRows == roleRow ? 2 : 0)) return true;
        }
        RefreshOptionalApi();
        if (slotCell == null && visibleRows == null) return false;
        try
        {
            if (slotCell != null)
            {
                object[] args = { x, row, null };
                if (slotCell.Invoke(null, args) is bool reserved && reserved) return true;
            }
            if (visibleRows != null && fullHeight != null &&
                visibleRows.Invoke(null, null) is int visible && fullHeight.Invoke(null, null) is int full &&
                visible > 0 && full > visible && full <= inventory.GetHeight() && row >= visible && row < full) return true;
        }
        catch (Exception error)
        {
            if (!warned)
            {
                warned = true;
                Plugin.Log?.LogWarning("Optional quick-slot protection is unavailable; preserving those carried items: " + error.GetBaseException().Message);
            }
            return true;
        }
        return false;
    }

    private static bool TryRow(System.Collections.Generic.Dictionary<string, string> data, string key, out int row)
    {
        row = -1;
        return data.TryGetValue(key, out string text) && int.TryParse(text, NumberStyles.None,
            CultureInfo.InvariantCulture, out row) && row >= 4 && row <= 9;
    }

    private static void RefreshOptionalApi()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (assemblies.Length == assemblyCount) return;
        assemblyCount = assemblies.Length; slotCell = null; visibleRows = null; fullHeight = null;
        foreach (var assembly in assemblies)
        {
            var api = assembly.GetType("EquipmentAndQuickSlots.API", false);
            if (api == null) continue;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            slotCell = api.GetMethod("IsSlotCell", flags, null, new[] { typeof(int), typeof(int), typeof(string).MakeByRefType() }, null);
            visibleRows = api.GetMethod("GetVisibleRows", flags, null, Type.EmptyTypes, null);
            fullHeight = api.GetMethod("GetFullHeight", flags, null, Type.EmptyTypes, null);
            break;
        }
    }
}
