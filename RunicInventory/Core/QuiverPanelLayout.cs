using System;

namespace RunicInventory.Core
{
    // Display coordinates only. Native rows and item coordinates are never changed.
    internal static class QuiverPanelLayout
    {
        internal static bool TryPlan(int nativeRows, float pitch, float originY, float? quiverY,
            bool compact, out float equipmentY, out float removedHeight)
        {
            equipmentY = removedHeight = 0f;
            if (nativeRows < 4 || nativeRows > 9 || !Finite(pitch) || pitch <= 0f || pitch > 2048f ||
                !Finite(originY) || quiverY.HasValue && !Finite(quiverY.Value)) return false;
            float originalY = originY - (nativeRows + 2) * pitch;
            equipmentY = originalY;
            if (!compact) return true;
            // Leave room above role labels, and never overlap either backpack or quiver cells.
            float gap = Math.Min(12f, pitch * 0.2f);
            float targetY = originY - nativeRows * pitch - gap;
            if (quiverY.HasValue) targetY = Math.Min(targetY, quiverY.Value - pitch - gap);
            equipmentY = Math.Max(originalY, Math.Min(targetY, originY - nativeRows * pitch));
            removedHeight = equipmentY - originalY;
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
