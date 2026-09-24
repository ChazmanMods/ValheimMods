using HarmonyLib;
namespace RunicAutomation
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class RegisterContainer
    {
        private static void Postfix(Container __instance) => ContainerAuthority.Register(__instance);
    }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class EndWorld
    {
        private static void Postfix() { ContainerAuthority.Clear(); MutationGate.EndSession(); }
    }
}
