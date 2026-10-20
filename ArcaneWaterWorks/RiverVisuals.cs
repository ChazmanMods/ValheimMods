using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Jotunn.Managers;
using Object=UnityEngine.Object;

namespace ArcaneWaterWorks;

internal sealed class RiverVisuals:MonoBehaviour
{
    readonly List<Object> owned=new List<Object>();
    Spring spring;
    Material waterMaterial;
    MeshRenderer waterRenderer;
    Renderer[] renderers;
    AudioSource audio;
    float next;
    float nextSurfaceRefresh;
    bool visible=true;
    int audioProfile=-1;
    internal static void Rebuild(Spring owner){
        var work=RebuildRoutine(owner);while(work.MoveNext()){} // Synchronous diagnostic entry point.
    }
    internal static IEnumerator RebuildRoutine(Spring owner){
        var go=new GameObject("waterworks_river_pending");go.SetActive(false);go.transform.SetParent(owner.transform,false);go.transform.rotation=Quaternion.identity;
        var visuals=go.AddComponent<RiverVisuals>();visuals.spring=owner;var work=visuals.Build();bool success=false;
        try{
            while(owner){
                bool more;try{more=work.MoveNext();}catch(Exception e){Plugin.Log.LogError("River build: "+e);yield break;}
                if(!more){success=true;break;}yield return work.Current;
            }
            if(success&&owner){var old=owner.transform.Find("waterworks_river");if(old){old.gameObject.SetActive(false);Object.Destroy(old.gameObject);}
                go.name="waterworks_river";go.SetActive(true);
            }
        }finally{(work as IDisposable)?.Dispose();if(!success&&go)Object.Destroy(go);}
    }
    internal static void RebuildNearby(Spring owner,Route oldRoute=null){
        RiverRebuildQueue.Request(owner,oldRoute);
    }
    static int Order(Spring a,Spring b){int n=a.transform.position.x.CompareTo(b.transform.position.x);if(n!=0)return n;n=a.transform.position.z.CompareTo(b.transform.position.z);if(n!=0)return n;return string.CompareOrdinal(a.View.GetZDO().m_uid.ToString(),b.View.GetZDO().m_uid.ToString());}
    T Own<T>(T value) where T:Object {owned.Add(value);return value;}
    static Vector3 V(Node n)=>new Vector3(n.X,n.Y,n.Z);
    IEnumerator Build(){
        var route=spring.Route;var nodes=route.Points;bool headless=SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null;
        // One sparse surface mesh follows the actual triangular terrain grid at the shore.
        // Cells are visited once even where river branches join.
        const float step=.5f;
        var cells=new HashSet<(int,int)>();float radius=route.Width*.5f+1.5f;
        if(!headless)for(int i=1;i<nodes.Count;i++){
            var a=nodes[route.Parent(i)];var b=nodes[i];
            for(int z=Mathf.FloorToInt((spring.transform.position.z+Mathf.Min(a.Z,b.Z)-radius)/step);z<=Mathf.CeilToInt((spring.transform.position.z+Mathf.Max(a.Z,b.Z)+radius)/step);z++)
            for(int x=Mathf.FloorToInt((spring.transform.position.x+Mathf.Min(a.X,b.X)-radius)/step);x<=Mathf.CeilToInt((spring.transform.position.x+Mathf.Max(a.X,b.X)+radius)/step);x++)cells.Add((x,z));
        }
        var verts=new List<Vector3>();var triangles=new List<int>();
        var neighbors=new List<Spring>();var region=RiverTerrain.Bounds(spring,route);
        var appearanceRivers=new List<Spring>{spring};
        var appearanceRegion=region;appearanceRegion.Expand(8);
        foreach(var other in Spring.Active)if(other&&other!=spring&&other.Live&&other.Route!=null&&appearanceRegion.Intersects(RiverTerrain.Bounds(other,other.Route)))appearanceRivers.Add(other);
        var joinedMembers=new List<Spring>();
        if(spring.SurfaceMembers!=null)foreach(var other in spring.SurfaceMembers)
            if(other&&other.Live)joinedMembers.Add(other);
        foreach(var other in Spring.Active)if(other&&other!=spring&&other.Live&&other.Route!=null&&Order(other,spring)<0&&region.Intersects(RiverTerrain.Bounds(other,other.Route)))neighbors.Add(other);
        var grid=new Dictionary<(float,float),(Vector3 p,float wet,float along)>();
        var bankWet=new Dictionary<(float,float),float>();
        var groundDepth=new Dictionary<(float,float),float>();
        int solidMask=LayerMask.GetMask("static_solid");
        (Vector3 p,float wet,float along) Point(float x,float z){
            if(grid.TryGetValue((x,z),out var cached))return cached;
            spring.Surface(x,z,out float distance,out float level,out float along);
            var position=new Vector3(x,level,z);
            float wet=radius-distance;
            if(joinedMembers.Count>0){
                wet=float.NegativeInfinity;
                foreach(var member in joinedMembers){if(!member||!member.Live||member.Route==null)continue;var offset=spring.transform.position-member.transform.position;
                    member.Route.Sample(x+offset.x,z+offset.z,out float d,out _,out _);
                    wet=Mathf.Max(wet,member.Route.Width*.5f+1.5f-d);
                }
            }
            float contact=1000;
            if(RiverTerrain.SamplePlanned(spring,spring.transform.position+position,out float ground)){
                contact=spring.transform.position.y+position.y-ground;wet=Mathf.Min(wet,contact-.015f);
            }
            // Sample fixed rock colliders once per grid point, never per frame.
            // Planned terrain remains authoritative while a terrain edit is queued.
            var worldContact=spring.transform.position+position;
            if(contact>0&&solidMask!=0){
                if(Physics.CheckSphere(worldContact,.025f,solidMask,QueryTriggerInteraction.Ignore))contact=-.025f;
                else if(Physics.Raycast(worldContact,Vector3.down,out var hit,Mathf.Min(32,contact),solidMask,QueryTriggerInteraction.Ignore))contact=Mathf.Min(contact,hit.distance);
                wet=Mathf.Min(wet,contact-.015f);
            }
            groundDepth[(x,z)]=contact;
            // A mesh ownership edge inside another river is not a shoreline.
            // Keep opacity there; fading both separate skirts exposes a dark seam.
            float shore=radius-distance;
            foreach(var other in appearanceRivers){if(!other||!other.Live||other.Route==null||other==spring)continue;
                var p=spring.transform.position+position-other.transform.position;
                other.Surface(p.x,p.z,out float d,out float h,out _);
                if(Mathf.Abs(h-p.y)<.05f)shore=Mathf.Max(shore,other.Route.Width*.5f+1.5f-d);
            }
            bankWet[(x,z)]=Mathf.Min(shore,contact-.015f);
            // Each overlap has one deterministic surface owner; section joins cannot flicker.
            foreach(var other in neighbors){if(!other||!other.Live||other.Route==null)continue;var p=spring.transform.position+position-other.transform.position;
                other.Surface(p.x,p.z,out var distance2,out var level2,out _);
                bool joined=spring.SurfaceMembers!=null&&ReferenceEquals(spring.SurfaceMembers,other.SurfaceMembers);
                if(!joined&&Mathf.Abs(level2+other.transform.position.y-(level+spring.transform.position.y))<.05f)
                    wet=Mathf.Min(wet,distance2-(other.Route.Width*.5f+1.5f));
            }
            var result=(position,wet,along);grid[(x,z)]=result;return result;
        }
        void Triangle((Vector3 p,float wet,float along) a,(Vector3 p,float wet,float along) b,(Vector3 p,float wet,float along) c){
            var input=new[]{a,b,c};var clipped=new List<(Vector3 p,float wet,float along)>(4);
            var previous=input[2];
            foreach(var current in input){
                if((previous.wet>0)!=(current.wet>0)){
                    float t=previous.wet/(previous.wet-current.wet);
                    clipped.Add((Vector3.Lerp(previous.p,current.p,t),0,Mathf.Lerp(previous.along,current.along,t)));
                }
                if(current.wet>0)clipped.Add(current);previous=current;
            }
            // Native ocean owns sea-level water, without an overlapping flat sheet.
            if(clipped.Count>0){
                var above=new List<(Vector3 p,float wet,float along)>();var prev=clipped[clipped.Count-1];float da=prev.p.y+spring.transform.position.y-OceanJoin.Sea;
                foreach(var curr in clipped){float db=curr.p.y+spring.transform.position.y-OceanJoin.Sea;
                    if((da>0)!=(db>0)){float t=da/(da-db);above.Add((Vector3.Lerp(prev.p,curr.p,t),Mathf.Lerp(prev.wet,curr.wet,t),Mathf.Lerp(prev.along,curr.along,t)));}
                    if(db>0)above.Add(curr);prev=curr;da=db;
                }clipped=above;
            }
            // Clip pond ownership separately from bank wetness. Combining their distances
            // before interpolation shifts the shared edge and opens a seam.
            if(clipped.Count>0){
                var outside=new List<(Vector3 p,float wet,float along)>();var prev=clipped[clipped.Count-1];
                float da=ArcaneWaterWorks.Pools.RiverGardens.PondEdge(spring.transform.position+prev.p);
                foreach(var curr in clipped){float db=ArcaneWaterWorks.Pools.RiverGardens.PondEdge(spring.transform.position+curr.p);
                    if((da>=0)!=(db>=0)){float t=da/(da-db);outside.Add((Vector3.Lerp(prev.p,curr.p,t),Mathf.Lerp(prev.wet,curr.wet,t),Mathf.Lerp(prev.along,curr.along,t)));}
                    if(db>=0)outside.Add(curr);prev=curr;da=db;
                }clipped=outside;
            }
            for(int i=1;i+1<clipped.Count;i++){
                if(Vector3.Cross(clipped[i].p-clipped[0].p,clipped[i+1].p-clipped[0].p).y<=.000001f)continue;
                if(joinedMembers.Count>0){
                    var center=(clipped[0].p+clipped[i].p+clipped[i+1].p)/3+spring.transform.position;
                    Spring owner=null;float best=float.NegativeInfinity;
                    foreach(var member in joinedMembers){if(!member||!member.Live||member.Route==null)continue;var p=center-member.transform.position;
                        member.Route.Sample(p.x,p.z,out float d,out _,out _);float score=member.Route.Width*.5f+1.5f-d;
                        if(score>best+.0001f||Mathf.Abs(score-best)<.0001f&&(owner==null||Order(member,owner)<0)){owner=member;best=score;}
                    }
                    if(owner!=spring)continue;
                }
                foreach(var point in new[]{clipped[0],clipped[i],clipped[i+1]}){
                    var p=point.p;
                    triangles.Add(verts.Count);verts.Add(p);
                }
            }
        }
        int budget=0;
        foreach(var cell in cells){
            if(++budget%8==0)yield return null;
            float x=cell.Item1*step-spring.transform.position.x,z=cell.Item2*step-spring.transform.position.z;
            var a=Point(x,z);var b=Point(x+step,z);var c=Point(x,z+step);var d=Point(x+step,z+step);
            Triangle(a,c,b);Triangle(b,c,d);
        }
        var waterUv=new Vector2[verts.Count];var flowUv=new Vector2[verts.Count];var shoreUv=new Vector2[verts.Count];
        var contactUv=new Vector2[verts.Count];
        var appearance=new Color[verts.Count];
        var appearanceCache=new Dictionary<(float,float),(Color color,Vector2 flow)>();
        var shoreCache=new Dictionary<(float,float),Vector2>();
        for(int i=0;i<verts.Count;i++){
            if(i%48==0)yield return null;
            var p=verts[i];waterUv[i]=new Vector2(p.x+spring.transform.position.x,p.z+spring.transform.position.z)*.2f;
            if(!shoreCache.TryGetValue((p.x,p.z),out var data)){
                Point(p.x,p.z);
                data=new Vector2(Mathf.Max(0,bankWet[(p.x,p.z)]),route.OutletBlend(p.x,p.z));
                shoreCache[(p.x,p.z)]=data;
            }
            shoreUv[i]=data;
            // Never interpolate infinity on the GPU: an absent pond needs a finite
            // distance outside the entire crossfade, or its alpha can become NaN.
            float pondEdge=ArcaneWaterWorks.Pools.RiverGardens.PondEdge(p+spring.transform.position);
            contactUv[i]=new Vector2(groundDepth[(p.x,p.z)],Mathf.Min(1024,pondEdge));
            if(!appearanceCache.TryGetValue((p.x,p.z),out var look)){
                var color=RiverAppearance.Sample(p+spring.transform.position,appearanceRivers,out var flow);
                look=(color,flow);appearanceCache[(p.x,p.z)]=look;
            }
            appearance[i]=look.color;flowUv[i]=look.flow;
            float turbulence=Mathf.Clamp01((look.flow.magnitude-.75f)/1.5f);
            float contact=Mathf.Clamp01(1-contactUv[i].x/.5f);
            appearance[i].g*=Mathf.Max(turbulence,contact*.5f);
            appearance[i].b=RiverSurface.Amplitude(contactUv[i].x,data.x,p.y+spring.transform.position.y-OceanJoin.Sea,contactUv[i].y,look.flow);
        }
        var waterMesh=Own(new Mesh{name="ArcaneNaturalWaters river",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=verts.ToArray(),uv=waterUv,uv2=flowUv,uv3=shoreUv,uv4=contactUv,colors=appearance,triangles=triangles.ToArray()});SmoothNormals(waterMesh);waterMesh.RecalculateBounds();var waveBounds=waterMesh.bounds;waveBounds.Expand(.3f);waterMesh.bounds=waveBounds;
        var surfaceNormals=waterMesh.normals;
        var normalCache=new Dictionary<(float,float),Vector3>();
        for(int i=0;i<verts.Count;i++){
            if(i%48==0)yield return null;
            var p=verts[i];spring.Surface(p.x,p.z,out _,out float nominal,out _);
            if(p.y<nominal-.02f)continue;
            if(!normalCache.TryGetValue((p.x,p.z),out var n)){n=GardenBridge.SurfaceNormal(p+spring.transform.position);normalCache[(p.x,p.z)]=n;}surfaceNormals[i]=n;
        }
        waterMesh.normals=surfaceNormals;
        var tangents=new Vector4[verts.Count];
        for(int i=0;i<tangents.Length;i++){var n=surfaceNormals[i];var t=new Vector3(n.y,-n.x,0).normalized;tangents[i]=new Vector4(t.x,t.y,t.z,-1);}
        waterMesh.tangents=tangents;
        var material=Own(new Material(Plugin.Water));
        waterMaterial=material;
        RiverSurface.Apply(material);
        var surface=new GameObject("river_surface");surface.layer=LayerMask.NameToLayer("Water");surface.transform.SetParent(transform,false);
        surface.AddComponent<MeshFilter>().sharedMesh=waterMesh;var renderer=surface.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.enabled=!headless;
        RiverAppearance.ConfigureLighting(renderer);
        // RiverSurface owns material binding; pond binding must not replace this shader.
        waterRenderer=renderer;
        if(!headless)surface.AddComponent<OceanJoin>().Initialize(waterMesh);

        AddVolume(renderer);
        if(headless)yield break;
        // Keep the sound emitter independent from the geometry transform.
        var sound=new GameObject("river_sound");sound.transform.SetParent(transform,false);audio=sound.AddComponent<AudioSource>();
        audio.playOnAwake=false;audio.loop=true;audio.spatialBlend=1;audio.rolloffMode=AudioRolloffMode.Linear;audio.minDistance=2;audio.maxDistance=24;audio.dopplerLevel=0;
        renderers=GetComponentsInChildren<Renderer>();
    }
    void AddVolume(MeshRenderer renderer){
        // A single cheap box replaces all per-segment convex meshes and pool wedges.
        // GetWaterSurface masks this broad phase to the indexed channel footprint.
        var bounds=new Bounds(V(spring.Route.Points[0]),Vector3.zero);
        foreach(var p in (spring.BankRoute??spring.Route).Points)bounds.Encapsulate(V(p));
        if(spring.JoinedRoute!=null)foreach(var p in spring.JoinedRoute.Points)bounds.Encapsulate(V(p)+spring.JoinedOrigin-spring.transform.position);
        bounds.Expand(new Vector3(spring.Route.Width,0,spring.Route.Width));
        var min=bounds.min;var max=bounds.max;min.y-=32;max.y+=2;bounds.SetMinMax(min,max);
        var go=new GameObject("river_water_volume");go.SetActive(false);go.transform.SetParent(transform,false);go.layer=LayerMask.NameToLayer("WaterVolume");
        var collider=go.AddComponent<BoxCollider>();collider.center=bounds.center;collider.size=bounds.size;collider.isTrigger=true;
        var water=go.AddComponent<WaterVolume>();water.m_waterSurface=renderer;water.m_forceDepth=0;water.m_useGlobalWind=true;water.m_surfaceOffset=0;
        var marker=go.AddComponent<RiverLiquid>();marker.Spring=spring;marker.WorldBounds=new Bounds(spring.transform.position+bounds.center,bounds.size);go.SetActive(true);
    }
    // Duplicated triangle vertices must share normals; otherwise every bend gets a lighting crease.
    internal static void SmoothNormals(Mesh mesh){
        var vertices=mesh.vertices;var triangles=mesh.triangles;var sums=new Dictionary<(int,int,int),Vector3>();
        (int,int,int) Key(Vector3 v)=>(Mathf.RoundToInt(v.x*10000),Mathf.RoundToInt(v.y*10000),Mathf.RoundToInt(v.z*10000));
        for(int i=0;i<triangles.Length;i+=3){var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];var n=Vector3.Cross(b-a,c-a);
            foreach(var v in new[]{a,b,c}){var key=Key(v);sums.TryGetValue(key,out var sum);sums[key]=sum+n;}}
        var normals=new Vector3[vertices.Length];for(int i=0;i<vertices.Length;i++)normals[i]=sums[Key(vertices[i])].normalized;mesh.normals=normals;
    }
    void Update(){
        // Native WaterVolume may instance the shared renderer material during Start.
        // Retain that generated instance too, so editing rivers does not leak materials.
        if(waterRenderer&&waterRenderer.sharedMaterial!=waterMaterial){waterMaterial=waterRenderer.sharedMaterial;Own(waterMaterial);}
        if(waterMaterial&&waterMaterial.shader.name=="ArcaneWaters/RiverFlow"){
            if(GameCamera.instance)GameCamera.instance.GetComponent<Camera>().depthTextureMode|=DepthTextureMode.Depth;
            waterMaterial.SetFloat("_RiverTime",RiverSurface.Clock);
            if(Time.time>=nextSurfaceRefresh){nextSurfaceRefresh=Time.time+2;RiverSurface.Refresh(waterMaterial);}
        }
        if(!audio||!spring||spring.Route==null||Time.time<next)return;next=Time.time+.25f;
        var player=Player.m_localPlayer;if(!player){audio.Stop();return;}
        var local=player.transform.position-spring.transform.position;float best=float.PositiveInfinity;Vector3 nearest=Vector3.zero;
        var points=spring.Route.Points;
        for(int i=1;i<points.Count;i++){var a=V(points[spring.Route.Parent(i)]);var b=V(points[i]);float t=Mathf.Clamp01(Vector3.Dot(local-a,b-a)/(b-a).sqrMagnitude);var p=Vector3.Lerp(a,b,t);float d=(local-p).sqrMagnitude;if(d<best){best=d;nearest=p;}}
        audio.transform.localPosition=nearest;var garden=ArcaneWaterWorks.Pools.RiverGardens.SettingsAt(spring.transform.position+nearest);
        audio.maxDistance=garden?.WaterfallRange??24;// Only audible sections contribute to overlap normalization.
        int audible=0;foreach(var river in Spring.Active){if(!river||!river.Live||river.Route==null||river.Route.Volume<=0)continue;
            var bounds=RiverTerrain.Bounds(river,river.Route);if(bounds.SqrDistance(player.transform.position)<audio.maxDistance*audio.maxDistance)audible++;}
        WaterAudio.SetVolume(audio,Plugin.MasterVolume.Value*Plugin.Volume.Value*spring.Route.Volume*(garden?.WaterfallVolume??1)/Mathf.Sqrt(Mathf.Max(1,audible)));
        if(audioProfile!=spring.Route.ResolvedSound){audio.Stop();audioProfile=spring.Route.ResolvedSound;audio.clip=RiverAudio.Get(audioProfile);}
        if(best<(audio.maxDistance+2)*(audio.maxDistance+2)&&audio.volume>0){if(audio.clip&&!audio.isPlaying)audio.Play();}else if(audio.isPlaying)audio.Stop();
        bool show=best<160*160;
        if(visible!=show){visible=show;foreach(var r in renderers)if(r)r.enabled=show;}
    }
    void OnDestroy(){foreach(var obj in owned)if(obj)Object.Destroy(obj);}
}
