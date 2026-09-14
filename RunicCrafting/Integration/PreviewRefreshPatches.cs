using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RunicCrafting.Integration
{
    [HarmonyPatch]
    internal static class PreviewRefreshPatches
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new (Type Type, string Name, Type[] Arguments)[]
            {
                (typeof(Player), "UpdateAvailablePiecesList", Type.EmptyTypes),
                (typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) }),
                (typeof(PieceTable), "UpdateAvailable", new[] { typeof(HashSet<string>), typeof(Player), typeof(bool), typeof(bool) }),
                (typeof(Hud), "UpdateBuild", new[] { typeof(Player), typeof(bool) }),
                (typeof(Hud), "UpdatePieceBuildStatus", new[] { typeof(List<Piece>), typeof(Player) }),
                (typeof(Hud), "UpdatePieceBuildStatusAll", new[] { typeof(List<Piece>), typeof(Player) }),
                (typeof(Hud), "SetupPieceInfo", new[] { typeof(Piece) }),
                (typeof(InventoryGui), "UpdateCraftingPanel", new[] { typeof(bool) }),
                (typeof(InventoryGui), "UpdateRecipeList", new[] { typeof(List<Recipe>) }),
                (typeof(InventoryGui), "UpdateRecipe", new[] { typeof(Player), typeof(float) }),
                (typeof(InventoryGui), "SetupRequirementList", new[] { typeof(int), typeof(Player), typeof(bool), typeof(int) })
            };
            foreach (var target in targets)
            {
                MethodInfo method = AccessTools.Method(target.Type, target.Name, target.Arguments);
                if (method != null) yield return method;
                else Plugin.Log?.LogWarning("Preview refresh optimization unavailable for " + target.Type.Name + "." + target.Name + ".");
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state) => __state = PreviewRefreshRuntime.Begin();

        // Finalizer executes even when another patch skips or throws during a refresh.
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(long __state) => PreviewRefreshRuntime.End(__state);
    }

    [HarmonyPatch(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) })]
    internal static class PreviewInventoryChangedPatch
    {
        private static void Prefix(Inventory __instance, out bool __state)
        {
            __state = !ReferenceEquals(__instance, PreviewRefreshRuntime.LoadingPreview);
            if (__state)
            {
                PreviewRefreshRuntime.Invalidate();
                UiPreviewCache.Changed(__instance);
            }
        }
        private static void Finalizer(Inventory __instance, bool __state)
        {
            if (__state)
            {
                PreviewRefreshRuntime.Invalidate();
                UiPreviewCache.Changed(__instance);
            }
        }
    }

    [HarmonyPatch]
    internal static class PreviewWorldChangedPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new (Type Type, string Name, Type[] Arguments)[]
            {
                (typeof(ZDO), "IncreaseDataRevision", Type.EmptyTypes),
                (typeof(ZDO), "IncreaseOwnerRevision", Type.EmptyTypes),
                (typeof(ZDO), "set_DataRevision", new[] { typeof(uint) }),
                (typeof(ZDO), "set_OwnerRevision", new[] { typeof(ushort) }),
                (typeof(ZDO), "Deserialize", new[] { typeof(ZPackage) }),
                (typeof(PrivateArea), "Awake", Type.EmptyTypes),
                (typeof(PrivateArea), "OnDestroy", Type.EmptyTypes)
            };
            foreach (var target in targets)
            {
                MethodInfo method = AccessTools.Method(target.Type, target.Name, target.Arguments);
                if (method != null) yield return method;
                else
                {
                    PreviewRefreshRuntime.Supported = false;
                    Plugin.Log?.LogWarning("Per-refresh caching disabled: missing invalidation hook " + target.Type.Name + "." + target.Name + ".");
                }
            }
        }
        private static void Prefix(object __instance) => Changed(__instance);
        private static void Finalizer(object __instance) => Changed(__instance);
        private static void Changed(object instance)
        {
            PreviewRefreshRuntime.Invalidate();
            if (instance is ZDO) UiPreviewCache.Changed(instance);
            else UiPreviewCache.Invalidate();
        }
    }

    [HarmonyPatch]
    internal static class PreviewActionBoundaryPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) });
            yield return AccessTools.Method(typeof(Player), "TryPlacePiece");
        }
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out bool __state)
        {
            __state = true;
            PreviewRefreshRuntime.EnterAction();
        }
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(bool __state)
        { if (__state) PreviewRefreshRuntime.ExitAction(); }
    }
}
