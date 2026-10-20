using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator ReconnectTest(Vector3 origin){
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+24;
        for(int attempt=0;attempt<20&&Location.IsInsideNoBuildLocation(origin);attempt++)origin+=Vector3.right*15;
        foreach(bool narrow in new[]{false,true}){
            var created=new List<GameObject>();
            PipePiece Place(PipeKind kind,Vector3 p,Quaternion q){var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),p,q);created.Add(go);return go.GetComponent<PipePiece>();}
            var wall=Instantiate(PrefabManager.Instance.GetPrefab("stone_wall_2x1"),origin,Quaternion.identity);created.Add(wall);wall.GetComponent<WearNTear>().m_noSupportWear=false;Physics.SyncTransforms();
            var bounds=wall.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger).bounds;
            var headKind=narrow?PipeKind.DragonNarrow:PipeKind.Dragon;var straight=narrow?PipeKind.StraightNarrow:PipeKind.Straight;var powered=narrow?PipeKind.PoweredNarrow:PipeKind.Powered;
            var face=DragonMount.CenterOnFace(new Vector3(bounds.center.x,bounds.center.y,bounds.max.z),Vector3.forward,PipeAssets.Load(narrow?"dragon_narrow":"dragon").Mesh.bounds.center.y);
            var headPos=face+Vector3.forward*.015f;
            var terminal=Place(straight,new Vector3(face.x,face.y,bounds.min.z+.015f)-Vector3.forward,Quaternion.identity);
            var terminalPos=terminal.transform.position;
            var middlePos=terminal.Port(0)-Vector3.forward;var middle=Place(straight,middlePos,Quaternion.identity);
            var powerPos=middle.Port(0)-Vector3.forward*.5f;var pump=Place(powered,powerPos,Quaternion.identity);
            var elbowKind=narrow?PipeKind.ElbowNarrow:PipeKind.Elbow;var turn=Quaternion.Euler(-90,0,0);var elbow=Place(elbowKind,pump.Port(0)-turn*PipePiece.LocalPort(elbowKind,1),turn);
            var intake=Place(narrow?PipeKind.IntakeNarrow:PipeKind.Intake,elbow.Port(0),Quaternion.identity);
            var water=NativeTestWater(intake.transform.position+Vector3.down*1.1f);
            Player.m_localPlayer.transform.position=origin+new Vector3(5,2,2);Player.m_localPlayer.GetComponent<Rigidbody>().position=Player.m_localPlayer.transform.position;
            Physics.SyncTransforms();yield return Settle();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.3f);
            Check(terminal.Flow[1]==0,"pipe slightly inside stone stays blocked without fountain "+narrow);
            Check(!terminal.GetComponentsInChildren<PipeOutlet>().Any(o=>o.gameObject.activeInHierarchy),"no stream through bare stone "+narrow);
            var head=Place(headKind,headPos,Quaternion.identity);Physics.SyncTransforms();yield return new WaitForSecondsRealtime(.8f);
            Check(head.Flow[1]>4.99f&&terminal.Flow[1]==0,"adding fountain starts only mouth stream "+narrow);
            // A rear feed can touch the same block without matching the hidden mouth origin exactly.
            var feedPieces=created.Select(go=>go.GetComponent<PipePiece>()).Where(p=>p&&!PipePrefabs.Dragon(p.Kind)).ToArray();
            foreach(var p in feedPieces)p.transform.position+=Vector3.right*.22f;
            PipeNetwork.Recalculate();
            Check(Math.Abs(head.Flow[1]-5)<.01f&&pump.Running,"offset rear feed on same stone supplies fountain "+narrow);
            foreach(var p in feedPieces)p.transform.position-=Vector3.right*.22f;
            // Only the bottom of the real strainer is submerged; its old central probe is dry.
            var oldWater=water.transform.position;water.transform.position=intake.transform.position+Vector3.down*1.94f;
            Physics.SyncTransforms();PipeNetwork.Recalculate();
            Check(!double.IsNaN(intake.Source())&&Math.Abs(head.Flow[1]-5)<.01f,"shallow immersion of strainer restores powered flow "+narrow);
            water.transform.position=intake.transform.position+Vector3.down*2.3f;Physics.SyncTransforms();PipeNetwork.Recalculate();
            Check(double.IsNaN(intake.Source())&&head.Flow[1]==0,"dry strainer cannot supply line "+narrow);
            water.transform.position=oldWater;Physics.SyncTransforms();PipeNetwork.Recalculate();
            for(int cycle=0;cycle<3;cycle++){
                ZNetScene.instance.Destroy(middle.gameObject);if(cycle>0)ZNetScene.instance.Destroy(pump.gameObject);if(cycle==2)ZNetScene.instance.Destroy(terminal.gameObject);
                yield return null;PipeNetwork.RefreshIfDirty();
                Check(cycle==2?!head.Connected[0]:!terminal.Connected[0],"destroyed piece immediately releases socket for replacement");
                yield return new WaitForSecondsRealtime(.8f);
                Check(head.Flow[1]==0&&!head.GetComponentsInChildren<PipeOutlet>().Any(o=>o.gameObject.activeInHierarchy),"removing supply sections stops fountain cycle "+cycle+" "+narrow);
                // Recreate actual build pieces with fresh ZDO IDs, not just moved transforms.
                if(cycle==2){
                    var ghost=new GameObject("replacement wall feed preview");var preview=ghost.AddComponent<PipePiece>();preview.Kind=straight;ghost.transform.position=terminalPos+Vector3.up*.08f;
                    Check(DragonMount.Snap(preview)&&Vector3.Distance(ghost.transform.position,terminalPos)<.001,"replacement wall feed snaps back to fountain");
                    terminal=Place(straight,ghost.transform.position,ghost.transform.rotation);DestroyImmediate(ghost);
                }
                var snapGhost=new GameObject("replacement middle preview");var snapPipe=snapGhost.AddComponent<PipePiece>();snapPipe.Kind=straight;snapGhost.transform.position=middlePos+Vector3.up*.08f;
                var postfix=typeof(PipePlacement).GetMethod("Postfix",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                object[] arguments={Player.m_localPlayer,snapGhost,Player.PlacementStatus.Valid,null,-1};postfix.Invoke(null,arguments);
                Check(Vector3.Distance(snapGhost.transform.position,middlePos)<.001,"replacement section snaps into original gap");
                middle=Place(straight,snapGhost.transform.position,snapGhost.transform.rotation);DestroyImmediate(snapGhost);if(cycle>0)pump=Place(powered,powerPos,Quaternion.identity);
                Physics.SyncTransforms();yield return new WaitForSecondsRealtime(.8f);
                Check(Math.Abs(head.Flow[1]-5)<.01f&&terminal.Flow[1]==0,"replaced supply sections restart fountain cycle "+cycle+" "+narrow);
                Check(head.GetComponentsInChildren<PipeOutlet>().Any(o=>o.Mesh&&o.Mesh.vertexCount>0),"restarted mouth renders water cycle "+cycle+" "+narrow);
                ZNetScene.instance.Destroy(head.gameObject);yield return new WaitForSecondsRealtime(.8f);
                Check(terminal.Flow[1]==0&&!terminal.GetComponentsInChildren<PipeOutlet>().Any(o=>o.gameObject.activeInHierarchy),"removing fountain caps wall feed cycle "+cycle+" "+narrow);
                head=Place(headKind,headPos,Quaternion.identity);Physics.SyncTransforms();yield return new WaitForSecondsRealtime(.8f);
                Check(Math.Abs(head.Flow[1]-5)<.01f,"replaced fountain resumes cycle "+cycle+" "+narrow);
            }
            ZNetScene.instance.Destroy(head.gameObject);yield return null;
            terminal.transform.position=terminalPos+Vector3.forward*.1f;PipeNetwork.Recalculate();
            Check(terminal.Flow[1]==0,"deeper embedded outlet is blocked by stone volume");
            terminal.transform.position=terminalPos;wall.SetActive(false);yield return new WaitForSecondsRealtime(.8f);
            Check(terminal.Flow[1]>4.99f,"removing stone exposes an ordinary free pipe outlet");
            wall.SetActive(true);yield return new WaitForSecondsRealtime(.8f);
            Check(terminal.Flow[1]==0,"replacing stone blocks the bare feed again");
            if(PipeEnvironment.SampleGround(origin,out float shoreGround)){
                intake.transform.position=new Vector3(origin.x,shoreGround+1.4f,origin.z);
                water.transform.position=new Vector3(origin.x,shoreGround+.32f,origin.z);Physics.SyncTransforms();
                Check(!double.IsNaN(intake.Source()),"exposed upper strainer draws when bottom is in shore terrain "+narrow);
                intake.transform.position=new Vector3(origin.x,shoreGround+.4f,origin.z);Physics.SyncTransforms();
                Check(double.IsNaN(intake.Source()),"fully buried strainer stays blocked "+narrow);
            }
            foreach(var go in created)if(go&&go.GetComponent<ZNetView>().IsValid())ZNetScene.instance.Destroy(go);Destroy(water);yield return Settle();origin+=Vector3.right*12;
        }
    }
}
