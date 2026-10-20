using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ArcaneDecorFlowCraft.Plumbing;

internal sealed class SyncFountain:MonoBehaviour,Hoverable,Interactable
{
    internal const string Id="piece_flowcraft_sync_fountain";
    internal const float BurstFlow=10,Duration=.75f,Reset=2;
    internal static readonly HashSet<SyncFountain> Active=new HashSet<SyncFountain>();
    internal ZNetView View;Transform nozzle;PipeOutlet outlet;
    internal bool Live=>View&&View.IsValid();
    internal static double Now=>ZNet.instance?ZNet.instance.GetTime().Ticks/(double)TimeSpan.TicksPerSecond:0;
    internal double Started=>Live?View.GetZDO().GetLong("fc_burst",0)/(double)TimeSpan.TicksPerSecond:0;
    internal double Age=>Started>0?Now-Started:double.PositiveInfinity;
    internal float Yaw=>Live?View.GetZDO().GetFloat("fc_yaw",0):0;
    internal float Tilt=>Live?View.GetZDO().GetFloat("fc_tilt",45):45;
    internal Vector3 Direction=>transform.TransformDirection(Quaternion.Euler(-Tilt,Yaw,0)*Vector3.forward);
    internal Vector3 Mouth=>nozzle?nozzle.TransformPoint(new Vector3(0,.18f,.22f)):transform.position+Vector3.up*.52f;
    internal float VisualFlow=>Age>=0&&Age<Duration+4?BurstFlow:0;
    void Awake(){View=GetComponent<ZNetView>();nozzle=transform.Find("nozzle");if(Live)View.Register<bool>("FC_Aim",AimRpc);}
    void OnEnable()=>Active.Add(this);
    void OnDisable(){Active.Remove(this);if(outlet)outlet.gameObject.SetActive(false);}
    void Update(){
        if(!Live)return;
        if(nozzle)nozzle.localRotation=Quaternion.Euler(0,Yaw,0)*Quaternion.Euler(45-Tilt,0,0);
        bool visible=VisualFlow>0&&Player.m_localPlayer&&(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude<80*80;
        if(visible&&!outlet){var go=new GameObject("fountain burst");go.transform.SetParent(transform,false);outlet=go.AddComponent<PipeOutlet>();outlet.Fountain=this;}
        if(outlet)outlet.gameObject.SetActive(visible);
    }
    internal bool WaterHit(){
        if(!Live||!View.IsOwner()||Age<Reset)return false;
        View.GetZDO().Set("fc_burst",ZNet.instance.GetTime().Ticks);return true;
    }
    internal bool Catches(Vector3 point,Vector3 normal){
        var p=transform.InverseTransformPoint(point);
        return Vector3.Dot(normal,transform.up)>.2f&&p.y>=.025f&&p.y<.255f&&p.x*p.x+p.z*p.z<.49f*.49f;
    }
    public bool Interact(Humanoid user,bool hold,bool alt){
        if(hold||!Live||Vector3.Distance(user.transform.position,transform.position)>4||!PrivateArea.CheckAccess(transform.position,0,true,false))return false;
        View.InvokeRPC("FC_Aim",alt);return true;
    }
    void AimRpc(long sender,bool tilt){
        if(!Live||!View.IsOwner())return;
        var peer=ZNet.instance.GetPeer(sender);if(peer==null&&sender!=ZNet.GetUID())return;var character=peer!=null?ZNetScene.instance.FindInstance(peer.m_characterID):Player.m_localPlayer?.gameObject;
        if(!character||Vector3.Distance(character.transform.position,transform.position)>4)return;
        if(!PrivateArea.CheckAccess(transform.position,0,false,false))return;
        if(tilt)View.GetZDO().Set("fc_tilt",Tilt>=90?15:Tilt+15);
        else View.GetZDO().Set("fc_yaw",Mathf.Repeat(Yaw+15,360));
    }
    public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    public float GetHoverOffset()=>0;
    public string GetHoverName()=>"Copper Pulse Fountain";
    public string GetHoverText()=>GetHoverName()+"\n[<color=yellow><b>$KEY_Use</b></color>] Turn 15 degrees  [<color=yellow>Shift + $KEY_Use</color>] Tilt\n"+"Aim: "+Yaw.ToString("0")+" / Tilt: "+Tilt.ToString("0")+" degrees\n"+(Age<Duration?"Water burst":Age<Reset?"Resetting":"Ready - aim incoming water at the dish");
    internal static void Register(){
        var root=PrefabManager.Instance.CreateClonedPrefab(Id,"wood_floor");
        for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        foreach(var collider in root.GetComponents<Collider>())Object.DestroyImmediate(collider);
        var visual=new GameObject("fountain_visual");visual.transform.SetParent(root.transform,false);
        var dish=PipeAssets.Visual("sync_dish",visual.transform);dish.AddComponent<MeshCollider>().sharedMesh=PipeAssets.Load("sync_dish").Mesh;
        // Hammer positioning ignores non-convex MeshColliders. This inscribed base
        // gives ClosestPoint a supported collider while retaining the dish mesh for water hits.
        var placementBase=dish.AddComponent<BoxCollider>();placementBase.center=new Vector3(0,.025f,0);placementBase.size=new Vector3(.7f,.05f,.7f);
        var nozzle=PipeAssets.Visual("sync_nozzle",root.transform);nozzle.name="nozzle";nozzle.transform.localPosition=Vector3.up*.34f;
        var piece=root.GetComponent<Piece>();piece.m_usage=0;piece.m_craftingStation=null;piece.m_groundOnly=false;piece.m_groundPiece=false;piece.m_waterPiece=false;piece.m_noInWater=false;piece.m_clipEverything=false;piece.m_clipGround=false;piece.m_noClipping=false;piece.m_canBeRemoved=true;
        var wear=root.GetComponent<WearNTear>();wear.m_new=wear.m_worn=wear.m_broken=visual;wear.m_wet=null;wear.m_snow=wear.m_snowWorn=wear.m_snowBroken=null;wear.m_autoCreateFragments=false;wear.m_fragmentRoots=Array.Empty<GameObject>();wear.m_nonSolidRenderers.Clear();wear.m_supports=true;wear.m_health=300;wear.m_burnable=false;wear.m_noSupportWear=true;wear.m_noRoofWear=false;wear.m_materialType=WearNTear.MaterialType.Iron;
        root.GetComponent<ZNetView>().m_distant=false;root.AddComponent<SyncFountain>();
        var config=new PieceConfig{Enabled=true,Name="Copper Pulse Fountain",Description="Water striking the dish triggers a 0.75 second burst at 10 gallons/sec. Resets every 2 seconds under continuous water. Use to turn; Shift+Use to tilt. Aim bursts into other dishes for cascading water features.",PieceTable="Hammer",Category="ArcaneDecor: FlowCraft",Icon=PipeAssets.Load("sync_dish").Icon};config.AddRequirement("Copper",2);
        if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))throw new InvalidOperationException("Pulse fountain registration failed");
    }
}

