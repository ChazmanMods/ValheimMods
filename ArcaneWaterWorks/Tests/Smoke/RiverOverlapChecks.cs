using System;
using System.Collections;
using System.IO;
using ArcaneWaterWorks;
using BepInEx;
using UnityEngine;

public sealed partial class Smoke
{
    IEnumerator IndependentRiverTest(GameObject prefab,Vector3 origin){
        var a=new Route{Width=6,Depth=1,Whitewater=0};a.Points.Add(new Node(0,0,0));a.Points.Add(new Node(0,-8,8));
        var b=a.Copy();b.Depth=5;b.Whitewater=1;
        var left=Instantiate(prefab,origin,Quaternion.identity);var right=Instantiate(prefab,origin+Vector3.right*4,Quaternion.identity);
        left.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",a.Encode());right.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",b.Encode());
        yield return StartCoroutine(WaitForWaterWork(2));
        var sa=left.GetComponent<Spring>();var sb=right.GetComponent<Spring>();
        Check(sa.JoinedRoute==null&&sb.JoinedRoute==null,"independent waterfall fixture has no connected endpoint");
        int Count(GameObject river,float x,float z,bool inclusive=false){
            var surface=river.transform.Find("waterworks_river/river_surface");var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
            var vs=mesh.vertices;var ts=mesh.triangles;int count=0;
            for(int i=0;i<ts.Length;i+=3){
                var p=surface.TransformPoint(vs[ts[i]]);var q=surface.TransformPoint(vs[ts[i+1]]);var r=surface.TransformPoint(vs[ts[i+2]]);
                float Cross(Vector3 u,Vector3 v)=>(v.x-u.x)*(z-u.z)-(v.z-u.z)*(x-u.x);
                float c1=Cross(p,q),c2=Cross(q,r),c3=Cross(r,p);
                float e=inclusive?-.00001f:.000001f;
                if(c1>e&&c2>e&&c3>e||c1< -e&&c2< -e&&c3< -e)count++;
            }return count;
        }
        int duplicates=0,holes=0;
        for(float x=2.137f;x<5.8f;x+=.19f)for(float z=1.173f;z<6.9f;z+=.23f){
            int hits=Count(left,origin.x+x,origin.z+z)+Count(right,origin.x+x,origin.z+z);
            if(hits>1)duplicates++;
            if(hits==0&&Count(left,origin.x+x,origin.z+z,true)+Count(right,origin.x+x,origin.z+z,true)==0)holes++;
        }
        Check(duplicates==0&&holes==0,"independent steep rivers render overlap exactly once including the outer 1.5 metre skirt: duplicates="+duplicates+", holes="+holes);
        foreach(var river in new[]{left,right}){
            var mesh=river.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
            bool finite=true;foreach(var uv in mesh.uv4)if(float.IsInfinity(uv.y)||float.IsNaN(uv.y))finite=false;
            Check(finite,"river without nearby ponds has finite shader fade coordinates");
        }
        var leftMesh=left.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        var vertices=leftMesh.vertices;var shore=leftMesh.uv3;int interiorEdges=0;bool opaque=true;
        for(int i=0;i<vertices.Length;i++)if(Mathf.Abs(vertices[i].x-2f)<1.001f&&vertices[i].z>1&&vertices[i].z<7){
            interiorEdges++;if(shore[i].x<.18f)opaque=false;
        }
        Check(interiorEdges>0&&opaque,"interior river ownership edge does not fade like a shoreline");
        var rivers=new[]{sa,sb};var point=origin+new Vector3(4.5f,-4,4);
        var before=RiverAppearance.Sample(point-Vector3.right*.001f,rivers);var after=RiverAppearance.Sample(point+Vector3.right*.001f,rivers);
        Check(Mathf.Abs(before.r-after.r)<.01f&&Mathf.Abs(before.g-after.g)<.01f,"different-depth independent rivers blend appearance across the ownership edge");
        Capture(sa,new Vector3(10,8,12),origin+new Vector3(2,-4,4));
        File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../independent-rivers-preview.png"),true);
        ZNetScene.instance.Destroy(left);ZNetScene.instance.Destroy(right);yield return StartCoroutine(WaitForWaterWork(1));
    }
}
