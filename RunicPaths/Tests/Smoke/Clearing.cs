using System;
using HarmonyLib;
using UnityEngine;
namespace RunicPaths.Smoke;
public sealed partial class Smoke
{
    private void CheckRoadClutter(Heightmap map,bool[] flags)
    {
        var method=AccessTools.Method(typeof(Plugin),"SuppressRoadClutter");
        var assembly=typeof(Plugin).Assembly;
        var depth=AccessTools.Field(assembly.GetType("RunicPaths.RoadClutterScope"),"Depth");
        var filter=AccessTools.Method(assembly.GetType("RunicPaths.RoadClutterFilter"),"Postfix");
        int pitch=map.m_width+1;
        for(int i=0;i<flags.Length;i++)
        {
            Vector3 point=map.transform.position+new Vector3((i%pitch-map.m_width/2)*map.m_scale+0.5f,0,(i/pitch-map.m_width/2)*map.m_scale+0.5f);
            if(!flags[i] || !(bool)method.Invoke(null,new object[]{point,map})) continue;
            object[] args={point,map,true};depth.SetValue(null,1);
            try {filter.Invoke(null,args);} finally {depth.SetValue(null,0);}
            if((bool)args[2]) throw new Exception("Road decorative clutter not rejected");
            args[2]=true;filter.Invoke(null,args);
            if(!(bool)args[2]) throw new Exception("Clutter patch affected non-generation ground queries");
            Logger.LogInfo("PASS native road clutter predicate and generation-only filter.");return;
        }
        throw new Exception("No painted road cell qualified for clutter suppression");
    }
}
