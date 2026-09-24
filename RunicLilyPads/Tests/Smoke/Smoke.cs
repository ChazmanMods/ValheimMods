using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;
[BepInPlugin("chazman.WaterGardensSmoke","WaterGardens smoke test","1.0.0")]
[BepInDependency("chazman.RunicLilyPads")]
public partial class Smoke:BaseUnityPlugin
{
    bool ready;
    void Awake(){
        // The client build only parses -savedir in its dedicated-server path.
        string root=System.IO.Path.GetFullPath(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../saves"));
        System.IO.Directory.CreateDirectory(root);Utils.SetSaveDataPath(root);
        PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;StartCoroutine(Run());
    }
    IEnumerator Run(){
        float until=Time.realtimeSinceStartup+100;
        while(!ready&&Time.realtimeSinceStartup<until)yield return null;
        yield return new WaitForSecondsRealtime(3);
        if(!ready){Logger.LogError("SMOKE FAIL assets timed out");Application.Quit();yield break;}
        var ids=new[]{"piece_runic_lilypad_small","piece_runic_lilypad_broad","piece_runic_lilypad_white","piece_runic_lilypad_pink_cluster","piece_arcanedecor_reeds","piece_arcanedecor_ferns","piece_arcanedecor_swamp_ferns","piece_arcanedecor_shrub","piece_arcanedecor_flowers","piece_arcanedecor_dandelion","piece_arcanedecor_ivy","piece_arcanedecor_stone","piece_arcanedecor_stepping_stone","piece_arcanedecor_pebbles","piece_arcanedecor_nightgarden","piece_arcanedecor_pond_round","piece_arcanedecor_pond_oval","piece_arcanedecor_pond_kidney","piece_arcanedecor_pond_wild","piece_runic_lilypad_white_bud","piece_runic_lilypad_white_open","piece_arcanedecor_mossrock_001","piece_arcanedecor_mossrock_002","piece_arcanedecor_fountain_001","piece_arcanedecor_fountain_002","piece_arcanedecor_fountain_004","piece_arcanedecor_fountain_005","piece_arcanedecor_cattail_001","piece_arcanedecor_rock_001","piece_arcanedecor_rock_002","piece_arcanedecor_rock_003","piece_arcanedecor_lantern_001","piece_arcanedecor_lily_blue_001","piece_arcanedecor_lily_blue_002","piece_arcanedecor_lily_blue_003","piece_arcanedecor_lily_white_001","piece_arcanedecor_lily_white_002","piece_arcanedecor_lily_white_003","piece_arcanedecor_fern_001","piece_arcanedecor_fern_002","piece_arcanedecor_fauna_001","piece_arcanedecor_glowshrooms_001","piece_arcanedecor_reeds_006","piece_arcanedecor_fern_red_001","piece_arcanedecor_grass_plains_gold","piece_arcanedecor_grass_plains_green","piece_arcanedecor_grass_blackforest","piece_arcanedecor_bush_forest","piece_arcanedecor_bush_plains","piece_arcanedecor_yggdrasil_001","piece_arcanedecor_yggdrasil_002","piece_arcanedecor_yggdrasil_003"};
        int errors=0;
        foreach(string id in ids){var p=PrefabManager.Instance.GetPrefab(id);if(!p||p.GetComponentsInChildren<MeshFilter>(true).Length==0){Logger.LogError("SMOKE FAIL prefab "+id);errors++;}else Logger.LogInfo("SMOKE PASS prefab "+id);}
        var asm=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="ArcaneDecorWaterGardens");
        try{var audio=asm.GetType("RunicLilyPads.GardenAudio").GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Static);for(int i=1;i<=9;i++){var clip=(AudioClip)audio.Invoke(null,new object[]{i});if(!clip||clip.samples<100)throw new Exception("clip "+i);Logger.LogInfo("SMOKE PASS audio "+clip.name+" "+clip.length);}}
        catch(Exception e){errors++;Logger.LogError("SMOKE FAIL audio "+e);}
        var audioTest=new GameObject("embedded_audio_playback_test");var output=audioTest.AddComponent<AudioSource>();output.loop=true;output.volume=0;
        for(int track=1;track<=9;track++){
            output.clip=(AudioClip)asm.GetType("RunicLilyPads.GardenAudio").GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{track});
            output.Play();yield return new WaitForSecondsRealtime(.3f);
            if(!output.isPlaying||output.timeSamples<=0){errors++;Logger.LogError("SMOKE FAIL playback "+track);}
            else Logger.LogInfo("SMOKE PASS embedded playback "+track+" samples="+output.timeSamples);
            output.Stop();
        }
        UnityEngine.Object.Destroy(audioTest);
        try {
            var source=PrefabManager.Instance.GetPrefab("FireFlies").GetComponentInChildren<ParticleSystem>(true);
            var fire=UnityEngine.Object.Instantiate(source.gameObject);var ps=fire.GetComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            asm.GetType("RunicLilyPads.GardenPiece").GetMethod("ConfigureFireflyColor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ps});
            ps.Emit(1);var buffer=new ParticleSystem.Particle[2];ps.GetParticles(buffer);Color color=buffer[0].startColor;
            if(color.r<.95f||color.g<.85f||color.b>.1f)throw new Exception("wrong particle color "+color);
            Logger.LogInfo("SMOKE PASS emitted yellow particle "+color);UnityEngine.Object.Destroy(fire);
        } catch(Exception e){errors++;Logger.LogError("SMOKE FAIL fireflies "+e);}
        GameObject ghost=null;
        try{ZNetView.m_forceDisableInit=true;ghost=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"));ghost.transform.position=new Vector3(0,-10000,0);}
        finally{ZNetView.m_forceDisableInit=false;}
        yield return new WaitForSecondsRealtime(2);
        if(ghost&&ghost.transform.Find("garden_generated/pond_water"))Logger.LogInfo("SMOKE PASS pond ghost generated in Unity");else{errors++;Logger.LogError("SMOKE FAIL pond ghost");}
        if(ghost)UnityEngine.Object.Destroy(ghost);
        for(int shape=0;shape<4;shape++) {
            GameObject test=null;
            try {
                ZNetView.m_forceDisableInit=true;
                test=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab(ids[15+shape]));
                test.transform.position=new Vector3(5000+shape*100,1000,5000);
            } finally { ZNetView.m_forceDisableInit=false; }
            yield return null;
            try {
                var piece=test.GetComponent(asm.GetType("RunicLilyPads.GardenPiece"));
                var holder=new GameObject("water_physics_test");holder.SetActive(false);holder.transform.SetParent(test.transform,false);
                asm.GetType("RunicLilyPads.PondBuilder").GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{piece,holder.transform,true});
                holder.SetActive(true);Physics.SyncTransforms();
                var mat=holder.transform.Find("pond_water").GetComponent<MeshRenderer>().sharedMaterial;
                if(mat.GetFloat("_DepthFade")<15||mat.GetColor("_ColorTop").g<=mat.GetColor("_ColorTop").r)throw new Exception("cave water properties retained");
                var pos=test.transform.position;
                float inside=Floating.GetLiquidLevel(pos+new Vector3(.1f,-.5f,.1f),1,LiquidType.Water);
                float outside=Floating.GetLiquidLevel(pos+new Vector3(10,-.5f,10),1,LiquidType.Water);
                if(Math.Abs(inside-999.85f)>.01f||outside>-1000)throw new Exception("water bounds "+inside+" / "+outside);
                if(holder.GetComponentsInChildren<WaterVolume>().Length!=32)throw new Exception("missing volumes");
                Logger.LogInfo("SMOKE PASS actual water physics shape "+shape);
            } catch(Exception e){errors++;Logger.LogError("SMOKE FAIL water physics "+e);}
            UnityEngine.Object.Destroy(test);yield return null;
        }
        yield return StartCoroutine(CheckWorld(ids,asm));errors+=worldErrors;
        Logger.LogInfo(errors==0?"SMOKE COMPLETE PASS":"SMOKE COMPLETE FAIL "+errors);
        yield return new WaitForSecondsRealtime(1);Application.Quit();
    }
}
