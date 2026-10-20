using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ArcaneWaterWorks;

[BepInPlugin(Guid,"ArcaneNaturalWaters","0.9.18")]
[BepInDependency(Jotunn.Main.ModGuid)]
[BepInIncompatibility("chazman.ArcaneWaterWorks")]
public sealed class Plugin:BaseUnityPlugin
{
    public const string Guid="chazman.ArcaneNaturalWaters";
    internal static BepInEx.Logging.ManualLogSource Log;
    internal static Material Water;
    internal static ConfigEntry<KeyboardShortcut> AddPoint,Finish;
    internal static ConfigEntry<float> Volume;
    internal static ConfigEntry<float> MasterVolume,TallWaterfallHeight;
    internal static Pools.GardenSettings DefaultSettings()=>new Pools.GardenSettings();
    Harmony harmony;
    // Called once for each registered build piece; no per-frame texture loading.
    internal static Sprite BuildIcon(Piece piece,Sprite fallback){
        string key="river-source";
        foreach(string shape in new[]{"round","oval","kidney","wild"})
            if(piece.name.EndsWith("hotspring_"+shape,StringComparison.Ordinal))key="hot-spring-"+shape;
        return BuildIcon(key,fallback);
    }
    internal static Sprite BuildIcon(string key,Sprite fallback){
        using(var stream=typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneWaterWorks.BuildIcons."+key+".png")){
            if(stream==null)return fallback;
            using(var bytes=new System.IO.MemoryStream()){
                stream.CopyTo(bytes);
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name=key,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false)){Object.Destroy(texture);return fallback;}
                var icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
                icon.name=key;return icon;
            }
        }
    }
    void Awake(){
        Log=Logger;GardenBridge.Register();
        MasterVolume=Config.Bind("Audio","AmbienceVolume",1f,new ConfigDescription("Local volume for all water sounds, including pools, waterfalls, rivers and hot springs. Set to zero to mute. ",new AcceptableValueRange<float>(0,1)));
        TallWaterfallHeight=Config.Bind("Audio","TallWaterfallHeight",1.5f,new ConfigDescription("Pool waterfall sound switch height.",new AcceptableValueRange<float>(.25f,10)));
        gameObject.AddComponent<Pools.GardenEditor>();gameObject.AddComponent<Pools.PondNetwork>();
        AddPoint=Config.Bind("Controls","AddRoutePoint",new KeyboardShortcut(KeyCode.F8),"While guiding a river, append a point at your feet.");
        Finish=Config.Bind("Controls","FinishRoute",new KeyboardShortcut(KeyCode.F9),"Finish guiding the river.");
        Volume=Config.Bind("Audio","Volume",.25f,new ConfigDescription("Local running-water volume.",new AcceptableValueRange<float>(0,1)));
        Input.ModalGameplayInput.IsOpen=()=>RiverEditor.Open||ArcaneWaterWorks.Pools.GardenMenuCursor.IsOpen;
        gameObject.AddComponent<RiverEditor>();
        gameObject.AddComponent<RiverRebuildQueue>();
        gameObject.AddComponent<HotSpringEditor>();gameObject.AddComponent<HotSpringAudio>();
        harmony=new Harmony(Guid);harmony.PatchAll();
        PrefabManager.OnVanillaPrefabsAvailable+=Register;
    }
    void OnDestroy(){Pools.GardenEditor.Close();GardenBridge.Unregister();PrefabManager.OnVanillaPrefabsAvailable-=Register;RiverEditor.Close();HotSpringEditor.Close();Input.ModalGameplayInput.Reset();harmony?.UnpatchSelf();}
    void Register(){
        PrefabManager.OnVanillaPrefabsAvailable-=Register;
        try{
            var volume=PrefabManager.Instance.GetPrefab("WaterCube_cave").GetComponentInChildren<WaterVolume>(true);
            Water=volume.m_waterSurface.sharedMaterial;
            var root=PrefabManager.Instance.CreateClonedPrefab("piece_arcanewaterworks_spring","wood_floor");
            for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach(var c in root.GetComponents<Collider>())Object.DestroyImmediate(c);
            var visual=new GameObject("spring_stones");visual.transform.SetParent(root.transform,false);
            var rock=PrefabManager.Instance.GetPrefab("Rock_4");
            var filters=rock.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter source=null;foreach(var f in filters)if(f.sharedMesh&&f.GetComponent<MeshRenderer>()){source=f;break;}
            if(!source)throw new InvalidOperationException("Native spring stone mesh unavailable.");
            for(int i=0;i<5;i++){
                var stone=new GameObject("spring_stone");stone.layer=LayerMask.NameToLayer("piece");stone.transform.SetParent(visual.transform,false);
                float angle=(80+i*50)*Mathf.Deg2Rad;stone.transform.localPosition=new Vector3(Mathf.Sin(angle)*.85f,.1f,Mathf.Cos(angle)*.85f);
                var bounds=source.sharedMesh.bounds;float scale=.65f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                stone.transform.localScale=Vector3.one*scale;stone.transform.localRotation=Quaternion.Euler(0,i*71,0);
                stone.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;stone.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
                var col=stone.AddComponent<BoxCollider>();col.center=bounds.center;col.size=bounds.size;
            }
            var piece=root.GetComponent<Piece>();piece.m_craftingStation=null;piece.m_groundOnly=true;piece.m_groundPiece=false;piece.m_waterPiece=false;piece.m_noInWater=true;piece.m_clipEverything=true;piece.m_canBeRemoved=true;
            // Embed the actual package medallion so local imports cannot lose the artwork.
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="ArcaneNaturalWaters medallion",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            using(var stream=typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneWaterWorks.Icon")){
                if(stream==null)throw new InvalidOperationException("ArcaneNaturalWaters medallion resource missing.");
                using(var bytes=new System.IO.MemoryStream()){
                    stream.CopyTo(bytes);
                    if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidOperationException("ArcaneNaturalWaters medallion could not be decoded.");
                }
            }
            piece.m_icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
            piece.m_icon.name="ArcaneNaturalWaters medallion";
            piece.m_icon=BuildIcon(piece,piece.m_icon);
            var wear=root.GetComponent<WearNTear>();wear.m_new=wear.m_worn=wear.m_broken=visual;wear.m_wet=null;wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;
            wear.m_autoCreateFragments=false;wear.m_fragmentRoots=Array.Empty<GameObject>();wear.m_nonSolidRenderers.Clear();wear.m_supports=false;wear.m_health=300;wear.m_burnable=false;wear.m_noSupportWear=wear.m_noRoofWear=false;
            root.AddComponent<Spring>();
            root.GetComponent<ZNetView>().m_distant=false;
            var config=new PieceConfig{Enabled=true,Name="River Source",Description="An endless spring. Face downhill when placing. Use its stones to shape a river; removing the spring restores the riverbed.",PieceTable="Hammer",Category="Arcane Natural Waters"};
            config.AddRequirement("Stone",20);config.AddRequirement("ElderBark",5);config.AddRequirement("SurtlingCore",1);
            if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new InvalidOperationException("Spring registration rejected.");
            RiverHoe.Register();
            Pools.GardenPrefabs.Water=Water;
            Pools.GardenPrefabs.Rock=rock;
            Pools.GardenPrefabs.Fireflies=PrefabManager.Instance.GetPrefab("FireFlies");
            Pools.GardenPrefabs.Icon=piece.m_icon;
            Pools.RiverGardens.RegisterStone();
            HotSpringPrefabs.Register(piece.m_icon);
            Log.LogInfo("ArcaneNaturalWaters 0.9.18: rivers and four hot springs registered.");
        }catch(Exception ex){Log.LogError("ArcaneNaturalWaters registration failed: "+ex);}
    }
}



