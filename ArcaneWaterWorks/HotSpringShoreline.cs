using System.Collections.Generic;
using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;
internal static class HotSpringShoreline
{
    // Extend the same water mesh beneath the bank. A coarse terrain triangle can
    // cross the nominal shoreline, so its water edge must terminate under land.
    internal static void Extend(GardenPiece pond,Mesh mesh,Transform surface){
        var vertices=new List<Vector3>(mesh.vertices);var uv=new List<Vector2>(mesh.uv);var triangles=new List<int>(mesh.triangles);
        var unique=new Dictionary<(int,int,int),int>();var canonical=new int[vertices.Count];
        for(int i=0;i<vertices.Count;i++){
            var p=vertices[i];var key=(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));
            if(!unique.TryGetValue(key,out int id)){id=i;unique[key]=id;}canonical[i]=id;
        }
        var edges=new Dictionary<(int,int),(int a,int b,int count)>();
        void Edge(int a,int b){a=canonical[a];b=canonical[b];var key=a<b?(a,b):(b,a);if(edges.TryGetValue(key,out var e))edges[key]=(e.a,e.b,e.count+1);else edges[key]=(a,b,1);}
        for(int i=0;i<triangles.Count;i+=3){Edge(triangles[i],triangles[i+1]);Edge(triangles[i+1],triangles[i+2]);Edge(triangles[i+2],triangles[i]);}
        var extended=new Dictionary<int,int>();
        Vector3 Outward(Vector3 p){
            const float e=.025f;
            var n=new Vector3(GardenIntegration.Distance(pond,p+Vector3.right*e)-GardenIntegration.Distance(pond,p-Vector3.right*e),0,GardenIntegration.Distance(pond,p+Vector3.forward*e)-GardenIntegration.Distance(pond,p-Vector3.forward*e));return n.normalized;
        }
        int ExtendVertex(int i){
            if(extended.TryGetValue(i,out int found))return found;
            var world=surface.TransformPoint(vertices[i]);world+=Outward(world)*1.5f;
            int result=vertices.Count;vertices.Add(surface.InverseTransformPoint(world));uv.Add(new Vector2(world.x,world.z)*.2f);extended[i]=result;return result;
        }
        void Triangle(int a,int b,int c){if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).y<0){int t=b;b=c;c=t;}triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        foreach(var e in edges.Values){
            if(e.count!=1)continue;var middle=surface.TransformPoint((vertices[e.a]+vertices[e.b])*.5f);
            if(GardenIntegration.Distance(pond,middle)<.98f)continue;
            var probe=middle+Outward(middle)*.12f;
            if(!float.IsNegativeInfinity(GardenIntegration.WaterLevel(probe))||!float.IsNegativeInfinity(GardenBridge.Water(probe)))continue;
            int a=ExtendVertex(e.a),b=ExtendVertex(e.b);Triangle(e.a,a,b);Triangle(e.a,b,e.b);
        }
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
    }
}
