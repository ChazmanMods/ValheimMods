using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator CompleteSetTest(Vector3 origin){
        EnvMan.instance.m_debugTime=.5f;
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+24;
        foreach(bool narrow in new[]{false,true}){
            var kinds=PipePrefabs.Catalogue(narrow);Check(kinds.Length==9&&kinds.Distinct().Count()==9,"exactly nine unique pieces in diameter set");
            var created=new List<GameObject>();
            PipePiece Place(PipeKind kind,Vector3 p,Quaternion q){var go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kind)),p,q);created.Add(go);return go.GetComponent<PipePiece>();}
            var intake=Place(narrow?PipeKind.IntakeNarrow:PipeKind.Intake,origin,Quaternion.identity);
            var elbow=Place(narrow?PipeKind.LongElbowNarrow:PipeKind.LongElbow,origin,Quaternion.Euler(-90,0,0));
            var pump=Place(narrow?PipeKind.Powered4Narrow:PipeKind.Powered4,elbow.Port(1)+Vector3.forward*2,Quaternion.identity);
            var tail=Place(narrow?PipeKind.StraightNarrow:PipeKind.Straight,pump.Port(1)+Vector3.forward,Quaternion.identity);
            var water=NativeTestWater(origin+Vector3.down*1.1f);Physics.SyncTransforms();yield return Settle();PipeNetwork.Recalculate();yield return new WaitForSecondsRealtime(.5f);
            Check(intake.Connected[0]&&elbow.Connected.All(x=>x)&&pump.Connected.All(x=>x)&&tail.Connected[0],"long elbow and 4m powered piece connect through matching sockets "+narrow);
            Check(Math.Abs(tail.Flow[1]-5)<.01&&pump.Running,"4m powered segment supplies five gallons/sec "+narrow);
            Check(Math.Abs(Vector3.Distance(pump.Port(0),pump.Port(1))-4)<.001,"powered pipe has 4m connection length");
            Check(PipePiece.LocalPort(elbow.Kind,1)==PipePiece.LocalPort(narrow?PipeKind.ElbowNarrow:PipeKind.Elbow,1)*2,"long elbow doubles bend radius");
            var id=PipePrefabs.ItemId(PipePrefabs.Powered4Item,narrow);
            Check(ObjectDB.instance.m_recipes.Any(r=>r.m_item&&r.m_item.name==id&&r.m_amount==1&&r.m_resources.Any(x=>x.m_resItem.name==PipePrefabs.ItemId(PipePrefabs.Copper4Item,narrow)&&x.m_amount==1)&&r.m_resources.Any(x=>x.m_resItem.name=="SurtlingCore"&&x.m_amount==2)),"4m powered recipe uses one 4m pipe and two cores");
            CapturePlumbing(narrow?"complete-4cm-additions.png":"complete-8cm-additions.png",origin+new Vector3(7,5,8),origin+new Vector3(0,0,3),true);
            foreach(var go in created)if(go)ZNetScene.instance.Destroy(go);Destroy(water);origin+=Vector3.right*10;yield return Settle();
        }
    }
}
