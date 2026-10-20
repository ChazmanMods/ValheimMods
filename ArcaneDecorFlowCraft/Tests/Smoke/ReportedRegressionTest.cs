using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator ReportedRegressionTest(Vector3 origin){
        var player=Player.m_localPlayer;
        var hammer=PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.Clone();player.GetInventory().AddItem(hammer);
        player.AddKnownItem(PrefabManager.Instance.GetPrefab("Copper").GetComponent<ItemDrop>().m_itemData);
        player.AddKnownItem(PrefabManager.Instance.GetPrefab("SurtlingCore").GetComponent<ItemDrop>().m_itemData);
        typeof(Player).GetMethod("UpdateKnownRecipesList",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null);
        var known=(HashSet<string>)typeof(Player).GetField("m_knownRecipes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player);
        var table=hammer.m_shared.m_buildPieces;table.UpdateAvailable(known,player,false,false);
        var available=table.m_availablePieces;
        foreach(bool narrow in new[]{false,true}){
            var expected=Enumerable.Range(0,PipePrefabs.Keys.Length).Select(i=>(PipeKind)i).Where(k=>PipePrefabs.Narrow(k)==narrow).ToArray();
            var pieces=expected.Select(k=>PrefabManager.Instance.GetPrefab(PipePrefabs.Id(k)).GetComponent<Piece>()).ToArray();
            Check(pieces.Length==9&&pieces.All(p=>available.Contains(p)),"all nine "+(narrow?"4cm":"8cm")+" pieces visible in survival Hammer menu from raw materials");
            Check(pieces.Select(p=>p.m_category).Distinct().Count()==1,"each diameter has one complete Hammer category");
            foreach(var piece in pieces)Check(player.HaveRequirements(piece,Player.RequirementMode.IsKnown),"discoverable counterpart "+piece.m_name);
        }
        var p8=PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.Powered)).GetComponent<Piece>();var p4=PrefabManager.Instance.GetPrefab(PipePrefabs.Id(PipeKind.PoweredNarrow)).GetComponent<Piece>();
        Check(p8.m_category==p4.m_category,"both complete sets share one FlowCraft category");
        Check(!player.HaveRequirements(p4,Player.RequirementMode.CanBuild),"discovery does not bypass crafted-pipe construction cost");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../complete-catalog.txt"),Enumerable.Range(0,PipePrefabs.Keys.Length).Select(i=>PipePrefabs.Names[i]));

        // Reproduce same-height fountains with independent one- and two-pump supplies.
        for(int i=0;i<20&&Location.IsInsideNoBuildLocation(origin);i++)origin+=Vector3.right*15;
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground;
        origin+=Vector3.up*24;
        var created=new List<GameObject>();
        PipePiece Place(PipeKind kind,Vector3 p,Quaternion q){var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),p,q);created.Add(go);return go.GetComponent<PipePiece>();}
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="flat jet measurement ground";floor.layer=LayerMask.NameToLayer("piece");floor.transform.position=origin+new Vector3(3,-2,8);floor.transform.localScale=new Vector3(20,.2f,25);created.Add(floor);
        var floorMaterial=new Material(PipeAssets.Load("pipe").Material){mainTexture=Texture2D.whiteTexture};floorMaterial.SetColor("_Color",new Color(.08f,.08f,.08f));
        foreach(var field in new[]{"_Metallic","_Glossiness","_Smoothness"})if(floorMaterial.HasProperty(field))floorMaterial.SetFloat(field,0);
        floor.GetComponent<MeshRenderer>().sharedMaterial=floorMaterial;
        var heads=new PipePiece[3];var pumps=new PipePiece[3];var waters=new GameObject[3];
        for(int line=0;line<3;line++){
            bool narrow=line==2;
            var wall=Instantiate(PrefabManager.Instance.GetPrefab("stone_wall_2x1"),origin+Vector3.right*(line*4),Quaternion.identity);created.Add(wall);wall.GetComponent<WearNTear>().m_noSupportWear=false;
            Physics.SyncTransforms();var bounds=wall.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger).bounds;
            var face=DragonMount.CenterOnFace(new Vector3(bounds.center.x+.3f,bounds.center.y+.2f,bounds.max.z),Vector3.forward,PipeAssets.Load(narrow?"dragon_narrow":"dragon").Mesh.bounds.center.y);
            heads[line]=Place(narrow?PipeKind.DragonNarrow:PipeKind.Dragon,face+Vector3.forward*.015f,Quaternion.identity);Physics.SyncTransforms();Check(DragonMount.Find(heads[line],out var mount),"comparison fountain mounted");
            var pump=Place(narrow?PipeKind.PoweredNarrow:PipeKind.Powered,mount.Back-Vector3.forward*.485f,Quaternion.identity);pumps[line]=pump;
            var inlet=pump.Port(0);
            if(line==1){var second=Place(PipeKind.Powered,inlet-Vector3.forward*.5f,Quaternion.identity);inlet=second.Port(0);}
            var elbowKind=narrow?PipeKind.ElbowNarrow:PipeKind.Elbow;var turn=Quaternion.Euler(-90,0,0);
            var elbow=Place(elbowKind,inlet-turn*PipePiece.LocalPort(elbowKind,1),turn);
            var intake=Place(narrow?PipeKind.IntakeNarrow:PipeKind.Intake,elbow.Port(0),Quaternion.identity);waters[line]=NativeTestWater(intake.transform.TransformPoint(new Vector3(0,-1.6f,0))+Vector3.up*.1f);
        }
        Physics.SyncTransforms();yield return Settle();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.8f);
        Check(Math.Abs(heads[0].Flow[1]-5)<.01&&Math.Abs(heads[1].Flow[1]-10)<.01,"independent one/two pump fountains receive 5/10 gallons/sec");
        var streams=heads.Select(h=>h.GetComponentInChildren<PipeOutlet>()).ToArray();
        Check(streams.All(s=>s&&s.HasImpact),"both comparison jets hit measurement ground");
        if(streams.All(s=>s)){
            float one=Vector3.Dot(streams[0].ImpactPoint-heads[0].Port(1),Vector3.forward),two=Vector3.Dot(streams[1].ImpactPoint-heads[1].Port(1),Vector3.forward);
            Logger.LogInfo("JET REACH one="+one+"m two="+two+"m");Check(two>one*1.5f,"two pumps throw at least fifty percent farther at equal mounting height");
        }
        CapturePlumbing("one-versus-two-pumps.png",origin+new Vector3(13,5,10),origin+new Vector3(2,0,2),true);
        if(streams[0]&&streams[2]){
            foreach(var stream in streams)typeof(PipeOutlet).GetMethod("Animate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(stream,null);
            float wide=Vector3.Distance(streams[0].Mesh.vertices[0],streams[0].Mesh.vertices[5]),thin=Vector3.Distance(streams[2].Mesh.vertices[0],streams[2].Mesh.vertices[5]);
            Check(Math.Abs(wide/thin-2)<.01,"8cm fountain stream is twice the diameter of 4cm at equal flow");
            var big=streams[0].Splash;var small=streams[2].Splash;
            Check(big&&small&&Math.Abs(big.main.startSize.constantMax/small.main.startSize.constantMax-2)<.01,"8cm splash droplets are twice the size");
            Check(big&&small&&Math.Abs(big.emission.rateOverTime.constant/small.emission.rateOverTime.constant-4)<.01,"8cm splash has four times the particle emission");
            Check(big&&small&&Math.Abs(big.shape.radius/small.shape.radius-2)<.01,"8cm splash starts across twice the impact radius");
            Logger.LogInfo("STREAM DIAMETERS 8cm="+wide+"m 4cm="+thin+"m");
        }
        CapturePlumbing("fountain-diameters.png",origin+new Vector3(4,3,10),origin+new Vector3(4,-.5f,2),true);
        CapturePlumbing("fountain-diameters-no-post.png",origin+new Vector3(4,3,10),origin+new Vector3(4,-.5f,2));
        var sprayMaterial=streams[0].Splash.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Logger.LogInfo("SPRAY MATERIAL "+sprayMaterial.name+" shader="+sprayMaterial.shader.name+" texture="+sprayMaterial.mainTexture);
        foreach(var key in new[]{"_Color","_TintColor","_EmissionColor"})if(sprayMaterial.HasProperty(key))Logger.LogInfo("SPRAY "+key+"="+sprayMaterial.GetColor(key));
        CapturePlumbing("red-orb-with-postprocessing.png",pumps[1].transform.TransformPoint(new Vector3(1.4f,.15f,.1f)),pumps[1].transform.TransformPoint(new Vector3(.3f,0,0)),true);
        var sleeve=pumps.SelectMany(p=>p.Joints).FirstOrDefault(j=>j&&j.gameObject.activeSelf);
        if(!sleeve)sleeve=PipePiece.Active.SelectMany(p=>p.Joints).FirstOrDefault(j=>j&&j.gameObject.activeSelf);
        Check(sleeve,"connected pair has seated joint sleeve");
        if(sleeve){var mesh=sleeve.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var t=mesh.triangles;var n=mesh.normals;
            Check(Vector3.Dot(Vector3.Cross(v[t[1]]-v[t[0]],v[t[2]]-v[t[0]]),n[t[0]])>0,"joint sleeve has outward-facing copper surface");
            var centre=sleeve.transform.TransformPoint(mesh.bounds.center);CapturePlumbing("closed-socket-seam.png",centre+new Vector3(1,.25f,.25f),centre,true);
        }
        foreach(var go in created)if(go){if(go.GetComponent<ZNetView>())ZNetScene.instance.Destroy(go);else Destroy(go);}foreach(var water in waters)Destroy(water);Destroy(floorMaterial);yield return Settle();
    }
}
