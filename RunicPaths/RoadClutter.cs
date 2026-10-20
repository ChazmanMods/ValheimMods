using System;
using HarmonyLib;
using UnityEngine;
namespace RunicPaths;

[HarmonyPatch(typeof(ClutterSystem),"GenerateVegPatch")]
internal static class RoadClutterScope
{
    internal static int Depth;
    private static void Prefix() {Depth++;}
    private static Exception? Finalizer(Exception? __exception) {Depth=Math.Max(0,Depth-1);return __exception;}
}

[HarmonyPatch(typeof(ClutterSystem),nameof(ClutterSystem.GetGroundInfo))]
internal static class RoadClutterFilter
{
    private static void Postfix(Vector3 point, Heightmap hmap, ref bool __result)
    {
        if(__result && RoadClutterScope.Depth>0 && Plugin.SuppressRoadClutter(point,hmap)) __result=false;
    }
}
