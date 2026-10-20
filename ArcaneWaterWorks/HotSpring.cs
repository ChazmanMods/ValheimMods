using System;
using System.Collections.Generic;
using Jotunn.Managers;
using ArcaneWaterWorks.Pools;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ArcaneWaterWorks;
internal sealed class HotSpring:MonoBehaviour,IGardenController
{
    internal static readonly List<HotSpring> All=new List<HotSpring>();
    internal GardenPiece Pond;
    internal HotSpringSettings Settings=new HotSpringSettings();
    internal GardenSettings Garden;
    internal ZNetView View;
    internal bool Live=>View&&View.IsValid();
    internal float Water=>GardenIntegration.WaterHeight(Pond);
    internal float WardRadius=>Garden.Width*.5f+4;
    internal Chair[] Seats=Array.Empty<Chair>();
    GameObject generated;
    GameObject ward;
    ParticleSystem steam,bubbles;
    Vector3[] emitPoints=Array.Empty<Vector3>();
    float check,nextParticles,steamCredit,bubbleCredit;
    float builtWidth=-1,builtDepth=-1;
    bool registered;
    static Material softSteam;
    public string PoolName=>"Arcane Hot Spring — "+HotSpringPrefabs.Shapes[Mathf.Clamp(Pond?Pond.Shape:GetComponent<GardenPiece>().Shape,0,3)];
    public void ConfigureDefaults(GardenSettings settings){settings.Track=0;settings.Width=6;settings.Depth=1.4f;settings.Fireflies=25;}
    public bool OpenMenu(){HotSpringEditor.Show(this);return true;}
    public void BuildLiquid(Transform surface,MeshRenderer renderer){
        if(!Pond)Pond=GetComponent<GardenPiece>();var settings=GardenIntegration.Settings(Pond);
        HotSpringShoreline.Extend(Pond,renderer.GetComponent<MeshFilter>().sharedMesh,surface);
        HotSpringSurface.Attach(Pond,renderer);
        var go=new GameObject("hot_spring_water_volume"){layer=LayerMask.NameToLayer("WaterVolume")};go.SetActive(false);go.transform.SetParent(surface,false);
        var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.center=new Vector3(0,-settings.Depth*.5f,0);box.size=new Vector3(settings.Width,settings.Depth+.4f,settings.Width*(Pond.Shape==1?.65f:1));
        var volume=go.AddComponent<WaterVolume>();volume.m_waterSurface=renderer;volume.m_forceDepth=0;volume.m_useGlobalWind=true;volume.m_surfaceOffset=0;
        var liquid=go.AddComponent<RiverLiquid>();liquid.Pond=Pond;go.SetActive(true);Physics.SyncTransforms();liquid.WorldBounds=box.bounds;
    }
    void Start(){Pond=GetComponent<GardenPiece>();View=GetComponent<ZNetView>();Garden=GardenIntegration.Settings(Pond);}
    internal bool Contains(Vector3 position,float margin=0)=>Pond&&Garden!=null&&Mathf.Abs(position.y-Water)<Garden.Depth+3&&GardenIntegration.Distance(Pond,position)<=1+margin/Mathf.Max(2,Garden.Width*.5f);
    internal bool Protects(Vector3 position)=>Live&&Settings.Warded&&Garden!=null&&Mathf.Abs(position.y-Water)<12&&new Vector2(position.x-transform.position.x,position.z-transform.position.z).sqrMagnitude<WardRadius*WardRadius;
    internal bool Occupied(){foreach(var seat in Seats)if(seat&&seat.IsInUse())return true;return false;}
    internal bool Save(GardenSettings garden,HotSpringSettings settings){
        if(!Live||!settings.Valid||garden.Depth<.8f||!GardenIntegration.CanEdit(Pond))return false;
        if((garden.Width!=Garden.Width||garden.Depth!=Garden.Depth)&&Occupied()){
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"Wait until everyone leaves their sitting stone before resizing the pool.");return false;
        }
        if(settings.Warded&&!PrivateArea.CheckAccess(transform.position,garden.Width*.5f+4,true))return false;
        return GardenIntegration.Save(Pond,garden,settings.Encode());
    }
    void Update(){
        if(!Pond)return;
        if(Time.time>=check){
            check=Time.time+.5f;
            Garden=GardenIntegration.Settings(Pond);
            if(Live){
                if(!registered){registered=true;All.Add(this);}
                if(HotSpringSettings.TryDecode(View.GetZDO().GetString("arcane_pool_controller",""),out var loaded))Settings=loaded;
            }
            if((builtWidth!=Garden.Width||builtDepth!=Garden.Depth)&&!Occupied())Rebuild();
            UpdateWard();
            if(Live&&Player.m_localPlayer&&Contains(Player.m_localPlayer.transform.position))Player.m_localPlayer.OnNearFire(transform.position);
            if(Live&&Settings.Warded)RepelCreatures();
        }
        if(!Live||!steam||!bubbles)return;
        bool nearby=Player.m_localPlayer&&(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude<3600;
        if(!nearby){if(steam.isPlaying)steam.Stop();if(bubbles.isPlaying)bubbles.Stop();return;}
        if(!steam.isPlaying)steam.Play();if(!bubbles.isPlaying)bubbles.Play();
        if(Time.time<nextParticles)return;nextParticles=Time.time+.1f;
        float scale=Mathf.Clamp(Garden.Width/6,1,3);
        Emit(steam,ref steamCredit,Settings.Steam*scale*3f,.12f);
        Emit(bubbles,ref bubbleCredit,Settings.Bubbles*scale*1.4f,-.06f);
    }
    void Emit(ParticleSystem system,ref float credit,float rate,float height){
        credit+=rate;int count=Mathf.Min(8,(int)credit);credit-=count;
        for(int i=0;i<count;i++){
            var point=emitPoints[UnityEngine.Random.Range(0,emitPoints.Length)];point.y=Water-transform.position.y+height;
            var p=new ParticleSystem.EmitParams{position=point,applyShapeToPosition=false};system.Emit(p,1);
        }
    }
    void Rebuild(){
        if(generated){generated.SetActive(false);Destroy(generated);}
        generated=new GameObject("hotspring_seats_and_heat");generated.SetActive(false);generated.transform.SetParent(transform,false);
        builtWidth=Garden.Width;builtDepth=Garden.Depth;
        int count=HotSpringSettings.SeatCount(Garden.Width,Pond.Shape);Seats=new Chair[count];
        for(int i=0;i<count;i++){
            float angle=(i+.5f)*Mathf.PI*2/count;
            // Follow the bed contour so flat stones remain supported even in a deep pool.
            var position=GardenIntegration.Boundary(Pond.Shape,Garden.Width,angle,HotSpringSettings.SeatContour(Garden.Depth));
            var seat=new GameObject("Submerged sitting stone "+(i+1)){layer=LayerMask.NameToLayer("piece")};
            seat.transform.SetParent(generated.transform,false);seat.transform.localPosition=position;
            seat.transform.localRotation=Quaternion.LookRotation(-new Vector3(position.x,0,position.z));
            var stone=GardenIntegration.WhiteSittingStone(seat.transform);
            foreach(var collider in stone.GetComponents<Collider>())DestroyImmediate(collider);
            var bounds=stone.GetComponent<MeshFilter>().sharedMesh.bounds;
            float width=Mathf.Min(.9f,Garden.Width*.15f);
            stone.transform.localScale=new Vector3(width/bounds.size.x,.25f/bounds.size.y,width/bounds.size.z);
            float top=-.15f-Mathf.Min(.45f,Garden.Depth*.4f);
            stone.transform.localPosition=new Vector3(-bounds.center.x*stone.transform.localScale.x,top-bounds.max.y*stone.transform.localScale.y,-bounds.center.z*stone.transform.localScale.z);
            var solid=seat.AddComponent<BoxCollider>();solid.center=new Vector3(0,top-.125f,0);solid.size=new Vector3(width,.25f,width);
            var anchor=new GameObject("hidden_sitting_point");anchor.transform.SetParent(seat.transform,false);anchor.transform.localPosition=new Vector3(0,top-.45f,0);
            var chair=seat.AddComponent<Chair>();chair.m_name="Hot spring sitting stone";chair.m_attachPoint=anchor.transform;chair.m_useDistance=3;
            chair.m_detachOffset=new Vector3(0,1.1f,.8f);Seats[i]=chair;
        }
        if(Live){
            var heat=new GameObject("natural_hot_spring_warmth"){layer=LayerMask.NameToLayer("character_trigger")};heat.transform.SetParent(generated.transform,false);
            var box=heat.AddComponent<BoxCollider>();box.isTrigger=true;box.center=new Vector3(0,-Garden.Depth*.5f,0);box.size=new Vector3(Garden.Width, Garden.Depth+2,Garden.Width*(Pond.Shape==1?.65f:1));
            var area=heat.AddComponent<EffectArea>();area.m_type=EffectArea.Type.Heat|EffectArea.Type.WarmCozyArea;area.m_playerOnly=true;
        }
        if(Live&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){
            var tub=PrefabManager.Instance.GetPrefab("piece_bathtub");
            steam=CloneParticles(tub,"vfx_bath_mist",160);bubbles=CloneParticles(tub,"bubbles",128);
            var points=new List<Vector3>();for(int i=0;i<96;i++){
                float angle=i*2.399963f,fraction=.86f*Mathf.Sqrt((i+.5f)/96);
                points.Add(GardenIntegration.Boundary(Pond.Shape,Garden.Width,angle,fraction));
            }emitPoints=points.ToArray();
        }
        generated.SetActive(true);
    }
    ParticleSystem CloneParticles(GameObject prefab,string name,int limit){
        ParticleSystem source=null;
        foreach(var p in prefab.GetComponentsInChildren<ParticleSystem>(true))if(p.name==name){source=p;break;}
        if(!source){Plugin.Log.LogWarning("Hot spring effect unavailable: "+name);return null;}
        var clone=Object.Instantiate(source.gameObject,generated.transform,false);
        // Particle assets only: no native smoke damage, audio or fuel machinery.
        foreach(var script in clone.GetComponentsInChildren<MonoBehaviour>(true))DestroyImmediate(script);
        foreach(var collider in clone.GetComponentsInChildren<Collider>(true))DestroyImmediate(collider);
        for(int i=clone.transform.childCount-1;i>=0;i--)DestroyImmediate(clone.transform.GetChild(i).gameObject);
        clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;clone.transform.localScale=Vector3.one;
        var ps=clone.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.playOnAwake=false;main.maxParticles=limit;main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var collision=ps.collision;collision.enabled=false;
        if(name=="vfx_bath_mist"){
            // The bathtub's world-space mist planes expose rectangular edges on broad pools.
            // Keep native particle rendering, but give rising steam a transparent round falloff.
            if(!softSteam){
                ParticleSystem bubbleSource=null;foreach(var p in prefab.GetComponentsInChildren<ParticleSystem>(true))if(p.name=="bubbles"){bubbleSource=p;break;}
                if(bubbleSource){
                    softSteam=new Material(bubbleSource.GetComponent<ParticleSystemRenderer>().sharedMaterial){name="ArcaneNaturalWaters soft steam"};
                    var texture=new Texture2D(64,64,TextureFormat.RGBA32,false){name="Hot spring soft steam falloff",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                    var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++){
                        float px=(x-31.5f)/31.5f,py=(y-31.5f)/31.5f,r2=px*px+py*py;
                        float fade=Mathf.Clamp01(1-r2);fade=fade*fade*fade;
                        float cloud=.55f+.45f*Mathf.PerlinNoise(x*.09f,y*.09f);
                        pixels[y*64+x]=new Color(1,1,1,fade*cloud);
                    }texture.SetPixels(pixels);texture.Apply(false,true);softSteam.mainTexture=texture;
                    if(softSteam.HasProperty("_TintColor"))softSteam.SetColor("_TintColor",Color.white);
                    if(softSteam.HasProperty("_Color"))softSteam.SetColor("_Color",Color.white);
                }
            }
            var renderer=ps.GetComponent<ParticleSystemRenderer>();if(softSteam)renderer.sharedMaterial=softSteam;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.alignment=ParticleSystemRenderSpace.View;
            main.startLifetime=new ParticleSystem.MinMaxCurve(4,6);main.startSize=new ParticleSystem.MinMaxCurve(1.1f,1.8f);main.startSpeed=0;
            main.startColor=new Color(.9f,.95f,1,.24f);main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            var sheet=ps.textureSheetAnimation;sheet.enabled=false;
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.x=0;velocity.y=.25f;velocity.z=0;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.7f,1,1.8f));
            var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.2f),new GradientAlphaKey(0,1)});color.color=gradient;
        }
        clone.SetActive(true);return ps;
    }
    void RepelCreatures(){
        foreach(var creature in Character.GetAllCharacters()){
            if(!creature||creature.IsPlayer()||!creature.IsOwner()||!Protects(creature.transform.position))continue;
            var delta=creature.transform.position-transform.position;delta.y=0;if(delta.sqrMagnitude<.01f)delta=Vector3.forward;
            Vector3 outside=transform.position+delta.normalized*(WardRadius+2);
            outside.y=creature.transform.position.y;
            if(ZoneSystem.instance&&ZoneSystem.instance.GetGroundHeight(outside,out float ground))outside.y=Mathf.Max(outside.y,ground+.3f);
            creature.transform.position=outside;
            var body=creature.GetComponent<Rigidbody>();if(body){body.position=outside;body.linearVelocity=Vector3.zero;}
        }
    }
    void UpdateWard(){
        if(!Live||!Settings.Warded){if(ward){ward.SetActive(false);Destroy(ward);ward=null;}return;}
        if(ward&&Mathf.Approximately(ward.GetComponent<SphereCollider>().radius,WardRadius))return;
        if(ward){ward.SetActive(false);Destroy(ward);}
        ward=new GameObject("hot_spring_creature_ward"){layer=LayerMask.NameToLayer("character_trigger")};ward.SetActive(false);ward.transform.SetParent(transform,false);
        var volume=ward.AddComponent<SphereCollider>();volume.isTrigger=true;volume.radius=WardRadius;
        var area=ward.AddComponent<EffectArea>();area.m_type=EffectArea.Type.NoMonsters;ward.SetActive(true);
    }
    void OnDestroy(){
        if(HotSpringEditor.Target==this)HotSpringEditor.Close();All.Remove(this);
        // Native seating handles the usual dismount; release the local player before destroying an occupied pool.
        foreach(var seat in Seats)if(seat&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,seat.m_attachPoint.position)<.15f)Player.m_localPlayer.AttachStop();
    }
}
