using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Jotunn.Managers;
using Jotunn.Configs;
using Jotunn.Entities;
namespace ArcaneWaterWorks.Pools
{
    internal static class GardenModels
    {
        internal sealed class Data{public Mesh Mesh;public Material Material;public Sprite Icon;}
        static bool IsPlant(string id)=>id=="cattail_001"||id.StartsWith("fern_")||id.StartsWith("fauna_")||id.StartsWith("reeds_")||id.StartsWith("glowshrooms_")||id.StartsWith("bluelilly_")||id.StartsWith("blueiris_")||id.StartsWith("whitelilly_");
        static readonly Dictionary<string,Data> Cache=new Dictionary<string,Data>();
        static Stream Open(string name)=>typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneNaturalWaters.Pools.Models."+name);
        static Texture2D Texture(string name){using(var stream=Open(name)){if(stream==null)return null;using(var bytes=new MemoryStream()){stream.CopyTo(bytes);var t=new Texture2D(2,2){name=name};if(!ImageConversion.LoadImage(t,bytes.ToArray(),true))throw new Exception(name);return t;}}}
        internal static Data Load(string id){
            if(Cache.TryGetValue(id,out var data))return data;
            data=new Data();using(var r=new BinaryReader(Open(id+".meshbin"))){
                if(r.ReadInt32()!=0x57474D31)throw new InvalidDataException(id);int count=r.ReadInt32(),indices=r.ReadInt32();
                var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var t=new int[indices];
                for(int i=0;i<count;i++){v[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());n[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}for(int i=0;i<indices;i++)t[i]=r.ReadInt32();
                data.Mesh=new Mesh{name=id,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=v,normals=n,uv=uv,triangles=t};data.Mesh.RecalculateBounds();data.Mesh.RecalculateTangents();
            }
            data.Material=new Material(GardenPrefabs.Rock.GetComponentInChildren<MeshRenderer>(true).sharedMaterial){name=id};
            data.Material.mainTexture=Texture(id+".jpg");data.Material.SetColor("_Color",Color.white);
            foreach(string field in new[]{"_TriplanarMap","_MossAlpha","_MossBlend","_BumpScale"})if(data.Material.HasProperty(field))data.Material.SetFloat(field,0);
            var glow=Texture(id+"_emission.png");data.Material.SetTexture("_EmissiveTex",glow?glow:Texture2D.blackTexture);data.Material.SetColor("_EmissionColor",id=="glowshrooms_001"?new Color(1,.55f,.08f)*2:Color.black);

            var icon=Texture(id+".png");data.Icon=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(.5f,.5f));Cache[id]=data;return data;
        }
        internal static GameObject Model(string id,Transform parent,float height,bool solid){
            var data=Load(id);var model=new GameObject(id){layer=LayerMask.NameToLayer("piece")};model.transform.SetParent(parent,false);model.transform.localScale=Vector3.one*height;
            model.AddComponent<MeshFilter>().sharedMesh=data.Mesh;model.AddComponent<MeshRenderer>().sharedMaterial=data.Material;
            if(solid)model.AddComponent<MeshCollider>().sharedMesh=data.Mesh;
            else{var box=model.AddComponent<BoxCollider>();box.center=data.Mesh.bounds.center;box.size=data.Mesh.bounds.size;box.isTrigger=true;}
            return model;
        }
    }
}
