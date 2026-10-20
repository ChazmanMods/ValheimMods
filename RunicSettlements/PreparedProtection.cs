using System;
using System.Collections.Generic;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;

namespace RunicSettlements;

internal static class PreparedProtection
{
    internal static List<Exclusion> Capture(float playerBuffer,Func<ZDO,bool>? ignore=null)
    {
        var result=new List<Exclusion>();
        foreach(int _ in CaptureSteps(result,playerBuffer,ignore)) {}
        return result;
    }
    internal static IEnumerable<int> CaptureSteps(List<Exclusion> result,float playerBuffer,Func<ZDO,bool>? ignore=null)
    {
        var objects=(Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance);
        var templates=new Dictionary<int,List<Bounds>>();
        // Snapshot before yielding: networking can mutate the live dictionary between frames.
        var records=new List<ZDO>(objects.Values);
        foreach(var zdo in records)
        {
            yield return 0;
            if(ignore!=null && ignore(zdo)) continue;
            int hash=zdo.GetPrefab();
            if(!templates.TryGetValue(hash,out var shapes))
            {
                templates[hash]=shapes=new List<Bounds>();
                var prefab=ZNetScene.instance.GetPrefab(hash);
                if(!prefab || !prefab.GetComponent<Piece>() || RoadObjects.Movable(prefab)) continue;
                foreach(var collider in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if(collider.isTrigger) continue;
                    Bounds bounds;
                    if(collider is BoxCollider box) bounds=new Bounds(box.center,box.size);
                    else if(collider is MeshCollider mesh && mesh.sharedMesh) bounds=mesh.sharedMesh.bounds;
                    else if(collider is SphereCollider sphere) bounds=new Bounds(sphere.center,Vector3.one*sphere.radius*2);
                    else if(collider is CapsuleCollider capsule) bounds=new Bounds(capsule.center,new Vector3(capsule.radius*2,capsule.height,capsule.radius*2));
                    else continue;
                    shapes.Add(TransformBounds(bounds,prefab.transform.worldToLocalMatrix*collider.transform.localToWorldMatrix));
                }
            }
            float margin=zdo.GetLong(ZDOVars.s_creator,0)!=0?playerBuffer:2;
            var matrix=Matrix4x4.TRS(zdo.GetPosition(),zdo.GetRotation(),zdo.GetVec3("scale",Vector3.one));
            foreach(var shape in shapes)
            {
                var b=TransformBounds(shape,matrix);
                result.Add(new Exclusion{Center=GameTerrain.PointOf(b.center),HalfX=b.extents.x+margin,HalfZ=b.extents.z+margin,Radius=new Vector2(b.extents.x+margin,b.extents.z+margin).magnitude});
            }
        }
    }
    private static Bounds TransformBounds(Bounds b,Matrix4x4 m)
    {
        var result=new Bounds(m.MultiplyPoint3x4(b.center),Vector3.zero);
        for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
            result.Encapsulate(m.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
        return result;
    }
}
