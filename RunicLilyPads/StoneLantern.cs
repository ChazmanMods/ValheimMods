using System;
using System.IO;
using UnityEngine;
namespace RunicLilyPads
{
    internal static class StoneLantern
    {
        internal static void Register()
        {
            var assembly=typeof(Plugin).Assembly;
            Mesh mesh;
            using(var reader=new BinaryReader(assembly.GetManifestResourceStream("WaterGardens.stone_lantern.meshbin"))){
                if(reader.ReadInt32()!=0x57474D31)throw new InvalidDataException("Lantern mesh version");
                int count=reader.ReadInt32(),indices=reader.ReadInt32();
                var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];var triangles=new int[indices];
                for(int i=0;i<count;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
                for(int i=0;i<indices;i++)triangles[i]=reader.ReadInt32();
                mesh=new Mesh{name="WaterGardens stone lantern",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=vertices,normals=normals,uv=uv,triangles=triangles};mesh.RecalculateBounds();mesh.RecalculateTangents();
            }
            var texture=new Texture2D(2,2){name="Stone lantern base color",wrapMode=TextureWrapMode.Repeat};
            using(var stream=assembly.GetManifestResourceStream("WaterGardens.stone_lantern.jpg"))using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(texture,bytes.ToArray(),true))throw new InvalidDataException("Lantern texture");}
            var material=new Material(GardenPrefabs.Rock.GetComponentInChildren<MeshRenderer>(true).sharedMaterial){name="WaterGardens lantern stone"};
            material.mainTexture=texture;if(material.HasProperty("_Color"))material.SetColor("_Color",Color.white);
            var root=GardenPrefabs.Root("piece_arcanedecor_stone_lantern","Stone Lantern","");
            root.GetComponent<WearNTear>().m_supports=true;
            var model=new GameObject("stone_lantern"){layer=LayerMask.NameToLayer("piece")};model.transform.SetParent(root.transform.Find("garden_visual"),false);
            model.AddComponent<MeshFilter>().sharedMesh=mesh;model.AddComponent<MeshRenderer>().sharedMaterial=material;
            model.AddComponent<MeshCollider>().sharedMesh=mesh;
            root.AddComponent<GardenFloat>().Kind=1;root.GetComponent<Piece>().m_groundOnly=false;
            GardenIcons.Assign(root,root.transform.Find("garden_visual").gameObject);
            GardenPrefabs.Register(root,"Stone Lantern","Weathered stone lantern. Place on terrain or the pond bed. Height: 2.4 meters.",10,true);
        }
    }
}
