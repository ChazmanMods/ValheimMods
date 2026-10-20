using HarmonyLib;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

[HarmonyPatch(typeof(Player),"UpdatePlacementGhost")]
internal static class PipePlacement
{
    static GameObject last;static int pitch,roll;
    [HarmonyPostfix,HarmonyPriority(Priority.Last)]
    static void Postfix(Player __instance,GameObject ___m_placementGhost,ref Player.PlacementStatus ___m_placementStatus,GameObject ___m_placementMarkerInstance,int ___m_manualSnapPoint){
        if(__instance!=Player.m_localPlayer||!___m_placementGhost||!___m_placementGhost.activeSelf)return;
        var pipe=___m_placementGhost.GetComponent<PipePiece>();if(!pipe)return;
        // Removed neighbours must release their sockets before a replacement snaps.
        PipeNetwork.RefreshIfDirty();
        if(PipePrefabs.Dragon(pipe.Kind)){
            if(___m_placementMarkerInstance&&___m_placementMarkerInstance.activeSelf){
                var marker=___m_placementMarkerInstance.transform;var normal=marker.forward;
                if(Mathf.Abs(normal.y)<.45f){pipe.transform.rotation=Quaternion.LookRotation(normal,Vector3.up);pipe.transform.position=DragonMount.CenterOnFace(marker.position,normal,PipeAssets.Load(PipePrefabs.Keys[(int)pipe.Kind]).Mesh.bounds.center.y)+normal*.015f;}
            }
            if(!DragonMount.Find(pipe,out _))___m_placementStatus=Player.PlacementStatus.Invalid;
            if(!PrivateArea.CheckAccess(pipe.transform.position,0,false))___m_placementStatus=Player.PlacementStatus.PrivateZone;
            return;
        }
        if(last!=___m_placementGhost){last=___m_placementGhost;pitch=roll=0;}
        if(!Chat.instance||!Chat.instance.HasFocus()){
            if(Plugin.PipeTilt.Value.IsDown())pitch=(pitch+1)%8;
            if(Plugin.PipeRoll.Value.IsDown())roll=(roll+1)%4;
        }
        pipe.transform.rotation=pipe.transform.rotation*Quaternion.Euler(pitch*45,0,roll*90);
        if((pitch!=0||roll!=0)&&___m_placementMarkerInstance&&___m_placementMarkerInstance.activeSelf){
            var marker=___m_placementMarkerInstance.transform;
            if(___m_manualSnapPoint>=0&&___m_manualSnapPoint<PipePiece.PortCount(pipe.Kind))
                pipe.transform.position=marker.position-pipe.transform.rotation*PipePiece.LocalPort(pipe.Kind,___m_manualSnapPoint);
            else PlaceOnSurface(pipe,marker.position,marker.forward);
        }
        Vector3 target=default,direction=default;int sourcePort=-1;float nearest=.5f*.5f;
        foreach(var other in PipePiece.Active){
            if(!other||!other.Live||PipePrefabs.Dragon(other.Kind)||PipePrefabs.Narrow(other.Kind)!=PipePrefabs.Narrow(pipe.Kind)||other==pipe||(other.transform.position-pipe.transform.position).sqrMagnitude>36)continue;
            for(int a=0;a<PipePiece.PortCount(pipe.Kind);a++)for(int b=0;b<PipePiece.PortCount(other.Kind);b++){
                if(other.Connected[b]||Vector3.Dot(pipe.Direction(a),other.Direction(b))>-.94f)continue;
                var delta=other.Port(b)-pipe.Port(a);float distance=delta.sqrMagnitude;if(distance<nearest){nearest=distance;sourcePort=a;target=other.Port(b);direction=-other.Direction(b);}
            }
        }
        if(sourcePort>=0){
            pipe.transform.rotation=Quaternion.FromToRotation(pipe.Direction(sourcePort),direction)*pipe.transform.rotation;
            pipe.transform.position=target-pipe.transform.rotation*PipePiece.LocalPort(pipe.Kind,sourcePort);
        }
        DragonMount.Snap(pipe);
        if(!PrivateArea.CheckAccess(pipe.transform.position,0,false))___m_placementStatus=Player.PlacementStatus.PrivateZone;
    }
    internal static void PlaceOnSurface(PipePiece pipe,Vector3 point,Vector3 normal){
        // Recompute native surface clearance after tilt/roll, otherwise a vertical pipe can sink halfway into a floor.
        var original=pipe.transform.position;pipe.transform.position=point+normal*50;
        Vector3 closest=point;float distance=float.PositiveInfinity;
        foreach(var collider in pipe.GetComponentsInChildren<Collider>()){
            if(!collider.enabled||collider.isTrigger)continue;
            var candidate=collider.ClosestPoint(point);float d=(candidate-point).sqrMagnitude;
            if(d<distance){distance=d;closest=candidate;}
        }
        pipe.transform.position=float.IsPositiveInfinity(distance)?original:point+pipe.transform.position-closest;
    }
}
