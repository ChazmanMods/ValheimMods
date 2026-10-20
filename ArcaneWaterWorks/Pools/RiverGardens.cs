using System;
using System.Collections.Generic;
using UnityEngine;
using Jotunn.Managers;

namespace ArcaneWaterWorks.Pools
{
    // ArcaneNaturalWaters owns its pool and river ambience state independently.
    public static class RiverGardens
    {
        public struct Span
        {
            public Vector3 A, B;
            public float Width;
            public Span(Vector3 a,Vector3 b,float width){A=a;B=b;Width=width;}
            public Vector3 Closest(Vector3 p){var d=B-A;d.y=0;var v=p-A;v.y=0;return Vector3.Lerp(A,B,Mathf.Clamp01(Vector3.Dot(v,d)/Mathf.Max(.001f,d.sqrMagnitude)));}
            public bool Contains(Vector3 p){var axis=B-A;axis.y=0;var offset=p-A;offset.y=0;float along=Vector3.Dot(axis,offset);if(along<-.001f||along>axis.sqrMagnitude+.001f)return false;var d=p-Closest(p);return d.x*d.x+d.z*d.z<=Mathf.Pow(Width*.5f+2,2);}
        }
        public static Func<Vector3,float> SampleWater;
        public static Func<Vector3,float,float> JoinPondWater;
        public static Action<GardenPiece,Mesh,Material> ConfigurePondSurface;

