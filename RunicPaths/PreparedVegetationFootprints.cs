using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RunicPaths;

// Reject a natural spawn whose trunk/rock footprint intersects the road, even
// when its origin is outside the paint. This avoids a later destruction sweep.
[HarmonyPatch(typeof(ZoneSystem),"PlaceVegetation")]
internal static class PreparedVegetationFootprints
{
    private static readonly Dictionary<GameObject,float> radii=new Dictionary<GameObject,float>();
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var code=instructions.ToList();
        var captured=code.Select(c=>c.operand).OfType<FieldInfo>().FirstOrDefault(f=>f.FieldType==typeof(ZoneSystem.ZoneVegetation));
        var locals=__originalMethod.GetMethodBody()!.LocalVariables.Where(v=>v.LocalType==(captured?.DeclaringType??typeof(ZoneSystem.ZoneVegetation))).ToArray();
        if(locals.Length!=1) throw new InvalidOperationException("Vegetation locals changed; footprint hook cannot be applied safely");
        int index=locals[0].LocalIndex;
        int count=0;
        var inside=AccessTools.Method(typeof(ZoneSystem),"InsideClearArea");
        for(int i=0;i<code.Count;i++)
        {
            yield return code[i];
            if(!code[i].Calls(inside)) continue;
            if(i==0 || !code[i-1].opcode.Name.StartsWith("ldloc",StringComparison.Ordinal) || code[i-1].opcode.Name.StartsWith("ldloca",StringComparison.Ordinal))
                throw new InvalidOperationException("Vegetation candidate load changed");
            yield return new CodeInstruction(code[i-1].opcode,code[i-1].operand);
            yield return new CodeInstruction(OpCodes.Ldloc,index);
            if(captured!=null) yield return new CodeInstruction(OpCodes.Ldfld,captured);
            yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PreparedVegetationFootprints),nameof(Filter)));count++;
        }
        if(count!=1) throw new InvalidOperationException("Vegetation clearance call changed");
    }
    private static bool Filter(bool blocked,Vector3 point,ZoneSystem.ZoneVegetation vegetation)
    {
        if(blocked || PreparedRuntime.Active==null || !vegetation.m_prefab) return blocked;
        if(!radii.TryGetValue(vegetation.m_prefab,out float radius))
        {radius=RoadObjects.Movable(vegetation.m_prefab)?PreparedObjects.PrefabRadius(vegetation.m_prefab):0;radii[vegetation.m_prefab]=radius;}
        return radius>0 && PreparedRuntime.Active.Cleared(GameTerrain.PointOf(point),radius*Math.Max(vegetation.m_scaleMin,vegetation.m_scaleMax)+.5f);
    }
}
