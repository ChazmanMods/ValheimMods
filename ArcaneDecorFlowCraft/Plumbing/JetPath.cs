using System.Collections.Generic;
using UnityEngine;
namespace ArcaneDecorFlowCraft.Plumbing;
internal static class JetPath
{
    static readonly RaycastHit[] hits=new RaycastHit[64];
    internal static void Trace(Vector3 origin,Vector3 direction,float flow,Transform source,List<Vector3> points,out bool impact,out Vector3 normal,out SyncFountain receiver,out float travel){
        points.Clear();points.Add(origin);impact=false;normal=Vector3.up;receiver=null;travel=0;
        Vector3 velocity=direction*PipeFlowModel.JetSpeed(flow);int mask=LayerMask.GetMask("terrain","piece","static_solid","Default");
        for(int i=1;i<81;i++){
            float t=i*.05f;var point=origin+velocity*t+Vector3.down*(4.905f*t*t);var previous=points[points.Count-1];var segment=point-previous;float length=segment.magnitude,nearest=length;
            int count=Physics.RaycastNonAlloc(previous,segment.normalized,hits,length,mask,QueryTriggerInteraction.Ignore);
            for(int h=0;h<count;h++)if(!hits[h].collider.transform.IsChildOf(source)&&hits[h].distance<=nearest){nearest=hits[h].distance;normal=hits[h].normal;impact=true;receiver=hits[h].collider.GetComponentInParent<SyncFountain>();}
            if(impact)point=previous+segment.normalized*nearest;
            float water=Floating.GetLiquidLevel(point,.05f,LiquidType.Water);
            if(!impact&&water>point.y&&water<origin.y-.03f){float mix=Mathf.Clamp01((previous.y-water)/Mathf.Max(.0001f,previous.y-point.y));point=Vector3.Lerp(previous,point,mix);point.y=water;normal=Vector3.up;impact=true;receiver=null;}
            points.Add(point);travel=(i-1)*.05f+.05f*(length>0?Vector3.Distance(previous,point)/length:1);if(impact)break;
        }
    }
}