        public static Func<Vector3,List<Span>> Section;
        public static event Action Changed;
        public static event Action PondsChanged;
        public static event Action<Bounds> PondRegionChanged;
        internal static void NotifyPondsChanged(Bounds region){PondsChanged?.Invoke();PondRegionChanged?.Invoke(region);}
        public static void NotifyChanged(){Changed?.Invoke();}
        public static bool ConfigureOutdoorWater(Material material)=>OutdoorWater.Apply(material);
        public static Material OutdoorWaterSource()=>OutdoorWater.Source();
        public static float RawPondLevel(Vector3 p)=>PondNetwork.RawWaterLevel(p);
        public static Material CreatePondMaterial(Vector3 point){
            GardenPiece nearest=null;float edge=float.PositiveInfinity;
            foreach(var p in GardenPiece.Ponds)if(p&&p.Live){float d=(float)PondMergeMath.Edge(PondNetwork.Footprint(p),point.x,point.z);if(d<edge){edge=d;nearest=p;}}
            if(!nearest)return null;
            var material=new Material(GardenPrefabs.Water);PondBuilder.ConfigureClearWater(material);
            return material;
        }
        public static bool PondConnection(Vector3 point,out float edge,out float height){
            edge=float.PositiveInfinity;height=0;GardenPiece nearest=null;
            foreach(var pond in GardenPiece.Ponds)if(pond&&pond.Live){float d=(float)PondMergeMath.Edge(PondNetwork.Footprint(pond),point.x,point.z);if(d<edge){edge=d;nearest=pond;}}
            if(!nearest||edge>=4)return false;
            var group=PondNetwork.Group(nearest);double min=double.PositiveInfinity;
            foreach(var p in group)min=Math.Min(min,PondMergeMath.Edge(PondNetwork.Footprint(p),point.x,point.z));
            double weights=0,total=0;foreach(var p in group){double w=Math.Exp(-(PondMergeMath.Edge(PondNetwork.Footprint(p),point.x,point.z)-min)*2.4);weights+=w;total+=w*p.WaterHeight;}
            height=(float)(total/weights);return true;
        }
        public static float PondEdge(Vector3 p){float edge=float.PositiveInfinity;foreach(var pond in GardenPiece.Ponds)if(pond&&pond.Live)edge=Mathf.Min(edge,(float)PondMergeMath.Edge(PondNetwork.Footprint(pond),p.x,p.z));return edge;}
        public static float WaterLevel(Vector3 p)=>SampleWater?.Invoke(p)??float.NegativeInfinity;
        public static float PondLevel(Vector3 p)=>PondNetwork.WaterLevel(p);
        public static void ConfigureWater(Material material)=>PondBuilder.ConfigureClearWater(material);
        public static void BindOutdoorWater(MeshRenderer renderer)=>OutdoorWater.Bind(renderer);
        public static GardenSettings SettingsAt(Vector3 p)
        {
            RiverGardenSection best=null;
            foreach(var section in RiverGardenSection.Active)
                if(section&&section.Piece&&section.Piece.Live&&section.Contains(p)&&(!best||section.Piece.Settings.GroupRevision>best.Piece.Settings.GroupRevision||
                   section.Piece.Settings.GroupRevision==best.Piece.Settings.GroupRevision&&Compare(section.transform.position,best.transform.position)<0))best=section;
            return best?best.Piece.Settings:null;
        }
        static int Compare(Vector3 a,Vector3 b){int n=a.x.CompareTo(b.x);if(n!=0)return n;n=a.z.CompareTo(b.z);return n!=0?n:a.y.CompareTo(b.y);}
        public static GardenPiece AtSection(Vector3 p){foreach(var s in RiverGardenSection.Active)if(s&&s.Contains(p))return s.Piece;return null;}
        public static bool OpenSection(Vector3 p){var piece=AtSection(p);if(!piece||!piece.CanEdit())return false;GardenEditor.Open(piece);return true;}
        internal static void RegisterStone()
        {
            if(!GardenPrefabs.Rock)return;
            var root=GardenPrefabs.Root("piece_arcanewaters_river_ambience","River Ambience Stone","");
            var visual=root.transform.Find("garden_visual");GardenPrefabs.AddVisual(GardenPrefabs.Rock,visual,.65f,true);
            root.AddComponent<GardenPiece>().Shape=-2;root.AddComponent<RiverGardenSection>();
            root.GetComponent<Piece>().m_icon=Plugin.BuildIcon("river-ambience",GardenPrefabs.Icon);
            GardenPrefabs.Register(root,"River Ambience Stone","Controls ambience, river audio and fireflies along 20 metres of ArcaneNaturalWaters river. Place beside the channel, then press E. Does not plant or spawn decorations.",4);
        }
    }
    public sealed class RiverGardenSection:MonoBehaviour
    {
        internal static readonly List<RiverGardenSection> Active=new List<RiverGardenSection>();
        internal GardenPiece Piece;
        List<RiverGardens.Span> spans=new List<RiverGardens.Span>();
        bool dirty=true;
        void Awake(){Piece=GetComponent<GardenPiece>();}
        void OnEnable(){Active.Add(this);RiverGardens.Changed+=Invalidate;dirty=true;}
        void OnDisable(){Active.Remove(this);RiverGardens.Changed-=Invalidate;}
        void Invalidate(){dirty=true;}
        void Update(){if(!dirty||!Piece||!Piece.Live)return;dirty=false;spans=RiverGardens.Section?.Invoke(transform.position)??new List<RiverGardens.Span>();Piece.RefreshRiver();}
        internal bool CanEdit(){foreach(var span in spans)for(int i=0;i<=4;i++)if(!PrivateArea.CheckAccess(Vector3.Lerp(span.A,span.B,i/4f),span.Width*.5f+2,false))return false;return true;}
        internal bool Contains(Vector3 p){foreach(var span in spans)if(span.Contains(p))return true;return false;}
        internal void Configure(ParticleSystem particle)
        {
            // One emitter follows the actual channel, including bends. No extra colliders or network objects.
            if(spans.Count==0){var emission=particle.emission;emission.enabled=false;return;}
            var vertices=new List<Vector3>();
            foreach(var span in spans){float length=Vector3.Distance(span.A,span.B);int count=Mathf.Max(1,Mathf.CeilToInt(length));
                var d=(span.B-span.A).normalized;var side=new Vector3(-d.z,0,d.x)*span.Width*.3f;
                for(int i=0;i<=count;i++){var p=Vector3.Lerp(span.A,span.B,i/(float)count)+Vector3.up*.8f;vertices.Add(particle.transform.InverseTransformPoint(p));vertices.Add(particle.transform.InverseTransformPoint(p+side));vertices.Add(particle.transform.InverseTransformPoint(p-side));}}
            var mesh=new Mesh{name="River garden firefly positions"};mesh.SetVertices(vertices);mesh.RecalculateBounds();Piece.Own(mesh);
            var shape=particle.shape;shape.shapeType=ParticleSystemShapeType.Mesh;shape.mesh=mesh;shape.meshShapeType=ParticleSystemMeshShapeType.Vertex;shape.scale=Vector3.one;
        }
    }
}
