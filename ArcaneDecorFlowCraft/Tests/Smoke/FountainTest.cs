using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke
{
    IEnumerator FountainTest(Vector3 origin){
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+24;
        var prefab=PrefabManager.Instance.GetPrefab(SyncFountain.Id);Check(prefab,"pulse fountain registered");if(!prefab)yield break;
        for(int attempt=0;attempt<40&&Location.IsInsideNoBuildLocation(origin);attempt++)origin+=Vector3.right*15;
        TestFountainPlacement(origin);
        var go=Instantiate(prefab,origin,Quaternion.identity);var fountain=go.GetComponent<SyncFountain>();go.GetComponent<WearNTear>().m_noSupportWear=false;
        var player=Player.m_localPlayer;player.transform.position=origin+Vector3.right*2;player.GetComponent<Rigidbody>().position=player.transform.position;
        yield return null;Physics.SyncTransforms();
        Check(fountain.Live&&fountain.View.IsOwner(),"pulse fountain owns persistent network state");
        Check(go.GetComponent<Piece>().m_category==PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.Straight)).GetComponent<Piece>().m_category,"pulse fountain uses existing FlowCraft category");
        Check(fountain.VisualFlow==0,"dry dish begins idle");
        Check(fountain.GetHoverText().Contains("Shift"),"fountain documents aiming controls");
        for(int i=0;i<24;i++)fountain.Interact(player,false,false);
        yield return null;Check(Math.Abs(fountain.Yaw)<.01,"nozzle rotates full 360 degrees and wraps");
        fountain.Interact(player,false,true);yield return null;Check(Math.Abs(fountain.Tilt-60)<.01,"shift use tilts nozzle");
        var zdo=fountain.View.GetZDO();zdo.Set("fc_tilt",45f);yield return null;
        var parent=go.transform.Find("nozzle");Check(Math.Abs(go.transform.eulerAngles.x)<.01&&parent,"aiming keeps dish flat");
        var jet=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.Powered)),origin+new Vector3(.3f,3,0),Quaternion.Euler(90,0,0));jet.GetComponent<WearNTear>().m_noSupportWear=false;
        var pipe=jet.GetComponent<PipePiece>();pipe.Flow[1]=5;
        var host=new GameObject("isolated trigger driver");var driver=host.AddComponent<FountainTriggers>();driver.enabled=false;
        Physics.SyncTransforms();driver.Tick();Check(fountain.Started>0,"actual pipe water impact triggers dish");
        double first=fountain.Started;driver.Tick();Check(fountain.Started==first,"continuous hit cannot retrigger during reset interval");
        pipe.Flow[1]=0;
        yield return new WaitForSecondsRealtime(.25f);
        Check(go.GetComponentsInChildren<PipeOutlet>().Any(o=>o.Fountain==fountain&&o.Mesh&&o.Mesh.vertexCount>0),"finite fountain burst renders in game");
        CapturePlumbing("pulse-fountain.png",origin+new Vector3(2,1.8f,2),origin+Vector3.up*.3f,true);
        zdo.Set("fc_burst",ZNet.instance.GetTime().Ticks-(long)(3*TimeSpan.TicksPerSecond));driver.Tick();Check(fountain.Age>2.9,"dry dish does not self-trigger");
        pipe.Flow[1]=5;driver.Tick();Check(fountain.Age<.1,"continuous water retriggers after reset");pipe.Flow[1]=0;
        // Test occlusion: solid geometry must stop incoming water before the dish.
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=origin+new Vector3(.3f,1,0);blocker.transform.localScale=Vector3.one*.6f;blocker.layer=LayerMask.NameToLayer("piece");Physics.SyncTransforms();
        zdo.Set("fc_burst",0L);pipe.Flow[1]=5;driver.Tick();Check(fountain.Started==0,"solid blocker prevents fountain triggering");pipe.Flow[1]=0;DestroyImmediate(blocker);
        // Put a second dish at the landing point of the first burst.
        var mouth=fountain.Mouth;var velocity=fountain.Direction*PipeFlowModel.JetSpeed(SyncFountain.BurstFlow);
        float targetY=origin.y+.10f,t=(velocity.y+Mathf.Sqrt(velocity.y*velocity.y+19.62f*(mouth.y-targetY)))/9.81f;
        var landing=mouth+velocity*t+Vector3.down*(4.905f*t*t);
        var next=Instantiate(prefab,new Vector3(landing.x-.3f,origin.y,landing.z),Quaternion.identity);next.GetComponent<WearNTear>().m_noSupportWear=false;var receiver=next.GetComponent<SyncFountain>();
        yield return null;Physics.SyncTransforms();
        zdo.Set("fc_burst",ZNet.instance.GetTime().Ticks);driver.Tick();Check(receiver.Started==0,"chain waits for water travel time");
        zdo.Set("fc_burst",ZNet.instance.GetTime().Ticks-(long)((t+.10f)*TimeSpan.TicksPerSecond));driver.Tick();Check(receiver.Started>0,"burst arriving at second dish triggers cascade");
        zdo.Set("fc_burst",0L);receiver.View.GetZDO().Set("fc_burst",0L);yield return null;
        Check(fountain.VisualFlow==0&&receiver.VisualFlow==0,"stopped fountain burst has no ongoing supply");
        ZNetScene.instance.Destroy(jet);ZNetScene.instance.Destroy(go);ZNetScene.instance.Destroy(next);Destroy(host);yield return null;
    }
    void TestFountainPlacement(Vector3 origin){
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var player=Player.m_localPlayer;var camera=GameCamera.instance.transform;
        var oldPlayer=player.transform.position;var oldCamera=camera.position;var oldRotation=camera.rotation;
        var field=typeof(Player).GetField("m_buildPieces",flags);var previous=field.GetValue(player);
        var floor=Instantiate(PrefabManager.Instance.GetPrefab("stone_floor_2x2"),origin,Quaternion.identity);floor.GetComponent<WearNTear>().m_noSupportWear=false;
        var bounds=floor.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger).bounds;
        var aim=new Vector3(bounds.center.x,bounds.max.y,bounds.center.z);
        var prefab=PrefabManager.Instance.GetPrefab(SyncFountain.Id);var piece=prefab.GetComponent<Piece>();
        try{
            player.AddKnownItem(PrefabManager.Instance.GetPrefab("Copper").GetComponent<ItemDrop>().m_itemData);
            typeof(Player).GetMethod("UpdateKnownRecipesList",flags).Invoke(player,null);
            var table=PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            typeof(Player).GetMethod("SetPlaceMode",flags).Invoke(player,new object[]{table});
            player.SetBuildCategory(piece.m_category);Check(player.SetSelectedPiece(piece),"select pulse fountain through actual Hammer table");
            var ghost=(GameObject)typeof(Player).GetField("m_placementGhost",flags).GetValue(player);
            Check(ghost,"Hammer creates pulse fountain preview");if(!ghost)return;
            camera.position=aim+new Vector3(0,2,-2);camera.LookAt(aim);player.transform.position=aim+new Vector3(0,0,-3);player.GetComponent<Rigidbody>().position=player.transform.position;
            Physics.SyncTransforms();typeof(Player).GetMethod("UpdatePlacementGhost",flags).Invoke(player,new object[]{false});
            var status=(Player.PlacementStatus)typeof(Player).GetField("m_placementStatus",flags).GetValue(player);
            Check(ghost.activeInHierarchy&&Vector3.Distance(ghost.transform.position,aim)<.1f,"Hammer preview sits visibly at aimed stone floor (distance "+Vector3.Distance(ghost.transform.position,aim)+")");
            Check(status==Player.PlacementStatus.Valid,"Hammer accepts pulse fountain on stone floor ("+status+")");
            Check(ghost.GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled&&r.gameObject.activeInHierarchy)>=2,"Hammer preview retains both dish and nozzle renderers");
            if(status==Player.PlacementStatus.Valid&&Vector3.Distance(ghost.transform.position,aim)<.1f){
                var existing=new HashSet<SyncFountain>(SyncFountain.Active);player.PlacePiece(piece,ghost.transform.position,ghost.transform.rotation,false);
                var placed=SyncFountain.Active.FirstOrDefault(f=>f.Live&&!existing.Contains(f));Check(placed&&Vector3.Distance(placed.transform.position,aim)<.1f,"native PlacePiece creates fountain on selected floor");
                if(placed)ZNetScene.instance.Destroy(placed.gameObject);
            }
        }finally{
            typeof(Player).GetMethod("SetPlaceMode",flags).Invoke(player,new[]{previous});
            camera.position=oldCamera;camera.rotation=oldRotation;player.transform.position=oldPlayer;player.GetComponent<Rigidbody>().position=oldPlayer;ZNetScene.instance.Destroy(floor);
        }
    }

}
