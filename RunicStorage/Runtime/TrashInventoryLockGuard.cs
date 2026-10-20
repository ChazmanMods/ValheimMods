using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicStorage.Runtime;

// HarmonyX can run another prefix even after our grid prefix returns false.
// Guard the actual lock operation, not just the shared grid event's original body.
internal static class TrashInventoryLockGuard
{
    private static MethodBase target;
    private static float retry;
    private static int assemblyCount = -1;
    internal static bool EnsureInstalled()
    {
        if (target != null) return true;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (assemblies.Length == assemblyCount && Time.unscaledTime < retry) return false;
        assemblyCount = assemblies.Length; retry = Time.unscaledTime + 5;
        foreach (var assembly in assemblies)
        {
            var type = assembly.GetType("RunicInventory.Integration.InventoryRuntime", false);
            if (type == null) continue;
            var method = type.GetMethod("TryTogglePointerLock", BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance, null, new[] { typeof(InventoryGrid), typeof(UIInputHandler) }, null);
            if (method == null) return false;
            new Harmony(Plugin.Guid).Patch(method, prefix: new HarmonyMethod(typeof(TrashInventoryLockGuard), nameof(Prefix)));
            target = method; return true;
        }
        return false;
    }
    internal static void ResetDiscovery() { target = null; retry = 0; assemblyCount = -1; }
    private static bool Prefix(InventoryGrid __0, ref bool __result)
    {
        if (!(InventoryTrash.Active?.OwnsTrashChord(__0) ?? false)) return true;
        __result = true; // The chord is handled; do not toggle any existing lock.
        return false;
    }
}
