using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace ArcaneWaterWorks;
internal sealed class HotSpringAudio:MonoBehaviour
{
    internal static readonly string[] SoundNames={"Off","Quiet bubbles","Mineral spring","Gentle simmer","Hidden grotto","Deep relaxation"};
    static HotSpringAudio instance;
    static readonly Dictionary<int,AudioClip> loops=new Dictionary<int,AudioClip>();
    readonly HashSet<int> loading=new HashSet<int>();
    AudioSource ambience,music,preview;
    AudioSource mutedNative;
    bool nativeWasMuted;
    int wantedMusic=-1,currentMusic=-1,musicRequest;
    float scan,previewUntil;
    HotSpring audible,inside;
    static bool Headless=>SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null;
    void Awake(){instance=this;if(Headless)return;ambience=Source("Bubbling hot spring",1);music=Source("Hot spring music",0);preview=Source("Hot spring sound preview",0);}
    AudioSource Source(string name,float spatial){var go=new GameObject(name);go.transform.SetParent(transform,false);var source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=true;source.spatialBlend=spatial;source.dopplerLevel=0;source.volume=0;source.rolloffMode=AudioRolloffMode.Linear;return source;}
    internal static void Preview(int id,float volume,float speed){
        if(!instance||Headless)return;instance.StartCoroutine(instance.PlayPreview(id,volume,speed));
    }
    IEnumerator PlayPreview(int id,float volume,float speed){
        StopPreview();if(id<=0)yield break;float requested=previewUntil=Time.unscaledTime+10;
        yield return EnsureLoop(id);
        if(previewUntil!=requested||!loops.TryGetValue(id,out var clip)||!clip)yield break;
        preview.clip=clip;WaterAudio.SetVolume(preview,volume*Plugin.MasterVolume.Value*Plugin.Volume.Value);preview.pitch=speed;preview.Play();previewUntil=Time.unscaledTime+8;
    }
    internal static void StopPreview(){if(instance){instance.previewUntil=0;if(instance.preview)instance.preview.Stop();}}
    IEnumerator EnsureLoop(int id){
        if(id<=0||loops.ContainsKey(id))yield break;
        if(!loading.Add(id)){while(loading.Contains(id))yield return null;yield break;}
        yield return Load("spring_"+id.ToString("000")+".ogg",AudioType.OGGVORBIS,false,clip=>loops[id]=clip);
        loading.Remove(id);
    }
    IEnumerator Load(string name,AudioType type,bool streaming,Action<AudioClip> done){
        string path=Path.Combine(Paths.CachePath,"ArcaneNaturalWaters-0.5.0",name);
        try{
            if(!File.Exists(path)){
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var resource=typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneWaterWorks.HotSprings."+name)){
                    if(resource==null)throw new FileNotFoundException(name);
                    using(var output=File.Create(path+".tmp"))resource.CopyTo(output);
                }
                File.Move(path+".tmp",path);
            }
        }catch(Exception error){Plugin.Log.LogError("Hot spring audio: "+error.Message);done(null);yield break;}
        using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,type)){
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio=streaming;
            yield return request.SendWebRequest();
            if(request.result!=UnityWebRequest.Result.Success){Plugin.Log.LogError("Hot spring audio: "+request.error);done(null);}
            else{var clip=DownloadHandlerAudioClip.GetContent(request);clip.name=name;done(clip);}
        }
    }
    void Update(){
        if(Headless||!ambience)return;
        if(preview.isPlaying&&Time.unscaledTime>=previewUntil)preview.Stop();
        if(Time.unscaledTime>=scan){
            scan=Time.unscaledTime+.25f;audible=null;inside=null;float best=float.MaxValue,bestInside=float.MaxValue;
            var player=Player.m_localPlayer;
            if(player&&!player.IsDead())foreach(var pool in HotSpring.All){
                if(!pool||!pool.Live||pool.Garden==null)continue;
                float distance=(pool.transform.position-player.transform.position).sqrMagnitude;
                if(pool.Settings.Sound>0&&pool.Settings.Volume>0&&distance<pool.Garden.Range*pool.Garden.Range&&distance<best){best=distance;audible=pool;}
                if(pool.Contains(player.transform.position)&&distance<bestInside){bestInside=distance;inside=pool;}
            }
            if(audible&&!loops.ContainsKey(audible.Settings.Sound)&&!loading.Contains(audible.Settings.Sound))StartCoroutine(EnsureLoop(audible.Settings.Sound));
            int desired=inside?inside.Settings.Music:0;
            if(desired!=wantedMusic){wantedMusic=desired;musicRequest++;}
        }
        AudioClip wanted=null;if(audible)loops.TryGetValue(audible.Settings.Sound,out wanted);
        bool same=ambience.clip==wanted;
        float target=audible&&wanted&&same?audible.Settings.Volume*Plugin.MasterVolume.Value*Plugin.Volume.Value:0;
        WaterAudio.SetVolume(ambience,target,Time.unscaledDeltaTime*.5f);
        if(ambience.volume<=.001f&&!same){ambience.Stop();ambience.clip=wanted;if(wanted)ambience.Play();}
        if(audible){ambience.transform.position=audible.transform.position;ambience.minDistance=audible.Garden.Width*.5f;ambience.maxDistance=Mathf.Max(ambience.minDistance+1,audible.Garden.Range);ambience.pitch=audible.Garden.Speed;}
        bool musicSame=currentMusic==wantedMusic;
        float musicTarget=inside&&musicSame&&music.clip?inside.Settings.MusicVolume*MusicMan.m_masterMusicVolume:0;
        music.volume=Mathf.MoveTowards(music.volume,musicTarget,Time.unscaledDeltaTime*.5f);
        if(music.volume<=.001f&&!musicSame){
            music.Stop();if(music.clip)Destroy(music.clip);music.clip=null;currentMusic=wantedMusic;
            if(wantedMusic>0)StartCoroutine(ChangeMusic(wantedMusic,musicRequest));
        }
        if(!music.isPlaying)RestoreNative();
    }
    IEnumerator ChangeMusic(int id,int request){
        AudioClip clip=null;yield return Load("AW_HSMusic_"+id.ToString("000")+".mp3",AudioType.MPEG,true,result=>clip=result);
        if(!this||request!=musicRequest||wantedMusic!=id){if(clip)Destroy(clip);yield break;}
        music.clip=clip;if(clip)music.Play();
    }
    internal static void OverrideMusic(AudioSource source){
        if(!instance||!instance.music||!instance.music.isPlaying||!source)return;
        if(instance.mutedNative!=source){instance.RestoreNative();instance.mutedNative=source;instance.nativeWasMuted=source.mute;}
        instance.music.outputAudioMixerGroup=source.outputAudioMixerGroup;
        source.mute=true;
    }
    void RestoreNative(){if(mutedNative)mutedNative.mute=nativeWasMuted;mutedNative=null;}
    void OnDisable(){RestoreNative();if(music)music.Stop();if(ambience)ambience.Stop();StopPreview();}
    void OnDestroy(){RestoreNative();if(music&&music.clip)Destroy(music.clip);foreach(var clip in loops.Values)if(clip)Destroy(clip);loops.Clear();if(instance==this)instance=null;}
}

[HarmonyPatch(typeof(MusicMan),"UpdateMusic")]
internal static class HotSpringMusicOverride
{
    static void Postfix(AudioSource ___m_musicSource)=>HotSpringAudio.OverrideMusic(___m_musicSource);
}

[HarmonyPatch(typeof(SpawnSystem),"IsSpawnPointGood")]
internal static class HotSpringSpawnWard
{
    static void Postfix(Vector3 spawnPoint,ref bool __result){
        if(!__result)return;foreach(var pool in HotSpring.All)if(pool&&pool.Protects(spawnPoint)){__result=false;break;}
    }
}
