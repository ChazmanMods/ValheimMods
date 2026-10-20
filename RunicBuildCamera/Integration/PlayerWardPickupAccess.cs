using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicBuildCamera.Integration
{
    internal static class PlayerWardPickupAccess
    {
        private static readonly FieldInfo AreasField =
            AccessTools.Field(typeof(PrivateArea), "m_allAreas");
        private static readonly Func<PrivateArea, bool> Enabled =
            AccessTools.MethodDelegate<Func<PrivateArea, bool>>(
                AccessTools.Method(typeof(PrivateArea), "IsEnabled"));
        private static readonly Func<PrivateArea, Vector3, float, bool> Inside =
            AccessTools.MethodDelegate<Func<PrivateArea, Vector3, float, bool>>(
                AccessTools.Method(typeof(PrivateArea), "IsInside"));
        private static readonly Func<PrivateArea, bool> LocalAccess =
            AccessTools.MethodDelegate<Func<PrivateArea, bool>>(
                AccessTools.Method(typeof(PrivateArea), "HaveLocalAccess"));

        // Do not use CheckAccess: it also includes game-created areas. Every overlapping
        // player ward must permit the local player, just like the strict native ward check.
        internal static bool Allows(Vector3 target)
        {
            var areas = AreasField?.GetValue(null) as List<PrivateArea>;
            if (areas == null)
                throw new InvalidOperationException("Cannot read the player ward registry.");
            foreach (PrivateArea area in areas)
            {
                if (!area) continue;
                Piece piece = area.GetComponent<Piece>();
                if (!piece || !piece.IsPlacedByPlayer()) continue;
                if (Enabled(area) && Inside(area, target, 0f) && !LocalAccess(area))
                    return false;
            }
            return true;
        }
    }
}
