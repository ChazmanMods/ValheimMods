using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ArcaneWaterWorks;
using Jotunn.Managers;
using RunicLilyPads;
using UnityEngine;

public sealed partial class Smoke
{
    IEnumerator HotSpringsTest(){
        var player=Player.m_localPlayer;if(!player){Check(false,"hot springs require isolated world");yield break;}
        var origin=player.transform.position+new Vector3(10,0,0);
        if(ZoneSystem.instance.GetGroundHeight(origin,out float ground))origin.y=ground;
        // Clear vegetation only in this disposable fixture so captures and placement
        // rays cannot be occluded by random test-world trees or bushes.
        foreach(var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)){
            if(!view||!view.IsValid()||(view.transform.position-origin).sqrMagnitude>40*40)continue;
            if(view.GetComponent<TreeBase>()||view.GetComponent<TreeLog>()||view.GetComponent<Pickable>()||view.GetComponent<Destructible>()&&!view.GetComponent<Piece>()&&!view.GetComponent<Character>())ZNetScene.instance.Destroy(view.gameObject);
        }
        yield return null;
        for(int shape=0;shape<4;shape++){
            var prefab=PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_"+HotSpringPrefabs.Shapes[shape]);
            Check(prefab&&prefab.GetComponent<GardenPiece>().Shape==shape,"hot spring shape registered "+shape);if(!prefab)continue;
            var root=Instantiate(prefab,origin,Quaternion.identity);yield return new WaitForSecondsRealtime(1.3f);
            var hot=root.GetComponent<HotSpring>();
            Check(hot.Live&&hot.Garden.Depth==1.4f&&hot.Garden.Track==0,"hot spring defaults / live ZDO "+shape);
            Check(hot.Seats.Length==HotSpringSettings.SeatCount(6,shape),"proportional initial seats "+shape);
            Check(hot.Seats.All(s=>s.m_attachPoint&&s.transform.Find("rock_001")&&s.transform.Find("rock_001").GetComponent<MeshFilter>().sharedMesh.name=="rock_001"),"original white round stones / hidden seats "+shape);
            Check(!root.GetComponent<Smelter>()&&!root.GetComponent<Fireplace>()&&root.GetComponent<Piece>().m_comfort==2,"fuel-free native comfort "+shape);
            Check(root.GetComponentsInChildren<EffectArea>().Any(a=>a.m_type.HasFlag(EffectArea.Type.WarmCozyArea)),"native warm-and-cozy bath area "+shape);
            var effects=root.GetComponentsInChildren<ParticleSystem>().Where(p=>p.name.StartsWith("vfx_bath_mist")||p.name.StartsWith("bubbles")).ToArray();
            Check(effects.Length==2&&effects.All(p=>p.main.maxParticles<=(p.name.StartsWith("vfx_bath_mist")?160:128)&&!p.collision.enabled),"bounded native steam and bubbles "+shape);
            Check(root.GetComponentsInChildren<WaterVolume>().Length==1,"one pool water trigger "+shape);
            var liquid=root.GetComponentInChildren<WaterVolume>();
            Check(Math.Abs(liquid.GetWaterSurface(origin)-hot.Water)<.02f,"pool liquid matches native surface "+shape);
            var corner=origin+new Vector3(2.95f,0,shape==1?1.92f:2.95f);
            Check(liquid.GetWaterSurface(corner)<-1000,"pool bounding-box dry corners are masked "+shape);
            if(shape==0){
                player.transform.position=origin+new Vector3(-3,1,0);player.GetComponent<Rigidbody>().position=player.transform.position;
                var settings=hot.Settings.Copy();settings.Sound=4;settings.Music=2;settings.Warded=true;
                var garden=hot.Garden.Copy();garden.Width=12;
                Check(hot.Save(garden,settings),"save pool size / sound / music / ward");yield return new WaitForSecondsRealtime(1.5f);
                Check(hot.Seats.Length==12&&hot.Settings.Warded&&hot.Settings.Sound==4&&hot.Settings.Music==2,"saved hot spring settings applied and seats expanded");
                var lily=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_lily_blue_001"),origin+Vector3.down,Quaternion.identity);yield return null;
                Check(Math.Abs(lily.transform.Find("lily_visual").position.y-hot.Water-.015f)<.03f,"WaterGardens lily floats in hot spring");
                var camera=GameCamera.instance.transform;var cp=camera.position;var cr=camera.rotation;
                var field=typeof(Player).GetField("m_placementGhost",BindingFlags.Instance|BindingFlags.NonPublic);var previousGhost=field.GetValue(player);
                foreach(var c in lily.GetComponentsInChildren<Collider>())c.enabled=false;
                try{
                    camera.position=origin+Vector3.up*5;camera.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);field.SetValue(player,lily);
                    var args=new object[]{Vector3.zero,Vector3.zero,null,null,null,true};
                    bool hit=(bool)typeof(Player).GetMethod("PieceRayTest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,args);
                    Check(hit&&Math.Abs(((Vector3)args[0]).y-hot.Water)<.04f&&args[4] is BoxCollider,"native decoration placement hits hot spring with no surface mesh colliders");
                }finally{field.SetValue(player,previousGhost);camera.SetPositionAndRotation(cp,cr);ZNetScene.instance.Destroy(lily);}
                Check(hot.Protects(origin)&&!hot.Protects(origin+new Vector3(30,0,0)),"bounded ward footprint");
                Check(EffectArea.IsPointInsideNoMonsterArea(origin),"native creature avoidance area active");
                var boar=Instantiate(PrefabManager.Instance.GetPrefab("Boar"),origin,Quaternion.identity);
                yield return new WaitForSecondsRealtime(1);
                Check(!hot.Protects(boar.transform.position)&&boar.GetComponent<Character>().GetHealth()>0,"ward moves creature out without damage");ZNetScene.instance.Destroy(boar);
                var seat=hot.Seats[0];player.transform.position=seat.m_attachPoint.position+Vector3.right*.1f;player.GetComponent<Rigidbody>().position=player.transform.position;
                seat.Interact(player,false,false);yield return new WaitForSecondsRealtime(.3f);
                Check(seat.IsInUse(),"native chair interaction attaches seated player");
                var larger=garden.Copy();larger.Width=16;Check(!hot.Save(larger,settings),"occupied pool cannot resize seats");player.AttachStop();
                HotSpringEditor.Show(hot);yield return null;
                GameCamera.instance.UpdateMouseCapture();
                Check(GardenMenuCursor.IsOpen&&Cursor.visible&&Cursor.lockState==CursorLockMode.None&&ZCursor.IsRequested,"hot spring menu keeps native and hardware cursor visible");
                CaptureMenu("hot-spring-menu.png");
                typeof(HotSpringEditor).GetMethod("Page",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{1});CaptureMenu("hot-spring-sound-menu.png");
                typeof(HotSpringEditor).GetMethod("Page",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{2});CaptureMenu("hot-spring-garden-menu.png");
                HotSpringEditor.Close();Check(!GardenMenuCursor.IsOpen,"cursor menu lease released");
                player.transform.position=origin+Vector3.up*.2f;player.GetComponent<Rigidbody>().position=player.transform.position;
                yield return new WaitForSecondsRealtime(2);
                var audio=UnityEngine.Object.FindFirstObjectByType<HotSpringAudio>();
                var music=(AudioSource)typeof(HotSpringAudio).GetField("music",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(audio);
                var native=(AudioSource)typeof(MusicMan).GetField("m_musicSource",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(UnityEngine.Object.FindFirstObjectByType<MusicMan>());
                Check(music.isPlaying&&music.clip.name=="AW_HSMusic_002.mp3"&&native.mute,"pool music replaces native track");
                player.transform.position=origin+new Vector3(25,2,0);player.GetComponent<Rigidbody>().position=player.transform.position;
                yield return new WaitForSecondsRealtime(2.5f);Check(!music.isPlaying&&!native.mute,"leaving pool restores Valheim music");
                for(int id=1;id<=5;id++){
                    yield return (IEnumerator)typeof(HotSpringAudio).GetMethod("EnsureLoop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(audio,new object[]{id});
                    var clips=(System.Collections.Generic.Dictionary<int,AudioClip>)typeof(HotSpringAudio).GetField("loops",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    Check(clips.TryGetValue(id,out var clip)&&clip&&clip.length>6,"native audio decoder plays bubbling loop "+id);
                }
                for(int id=1;id<=6;id++){
                    AudioClip decoded=null;Action<AudioClip> done=c=>decoded=c;
                    yield return (IEnumerator)typeof(HotSpringAudio).GetMethod("Load",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(audio,new object[]{"AW_HSMusic_"+id.ToString("000")+".mp3",AudioType.MPEG,true,done});
                    Check(decoded&&decoded.channels==2&&decoded.length>180,"full stereo music recording decodes "+id);if(decoded)Destroy(decoded);
                }
                CaptureHotSpring(hot);
            }
            ZNetScene.instance.Destroy(root);yield return new WaitForSecondsRealtime(.5f);
        }
        Check(HotSpring.All.Count==0,"destroying pools releases hot spring registry");
        var ordinary=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_nightgarden"),player.transform.position,Quaternion.identity);yield return new WaitForSecondsRealtime(.6f);
        GardenEditor.Open(ordinary.GetComponent<GardenPiece>());yield return null;GameCamera.instance.UpdateMouseCapture();
        Check(GardenMenuCursor.IsOpen&&Cursor.visible&&ZCursor.IsRequested&&Cursor.lockState==CursorLockMode.None,"original WaterGardens menu keeps cursor visible");
        GardenEditor.Close();ZNetScene.instance.Destroy(ordinary);
    }
    void CaptureHotSpring(HotSpring pool){
        var go=new GameObject("hot_spring_capture");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
        camera.transform.position=pool.transform.position+new Vector3(-12,13,-13);camera.transform.LookAt(pool.transform.position);camera.fieldOfView=55;
        var rt=new RenderTexture(1280,960,24);rt.Create();camera.targetTexture=rt;var previous=RenderTexture.active;var texture=new Texture2D(1280,960,TextureFormat.RGB24,false);
        try{camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,960),0,0);texture.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../hot-spring-preview.png"),texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(texture);Destroy(go);}
    }
}
