using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;
namespace RunicLilyPads
{
    public sealed class GardenPiece : MonoBehaviour, Hoverable, Interactable
    {
        internal static readonly List<GardenPiece> Ponds=new List<GardenPiece>();
        public int Shape=-1;
        internal GardenSettings Settings=new GardenSettings();
        internal bool Live=>view&&view.IsValid();
        internal float WaterHeight=>transform.position.y-.15f;
        ZNetView view;GameObject generated;AudioSource audioSource;ParticleSystem[] particles=Array.Empty<ParticleSystem>();
        string last="";float check;int playingTrack=-1;bool registered;bool ambienceOn;
        GardenSettings pending;float pendingDeadline;
        readonly List<Object> assets=new List<Object>();
        internal void Own(Object obj)=>assets.Add(obj);
        readonly List<Object> surfaceAssets=new List<Object>();
        internal void OwnSurface(Object obj)=>surfaceAssets.Add(obj);
        void ClearSurface(){foreach(var a in surfaceAssets)if(a)Object.Destroy(a);surfaceAssets.Clear();}
        void Start(){view=GetComponent<ZNetView>(); Settings=Plugin.DefaultSettings(); Poll(true);}
        void Update()
        {
            if(Time.time>=check){check=Time.time+.5f;Poll(false);SettleControlStone();}
            if(pending!=null){
                if(!CanEdit()||Time.time>pendingDeadline)pending=null;
                else if(view.IsOwner()){view.GetZDO().Set("arcane_garden_settings",pending.Encode());pending=null;Poll(true);}
            }
            if(!Live||!Player.m_localPlayer)return;
            bool near=Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<Settings.Range+10;
            bool active=near&&(!Settings.NightOnly||EnvMan.IsNight());
            if(active!=ambienceOn){ambienceOn=active;foreach(var p in particles){if(active&&Settings.Fireflies>0)p.Play();else p.Stop(true,ParticleSystemStopBehavior.StopEmitting);}}
            if(audioSource){
                if(active&&Settings.Track>0&&playingTrack!=Settings.Track){audioSource.Stop();audioSource.clip=GardenAudio.Get(Settings.Track);playingTrack=Settings.Track;}
                float target=active&&Settings.Track>0?Settings.Volume*Plugin.MasterVolume.Value:0;
                // Nearby sound emitters share the loudness budget to avoid stacked full-volume loops.
                if(active){int nearby=0;foreach(var g in Active)if(g&&g.Live&&g.Settings.Track>0&&g.Settings.Volume>0&&(!g.Settings.NightOnly||EnvMan.IsNight())&&Vector3.Distance(Player.m_localPlayer.transform.position,g.transform.position)<g.Settings.Range)nearby++;target/=Mathf.Sqrt(Mathf.Clamp(nearby,1,4));}
                audioSource.volume=Mathf.MoveTowards(audioSource.volume,target,Time.deltaTime*.3f);
                audioSource.pitch=Settings.Speed;
                if(active&&Settings.Track>0&&audioSource.clip&&!audioSource.isPlaying)audioSource.Play();
                if((!active||Settings.Track==0)&&audioSource.volume<=.001f)audioSource.Stop();
            }
        }
        void SettleControlStone(){
            if(!Live||Shape<0)return;var stone=transform.Find("garden_visual/control_stone");if(!stone)return;
            var p=stone.position;if(PondTerrain.Sample(p,out float ground)){p.y=ground+.015f;if((stone.position-p).sqrMagnitude>.000001f)stone.position=p;}
        }
        internal static readonly List<GardenPiece> Active=new List<GardenPiece>();
        internal string SoundStatus
        {
            get{
                if(Settings.Track==0)return "Sound is off.";
                if(Settings.Volume<=0||Plugin.MasterVolume.Value<=0)return "Sound is muted (volume is zero).";
                if(AudioListener.volume<=0||AudioListener.pause)return "Game audio is muted or paused.";
                if(Settings.NightOnly&&!EnvMan.IsNight())return "Waiting for night. Turn off Night only and save to play now.";
                if(!audioSource)return "Audio source is not ready.";
                if(!audioSource.clip)return "Waiting for nearby listener / recording.";
                return audioSource.isPlaying?"Playing night_ambience_"+Settings.Track.ToString("000"):"Sound is outside its active range.";
            }
        }
        void Poll(bool force)
        {
            if(!view)return;
            if(Live){
                if(!registered){registered=true;Active.Add(this);if(Shape>=0)Ponds.Add(this);}
                string raw=view.GetZDO().GetString("arcane_garden_settings","");
                if(raw==""&&view.IsOwner()){view.GetZDO().Set("arcane_garden_settings",Settings.Encode());raw=Settings.Encode();}
                if(force||raw!=last){if(GardenSettings.TryDecode(raw,out var loaded)){if(raw!="")Settings=loaded;last=raw;Rebuild();}else Plugin.Log.LogWarning("Invalid pond settings retained without applying.");}
            }else if(!generated)Rebuild();
        }
        void Rebuild()
        {
            ClearSurface();
            if(generated){generated.SetActive(false);Object.Destroy(generated);}foreach(var a in assets)if(a)Object.Destroy(a);assets.Clear();
            generated=new GameObject("garden_generated");generated.SetActive(false);generated.transform.SetParent(transform,false);
            if(Shape>=0){PondBuilder.Build(this,generated.transform,Live);var stone=transform.Find("garden_visual/control_stone");if(stone)stone.localPosition=new Vector3(-Settings.Width*.57f,.02f,0);}
            if(Live&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){
                audioSource=generated.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.loop=true;audioSource.spatialBlend=1;audioSource.rolloffMode=AudioRolloffMode.Linear;audioSource.minDistance=2;audioSource.maxDistance=Settings.Range;audioSource.volume=0;audioSource.dopplerLevel=0;playingTrack=-1;
                if(GardenPrefabs.Fireflies){
                    // Clone only the particle child: exclude native ZNetView and 120-second TimedDestruction.
                    var source=GardenPrefabs.Fireflies.GetComponentInChildren<ParticleSystem>(true);
                    if(source){var fire=Object.Instantiate(source.gameObject,generated.transform,false);fire.transform.localPosition=Vector3.up*.8f;
                        foreach(var script in fire.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(script);
                        particles=fire.GetComponentsInChildren<ParticleSystem>(true);
                        foreach(var p in particles){p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=p.main;main.loop=true;main.playOnAwake=false;main.maxParticles=Settings.Fireflies;main.startLifetime=6;main.simulationSpace=ParticleSystemSimulationSpace.Local;
                            ConfigureFireflyColor(p);
                            var emission=p.emission;emission.rateOverTime=Settings.Fireflies/6f;
                            var shape=p.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(Settings.Width,.9f,Settings.Width*(float)PondMath.Aspect(Shape));}
                    }
                }
            }
            ambienceOn=false;generated.SetActive(true);if(Shape>=0&&Live){PondTerrain.Refresh(transform.position,18);PondNetwork.MarkDirty();}
        }
        internal static void ConfigureFireflyColor(ParticleSystem p)
        {
            // Keep the native additive glow and motion, replacing its blue starting color.
            var main=p.main;main.startColor=new Color(1f,.92f,.02f,1f);
            var fade=new Gradient();fade.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(1,.8f),new GradientAlphaKey(0,1)});
            var lifetime=p.colorOverLifetime;lifetime.enabled=true;lifetime.color=fade;
            var speed=p.colorBySpeed;speed.enabled=false;
        }
        internal bool CanEdit()=>Live&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<Settings.Width+6&&PrivateArea.CheckAccess(transform.position,Shape>=0?Settings.Width*.7f:0,false);
        internal bool Save(GardenSettings settings)
        {
            if(!settings.Valid||!CanEdit()||!PrivateArea.CheckAccess(transform.position,Shape>=0?settings.Width*.7f:0,true))return false;
            if(Shape>=0&&!settings.SameGroup(PondNetwork.GroupSettings(this))){
                foreach(var pond in PondNetwork.Group(this))if(!PrivateArea.CheckAccess(pond.transform.position,pond.Settings.Width*.7f,true))return false;
                settings.GroupRevision=Math.Max(DateTime.UtcNow.Ticks,PondNetwork.GroupSettings(this).GroupRevision+1);
            }
            pending=settings.Copy();pendingDeadline=Time.time+5;view.ClaimOwnership();return true;
        }
        public string GetHoverName()=>Shape<0?global::Runic.Localization.RunicText.Get("text_c92e4ced068f"):global::Runic.Localization.RunicText.Get("text_98ab40b0cda3");
        public float GetHoverOffset()=>0;
        public string GetHoverText()=>GetHoverName()+global::Runic.Localization.RunicText.Get("text_d5c2bb3c1387");
        public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit())return false;GardenEditor.Open(this);return true;}
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        internal void SyncGroup(GardenSettings settings){
            if(!Live||!view.IsOwner()||Settings.GroupRevision>=settings.GroupRevision)return;
            Settings.CopyGroupFrom(settings);view.GetZDO().Set("arcane_garden_settings",Settings.Encode());last=Settings.Encode();
        }
        internal void RefreshSurface(){
            if(!Live||Shape<0||!generated)return;
            var surface=generated.transform.Find("pond_water");if(surface){surface.gameObject.SetActive(false);Object.Destroy(surface.gameObject);}
            ClearSurface();PondBuilder.Build(this,generated.transform,true);
        }
        void OnDestroy(){ClearSurface();if(GardenEditor.Target==this)GardenEditor.Close();bool pond=Ponds.Remove(this);Active.Remove(this);foreach(var a in assets)if(a)Object.Destroy(a);if(pond){PondTerrain.Refresh(transform.position,18);PondNetwork.MarkDirty();}}
    }
}

