#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(Container))]
public class AccessPrivateChests
{
  [HarmonyPatch(nameof(Container.CheckAccess)), HarmonyPostfix]
  static bool CheckAccess(bool result) => result || Settings.AccessPrivateChests;


  [HarmonyPatch(nameof(Container.RPC_OpenResponse)), HarmonyPrefix]
  static void RPC_OpenResponse(ref bool granted) => granted |= Settings.AccessPrivateChests;
}

