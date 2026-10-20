using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace RunicCrafting.Integration
{
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class AuthorityBuildingPlacementPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance) => AuthorityBuildingExecution.NativePlacementEntered(__instance);
        private static void Postfix(Player __instance, bool __result) => AuthorityBuildingExecution.NativePlacementReturned(__instance, __result);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            var updateGhost = AccessTools.Method(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) });
            int found = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(updateGhost)) continue;
                if (found >= 0) throw new InvalidOperationException("TryPlacePiece contains multiple placement-ghost updates");
                found = i;
            }
            if (found < 0 || found + 1 >= codes.Count) throw new MissingMethodException("TryPlacePiece placement-ghost update boundary is unavailable");
            var proceed = generator.DefineLabel();
            // Consume one resumed click after native/modded ghost updates. Only a changed
            // build context cancels it; native placement checks validate the live target.
            // BepInEx's Harmony targets Mono mscorlib; use its non-generic collection here
            // to avoid the netstandard reference facade assigning Label a different identity.
            ((System.Collections.IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(codes[found + 1])).Add(proceed);
            codes.InsertRange(found + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AuthorityBuildingExecution), nameof(AuthorityBuildingExecution.ResumeAfterGhostUpdate))),
                new CodeInstruction(OpCodes.Brtrue, proceed), new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Ret)
            });
            return codes;
        }
    }
}
