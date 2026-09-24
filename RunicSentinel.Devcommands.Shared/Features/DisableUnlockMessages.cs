#nullable enable
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(MessageHud), nameof(MessageHud.QueueUnlockMsg))]
public class DisableUnlockMessages {
  static bool Prefix() {
    return !Settings.DisableUnlockMessages;
  }
}
