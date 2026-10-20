using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ArcaneWaterWorks;

// Ship averages five probes, including probes inside the bank. Returning sea level
// at the channel boundary creates a fictitious wave and a large balancing torque.
// Continue the surface UNDER the bank for ship physics only, as native ocean water
// does. Rendering, swimming, liquid triggers and ordinary collision damage are unchanged.
[HarmonyPatch(typeof(Ship),nameof(Ship.CustomFixedUpdate))]
internal static class RiverShipWater
{
    internal static float Level(Vector3 point,ref WaterVolume previous){
        float level=Floating.GetWaterLevel(point,ref previous),bankLevel=level,wetLevel=level;bool inRiver=false;
        foreach(var spring in Spring.Active){
            if(!spring||!spring.Live||spring.Route==null)continue;
            var route=spring.BankRoute??spring.Route;
            var local=point-spring.transform.position;
            float radius=route.Width*.5f,reach=radius+16;
            // Find the closest point on the finite channel, including branch ends.
            // Route's terrain index deliberately excludes this dry-bank apron.
            float best=reach*reach;Vector2 closest=default;
            for(int i=1;i<route.Points.Count;i++){
                var a=route.Points[route.Parent(i)];var b=route.Points[i];
                if(local.x<Mathf.Min(a.X,b.X)-reach||local.x>Mathf.Max(a.X,b.X)+reach||
                   local.z<Mathf.Min(a.Z,b.Z)-reach||local.z>Mathf.Max(a.Z,b.Z)+reach)continue;
                var start=new Vector2(a.X,a.Z);var delta=new Vector2(b.X-a.X,b.Z-a.Z);
                var on=start+delta*Mathf.Clamp01(Vector2.Dot(new Vector2(local.x,local.z)-start,delta)/delta.sqrMagnitude);
                float distance=(new Vector2(local.x,local.z)-on).sqrMagnitude;
                if(distance<best){best=distance;closest=on;}
            }
            if(best>=reach*reach)continue;
            float distanceToPath=Mathf.Sqrt(best);
            if(distanceToPath<=radius){
                spring.Surface(local.x,local.z,out _,out float wetMean,out _);
                wetLevel=Mathf.Max(wetLevel,RiverSurface.Level(spring,point,wetMean+spring.transform.position.y));
                inRiver=true;continue;
            }
            var sample=new Vector2(local.x,local.z);
            if(distanceToPath>radius)sample=closest+(sample-closest)*(radius/distanceToPath);
            spring.Surface(sample.x,sample.y,out _,out float mean,out _);
            var waterPoint=spring.transform.position+new Vector3(sample.x,mean,sample.y);
            float surface=RiverSurface.Level(spring,waterPoint,waterPoint.y);
            float outside=Mathf.Max(0,distanceToPath-radius);
            // A four-metre support apron covers hull probes at a bank. Then descend
            // continuously below the bank to sea level; never extend a navigable river.
            float sea=ZoneSystem.instance?ZoneSystem.instance.m_waterLevel:30;
            surface=Mathf.Lerp(surface,Mathf.Min(surface,sea),Mathf.SmoothStep(0,1,(outside-4)/12));
            bankLevel=Mathf.Max(bankLevel,surface);
        }
        // A nearby higher branch's dry apron must not override actual water in a
        // lower branch. Pond interiors likewise retain their real receiving level.
        if(inRiver||!float.IsNegativeInfinity(Pools.GardenIntegration.WaterLevel(point)))return wetLevel;
        return bankLevel;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions){
        var original=AccessTools.Method(typeof(Floating),nameof(Floating.GetWaterLevel));
        var replacement=AccessTools.Method(typeof(RiverShipWater),nameof(Level));
        int count=0;
        foreach(var instruction in instructions){
            if(instruction.Calls(original)){instruction.opcode=OpCodes.Call;instruction.operand=replacement;count++;}
            yield return instruction;
        }
        if(count!=5)throw new InvalidOperationException("Expected five Ship water probes, found "+count);
    }
}
