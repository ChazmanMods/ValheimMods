#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
namespace RunicSentinel.Devcommands;
[HarmonyPatch(typeof(Player), nameof(Player.Update))]
public class DisableDebugModeKeys
{
  private static bool DebugKeysEnabled()=>Player.m_debugMode&&!Settings.DisableDebugModeKeys;
  static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
  {
    return new CodeMatcher(instructions)
         .MatchStartForward(new CodeMatch(OpCodes.Ldsfld, typeof(Player).GetField(nameof(Player.m_debugMode),System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance)))
         .SetAndAdvance( // Replace the debugmode check with a custome one.
              OpCodes.Call, Transpilers.EmitDelegate(DebugKeysEnabled).operand)
         .InstructionEnumeration();
  }
}
