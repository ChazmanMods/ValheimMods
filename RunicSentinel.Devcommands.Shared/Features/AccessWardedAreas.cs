#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.HaveLocalAccess))]
public class AccessWardedAreas {
  static void Postfix(ref bool __result) {
    __result |= Settings.AccessWardedAreas;
  }
}

