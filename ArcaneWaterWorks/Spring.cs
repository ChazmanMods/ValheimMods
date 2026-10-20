using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArcaneWaterWorks;

public sealed class Spring:MonoBehaviour,Hoverable,Interactable
{
    internal static readonly List<Spring> Active=new List<Spring>();
    internal Route Route;
    internal Route JoinedRoute;
    internal Route BankRoute;
    internal List<Spring> SurfaceMembers;
    internal Dictionary<(float,float),float> SurfaceHeights;
    internal Vector3 JoinedOrigin;
    internal bool RawSurface(float x,float z,out float distance,out float level,out float progress){
        bool wet=(BankRoute??Route).Surface(x,z,out distance,out level,out progress);
        if(JoinedRoute!=null){var offset=transform.position-JoinedOrigin;
            JoinedRoute.Surface(x+offset.x,z+offset.z,out _,out float joined,out _);level=joined-offset.y;
        }
        return wet;
    }
    internal bool Surface(float x,float z,out float distance,out float level,out float progress){
        bool wet=RawSurface(x,z,out distance,out level,out progress);
        if(SurfaceMembers!=null){
            float weight=0,total=0;var worldPoint=transform.position+new Vector3(x,0,z);
            var key=(worldPoint.x,worldPoint.z);
            if(SurfaceHeights.TryGetValue(key,out float cached))level=cached-transform.position.y;
            else{
            foreach(var member in SurfaceMembers){if(!member||!member.Live||member.Route==null)continue;
                var p=worldPoint-member.transform.position;member.RawSurface(p.x,p.z,out float d,out float h,out _);
                float w=Mathf.Max(0,1-d/(member.Route.Width*.5f+1.5f));w=w*w*w*w;
                if(w<=0)continue;weight+=w;total+=(h+member.transform.position.y)*w;
            }
            if(weight>0){float height=total/weight;level=height-transform.position.y;if(SurfaceHeights.Count<65536)SurfaceHeights[key]=height;}
            }
        }
        var world=transform.position+new Vector3(x,level,z);level=GardenBridge.JoinRiver(world,world.y)-transform.position.y;
        return wet;
    }
    internal string Status="Waiting for terrain.";
    internal ZNetView View;
    string last="",pending;
    float next,deadline;
    bool registered;
    internal bool Live=>View&&View.IsValid();
    internal float Sea=>(ZoneSystem.instance?ZoneSystem.instance.m_waterLevel:30)-transform.position.y;
    internal bool Busy=>pending!=null;
    void Start(){View=GetComponent<ZNetView>();
        if(Live&&View.GetZDO().GetBool("aww_continuation",false)){
            var ring=transform.Find("spring_stones");if(ring)for(int i=1;i<ring.childCount;i++)ring.GetChild(i).gameObject.SetActive(false);
            GetComponent<Piece>().m_resources=Array.Empty<Piece.Requirement>();
        }
    }
    void Update(){
        if(!Live)return;
        if(!registered){registered=true;Active.Add(this);}
        if(Time.time<next)return;next=Time.time+.4f;

        if(pending!=null){
            if(Time.time>deadline){pending=null;Status="Could not obtain ownership. Try saving again.";}
            else if(View.IsOwner()){
                if(ArcaneWaterWorks.Route.TryDecode(pending,out var proposed)&&CanEdit(proposed))View.GetZDO().Set("aww_route_v1",pending);
                else Status="Route edit denied by building permissions.";
                pending=null;
            }
        }
        string raw=View.GetZDO().GetString("aww_route_v1","");
        if(raw==""&&View.IsOwner()&&Player.m_localPlayer&&Vector3.Distance(Player.m_localPlayer.transform.position,transform.position)<20){
            var created=Trace(4,4,.8f,out var status);Status=status;
            if(created!=null&&CanEdit(created)){raw=created.Encode();View.GetZDO().Set("aww_route_v1",raw);}
            else if(created!=null)Status="Default river crosses protected land. Configure a shorter route.";
        }
        if(raw==last)return;
        if(!ArcaneWaterWorks.Route.TryDecode(raw,out var route)){Status="Saved route is invalid; retained without applying.";last=raw;return;}
        if(raw.StartsWith("1|")){
            for(int i=0;i<route.Points.Count;i++){var p=route.Points[i];route.Points[i]=new Node(p.X,Mathf.Max(Sea,p.Y-.45f),p.Z,p.Parent);}
            route.Invalidate();
        }
        last=raw;var old=Route;Route=route;Status=route.Mouth?"River ready - natural-water outlet.":"River ready.";
        bool terrainChanged=old==null;
        if(old!=null){var comparison=route.Copy();comparison.CopyAppearance(old);terrainChanged=comparison.Encode()!=old.Encode();}
        try{if(terrainChanged||old.Whitewater!=route.Whitewater)RiverVisuals.RebuildNearby(this,old);}catch(Exception e){Plugin.Log.LogError("River visual rebuild: "+e);Status="Visual build failed; see BepInEx log.";}
        if(terrainChanged){RiverTerrain.Refresh(this,Route,old);ArcaneWaterWorks.Pools.RiverGardens.NotifyChanged();CancelInvoke(nameof(SettleStones));Invoke(nameof(SettleStones),1.2f);}
        else if(old.Mud!=route.Mud)RiverTerrain.Refresh(this,Route);

    }
    internal float? Receiving(float x,float z){var p=transform.position+new Vector3(x,0,z);float pond=ArcaneWaterWorks.Pools.RiverGardens.PondLevel(p);return float.IsNegativeInfinity(pond)?Sea:pond-transform.position.y;}
    internal float? Ground(float x,float z)=>RiverTerrain.SampleBase(transform.position+new Vector3(x,0,z),out var height)?height-transform.position.y:(float?)null;
    void SettleStones(){
        if(Route==null)return;var ring=transform.Find("spring_stones");if(!ring)return;
        foreach(Transform stone in ring){
            var p=stone.localPosition;var direction=new Vector3(p.x,0,p.z).normalized;
            p.x=direction.x*(Route.Width*.5f+1.2f);p.z=direction.z*(Route.Width*.5f+1.2f);stone.localPosition=p;
            var world=stone.position;if(!RiverTerrain.SampleCurrent(world,out float ground))continue;
            var mesh=stone.GetComponent<MeshFilter>();float bottom=mesh?mesh.sharedMesh.bounds.min.y*stone.lossyScale.y:0;
            world.y=ground-bottom-.06f;stone.position=world;
        }
    }
    internal Route Trace(float length,float width,float depth,out string status)=>ArcaneWaterWorks.Route.Trace(Ground,Sea,transform.eulerAngles.y*Mathf.Deg2Rad,length,width,depth,out status,Receiving);
    internal bool CanEdit(Route candidate=null){
        var player=Player.m_localPlayer;if(!Live||!player||Vector3.Distance(player.transform.position,transform.position)>220||!PrivateArea.CheckAccess(transform.position,2,false))return false;
        bool Allowed(Route r){if(r==null)return true;for(int i=1;i<r.Points.Count;i++){
            var a=r.Points[r.Parent(i)];var b=r.Points[i];int steps=Mathf.CeilToInt(Node.Distance(a,b));
            for(int j=0;j<=steps;j++){var p=Node.Lerp(a,b,j/(float)Math.Max(1,steps));if(!PrivateArea.CheckAccess(transform.position+new Vector3(p.X,p.Y,p.Z),r.Width*.5f+r.BankPadding,false))return false;}
        }return true;}
        return Allowed(Route)&&Allowed(candidate);
    }
    internal bool Save(Route route){
        if(Busy){Status="Waiting for the previous change to save.";return false;}
        if(route==null||!route.Valid||!CanEdit(route)){Status="Invalid route or protected land. Nothing changed.";return false;}
        pending=route.Encode();deadline=Time.time+5;View.ClaimOwnership();Status="Saving river…";return true;
    }
    public string GetHoverName()=>"River Source";
    public string GetHoverText()=>"River Source\n[<color=yellow><b>E</b></color>] Shape river";
    public float GetHoverOffset()=>0;
    public bool Interact(Humanoid user,bool hold,bool alt){if(hold||user!=Player.m_localPlayer||!CanEdit())return false;RiverEditor.Show(this);return true;}
    public bool UseItem(Humanoid user,ItemDrop item)=>false;
    public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    void OnDestroy(){Active.Remove(this);ArcaneWaterWorks.Pools.RiverGardens.NotifyChanged();if(RiverEditor.Target==this)RiverEditor.Close();RiverTerrain.Refresh(this,Route);if(Route!=null)RiverVisuals.RebuildNearby(this,Route);}
}
