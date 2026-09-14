using HarmonyLib;
using UnityEngine;

namespace RunicExploration.Integration
{
    [HarmonyPatch(typeof(Minimap), "InTextInput", new System.Type[] { })]
    internal static class MinimapTextInputPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance?.Runtime?.BlocksMapInput ?? false) __result = true;
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapLeftDown", typeof(UIInputHandler))]
    internal static class MinimapLeftDownPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !BlocksPointer();

        private static bool BlocksPointer() =>
            Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false;

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapLeftUp", typeof(UIInputHandler))]
    internal static class MinimapLeftUpPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() =>
            !(Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false);

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick", new System.Type[] { })]
    internal static class MinimapLeftClickPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() =>
            !(Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false);

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapDblClick", new System.Type[] { })]
    internal static class MinimapDoubleClickPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() =>
            !(Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false);

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapMiddleClick", typeof(UIInputHandler))]
    internal static class MinimapMiddleClickPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() =>
            !(Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false);

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer", new System.Type[] { })]
    internal static class MinimapRemovePinUnderPointerPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() =>
            !(Plugin.Instance?.Runtime?.BlocksMapPointer(CurrentGuiPointer()) ?? false);

        private static Vector2 CurrentGuiPointer()
        {
            Vector3 position = Input.mousePosition;
            return new Vector2(position.x, Screen.height - position.y);
        }
    }
}
