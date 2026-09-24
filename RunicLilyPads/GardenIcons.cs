using System;
using System.Collections.Generic;
using UnityEngine;
using Jotunn.Managers;
using Object=UnityEngine.Object;
namespace RunicLilyPads
{
    internal static class GardenIcons
    {
        static Sprite serverIcon;
        // Jotunn validates Piece.m_icon even on a dedicated server. Supply a
        // shared CPU-created sprite without running cameras or render textures.
        static bool AssignServerIcon(GameObject root){
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)return false;
            if(!serverIcon){
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="WaterGardens server icon"};
                serverIcon=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f));
            }
            root.GetComponent<Piece>().m_icon=serverIcon;
            return true;
        }
        // Render only visual children, never a networked piece or its gameplay components.
        internal static void Assign(GameObject root,GameObject visual,bool lily=false,float distance=1){
            if(AssignServerIcon(root))return;
            GameObject preview=null;var owned=new List<Mesh>();
            try{
                preview=Object.Instantiate(visual);preview.name=root.name+"_icon";
                foreach(var motion in preview.GetComponentsInChildren<PlantTouch>(true))Object.DestroyImmediate(motion);
                preview.SetActive(true);
                if(lily)foreach(var filter in preview.GetComponentsInChildren<MeshFilter>(true)){
                    if(filter.name!="vanilla_waterlilies")continue;
                    // Exclude submerged stems from thumbnail framing; the placed mesh is untouched.
                    var original=filter.sharedMesh;var vertices=original.vertices;var normals=original.normals;var uv=original.uv;
                    var positions=new List<Vector3>();var ns=new List<Vector3>();var coords=new List<Vector2>();var indices=new List<int>();
                    float cutoff=original.bounds.max.y-.08f/Mathf.Max(.001f,filter.transform.lossyScale.y);
                    var triangles=original.triangles;
                    for(int i=0;i<triangles.Length;i+=3){
                        if(vertices[triangles[i]].y<cutoff||vertices[triangles[i+1]].y<cutoff||vertices[triangles[i+2]].y<cutoff)continue;
                        for(int j=0;j<3;j++){int v=triangles[i+j];indices.Add(positions.Count);positions.Add(vertices[v]);ns.Add(normals[v]);coords.Add(uv[v]);}
                    }
                    if(indices.Count==0)throw new Exception("No leaf triangles in "+root.name);
                    var mesh=new Mesh{name="Leaf canopy thumbnail",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                    mesh.SetVertices(positions);mesh.SetNormals(ns);mesh.SetUVs(0,coords);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();owned.Add(mesh);filter.sharedMesh=mesh;
                }
                var icon=RenderManager.Instance.Render(new RenderManager.RenderRequest(preview){Width=128,Height=128,Rotation=lily?Quaternion.Euler(65,20,0):RenderManager.IsometricRotation,DistanceMultiplier=distance,ParticleSimulationTime=-1,UseCache=false});
                if(!icon)throw new Exception("Empty icon render for "+root.name);
                root.GetComponent<Piece>().m_icon=icon;
            }catch(Exception e){Plugin.Log.LogError("Garden icon: "+e);}
            finally{if(preview)Object.DestroyImmediate(preview);foreach(var mesh in owned)Object.DestroyImmediate(mesh);}
        }
        internal static void Pond(GameObject root,int shape){
            if(AssignServerIcon(root))return;
            var preview=new GameObject("pond_thumbnail");var owned=new List<Object>();
            try{
                // A miniature basin uses the same outline calculation as the placed pond.
                const int count=64;var points=new Vector3[count+1];var uv=new Vector2[count+1];var triangles=new int[count*3];
                for(int i=0;i<count;i++){
                    double a=i*Math.PI*2/count;float r=(float)PondMath.Radius(shape,a)*3;
                    points[i+1]=new Vector3((float)Math.Cos(a)*r,0,(float)Math.Sin(a)*r*(float)PondMath.Aspect(shape));
                    uv[i+1]=new Vector2(points[i+1].x,points[i+1].z);
                    triangles[i*3]=0;triangles[i*3+1]=(i+1)%count+1;triangles[i*3+2]=i+1;
                }
                var water=new Mesh{vertices=points,triangles=triangles,uv=uv};water.RecalculateNormals();owned.Add(water);
                var material=new Material(GardenPrefabs.Rock.GetComponentInChildren<MeshRenderer>(true).sharedMaterial);
                material.mainTexture=Texture2D.whiteTexture;material.SetColor("_Color",new Color(.16f,.48f,.53f));
                foreach(string field in new[]{"_MossAlpha","_MossBlend","_BumpScale","_TriplanarMap"})if(material.HasProperty(field))material.SetFloat(field,0);
                owned.Add(material);var surface=new GameObject("water");surface.transform.SetParent(preview.transform,false);
                surface.AddComponent<MeshFilter>().sharedMesh=water;surface.AddComponent<MeshRenderer>().sharedMaterial=material;
                for(int i=0;i<count;i+=4){
                    var stone=GardenPrefabs.AddVisual(GardenPrefabs.Rock,preview.transform,.48f,true);
                    stone.transform.localPosition=points[i+1]*1.06f+Vector3.down*.14f;
                }
                Assign(root,preview,true);
            }finally{Object.DestroyImmediate(preview);foreach(var item in owned)Object.DestroyImmediate(item);}
        }
    }
}
