using System;
using System.Linq;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;
namespace RunicPaths.Smoke;
public sealed partial class Smoke
{
    private void SeedOldSeam(TerrainComp compiler)
    {
        var assembly=typeof(Plugin).Assembly;
        var stateType=assembly.GetType("RunicPaths.TerrainState");
        var journalType=assembly.GetType("RunicPaths.TerrainJournal");
        var baseline=AccessTools.Constructor(stateType,new[]{typeof(TerrainComp)}).Invoke(new object[]{compiler});
        var edited=AccessTools.Constructor(stateType,new[]{stateType}).Invoke(new[]{baseline});
        int index=32*65+64;
        ((float[])AccessTools.Field(stateType,"Level").GetValue(edited))[index]=1;
        ((bool[])AccessTools.Field(stateType,"Changed").GetValue(edited))[index]=true;
        var journal=AccessTools.Constructor(journalType,Type.EmptyTypes).Invoke(Array.Empty<object>());
        AccessTools.Method(journalType,"Record").Invoke(journal,new[]{(object)index,baseline,edited,false,true});
        AccessTools.Method(stateType,"Commit").Invoke(edited,new object[]{compiler});
        compiler.GetComponent<ZNetView>().GetZDO().Set("RunicPaths.TerrainJournal.v1",(byte[])AccessTools.Method(journalType,"Serialize").Invoke(journal,null));
        AccessTools.Method(typeof(TerrainComp),"Save",new[]{typeof(bool)}).Invoke(compiler,new object[]{false});
    }
    private void CheckSeamsAndStructures(Heightmap map,float[] heights)
    {
        int pitch=map.m_width+1;
        for(int z=0;z<pitch;z++) for(int x=0;x<pitch;x++)
            if((x==0 || z==0 || x==pitch-1 || z==pitch-1) && Math.Abs(heights[z*pitch+x])>0.0001f)
                throw new Exception("Road changed shared-edge height while neighbor may be pending");
        var neighbor=Heightmap.FindHeightmap(map.transform.position+new Vector3(map.m_width*map.m_scale,0,0));
        if(!neighbor) throw new Exception("Missing neighbor for native seam test");
        var getHeight=AccessTools.Method(typeof(Heightmap),"GetHeight",new[]{typeof(int),typeof(int)});
        for(int z=0;z<pitch;z++)
        {
            float a=(float)getHeight.Invoke(map,new object[]{map.m_width,z})+map.transform.position.y;
            float b=(float)getHeight.Invoke(neighbor,new object[]{0,z})+neighbor.transform.position.y;
            if(Math.Abs(a-b)>0.002f) throw new Exception($"Native mesh seam mismatch at {z}: {a} vs {b}");
        }
        var prefab=ZNetScene.instance.GetPrefab("woodwall");if(!prefab) prefab=ZNetScene.instance.GetPrefab("wood_wall");
        var wall=Instantiate(prefab,new Vector3(target.X,WorldGenerator.instance.GetHeight(target.X,target.Z),target.Z),Quaternion.identity);
        try
        {
            var bounds=wall.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger && c.enabled).Select(c=>c.bounds).ToArray();
            if(bounds.Length==0) throw new Exception("Missing wall collider");
            var zone=new ZonePaths{Width=8,BuildBuffer=12};zone.Exclusions.Add(new Exclusion{Center=target,Radius=40});
            var type=typeof(Plugin).Assembly.GetType("RunicPaths.TerrainPainter");
            var painter=Activator.CreateInstance(type,new object[]{map,zone});
            AccessTools.Method(type,"RefreshProtection").Invoke(painter,null);
            var check=AccessTools.Method(type,"Protected");
            float maxX=bounds.Max(b=>b.max.x);float z=wall.transform.position.z;
            if(!(bool)check.Invoke(painter,new object[]{new Point(maxX+1.9f,z)})) throw new Exception("Missing 2 m structure protection");
            if((bool)check.Invoke(painter,new object[]{new Point(maxX+2.1f,z)})) throw new Exception("Broad POI circle still blocks open ground");
        }
        finally {ZNetScene.instance.Destroy(wall);}
        Logger.LogInfo("PASS native shared-edge heights match pending neighbor; generated wall protected to 2 m without broad POI halo.");
    }
}
