using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

internal static class PipeAssets
{
    internal sealed class Asset { internal Mesh Mesh;internal Material Material,OrbMaterial;internal Sprite Icon; }
    static readonly Dictionary<string,Asset> cache=new Dictionary<string,Asset>();
    static Stream Open(string name)=>typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneDecorFlowCraft.Plumbing."+name)??throw new FileNotFoundException(name);
    static Texture2D Texture(string name){
        using(var input=Open(name))using(var bytes=new MemoryStream()){
            input.CopyTo(bytes);var texture=new Texture2D(2,2,TextureFormat.RGBA32,true){name=name};
            if(!ImageConversion.LoadImage(texture,bytes.ToArray(),true))throw new InvalidDataException(name);return texture;
        }
    }
    internal static Asset Load(string key){
        if(cache.TryGetValue(key,out var asset))return asset;
        if(key.EndsWith("_narrow",StringComparison.Ordinal)){
            var original=Load(key.Substring(0,key.Length-7));var mesh=UnityEngine.Object.Instantiate(original.Mesh);
            var points=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<points.Length;i++){
                var p=points[i];
                if(key=="dragon_narrow")p*=.5f;
                else if(key=="intake_narrow"){p.x*=.5f;p.z*=.5f;normals[i]=new Vector3(normals[i].x*2,normals[i].y,normals[i].z*2).normalized;}
                else if(key=="elbow_narrow"||key=="elbow_long_narrow"){
                    float bend=key=="elbow_long_narrow"?2:1;
                    float angle=Mathf.Clamp(Mathf.Atan2(p.z/(.74099f*bend),(p.y+.69195f*bend)/(.69195f*bend)),0,Mathf.PI*.5f);
                    var centre=new Vector3(0,-.69195f+.69195f*Mathf.Cos(angle),.74099f*Mathf.Sin(angle))*bend;
                    p=centre+(p-centre)*.5f;
                }else{p.x*=.5f;p.y*=.5f;normals[i]=new Vector3(normals[i].x*2,normals[i].y*2,normals[i].z).normalized;}
                points[i]=p;
            }
            mesh.name="FC "+key;mesh.vertices=points;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
            var picture=Texture(key+".png");var narrowIcon=Sprite.Create(picture,new Rect(0,0,picture.width,picture.height),new Vector2(.5f,.5f));narrowIcon.name="FC "+key+" model icon";
            asset=new Asset{Mesh=mesh,Material=original.Material,OrbMaterial=original.OrbMaterial,Icon=narrowIcon};cache[key]=asset;return asset;
        }
        if(key=="pipe4"||key=="powered2"||key=="powered4"||key=="elbow_long"){
            var original=Load(key=="pipe4"?"pipe":key=="elbow_long"?"elbow":"powered");var mesh=UnityEngine.Object.Instantiate(original.Mesh);mesh.name="AW "+key;
            var points=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<points.Length;i++){
                if(key=="elbow_long"){
                    var p=points[i];float angle=Mathf.Clamp(Mathf.Atan2(p.z/.74099f,(p.y+.69195f)/.69195f),0,Mathf.PI*.5f);
                    points[i]+=new Vector3(0,-.69195f+.69195f*Mathf.Cos(angle),.74099f*Mathf.Sin(angle));
                }else if(key=="powered4"){
                    // Extend the barrel while retaining the central orb and socket detailing.
                    float z=points[i].z,amount=Mathf.Clamp01((Mathf.Abs(z)-.16f)/.22f);
                    points[i].z+=Mathf.Sign(z)*1.5f*amount;
                    if(Mathf.Abs(z)>.16f&&Mathf.Abs(z)<.38f)normals[i].z/=1+1.5f/.22f;
                    normals[i].Normalize();
                }else{points[i].z*=2;normals[i].z*=.5f;normals[i].Normalize();}
            }
            mesh.vertices=points;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
            var image=Texture(key+".png");var sprite=Sprite.Create(image,new Rect(0,0,image.width,image.height),new Vector2(.5f,.5f));sprite.name="AW "+key+" model icon";
            asset=new Asset{Mesh=mesh,Material=original.Material,OrbMaterial=original.OrbMaterial,Icon=sprite};cache[key]=asset;return asset;
        }
        asset=new Asset();
        using(var input=Open(key+".meshbin"))using(var reader=new BinaryReader(input)){
            if(reader.ReadInt32()!=0x57474D31)throw new InvalidDataException(key);
            int count=reader.ReadInt32(),triangles=reader.ReadInt32();
            var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];var indices=new int[triangles];
            for(int i=0;i<count;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
            for(int i=0;i<triangles;i++)indices[i]=reader.ReadInt32();
            // Imported shells are open at their sockets. Add inward-facing surfaces so looking
            // into an elbow sees its copper wall, rather than the sky through culled back faces.
            var bothVertices=new Vector3[count*2];var bothNormals=new Vector3[count*2];var bothUv=new Vector2[count*2];var bothIndices=new int[triangles*2];
            for(int i=0;i<count;i++){bothVertices[i]=bothVertices[count+i]=vertices[i];bothNormals[i]=normals[i];bothNormals[count+i]=-normals[i];bothUv[i]=bothUv[count+i]=uv[i];}
            Array.Copy(indices,bothIndices,triangles);
            for(int i=0;i<triangles;i+=3){bothIndices[triangles+i]=indices[i]+count;bothIndices[triangles+i+1]=indices[i+2]+count;bothIndices[triangles+i+2]=indices[i+1]+count;}
            asset.Mesh=new Mesh{name="AW "+key,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=bothVertices,normals=bothNormals,uv=bothUv,triangles=bothIndices};
            asset.Mesh.RecalculateBounds();asset.Mesh.RecalculateTangents();
            var colors=new Color[count*2];for(int i=0;i<count;i++){colors[i]=Color.white;colors[count+i]=new Color(.55f,.55f,.55f,1);}asset.Mesh.colors=colors;
        }
        var native=Jotunn.Managers.PrefabManager.Instance.GetPrefab("Copper").GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
        asset.Material=new Material(native){name="AW copper "+key,mainTexture=Texture(key+".jpg")};
        asset.Material.SetColor("_Color",Color.white);
        foreach(string field in new[]{"_TriplanarMap","_MossAlpha","_MossBlend","_BumpScale"})if(asset.Material.HasProperty(field))asset.Material.SetFloat(field,0);
        if(asset.Material.HasProperty("_EmissionColor"))asset.Material.SetColor("_EmissionColor",Color.black);
        if(key=="powered"){
            asset.OrbMaterial=new Material(asset.Material){name="Surtling red orb",mainTexture=Texture2D.whiteTexture};
            asset.OrbMaterial.SetColor("_Color",new Color(.28f,.004f,.002f));
            asset.OrbMaterial.SetColor("_EmissionColor",Color.black);
            asset.OrbMaterial.SetColor("_SpecColor",Color.black);
            foreach(string field in new[]{"_Metallic","_Glossiness","_Smoothness"})if(asset.OrbMaterial.HasProperty(field))asset.OrbMaterial.SetFloat(field,0);
            asset.OrbMaterial.SetTexture("_EmissionMap",Texture2D.whiteTexture);asset.OrbMaterial.EnableKeyword("_EMISSION");
            var mesh=asset.Mesh;var v=mesh.vertices;var all=mesh.triangles;var copper=new List<int>();var orb=new List<int>();
            for(int t=0;t<all.Length;t+=3){
                var centre=(v[all[t]]+v[all[t+1]]+v[all[t+2]])/3;
                var list=centre.x>.255f&&Mathf.Abs(centre.y)<.12f&&Mathf.Abs(centre.z)<.115f?orb:copper;
                list.Add(all[t]);list.Add(all[t+1]);list.Add(all[t+2]);
            }
            mesh.subMeshCount=2;mesh.SetTriangles(copper,0);mesh.SetTriangles(orb,1);
        }
        var icon=Texture(key+".png");asset.Icon=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(.5f,.5f));asset.Icon.name="AW "+key+" model icon";
        cache[key]=asset;return asset;
    }
    internal static GameObject Visual(string key,Transform parent,float scale=1){
        var asset=Load(key);var go=new GameObject("plumbing_visual"){layer=LayerMask.NameToLayer("piece")};go.transform.SetParent(parent,false);go.transform.localScale=Vector3.one*scale;
        go.AddComponent<MeshFilter>().sharedMesh=asset.Mesh;go.AddComponent<MeshRenderer>().sharedMaterials=asset.OrbMaterial?new[]{asset.Material,asset.OrbMaterial}:new[]{asset.Material};return go;
    }
}
