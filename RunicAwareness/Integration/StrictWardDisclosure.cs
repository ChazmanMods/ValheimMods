using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RunicAwareness.Integration
{
    internal static class StrictWardDisclosure
    {
        internal const int MaximumWardAreasExamined = 4096;

        private delegate bool WardStateDelegate(PrivateArea area);
        private delegate bool WardContainsDelegate(PrivateArea area, Vector3 point, float radius);

        private static readonly List<PrivateArea> WardAreas = ResolveWardAreas();
        private static readonly WardStateDelegate WardEnabled = ResolveWardState("IsEnabled");
        private static readonly WardStateDelegate WardLocalAccess =
            ResolveWardState("HaveLocalAccess");
        private static readonly WardContainsDelegate WardContains = ResolveWardContains();

        internal static bool IsSupported =>
            WardAreas != null && WardEnabled != null && WardLocalAccess != null &&
            WardContains != null;

        internal static bool Allows(Vector3 position)
        {
            if (!IsSupported || !IsFinite(position)) return false;
            try
            {
                int count = WardAreas.Count;
                if (count > MaximumWardAreasExamined) return false;
                for (int index = 0; index < count; index++)
                {
                    PrivateArea area = WardAreas[index];
                    if (area == null) continue;
                    bool enabled = WardEnabled(area);
                    bool contains = enabled && WardContains(area, position, 0f);
                    bool localAccess = !contains || WardLocalAccess(area);
                    if (IsHostileOverlap(enabled, contains, localAccess)) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsHostileOverlap(
            bool enabled,
            bool containsTarget,
            bool localAccess) =>
            enabled && containsTarget && !localAccess;

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static List<PrivateArea> ResolveWardAreas()
        {
            try
            {
                FieldInfo field = typeof(PrivateArea).GetField(
                    "m_allAreas",
                    BindingFlags.Static | BindingFlags.NonPublic);
                return field != null && field.FieldType == typeof(List<PrivateArea>)
                    ? field.GetValue(null) as List<PrivateArea>
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static WardStateDelegate ResolveWardState(string name)
        {
            try
            {
                MethodInfo method = typeof(PrivateArea).GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                return method != null && method.ReturnType == typeof(bool) && !method.IsStatic
                    ? (WardStateDelegate)method.CreateDelegate(typeof(WardStateDelegate))
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static WardContainsDelegate ResolveWardContains()
        {
            try
            {
                MethodInfo method = typeof(PrivateArea).GetMethod(
                    "IsInside",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Vector3), typeof(float) },
                    null);
                return method != null && method.ReturnType == typeof(bool) && !method.IsStatic
                    ? (WardContainsDelegate)method.CreateDelegate(typeof(WardContainsDelegate))
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
