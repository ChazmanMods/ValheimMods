using System.Collections.Generic;
using UnityEngine;

namespace ArcaneWaterWorks;

internal sealed class OceanJoin:MonoBehaviour
{
    Mesh mesh;Vector3[] vertices;Vector2[] shore,contact;readonly List<(int index,Vector3 world,WaterVolume ocean)> edge=new List<(int,Vector3,WaterVolume)>();float next;
    readonly Dictionary<(float,float),float> cache=new Dictionary<(float,float),float>();
    internal static float Sea=>ZoneSystem.instance?ZoneSystem.instance.m_waterLevel:30;
    internal static float BankWeight(float shore,float depth)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(shore/.6f))*Mathf.SmoothStep(0,1,Mathf.Clamp01(depth/.65f));
    static WaterVolume Find(Vector3 point){
        foreach(var water in WaterVolume.Instances){if(!water||!water.m_heightmap||water.m_heightmap.IsDistantLod)continue;
            var map=water.m_heightmap;float half=map.m_width*map.m_scale*.5f;var p=point-map.transform.position;
            if(Mathf.Abs(p.x)<=half&&Mathf.Abs(p.z)<=half)return water;
        }return null;
    }
    static float Level(Vector3 point,float mean,WaterVolume ocean){
        if(!ocean)return mean;
        float weight=1-Mathf.SmoothStep(0,1,Mathf.Max(0,mean-Sea)/2);
        return mean+(ocean.GetWaterSurface(point)-Sea)*weight;
    }
    internal static float Level(Vector3 point,float mean)=>mean<Sea+2?Level(point,mean,Find(point)):mean;
    internal void Initialize(Mesh source){
        mesh=source;vertices=mesh.vertices;shore=mesh.uv3;contact=mesh.uv4;
        for(int i=0;i<vertices.Length;i++){var p=transform.TransformPoint(vertices[i]);if(p.y<Sea+2)edge.Add((i,p,Find(p)));}
        enabled=edge.Count>0;
    }
    void Update(){
        if(Time.time<next)return;next=Time.time+.05f;cache.Clear();
        foreach(var point in edge){
            var key=(point.world.x,point.world.z);if(!cache.TryGetValue(key,out float height)){
                height=Level(point.world,point.world.y,point.ocean);cache[key]=height;
            }
            float bank=BankWeight(shore[point.index].x,contact[point.index].x);
            var v=vertices[point.index];v.y=point.world.y+(height-point.world.y)*bank-transform.position.y;vertices[point.index]=v;
        }
        mesh.vertices=vertices;mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(.3f);mesh.bounds=bounds;
    }
}
