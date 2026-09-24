using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Jotunn.Managers;
using Jotunn.Configs;
using Jotunn.Entities;
namespace RunicLilyPads
{
    internal static class GardenModels
    {
        internal sealed class Data{public Mesh Mesh;public Material Material;public Sprite Icon;}
        static bool IsPlant(string id)=>id=="cattail_001"||id.StartsWith("fern_")||id.StartsWith("fauna_")||id.StartsWith("reeds_")||id.StartsWith("glowshrooms_")||id.StartsWith("bluelilly_")||id.StartsWith("blueiris_")||id.StartsWith("whitelilly_");
        static readonly Dictionary<string,Data> Cache=new Dictionary<string,Data>();
        static Stream Open(string name)=>typeof(Plugin).Assembly.GetManifestResourceStream("WaterGardens.Models."+name);
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
            if(IsPlant(id))GardenWind.ConfigureImported(data.Material);
            var icon=Texture(id+".png");data.Icon=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(.5f,.5f));Cache[id]=data;return data;
        }
        internal static GameObject Model(string id,Transform parent,float height,bool solid){
            var data=Load(id);var model=new GameObject(id){layer=LayerMask.NameToLayer("piece")};model.transform.SetParent(parent,false);model.transform.localScale=Vector3.one*height;
            model.AddComponent<MeshFilter>().sharedMesh=data.Mesh;model.AddComponent<MeshRenderer>().sharedMaterial=data.Material;
            if(solid)model.AddComponent<MeshCollider>().sharedMesh=data.Mesh;
            else{var box=model.AddComponent<BoxCollider>();box.center=data.Mesh.bounds.center;box.size=data.Mesh.bounds.size;box.isTrigger=true;}
            return model;
        }
        static void Static(string id,string asset,string name,float height,int cost,int feature=0){
            bool plant=asset=="cattail_001"||asset.StartsWith("fern_")||asset.StartsWith("fauna_")||asset.StartsWith("reeds_")||asset.StartsWith("glowshrooms_");
            var root=GardenPrefabs.Root(id,name,"");Model(asset,root.transform.Find("garden_visual"),height,!plant);root.GetComponent<Piece>().m_icon=Load(asset).Icon;
            root.GetComponent<WearNTear>().m_supports=!plant;
            if(plant)root.AddComponent<GardenWind>();
            if(plant)root.transform.Find("garden_visual").gameObject.AddComponent<PlantTouch>();
            if(!plant){root.AddComponent<GardenFloat>().Kind=feature==-1?1:feature==4?3:feature>0?2:0;root.GetComponent<Piece>().m_groundOnly=false;}
            if(feature!=0){var f=root.AddComponent<GardenFeature>();f.Kind=feature;f.ModelHeight=height;}
            GardenPrefabs.Register(root,name,feature==0?GardenLanguage.Token("text_775ddf77b69d"):GardenLanguage.Token("text_87269e395fba"),cost,feature==3);
        }
        internal static void RegisterStatics(){
            Static("piece_arcanedecor_fern_red_001","fern_red_001",GardenLanguage.Token("text_4208168eba6d"),1.0f,2);
            Static("piece_arcanedecor_fern_001","fern_001",GardenLanguage.Token("text_543795048192"),1.1f,2);
            Static("piece_arcanedecor_fern_002","fern_002",GardenLanguage.Token("text_e3c6cd4408bb"),.8f,2);
            Static("piece_arcanedecor_fauna_001","fauna_001",GardenLanguage.Token("text_102dfbcc80e7"),.9f,2);
            Static("piece_arcanedecor_glowshrooms_001","glowshrooms_001",GardenLanguage.Token("text_de08fd38995a"),.65f,2);
            Static("piece_arcanedecor_reeds_006","reeds_006",GardenLanguage.Token("text_550f5d04e50b"),1.3f,2);
            Static("piece_arcanedecor_cattail_001","cattail_001",GardenLanguage.Token("text_4957f6b9dcc1"),1.6f,2);
            Static("piece_arcanedecor_rock_001","rock_001",GardenLanguage.Token("text_bc0fb957ca78"),.45f,2);
            Static("piece_arcanedecor_rock_002","rock_002",GardenLanguage.Token("text_f89d7083b5cb"),.3f,2);
            Static("piece_arcanedecor_rock_003","rock_003",GardenLanguage.Token("text_25b6d38fe761"),.4f,2);
            Static("piece_arcanedecor_mossrock_001","mossrock_001",GardenLanguage.Token("text_acf3dc50af70"),.35f,2);
            Static("piece_arcanedecor_mossrock_002","mossrock_002",GardenLanguage.Token("text_53058b04a3fb"),.3f,2);
            Static("piece_arcanedecor_lantern_001","lantern_001",GardenLanguage.Token("text_0961a763d612"),2.4f,10,-1);
            for(int i=1;i<=5;i++)if(i!=3)Static("piece_arcanedecor_fountain_00"+i,"fountain_00"+i,i==4?GardenLanguage.Token("text_79eafab85a1b"):(i==1?GardenLanguage.Token("piece_fountain_1"):i==2?GardenLanguage.Token("piece_fountain_2"):GardenLanguage.Token("piece_fountain_5")),3f,20,i);
        }
        static void RegisterOriginalLilies(){
            string[] ids={"small","broad","white","pink_cluster"};
            string[] names={GardenLanguage.Token("text_416e68e17069"),GardenLanguage.Token("text_ecb64c23b7d4"),GardenLanguage.Token("text_3aad34cea38b"),GardenLanguage.Token("text_6c0e09413ee2")};
            for(int i=0;i<4;i++){
                var root=GardenPrefabs.Root("piece_runic_lilypad_"+ids[i],names[i],"");
                var visual=root.transform.Find("garden_visual");visual.name="lily_visual";
                var moving=new GameObject("floating_lily");moving.transform.SetParent(visual,false);moving.AddComponent<PlantTouch>();root.AddComponent<GardenWind>().Strength=.018f;
                VanillaLilies.Build(moving.transform,i);
                var piece=root.GetComponent<Piece>();piece.m_groundOnly=false;piece.m_waterPiece=true;root.AddComponent<LilyFloat>();
                GardenIcons.Assign(root,visual.gameObject,true);
                var c=new PieceConfig{Name=names[i],Description=GardenLanguage.Token("text_85416ebd2722"),PieceTable="Hammer",Category="Water Garden"};
                c.AddRequirement("Wood",i==3?4:2);if(i>=2)c.AddRequirement("Dandelion",1);
                if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,c)))throw new Exception(root.name);
            }
        }
        internal static void RegisterLilies(){
            if(!VanillaLilies.Load())throw new Exception("Native lily base unavailable");
            RegisterOriginalLilies();
            string[] ids={"blue_001","blue_002","white_002","blue_003","white_001","white_003"};
            string[] assets={"bluelilly_001","blueiris_001","whitelilly_002","bluelilly_003","whitelilly_001","whitelilly_003"};
            string[] names={GardenLanguage.Token("text_d79c948d74be"),GardenLanguage.Token("text_45c332271437"),GardenLanguage.Token("text_27c951887acf"),GardenLanguage.Token("text_917ee3ebf289"),GardenLanguage.Token("text_4b31eceaef76"),GardenLanguage.Token("text_d3e94a60aa85")};
            for(int i=0;i<ids.Length;i++){
                var root=GardenPrefabs.Root("piece_arcanedecor_lily_"+ids[i],names[i],"");var visual=root.transform.Find("garden_visual");visual.name="lily_visual";
                var moving=new GameObject("floating_lily");moving.transform.SetParent(visual,false);moving.AddComponent<PlantTouch>();root.AddComponent<GardenWind>().Strength=.018f;
                VanillaLilies.Build(moving.transform,0);
                int stage=i==0||i==4?0:i==1||i==2?1:2;
                moving.transform.GetChild(0).localScale*=stage==0?.8f:stage==1?1.25f:1.8f;
                var touch=moving.GetComponent<PlantTouch>();touch.Radius=stage==0?.65f:stage==1?.85f:1.05f;
                root.AddComponent<GardenFlowerScale>();
                var flower=Model(assets[i],moving.transform,stage==0?.25f:stage==1?.4f:.55f,false);flower.transform.localPosition=Vector3.down*(stage==0?.06f:.08f);
                var piece=root.GetComponent<Piece>();piece.m_groundOnly=false;piece.m_waterPiece=true;root.AddComponent<LilyFloat>();GardenIcons.Assign(root,visual.gameObject,true);
                var c=new PieceConfig{Name=names[i],Description=GardenLanguage.Token("text_067fe535f574"),PieceTable="Hammer",Category="Water Garden"};c.AddRequirement("Wood",i==3?4:2);if(i>=2)c.AddRequirement("Dandelion",1);
                if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,c)))throw new Exception(root.name);
                Plugin.Log.LogInfo("Registered "+root.name);
                if(i==4||i==5){
                    // Preserve the two IDs introduced in 0.3.0 without duplicate hammer entries.
                    var alias=PrefabManager.Instance.CreateClonedPrefab("piece_runic_lilypad_"+(i==4?"white_bud":"white_open"),root);
                    alias.GetComponent<Piece>().m_resources=new[]{
                        new Piece.Requirement{m_resItem=PrefabManager.Instance.GetPrefab("Wood").GetComponent<ItemDrop>(),m_amount=2,m_recover=true},
                        new Piece.Requirement{m_resItem=PrefabManager.Instance.GetPrefab("Dandelion").GetComponent<ItemDrop>(),m_amount=1,m_recover=true}};
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(alias,false));
                }
            }
        }
    }
}

