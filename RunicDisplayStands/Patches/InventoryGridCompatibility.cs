using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace RunicDisplayStands
{
    /// <summary>
    /// Some inventory utility mods calculate a hovered cell using the player's larger
    /// inventory dimensions and then ask the currently visible compact stand grid for
    /// that cell. Vanilla InventoryGrid.GetElement assumes the requested index is valid.
    /// Returning null for an out-of-range request matches the meaning of "no hovered
    /// element" and prevents those compatibility patches from throwing every frame.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "GetElement")]
    [HarmonyPriority(Priority.First)]
    internal static class InventoryGrid_GetElement_Bounds_Patch
    {
        private static readonly FieldInfo ElementsField =
            AccessTools.Field(typeof(InventoryGrid), "m_elements");

        private static bool Prefix(
            int x,
            int y,
            int width,
            InventoryGrid __instance,
            ref InventoryElement __result)
        {
            var elements = ElementsField?.GetValue(__instance) as IList;
            if (elements == null) return true;

            long index = (long)y * width + x;
            if (x >= 0 && x < width && y >= 0 && width > 0 && index >= 0 && index < elements.Count)
                return true;

            __result = null;
            return false;
        }
    }
}
