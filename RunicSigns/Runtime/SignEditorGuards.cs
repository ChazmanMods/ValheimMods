using HarmonyLib;
using UnityEngine;
namespace RunicSigns.Runtime;
[HarmonyPatch(typeof(ZInput), "GetKeyDown", typeof(KeyCode), typeof(bool))]
internal static class SignEscapeGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(KeyCode key, ref bool __result)
    { if (key == KeyCode.Escape && SignEditor.SuppressCancel) __result = false; }
}
[HarmonyPatch(typeof(ZInput), "GetButtonDown", typeof(string))]
internal static class SignControllerCancelGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(string name, ref bool __result)
    { if (name == "JoyButtonB" && SignEditor.SuppressCancel) __result = false; }
}

[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
internal static class SignCursorGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix() => SignEditor.KeepCursor();
}

[HarmonyPatch]
internal static class SignClosingActionGuard
{
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "GetButton", "GetButtonDown", "GetButtonUp" })
            yield return AccessTools.Method(typeof(ZInput), name, new[] { typeof(string) });
    }
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(string name, ref bool __result)
    { if (SignEditor.SuppressClosingAction(name)) __result = false; }
}
