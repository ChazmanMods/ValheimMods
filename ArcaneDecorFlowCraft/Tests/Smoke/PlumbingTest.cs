using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArcaneDecorFlowCraft;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator PlumbingTest(Vector3 origin){
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        // This fixture uses separate saves. Clear only its nearby trees and shrubs for a meaningful visual review.
        foreach(var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)){
            if(!view||!view.IsValid()||(view.transform.position-origin).sqrMagnitude>55*55)continue;
            if(view.GetComponent<TreeBase>()||view.GetComponent<TreeLog>()||view.GetComponent<Pickable>()||view.GetComponent<Destructible>()&&!view.GetComponent<Piece>()&&!view.GetComponent<Character>())ZNetScene.instance.Destroy(view.gameObject);
        }
        yield return new WaitForSecondsRealtime(4);
        Check(!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("chazman.RunicLilyPads"),"plumbing has no WaterGardens dependency");
        for(int i=0;i<6;i++){
            var kind=(PipeKind)i;var prefab=PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind));
            Check(prefab&&prefab.GetComponent<PipePiece>(),"plumbing prefab "+kind);if(!prefab)continue;
            Check(prefab.GetComponentsInChildren<Transform>(true).Count(t=>t.CompareTag("snappoint"))==PipePiece.PortCount(kind),"end snap points "+kind);
            var piece=prefab.GetComponent<Piece>();
            var nativeSnaps=new List<Transform>();piece.GetSnapPoints(nativeSnaps);Check(nativeSnaps.Count==PipePiece.PortCount(kind),"native hammer enumerates pipe snap points "+kind);
            Check(piece.m_icon&&piece.m_icon.name.Contains("model icon"),"actual model hammer icon "+kind);
            Check(!piece.m_groundOnly&&!piece.m_noInWater&&prefab.GetComponent<WearNTear>().m_noSupportWear&&prefab.GetComponent<WearNTear>().m_materialType==WearNTear.MaterialType.Iron,"surface / water / native metal integrity flags "+kind);
            Check(piece.m_resources.Length==1&&piece.m_resources[0].m_amount==(kind==PipeKind.Straight4?2:1),"construction cost "+kind);
            var mesh=prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            var importedVertices=mesh.vertices;var importedNormals=mesh.normals;var importedTriangles=mesh.triangles;int agreeing=0,total=0;
            int half=importedVertices.Length/2;Check(importedVertices[0]==importedVertices[half]&&Vector3.Dot(importedNormals[0],importedNormals[half])<-.99f,"inward-facing copper shell "+kind);
            for(int t=0;t<importedTriangles.Length;t+=3){var a=importedVertices[importedTriangles[t]];var b=importedVertices[importedTriangles[t+1]];var c=importedVertices[importedTriangles[t+2]];
                var face=Vector3.Cross(b-a,c-a);if(face.sqrMagnitude<.000000000001f)continue;total++;if(Vector3.Dot(face,importedNormals[importedTriangles[t]])>0)agreeing++;}
            Check(agreeing>total*.9f,"imported mesh outward winding "+kind);
            if(kind==PipeKind.Straight||kind==PipeKind.Straight4||PipePrefabs.Powered(kind)){
                float length=kind==PipeKind.Straight4?4:kind==PipeKind.Powered?1:2;
                Check(Math.Abs(mesh.bounds.size.z-length)<.002,"model metre length "+kind);
                Check(Math.Abs(Vector3.Distance(PipePiece.LocalPort(kind,0),PipePiece.LocalPort(kind,1))-length)<.001,"snap metre length "+kind);
            }
        }
        var recipes=ObjectDB.instance.m_recipes;
        bool Recipe(string item,int amount,string material,int cost,int cores)=>recipes.Any(r=>r.m_item&&r.m_item.name==item&&r.m_amount==amount&&r.m_resources.Any(x=>x.m_resItem.name==material&&x.m_amount==cost)&&(cores==0||r.m_resources.Any(x=>x.m_resItem.name=="SurtlingCore"&&x.m_amount==cores)));
        Check(Recipe(PipePrefabs.CopperItem,1,"Copper",1,0),"2m copper crafting cost");
        Check(Recipe(PipePrefabs.Copper4Item,1,"Copper",2,0),"4m copper crafting cost");
        Check(Recipe(PipePrefabs.PoweredItem,2,PipePrefabs.CopperItem,1,1),"1 copper pipe + 1 core = 2 powered 1m pipes");
        Check(Recipe(PipePrefabs.PoweredItem,4,PipePrefabs.Copper4Item,1,2),"4m copper + 2 cores = 4 powered 1m pipes");
        Check(Recipe(PipePrefabs.Powered2Item,2,PipePrefabs.Copper4Item,1,2),"4m copper + 2 cores = 2 powered 2m pipes");
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+(Combined?0:2);
        var player=Player.m_localPlayer;player.transform.position=origin+new Vector3(-3,2,0);player.GetComponent<Rigidbody>().position=player.transform.position;
        var pool=CreateWater(origin);yield return Settle();
        TestNativePipePlacement(origin);
        var created=new List<GameObject>();
        PipePiece Place(PipeKind kind,Vector3 p,Quaternion q){var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),p,q);created.Add(go);return go.GetComponent<PipePiece>();}
        Vector3 source=origin+Vector3.up*.5f;var upright=Quaternion.Euler(-90,0,0);
        var intake=Place(PipeKind.Intake,source,Quaternion.identity);
        var pump=Place(PipeKind.Powered,source+Vector3.up*.5f,upright);
        var tall=Place(PipeKind.Straight,source+Vector3.up*2,upright);
        yield return Settle();PipeNetwork.Recalculate();yield return null;
        Check(!double.IsNaN(intake.Source()),"intake draws through native water query");
        Check(intake.Connected[0]&&pump.Connected[0]&&pump.Connected[1]&&tall.Connected[0],"physical end ports connect vertically");
        Check(Math.Abs(tall.Flow[1]-5)<.01,"one powered pipe gives uphill outlet five gallons per second");
        var pump2=Place(PipeKind.Powered2,source+Vector3.up*4,upright);yield return Settle();PipeNetwork.Recalculate();yield return null;
        Check(Math.Abs(pump2.Flow[1]-10)<.01&&tall.Flow[1]==0,"2m powered pipe adds five and moves outlet to last piece");
        var elbow=Place(PipeKind.Elbow,source+Vector3.up*5,upright);
        var elbowEnd=elbow.Port(1);var end=Place(PipeKind.Straight4,elbowEnd+elbow.Direction(1)*2,Quaternion.LookRotation(elbow.Direction(1)));
        yield return Settle();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.5f);
        Check(elbow.Connected[0]&&elbow.Connected[1]&&end.Connected[0],"elbow and four metre pipe connect");
        Check(Math.Abs(end.Flow[1]-10)<.01&&pump2.Flow[1]==0,"only terminal pipe pours total pumping capacity");
        Check(end.GetComponentsInChildren<PipeOutlet>().Any(o=>o.Mesh&&o.Mesh.vertexCount>8),"visible outlet water mesh generated");
        var outlet=end.GetComponentsInChildren<PipeOutlet>().First(o=>o.Mesh&&o.Mesh.vertexCount>8);
        Check(outlet.HasImpact&&outlet.Splash&&outlet.Splash.isPlaying,"stream reaches ground and starts impact spray");
        var firstUv=outlet.Mesh.uv[0];var firstVertex=outlet.Mesh.vertices[10];yield return new WaitForSecondsRealtime(.3f);
        Check(outlet.Mesh.uv[0]!=firstUv&&outlet.Mesh.vertices[10]!=firstVertex,"water streaks and surface ripples animate over time");
        Check(outlet.Splash.particleCount>0,"impact spray contains visible live particles");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="isolated splash collision test";wall.layer=LayerMask.NameToLayer("piece");
        wall.GetComponent<MeshRenderer>().sharedMaterial=PrefabManager.Instance.GetPrefab("wood_floor").GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
        wall.transform.position=end.Port(1)+end.Direction(1)*2+Vector3.down*.5f;wall.transform.rotation=Quaternion.LookRotation(end.Direction(1));wall.transform.localScale=new Vector3(3,3,.1f);Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.3f);
        Check(outlet.HasImpact&&Vector3.Dot(outlet.ImpactNormal,-end.Direction(1))>.95f,"stream stops at solid wall");
        Check(Vector3.Dot(outlet.Splash.transform.forward,outlet.ImpactNormal)>.99f,"splash sprays away from the struck surface");
        CapturePlumbing("plumbing-wall-impact.png",outlet.ImpactPoint+new Vector3(4,2,-4),outlet.ImpactPoint+Vector3.up*.4f);
        wall.SetActive(false);Destroy(wall);yield return new WaitForSecondsRealtime(.2f);
        Check(PipeNetwork.CanConnect(pump.Port(1),pump.Direction(1),tall.Port(0),tall.Direction(0)),"snapped mating port orientation");
        Check(!PipeNetwork.CanConnect(pump.Port(1),pump.Direction(1),tall.Port(0),pump.Direction(1)),"same-facing endpoints do not connect");
        // Disconnecting/reconnecting uses native piece transforms and requires no saved flow state.
        var previous=tall.transform.position;tall.transform.position+=Vector3.right*2;PipeNetwork.Recalculate();
        Check(end.Flow[1]==0,"broken line stops downstream outlet");tall.transform.position=previous;PipeNetwork.Recalculate();
        Check(Math.Abs(end.Flow[1]-10)<.01,"reconnected line resumes");
        // Exercise the same placement postfix used by the hammer with a non-networked preview.
        var preview=new GameObject("plumbing_snap_test");var previewPipe=preview.AddComponent<PipePiece>();previewPipe.Kind=PipeKind.Powered;
        preview.transform.rotation=Quaternion.LookRotation(end.Direction(1));preview.transform.position=end.Port(1)+end.Direction(1)*.5f+Vector3.right*.1f;
        var postfix=typeof(PipePlacement).GetMethod("Postfix",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        object[] arguments={player,preview,Player.PlacementStatus.Valid,null,-1};postfix.Invoke(null,arguments);
        Check(Vector3.Distance(previewPipe.Port(0),end.Port(1))<.001f,"hammer preview snaps exactly to the free terminal port");
        var previewCollider=preview.AddComponent<CapsuleCollider>();previewCollider.direction=2;previewCollider.radius=.29f;previewCollider.height=1;
        preview.transform.rotation=Quaternion.Euler(-90,0,0);PipePlacement.PlaceOnSurface(previewPipe,origin,Vector3.up);Physics.SyncTransforms();
        Check(Math.Abs(previewCollider.bounds.min.y-origin.y)<.005f,"tilted pipe sits flush on a flat floor");
        preview.transform.rotation=Quaternion.identity;PipePlacement.PlaceOnSurface(previewPipe,origin,Vector3.right);Physics.SyncTransforms();
        Check(Math.Abs(previewCollider.bounds.min.x-origin.x)<.005f,"pipe sits flush on a flat wall");
        PipePlacement.PlaceOnSurface(previewPipe,origin,Vector3.down);Physics.SyncTransforms();Check(Math.Abs(previewCollider.bounds.max.y-origin.y)<.005f,"pipe sits flush under a ceiling");Destroy(preview);
        var intakePosition=intake.transform.position;intake.transform.position+=Vector3.up*4;PipeNetwork.Recalculate();
        Check(end.Flow[1]==0,"dry/disconnected intake stops water");intake.transform.position=intakePosition;PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.5f);
        // Rendered fixture image also covers imported materials, model scale, and the visible stream.
        var camera=GameCamera.instance.GetComponent<Camera>();var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        camera.transform.position=origin+new Vector3(9,7,12);camera.transform.LookAt(origin+new Vector3(0,3,2));
        var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();var previousTarget=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../plumbing-preview.png"),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=previousTarget;Destroy(image);Destroy(target);camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;
        CapturePlumbing("plumbing-water-closeup.png",end.Port(1)+new Vector3(6,1,6),Vector3.Lerp(end.Port(1),outlet.ImpactPoint,.5f));
        var interior=new GameObject("isolated elbow interior view");interior.transform.position=origin+Vector3.up*15;PipeAssets.Visual("elbow",interior.transform);
        CapturePlumbing("plumbing-elbow-interior.png",interior.transform.position+new Vector3(0,.03f,-1.3f),interior.transform.position+Vector3.forward*.3f);Destroy(interior);
        foreach(var go in created)ZNetScene.instance.Destroy(go);if(Combined)ZNetScene.instance.Destroy(pool);else Destroy(pool);yield return Settle();
        Check(!PipePiece.Active.Any(p=>p&&p.Live),"pipe graph cleans up removed pieces");
        yield return PipeIntegrityTest(origin);
    }
    IEnumerator PipeIntegrityTest(Vector3 origin){
        float Support(WearNTear wear)=>(float)typeof(WearNTear).GetMethod("GetSupport",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(wear,null);
        var update=typeof(WearNTear).GetMethod("UpdateSupport",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var created=new List<GameObject>();
        for(int i=0;i<6;i++){
            var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id((PipeKind)i)),origin+new Vector3(i*4,25,10),Quaternion.identity);created.Add(go);
            Physics.SyncTransforms();var wear=go.GetComponent<WearNTear>();update.Invoke(wear,null);
            Check(Support(wear)==0,"floating pipe has zero native integrity "+(PipeKind)i);
            wear.UpdateWear(Time.time+60);yield return null;
            Check(!go||!go.GetComponent<ZNetView>().IsValid(),"unsupported pipe collapses "+(PipeKind)i);
        }
        var anchor=origin+Vector3.right*10;PipeEnvironment.SampleGround(anchor,out float ground);anchor.y=ground+.9f;
        var upright=Quaternion.Euler(-90,0,0);
        var lower=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.Straight)),anchor,upright);created.Add(lower);
        var upper=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.Straight)),anchor+Vector3.up*2,upright);created.Add(upper);
        Physics.SyncTransforms();var baseWear=lower.GetComponent<WearNTear>();var upperWear=upper.GetComponent<WearNTear>();
        update.Invoke(baseWear,null);update.Invoke(upperWear,null);
        Check(Support(baseWear)==1500,"grounded copper pipe receives native metal foundation integrity");
        Check(Support(upperWear)>20&&Support(upperWear)<Support(baseWear),"snapped pipe transfers support with native distance loss");
        upperWear.UpdateWear(Time.time+60);yield return null;Check(upper&&upper.GetComponent<ZNetView>().IsValid(),"supported pipe survives wear update");
        lower.SetActive(false);ZNetScene.instance.Destroy(lower);yield return null;Physics.SyncTransforms();
        update.Invoke(upperWear,null);Check(Support(upperWear)==0,"removing foundation clears attached pipe integrity");
        upperWear.UpdateWear(Time.time+60);yield return null;Check(!upper||!upper.GetComponent<ZNetView>().IsValid(),"attached pipe collapses after foundation removal");
        foreach(var go in created)if(go&&go.GetComponent<ZNetView>().IsValid())ZNetScene.instance.Destroy(go);
    }
    void CapturePlumbing(string name,Vector3 position,Vector3 focus,bool withPost=false){
        var trees=UnityEngine.Object.FindObjectsOfType<TreeBase>().Where(t=>Vector3.Distance(t.transform.position,focus)<65).Select(t=>t.gameObject).ToArray();
        foreach(var tree in trees)tree.SetActive(false);
        var camera=GameCamera.instance.GetComponent<Camera>();var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        var post=camera.GetComponents<Behaviour>().FirstOrDefault(b=>b.GetType().Name.Contains("PostProcess"));bool postEnabled=post&&post.enabled;if(post&&!withPost)post.enabled=false;
        var profile=camera.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>()?.profile;
        bool motion=profile&&profile.motionBlur.enabled,taa=profile&&profile.antialiasing.enabled,dof=profile&&profile.depthOfField.enabled;
        if(withPost&&profile){profile.motionBlur.enabled=false;profile.antialiasing.enabled=false;profile.depthOfField.enabled=false;}
        camera.transform.position=position;camera.transform.LookAt(focus);var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();var oldActive=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../"+name),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=oldActive;Destroy(image);Destroy(target);camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;
        if(post)post.enabled=postEnabled;
        if(profile){profile.motionBlur.enabled=motion;profile.antialiasing.enabled=taa;profile.depthOfField.enabled=dof;}
        foreach(var tree in trees)if(tree)tree.SetActive(true);
    }
    void TestNativePipePlacement(Vector3 origin){
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var ghostField=typeof(Player).GetField("m_placementGhost",flags);
        var statusField=typeof(Player).GetField("m_placementStatus",flags);
        var update=typeof(Player).GetMethod("UpdatePlacementGhost",flags);
        var player=Player.m_localPlayer;var camera=GameCamera.instance.transform;
        var oldGhost=ghostField.GetValue(player);var oldCamera=camera.position;var oldRotation=camera.rotation;var oldPlayer=player.transform.position;
        var centre=origin+Vector3.up*10;
        // Spawn stones are a native no-build location; move this fixture outside it.
        for(int i=0;i<30&&Location.IsInsideNoBuildLocation(centre);i++)centre+=Vector3.right*20;
        GameObject target=null,ghost=null;
        try{
            player.transform.position=centre+Vector3.left*3;player.GetComponent<Rigidbody>().position=player.transform.position;
            for(int targetIndex=0;targetIndex<6;targetIndex++){
                target=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id((PipeKind)targetIndex)),centre,Quaternion.identity);
                var aim=target.GetComponentInChildren<Collider>().bounds.center;
                camera.position=aim+Vector3.right*3;camera.LookAt(aim);Physics.SyncTransforms();
                Check(Physics.Raycast(camera.position,camera.forward,out var rayHit,5,LayerMask.GetMask("piece"))&&rayHit.collider.GetComponentInParent<PipePiece>()==target.GetComponent<PipePiece>(),"hammer ray hits target "+(PipeKind)targetIndex);
                for(int previewIndex=0;previewIndex<6;previewIndex++){
                    ZNetView.m_forceDisableInit=true;
                    try{ghost=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id((PipeKind)previewIndex)),centre+Vector3.up*50,Quaternion.identity);}
                    finally{ZNetView.m_forceDisableInit=false;}
                    foreach(var child in ghost.GetComponentsInChildren<Transform>(true))child.gameObject.layer=LayerMask.NameToLayer("Ignore Raycast");
                    ghostField.SetValue(player,ghost);
                    if(targetIndex==0&&previewIndex==0){
                        target.GetComponent<WearNTear>().m_supports=false;
                        update.Invoke(player,new object[]{false});
                        Check((Player.PlacementStatus)statusField.GetValue(player)==Player.PlacementStatus.Invalid,"native hammer reproduces 0.9.0 unsupported pipe rejection");
                        target.GetComponent<WearNTear>().m_supports=true;
                    }
                    ghost.transform.position=centre+Vector3.up*50;Physics.SyncTransforms();
                    update.Invoke(player,new object[]{false});
                    var status=(Player.PlacementStatus)statusField.GetValue(player);
                    Check(status==Player.PlacementStatus.Valid,"native hammer accepts "+(PipeKind)previewIndex+" against "+(PipeKind)targetIndex+" ("+status+")");
                    ghostField.SetValue(player,null);DestroyImmediate(ghost);ghost=null;
                }
                target.SetActive(false);ZNetScene.instance.Destroy(target);target=null;Physics.SyncTransforms();
            }
        }finally{
            ghostField.SetValue(player,oldGhost);if(ghost)DestroyImmediate(ghost);if(target)ZNetScene.instance.Destroy(target);
            camera.position=oldCamera;camera.rotation=oldRotation;player.transform.position=oldPlayer;player.GetComponent<Rigidbody>().position=oldPlayer;
        }
    }
}
