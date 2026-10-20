using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ArcaneWaterWorks;

internal sealed class RiverRebuildQueue:MonoBehaviour
{
    static readonly HashSet<Spring> pending=new HashSet<Spring>();
    static readonly List<Bounds> regions=new List<Bounds>();
    IEnumerator build;
    static RiverRebuildQueue instance;
    void Awake(){instance=this;}
    internal static bool IsBusy=>PendingCount>0||instance&&instance.build!=null;
    internal static int Completed;
    internal static double MaxSliceMilliseconds;
    internal static int PendingCount=>pending.Count+regions.Count;
    internal static void Request(Spring source,Route previous=null){
        var route=source.Route??previous;if(route==null)return;
        var bounds=RiverTerrain.Bounds(source,route);if(previous!=null)bounds.Encapsulate(RiverTerrain.Bounds(source,previous));bounds.Expand(8);
        regions.Add(bounds);
        if(source.SurfaceMembers!=null)foreach(var river in source.SurfaceMembers)
            if(river&&river.Live)pending.Add(river);
    }
    internal static void Region(Bounds bounds){bounds.Expand(8);regions.Add(bounds);}
    // Gather every source's Update before starting a batch, regardless of Unity's
    // component execution order during streamed object activation.
    void LateUpdate(){
        var timer=Stopwatch.StartNew();
        if(build==null&&regions.Count>0){
            RiverNetwork.Refresh();
            foreach(var river in Spring.Active){if(!river||!river.Live||river.Route==null)continue;
                foreach(var region in regions)if(region.Intersects(RiverTerrain.Bounds(river,river.Route))){pending.Add(river);break;}
            }
            // Include all members of an affected shared surface after topology changes.
            foreach(var changed in new List<Spring>(pending))if(changed&&changed.SurfaceMembers!=null)
                foreach(var river in changed.SurfaceMembers)if(river&&river.Live)pending.Add(river);
            foreach(var river in pending)if(river&&river.Live)RiverTerrain.Refresh(river,river.Route);
            regions.Clear();
        }
        while(timer.Elapsed.TotalMilliseconds<3){
            if(build==null){
                Spring selected=null;foreach(var candidate in pending){selected=candidate;break;}
                if(ReferenceEquals(selected,null))break;pending.Remove(selected);
                if(!selected||!selected.Live||selected.Route==null)continue;
                build=RiverVisuals.RebuildRoutine(selected);
            }
            if(build.MoveNext())continue;
            (build as System.IDisposable)?.Dispose();build=null;Completed++;
        }
        MaxSliceMilliseconds=System.Math.Max(MaxSliceMilliseconds,timer.Elapsed.TotalMilliseconds);
    }
    void OnDestroy(){(build as System.IDisposable)?.Dispose();build=null;pending.Clear();regions.Clear();}
}
