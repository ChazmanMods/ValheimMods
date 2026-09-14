using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RunicWorldEngine.Core
{
    internal static class CapacityRewrite
    {
        internal static List<CodeInstruction> Apply(IEnumerable<CodeInstruction> instructions, CapacityAudit.Site site, int players, bool dedicated)
        {
            var il = instructions.ToList();
            var matches = new List<int>();
            for (int i = 0; i < il.Count; i++)
            {
                int adjacent = i + (site.Previous ? -1 : 1);
                if (!il[i].LoadsConstant(site.Baseline) || adjacent < 0 || adjacent >= il.Count) continue;
                if (il[adjacent].operand is MemberInfo member && member.Name == site.Member && member.DeclaringType.FullName == site.MemberType)
                    matches.Add(i);
            }
            if (matches.Count != 1) throw new InvalidOperationException("Capacity transpiler expected one replacement: " + site.Type + "." + site.Method);
            int limit = CapacityAudit.Limit(players, dedicated, site.Transport);
            // Mutate only after full validation. Preserve labels and exception boundaries on the instruction.
            il[matches[0]].opcode = OpCodes.Ldc_I4;
            il[matches[0]].operand = limit;
            return il;
        }
    }
}
