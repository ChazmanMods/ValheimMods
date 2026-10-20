using System;
using System.Collections.Generic;
using HarmonyLib;
using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;

[HarmonyPatch(typeof(Heightmap),"ApplyModifiers")]
internal static class HotSpringBank
{
    [HarmonyPostfix,HarmonyPriority(Priority.Low)]
    static void Postfix(Heightmap __instance,List<float> ___m_heights){
        if(__instance.IsDistantLod||HotSpring.All.Count==0)return;
        int n=__instance.m_width;float step=__instance.m_scale,half=n*step*.5f;var map=__instance.transform.position;
        foreach(var hot in HotSpring.All){
            if(!hot||!hot.Live||hot.Garden==null)continue;
            var center=hot.transform.position;float radius=hot.Garden.Width*.5f,reach=radius+3f;
            if(Mathf.Abs(map.x-center.x)>half+reach||Mathf.Abs(map.z-center.z)>half+reach)continue;
            int x0=Mathf.Clamp(Mathf.FloorToInt((center.x-reach-map.x+half)/step),0,n),x1=Mathf.Clamp(Mathf.CeilToInt((center.x+reach-map.x+half)/step),0,n);
            int z0=Mathf.Clamp(Mathf.FloorToInt((center.z-reach-map.z+half)/step),0,n),z1=Mathf.Clamp(Mathf.CeilToInt((center.z+reach-map.z+half)/step),0,n);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                var point=new Vector3(map.x+x*step-half,hot.Water,map.z+z*step-half);
                float distance=GardenIntegration.Distance(hot.Pond,point),edge=(distance-1)*radius;
                float lift=(float)HotSpringBankProfile.Height(edge);if(lift<=0||distance<.001f)continue;
                if(distance>1.0001f&&(!float.IsNegativeInfinity(GardenIntegration.WaterLevel(point))||!float.IsNegativeInfinity(GardenBridge.Water(point))))continue;
                // Leave shared water open where a pool or river meets this shoreline.
                var delta=point-center;delta.y=0;var outside=center+delta*((1+.12f/radius)/distance);outside.y=hot.Water;
                if(!float.IsNegativeInfinity(GardenIntegration.WaterLevel(outside))||!float.IsNegativeInfinity(GardenBridge.Water(outside)))continue;
                int index=z*(n+1)+x;
                // Absolute lower bound, rather than cumulative raising on each rebuild.
                ___m_heights[index]=(float)HotSpringBankProfile.Apply(___m_heights[index]+map.y,hot.Water,edge)-map.y;
            }
        }
    }
}

