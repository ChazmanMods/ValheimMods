using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator ExpansionTest(Vector3 origin){
        for(int index=6;index<PipePrefabs.Keys.Length;index++){
            var kind=(PipeKind)index;var prefab=PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind));Check(prefab,"new prefab "+kind);
            var pipe=prefab.GetComponent<PipePiece>();var piece=prefab.GetComponent<Piece>();var mesh=prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            Check(pipe.Kind==kind&&piece.m_icon&&piece.m_icon.name.Contains("model icon"),"new model icon and identity "+kind);
            Check(prefab.GetComponent<WearNTear>().m_noSupportWear,"new piece uses structural integrity "+kind);
            var wide=PipeAssets.Load(PipePrefabs.Keys[(int)PipePrefabs.BaseKind(kind)]).Mesh;
            if(!PipePrefabs.Dragon(kind)&&PipePrefabs.Narrow(kind)){
                Check(Math.Abs(mesh.bounds.size.x-wide.bounds.size.x*.5f)<.002,"half diameter "+kind);
                for(int port=0;port<PipePiece.PortCount(kind);port++)Check(PipePiece.LocalPort(kind,port)==PipePiece.LocalPort(PipePrefabs.BaseKind(kind),port),"unchanged connection length "+kind+"/"+port);
                if(PipePrefabs.BaseKind(kind)==PipeKind.Intake)Check(Math.Abs(mesh.bounds.size.y-wide.bounds.size.y)<.002,"intake keeps full height");
                else if(!PipePrefabs.Elbow(kind))Check(Math.Abs(mesh.bounds.size.z-wide.bounds.size.z)<.002,"straight pipe keeps full length "+kind);
            }
        }
        Check((PipeAssets.Load("dragon").Mesh.bounds.size-PipeAssets.Load("dragon_narrow").Mesh.bounds.size*2).magnitude<.001,"large dragon is twice small dragon size");
        foreach(var id in new[]{PipePrefabs.CopperItem,PipePrefabs.Copper4Item,PipePrefabs.PoweredItem,PipePrefabs.Powered2Item,PipePrefabs.Powered4Item})Check(ObjectDB.instance.GetItemPrefab(PipePrefabs.ItemId(id,true)),"independent narrow crafting item "+id);
        var narrowRecipes=ObjectDB.instance.m_recipes.Where(r=>r.m_item&&r.m_item.name.EndsWith("4cm",StringComparison.Ordinal)).ToArray();
        Check(narrowRecipes.Length==6,"six narrow recipes");
        Check(narrowRecipes.Any(r=>r.m_item.name==PipePrefabs.PoweredItem+"4cm"&&r.m_amount==4&&r.m_resources.Any(x=>x.m_resItem.name==PipePrefabs.Copper4Item+"4cm"&&x.m_amount==1)),"narrow 4m recipe yields four powered 1m pipes");
        var nativeWall=PrefabManager.Instance.GetPrefab("stone_wall_2x1");Check(nativeWall,"native stone wall available");if(!nativeWall)yield break;
        for(int attempt=0;attempt<20&&Location.IsInsideNoBuildLocation(origin);attempt++)origin+=Vector3.right*15;
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+24;
        Player.m_localPlayer.transform.position=origin+new Vector3(4,2,4);Player.m_localPlayer.GetComponent<Rigidbody>().position=Player.m_localPlayer.transform.position;
        foreach(bool narrow in new[]{false,true}){
            var created=new List<GameObject>();
            PipePiece Place(PipeKind kind,Vector3 p,Quaternion q){var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),p,q);created.Add(go);return go.GetComponent<PipePiece>();}
            var wall=Instantiate(nativeWall,origin,Quaternion.identity);created.Add(wall);wall.GetComponent<WearNTear>().m_noSupportWear=false;
            wall.transform.localScale=new Vector3(1,1,narrow?2:1);TestDragonHammer(narrow?PipeKind.DragonNarrow:PipeKind.Dragon,wall);
            Physics.SyncTransforms();var bounds=wall.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger).bounds;
            var dragon=Place(narrow?PipeKind.DragonNarrow:PipeKind.Dragon,new Vector3(bounds.center.x,bounds.center.y,bounds.max.z+.015f),Quaternion.identity);
            Physics.SyncTransforms();Check(DragonMount.Find(dragon,out var mount),"dragon mounts to actual stone collider "+narrow);
            foreach(var material in new[]{WearNTear.MaterialType.Stone,WearNTear.MaterialType.Marble,WearNTear.MaterialType.Iron}){
                wall.GetComponent<WearNTear>().m_materialType=material;Check(DragonMount.Find(dragon,out _),"supported mount material "+material);
            }
            wall.GetComponent<WearNTear>().m_materialType=WearNTear.MaterialType.Wood;Check(!DragonMount.Find(dragon,out _),"wood rejected as fountain mount");wall.GetComponent<WearNTear>().m_materialType=WearNTear.MaterialType.Stone;
            var pump=Place(narrow?PipeKind.PoweredNarrow:PipeKind.Powered,mount.Back+Vector3.forward*.015f-Vector3.forward*.5f,Quaternion.identity);
            pump.transform.rotation=Quaternion.Euler(0,15,0);
            Check(SnapDetachedPreview(pump)&&Vector3.Dot(pump.Direction(1),mount.Normal)>.9999f,"wall snap squares perpendicular pipe to surface");
            var intake=Place(narrow?PipeKind.IntakeNarrow:PipeKind.Intake,pump.Port(0),Quaternion.Euler(90,0,0));
            var waterOrigin=intake.transform.TransformPoint(new Vector3(0,-1.6f,0))+Vector3.up*.1f;
            // Native water fixture is independent of optional natural-water contents.
            var water=NativeTestWater(waterOrigin);yield return Settle();Physics.SyncTransforms();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.5f);
            Check(dragon.Connected[0]&&pump.Connected[1],"straight pipe connects through wall "+narrow);
            Check(dragon.Flow[1]>4.99f&&pump.Flow[1]==0,"mouth is the sole terminal outlet "+narrow);
            Check(pump.Running&&pump.PowerLight&&pump.PowerLight.enabled&&pump.PowerLight.color==Color.red,"powered orb red light runs with flow "+narrow);
            var powerMaterial=PipeAssets.Load(narrow?"powered_narrow":"powered").OrbMaterial;
            Check(powerMaterial.HasProperty("_EmissionMap")&&powerMaterial.GetTexture("_EmissionMap"),"native shader supports masked orb emission");
            var block=new MaterialPropertyBlock();pump.transform.Find("plumbing_visual").GetComponent<MeshRenderer>().GetPropertyBlock(block,1);Check(block.GetColor("_EmissionColor").r>1f&&block.GetColor("_EmissionColor").r<3&&block.GetColor("_EmissionColor").g==0,"orb emission is red");
            var stream=dragon.GetComponentInChildren<PipeOutlet>();Check(stream&&stream.Mesh,"dragon mouth renders running stream");
            if(stream)Check((dragon.Port(1)-dragon.transform.position).magnitude>(narrow?.4f:.8f),"stream starts at mouth ahead of plate");
            // A nearby wrong-size pipe must not match either the rear fitting or a normal pipe socket.
            var wrong=Place(narrow?PipeKind.Straight:PipeKind.StraightNarrow,pump.transform.position,Quaternion.identity);
            Check(!DragonMount.Target(wrong,1,dragon,mount,out _,out _),"wrong diameter rejected at wall");wrong.gameObject.SetActive(false);ZNetScene.instance.Destroy(wrong.gameObject);
            // Turn the same line parallel to the wall; snap it to the computed automatic elbow.
            pump.transform.rotation=Quaternion.LookRotation(Vector3.right);DragonMount.Target(pump,1,dragon,mount,out var target,out bool bend);
            Check(bend,"parallel line requests a 90 degree fitting");pump.transform.position=target-pump.transform.rotation*PipePiece.LocalPort(pump.Kind,1)+Vector3.up*.08f;
            // Release old network pair before exercising the actual placement snap.
            Check(SnapDetachedPreview(pump),"rear wall connection snaps from nearby cursor position");
            Check(Vector3.Distance(pump.Port(1),target)<.001,"elbow inlet is flush at calculated wall clearance");
            intake.transform.rotation=Quaternion.FromToRotation(Vector3.up,Vector3.right);intake.transform.position=pump.Port(0);
            water.transform.position=intake.transform.TransformPoint(new Vector3(0,-1.6f,0))+Vector3.up*.1f;
            Physics.SyncTransforms();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.5f);
            Check(dragon.Connected[0]&&dragon.Flow[1]>4.99f,"parallel pipe supplies mouth through wall "+narrow);
            Check(dragon.WallAdapter&&dragon.WallAdapter.gameObject.activeSelf,"automatic elbow visible "+narrow);
            if(dragon.WallAdapter){var fitting=dragon.WallAdapter.GetComponent<MeshFilter>().sharedMesh;var v=fitting.vertices;var ns=fitting.normals;var ts=fitting.triangles;Check(Vector3.Dot(Vector3.Cross(v[ts[1]]-v[ts[0]],v[ts[2]]-v[ts[0]]),ns[ts[0]])>0,"automatic fitting winding faces outward");}
            CapturePlumbing(narrow?"dragon-4cm-front.png":"dragon-8cm-front.png",dragon.transform.position+new Vector3(2,1,3),dragon.transform.position+Vector3.forward*.3f);
            CapturePlumbing(narrow?"dragon-4cm-rear.png":"dragon-8cm-rear.png",dragon.transform.position+new Vector3(-3,2,-4),mount.Back);
            // Rotate all placed objects around the same wall; the fitting must follow its geometry.
            var turn=Quaternion.Euler(0,37,0);
            foreach(var go in created)if(go){go.transform.position=origin+turn*(go.transform.position-origin);go.transform.rotation=turn*go.transform.rotation;}
            water.transform.position=origin+turn*(water.transform.position-origin);Physics.SyncTransforms();PipeNetwork.Recalculate();
            Check(DragonMount.Find(dragon,out _)&&dragon.Connected[0]&&dragon.Flow[1]>4.99f,"rotated wall preserves fitting and water supply");
            intake.transform.position+=Vector3.up*5;Physics.SyncTransforms();PipeNetwork.Recalculate();pump.UpdatePowerIndicator(true);
            Check(!pump.Running&&(!pump.PowerLight||!pump.PowerLight.enabled)&&dragon.Flow[1]==0,"dry line extinguishes orb and fountain");
            wall.SetActive(false);Physics.SyncTransforms();PipeNetwork.Recalculate();Check(!dragon.Connected[0]&&dragon.Flow[1]==0,"removing wall breaks hidden connection");
            Check(!dragon.WallAdapter||!dragon.WallAdapter.gameObject.activeSelf,"removed mount hides automatic fitting");
            foreach(var go in created)if(go&&go.GetComponent<ZNetView>().IsValid())ZNetScene.instance.Destroy(go);Destroy(water);yield return Settle();
            origin+=Vector3.right*8;
        }
    }
    bool SnapDetachedPreview(PipePiece pipe){
        // A real Hammer preview is not part of the live flow graph. Detach the
        // reused fixture piece before testing the same preview snap operation.
        bool enabled=pipe.enabled;pipe.enabled=false;
        try{return DragonMount.Snap(pipe);}finally{pipe.enabled=enabled;}
    }
    void TestDragonHammer(PipeKind kind,GameObject wall){
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var ghostField=typeof(Player).GetField("m_placementGhost",flags);var statusField=typeof(Player).GetField("m_placementStatus",flags);
        var update=typeof(Player).GetMethod("UpdatePlacementGhost",flags);var player=Player.m_localPlayer;var camera=GameCamera.instance.transform;
        var oldGhost=ghostField.GetValue(player);var oldCamera=camera.position;var oldRotation=camera.rotation;var oldPlayer=player.transform.position;GameObject ghost=null;
        try{
            Physics.SyncTransforms();var bounds=wall.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger).bounds;
            var aim=new Vector3(bounds.center.x+.25f,bounds.center.y+.2f,bounds.max.z);camera.position=aim+Vector3.forward*3;camera.LookAt(aim);player.transform.position=camera.position+Vector3.down;player.GetComponent<Rigidbody>().position=player.transform.position;
            ZNetView.m_forceDisableInit=true;
            try{ghost=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),aim+Vector3.up*20,Quaternion.identity);}finally{ZNetView.m_forceDisableInit=false;}
            foreach(var child in ghost.GetComponentsInChildren<Transform>(true))child.gameObject.layer=LayerMask.NameToLayer("Ignore Raycast");
            ghostField.SetValue(player,ghost);Physics.SyncTransforms();update.Invoke(player,new object[]{false});
            var status=(Player.PlacementStatus)statusField.GetValue(player);Check(status==Player.PlacementStatus.Valid,"native hammer accepts dragon on wall "+kind+" ("+status+")");
            Check(Math.Abs(ghost.transform.position.z-bounds.max.z-.015f)<.02&&Vector3.Dot(ghost.transform.forward,Vector3.forward)>.99f,"hammer aligns dragon mounting plate flush to wall");
            var visibleCenter=ghost.transform.TransformPoint(PipeAssets.Load(PipePrefabs.Keys[(int)kind]).Mesh.bounds.center);
            Check(Math.Abs(visibleCenter.x-bounds.center.x)<.01&&Math.Abs(visibleCenter.y-bounds.center.y)<.01,"off-centre cursor still places fountain at block centre");
        }finally{ghostField.SetValue(player,oldGhost);if(ghost)DestroyImmediate(ghost);camera.position=oldCamera;camera.rotation=oldRotation;player.transform.position=oldPlayer;player.GetComponent<Rigidbody>().position=oldPlayer;}
    }
    GameObject NativeTestWater(Vector3 position){
        var go=new GameObject("dragon test water");go.SetActive(false);go.layer=LayerMask.NameToLayer("WaterVolume");go.transform.position=position;
        var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.center=Vector3.down;box.size=new Vector3(3,3,3);
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=ArcaneDecorFlowCraft.Plugin.Water;
        var volume=go.AddComponent<WaterVolume>();volume.m_waterSurface=renderer;volume.m_forceDepth=0;volume.m_useGlobalWind=false;volume.m_surfaceOffset=0;go.SetActive(true);return go;
    }
}