// Gameplay impact detection runs independently of client-only stream meshes and distance culling.
internal sealed class FountainTriggers:MonoBehaviour
{
    float next;readonly List<Vector3> path=new List<Vector3>();readonly List<SyncFountain> receivers=new List<SyncFountain>();
    void Update(){if(Time.time<next)return;next=Time.time+.1f;Tick();}
    internal void Tick(){
        receivers.Clear();
        foreach(var pipe in PipePiece.Active){
            if(!pipe||!pipe.Live||!pipe.isActiveAndEnabled)continue;
            for(int port=0;port<PipePiece.PortCount(pipe.Kind);port++)if(pipe.Flow[port]>.001f)
                Trace(pipe.Port(port),pipe.Direction(port),pipe.Flow[port],pipe.transform,null);
        }
        foreach(var fountain in SyncFountain.Active)if(fountain&&fountain.Live&&fountain.VisualFlow>0)
            Trace(fountain.Mouth,fountain.Direction,SyncFountain.BurstFlow,fountain.transform,fountain);
        // Trigger after scanning all sources, so a chain cannot fire in hash-set iteration order.
        foreach(var receiver in receivers)if(receiver)receiver.WaterHit();
    }
    void Trace(Vector3 origin,Vector3 direction,float flow,Transform source,SyncFountain pulse){
        JetPath.Trace(origin,direction,flow,source,path,out bool impact,out var normal,out var receiver,out float travel);
        if(!impact||!receiver||receiver==pulse||!receiver.Catches(path[path.Count-1],normal))return;
        if(pulse&&(pulse.Age<travel||pulse.Age>=travel+SyncFountain.Duration))return;
        if(!receivers.Contains(receiver))receivers.Add(receiver);
    }
}

