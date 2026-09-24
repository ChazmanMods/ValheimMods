using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke
{
    int worldErrors;
    IEnumerator CheckWorld(string[] ids,Assembly asm)
    {
        Logger.LogInfo("WORLD TEST loading isolated local world (no public or open server)");
        var world=World.GetCreateWorld("WaterGardensRegression022",FileHelpers.FileSource.Local);
        Game.SetProfile("watergardens_regression022",FileHelpers.FileSource.Local);
        ZNet.SetServer(true,false,false,"WaterGardensRegression022","",world);
        ZNet.ResetServerHost();
        typeof(FejdStartup).GetMethod("LoadMainScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
        float until=Time.realtimeSinceStartup+180;
        while((!Game.instance||!ZNetScene.instance||!Player.m_localPlayer)&&Time.realtimeSinceStartup<until)yield return null;
        if(!Player.m_localPlayer){worldErrors++;Logger.LogError("WORLD FAIL no player/world ready");yield break;}
        if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../native-survey"))){SurveyNative();yield break;}
        var player=Player.m_localPlayer;
        player.SetGodMode(true);typeof(Player).GetField("m_enableAutoPickup",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;
        until=Time.realtimeSinceStartup+150;
        while((player.InIntro()||!player.IsOnGround())&&Time.realtimeSinceStartup<until)yield return null;
        yield return new WaitForSecondsRealtime(2);
        var position=player.transform.position+new Vector3(8,0,0);
        if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../presentation-only"))){
            // Move beyond the spawn stone ring, then wait for terrain streaming.
            player.transform.position=new Vector3(80,50,0);
            yield return new WaitForSecondsRealtime(8);
            position=player.transform.position+new Vector3(12,0,0);
            // This private test world persists across runs. Remove abandoned test
            // placements before evaluating a fresh two-pond join.
            foreach(string type in new[]{"GardenPiece","GardenFloat","GardenFeature","LilyFloat"})foreach(var component in UnityEngine.Object.FindObjectsOfType(asm.GetType("RunicLilyPads."+type))){
                if(!component)continue;
                var placed=((Component)component).gameObject;var view=placed.GetComponent<ZNetView>();
                if(view&&view.IsValid()&&view.IsOwner())ZNetScene.instance.Destroy(placed);
            }
            yield return new WaitForSecondsRealtime(2);
        }
        Physics.SyncTransforms();
        if(Physics.Raycast(position+Vector3.up*100,Vector3.down,out var groundHit,300,LayerMask.GetMask("terrain")))position=groundHit.point;
        else{worldErrors++;Logger.LogError("WORLD FAIL no loaded terrain under test location");yield break;}
        if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../waterfall-audio-only"))){yield return StartCoroutine(CheckWaterfallAudio(asm,player,position));yield break;}
        if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../performance-only"))){yield return StartCoroutine(CheckPerformance(asm,position));yield break;}
        if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../presentation-only"))){player.transform.position=position+new Vector3(-2,1,0);if(System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../water-only"))){yield return StartCoroutine(CheckPondControls(asm,player,position));yield break;}yield return StartCoroutine(CheckWind(asm,ids,position));yield return StartCoroutine(CheckFlora(asm,position));yield return StartCoroutine(CheckShoreAndStone(asm,position));yield return StartCoroutine(CheckPondControls(asm,player,position));yield return StartCoroutine(CheckWaterfallAudio(asm,player,position));yield return StartCoroutine(CheckFeatures(asm,player));yield break;}
        Logger.LogInfo("WORLD TEST terrain position "+position+" player "+player.transform.position);
        var visualMethod=typeof(WearNTear).GetMethod("SetHealthVisual",BindingFlags.Instance|BindingFlags.NonPublic);
        var contactMethod=asm.GetType("RunicLilyPads.PondPlacement").GetMethod("HasSurfaceContact",BindingFlags.Static|BindingFlags.NonPublic);
        for(int i=0;i<ids.Length;i++){
            GameObject placed=null;ZDO zdo=null;int before=0,expected=0;bool removed=false;
            try{
                placed=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab(ids[i]),position,Quaternion.identity);
                var view=placed.GetComponent<ZNetView>();zdo=view.GetZDO();
                if(!view.IsValid()||!view.IsOwner())throw new Exception("not a live owned piece");
                typeof(Piece).GetField("m_creator",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(placed.GetComponent<Piece>(),1L);
                zdo.Set(ZDOVars.s_creator,1L);
                var wear=placed.GetComponent<WearNTear>();
                if(wear.m_autoCreateFragments||wear.m_fragmentRoots.Length!=0)throw new Exception("fragment references remain");
                foreach(float health in new[]{1f,.5f,.1f}){visualMethod.Invoke(wear,new object[]{health,false});if(!wear.m_new.activeSelf)throw new Exception("health visual inactive");}
                if(!placed.GetComponent(asm.GetType("RunicLilyPads.LilyFloat"))){
                    Physics.SyncTransforms();
                    if(!(bool)contactMethod.Invoke(null,new object[]{placed,true}))throw new Exception("ground contact rejected");
                    placed.transform.position=position+Vector3.up*2;Physics.SyncTransforms();
                    if((bool)contactMethod.Invoke(null,new object[]{placed,true}))throw new Exception("floating base accepted");
                    placed.transform.position=position;Physics.SyncTransforms();
                    if(ids[i].EndsWith("ivy")){
                        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=position+Vector3.up*2+Vector3.forward*.5f;wall.transform.localScale=new Vector3(3,3,1);wall.layer=LayerMask.NameToLayer("piece");
                        placed.transform.position=position+Vector3.up*2;Physics.SyncTransforms();
                        if(!(bool)contactMethod.Invoke(null,new object[]{placed,false}))throw new Exception("wall contact rejected");
                        wall.transform.position+=Vector3.forward*4;Physics.SyncTransforms();
                        if((bool)contactMethod.Invoke(null,new object[]{placed,false}))throw new Exception("floating ivy accepted");
                        UnityEngine.Object.Destroy(wall);placed.transform.position=position;
                    }
                }
                foreach(var r in placed.GetComponent<Piece>().m_resources)if(r.m_resItem&&r.m_recover)expected+=r.m_amount;
                before=CountDrops(position);
                wear.Remove(); // The actual vanilla hammer removal RPC, including refunds.
                removed=true;
            }catch(Exception e){worldErrors++;Logger.LogError("WORLD FAIL "+ids[i]+" "+e);if(placed)ZNetScene.instance.Destroy(placed);}
            yield return new WaitForSecondsRealtime(.6f);
            if(placed){worldErrors++;Logger.LogError("WORLD FAIL object survived removal "+ids[i]);ZNetScene.instance.Destroy(placed);}
            else if(zdo!=null&&removed){
                int returned=CountDrops(position)-before;
                if(returned!=expected){worldErrors++;Logger.LogError("WORLD FAIL refund count "+ids[i]+" expected="+expected+" actual="+returned);}
                else Logger.LogInfo("WORLD PASS placement, health states, native removal/refund "+ids[i]+" returned="+returned);
            }
        }
        GameObject stale=null;int drops=CountDrops(position);
        try{
            stale=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab(ids[4]),position,Quaternion.identity);
            stale.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_health,0f);
        }catch(Exception e){worldErrors++;Logger.LogError("WORLD FAIL stale setup "+e);}
        yield return new WaitForSecondsRealtime(1);
        if(stale||CountDrops(position)!=drops){worldErrors++;Logger.LogError("WORLD FAIL stale refunded piece cleanup");}
        else Logger.LogInfo("WORLD PASS previously refunded zero-health piece removed without another refund");
        yield return StartCoroutine(CheckPondUse(asm,player));
        yield return StartCoroutine(CheckFeatures(asm,player));
        Logger.LogInfo("WORLD COMPLETE errors="+worldErrors);
    }
    static int CountDrops(Vector3 p){int count=0;foreach(var item in UnityEngine.Object.FindObjectsOfType<ItemDrop>())count+=item.m_itemData.m_stack;return count;}
}




