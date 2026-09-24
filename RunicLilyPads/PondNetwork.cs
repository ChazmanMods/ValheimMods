using System;
using System.Collections.Generic;
using UnityEngine;
namespace RunicLilyPads
{
    internal sealed class PondNetwork:MonoBehaviour
    {
        static bool dirty;static readonly Dictionary<GardenPiece,List<GardenPiece>> Groups=new Dictionary<GardenPiece,List<GardenPiece>>();
        static readonly Dictionary<GardenPiece,Dictionary<GardenPiece,PondChannel>> Channels=new Dictionary<GardenPiece,Dictionary<GardenPiece,PondChannel>>();
        float next;
        internal static void MarkDirty(){dirty=true;Groups.Clear();Channels.Clear();}
        internal static bool TryChannel(GardenPiece high,GardenPiece low,out PondChannel channel){
            if(!Channels.TryGetValue(high,out var entries)){
                entries=new Dictionary<GardenPiece,PondChannel>();var h=Footprint(high);
                foreach(var p in GardenPiece.Ponds)if(p&&p.Live&&PondChannel.TryCreate(h,Footprint(p),out var c))entries[p]=c;
                Channels[high]=entries;
            }
            return entries.TryGetValue(low,out channel);
        }
        internal static PondFootprint Footprint(GardenPiece p)=>new PondFootprint(p.transform.position.x,p.transform.position.z,p.Settings.Width,p.WaterHeight,p.Settings.Depth,p.Shape,p.transform.eulerAngles.y*Math.PI/180);
        internal static double Distance(GardenPiece p,Vector3 point)=>Footprint(p).Distance(point.x,point.z);
        internal static List<GardenPiece> Group(GardenPiece origin){
            if(Groups.TryGetValue(origin,out var cached))return cached;
            var group=new List<GardenPiece>{origin};
            if(origin.Shape>=0)for(int i=0;i<group.Count;i++)foreach(var p in GardenPiece.Ponds)
                if(p&&p.Live&&!group.Contains(p)&&PondMergeMath.Overlap(Footprint(group[i]),Footprint(p)))group.Add(p);
            foreach(var p in group)Groups[p]=group;return group;
        }
        internal static GardenSettings GroupSettings(GardenPiece p){
            var leader=p;foreach(var other in Group(p))if(other.Settings.GroupRevision>leader.Settings.GroupRevision||(other.Settings.GroupRevision==leader.Settings.GroupRevision&&Compare(other,leader)>0))leader=other;
            return leader.Settings;
        }
        static int Compare(GardenPiece a,GardenPiece b){int n=a.transform.position.x.CompareTo(b.transform.position.x);if(n!=0)return n;n=a.transform.position.z.CompareTo(b.transform.position.z);if(n!=0)return n;return string.CompareOrdinal(a.GetComponent<ZNetView>().GetZDO()?.m_uid.ToString(),b.GetComponent<ZNetView>().GetZDO()?.m_uid.ToString());}
        internal static bool Covers(GardenPiece a,GardenPiece b)=>a!=b&&(a.WaterHeight>b.WaterHeight+.08f||(Mathf.Abs(a.WaterHeight-b.WaterHeight)<=.08f&&Compare(a,b)>0));
        internal static GardenPiece At(Vector3 point){GardenPiece best=null;foreach(var p in GardenPiece.Ponds)if(p&&p.Live&&Distance(p,point)<1&&(!best||p.WaterHeight>best.WaterHeight))best=p;return best;}
        internal static float WaterLevel(Vector3 point){
            foreach(var p in GardenPiece.Ponds){if(!p||!p.Live||Distance(p,point)>1.0001)continue;
                double nearest=double.PositiveInfinity;var group=Group(p);foreach(var q in group)nearest=Math.Min(nearest,PondMergeMath.Edge(Footprint(q),point.x,point.z));
                double weight=0,height=0;foreach(var q in group){double w=Math.Exp(-(PondMergeMath.Edge(Footprint(q),point.x,point.z)-nearest)*2.4);weight+=w;height+=w*q.WaterHeight;}return (float)(height/weight);
            }return float.NegativeInfinity;
        }        void Update(){
            if(Time.time<next)return;next=Time.time+.5f;
            foreach(var p in GardenPiece.Ponds)if(p&&p.Live)p.SyncGroup(GroupSettings(p));
            if(!dirty)return;dirty=false;
            foreach(var p in GardenPiece.Ponds)if(p&&p.Live){p.RefreshSurface();PondTerrain.Refresh(p.transform.position,18);}
        }
        internal static Mesh Surface(GardenPiece owner){
            var vertices=new List<Vector3>();var indices=new List<int>();var tex=new List<Vector2>();
            var neighbors=new List<GardenPiece>();foreach(var p in GardenPiece.Ponds)if(p&&p.Live&&Covers(p,owner)&&PondMergeMath.Overlap(Footprint(p),Footprint(owner)))neighbors.Add(p);
            var center=owner.transform.position;float r=owner.Settings.Width*.5f+1.2f,step=.2f;
            float startX=Mathf.Floor((center.x-r)/step)*step,startZ=Mathf.Floor((center.z-r)/step)*step;
            for(float x=startX;x<center.x+r;x+=step)for(float z=startZ;z<center.z+r;z+=step){
                var a=new Vector3(x,owner.WaterHeight,z);var b=a+Vector3.right*step;var c=b+Vector3.forward*step;var d=a+Vector3.forward*step;
                Add(new List<Vector3>{a,c,b});Add(new List<Vector3>{a,d,c});
            }
            void Add(List<Vector3> polygon){
                polygon=Clip(polygon,owner,false);foreach(var p in neighbors){polygon=Clip(polygon,p,true);if(polygon.Count==0)return;}
                if(polygon.Count<3)return;int start=vertices.Count;
                foreach(var v in polygon){var local=owner.transform.InverseTransformPoint(v);float water=WaterLevel(v);local.y=float.IsNegativeInfinity(water)?0:water-owner.WaterHeight;vertices.Add(local);tex.Add(new Vector2(v.x,v.z)*.2f);}
                for(int i=1;i<polygon.Count-1;i++){indices.Add(start);indices.Add(start+i);indices.Add(start+i+1);}
            }
            var mesh=new Mesh{name="Joined pond surface",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,tex);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static List<Vector3> Clip(List<Vector3> input,GardenPiece pond,bool outside){
            var output=new List<Vector3>();if(input.Count==0)return output;
            Vector3 a=input[input.Count-1];double da=(SurfaceDistance(pond,a,outside)-1)*(outside?-1:1);
            foreach(var b in input){double db=(SurfaceDistance(pond,b,outside)-1)*(outside?-1:1);if((da<=0)!=(db<=0))output.Add(Vector3.Lerp(a,b,(float)(da/(da-db))));if(db<=0)output.Add(b);a=b;da=db;}return output;
        }
        static double SurfaceDistance(GardenPiece high,Vector3 point,bool coverage)=>Distance(high,point);        internal static void Waterfalls(GardenPiece high,Transform parent){
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            foreach(var low in GardenPiece.Ponds){
                if(!low||!low.Live||Math.Abs(high.WaterHeight-low.WaterHeight)<=.15f||!PondMergeMath.Overlap(Footprint(high),Footprint(low)))continue;
                NaturalSpillway.Create(parent,high.WaterHeight>low.WaterHeight?high:low,high.WaterHeight>low.WaterHeight?low:high);
            }
        }
    }
}



