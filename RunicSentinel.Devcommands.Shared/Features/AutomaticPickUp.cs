#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
public class AutomaticPickUp
{
  static void Postfix() => Player.m_enableAutoPickup = Settings.AutomaticItemPickUp;
}
