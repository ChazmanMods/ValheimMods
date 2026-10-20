using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

// Local visuals: ballistic jet, travelling ripples and surface-normal impact spray.
internal sealed class PipeOutlet:MonoBehaviour
{
    internal PipePiece Pipe;internal SyncFountain Fountain;internal int PortIndex;
    float Diameter=>Pipe?PipePrefabs.DiameterScale(Pipe.Kind):1;
    bool HasSource=>Pipe||Fountain;internal Mesh Mesh;
    internal bool HasImpact;internal Vector3 ImpactPoint,ImpactNormal;internal ParticleSystem Splash;
    Material material;Texture2D streaks;float nextTrace,nextFrame;
    readonly RaycastHit[] hits=new RaycastHit[32];readonly List<Vector3> points=new List<Vector3>();
    Vector3[] vertices,normals;Vector2[] uv;Color[] colors;int[] triangles;
    const int Rings=81,Sides=10;const float Step=.05f;float speed,flow;
    void Start(){
        Mesh=new Mesh{name="Arcane pipe water stream"};Mesh.MarkDynamic();gameObject.AddComponent<MeshFilter>().sharedMesh=Mesh;
        var source=PrefabManager.Instance.GetPrefab("vfx_watersplash_bathtub")?.GetComponentInChildren<ParticleSystem>(true);
        var sprayMaterial=source?source.GetComponent<ParticleSystemRenderer>().sharedMaterial:null;
        material=new Material(sprayMaterial?sprayMaterial:Plugin.Water);
        streaks=new Texture2D(64,128,TextureFormat.RGBA32,false){name="Arcane moving water streaks",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
        var pixels=new Color[64*128];
        for(int y=0;y<128;y++)for(int x=0;x<64;x++){
            float u=x/64f*Mathf.PI*2,v=y/128f*Mathf.PI*2;
            float glint=Mathf.Pow(.5f+.5f*Mathf.Sin(u*3+Mathf.Sin(v*2)*.7f),6)*(.5f+.5f*Mathf.Sin(v*5+u));
            pixels[y*64+x]=new Color(.65f+glint*.35f,.85f+glint*.15f,.95f+glint*.05f,.45f+glint*.45f);
        }
        streaks.SetPixels(pixels);streaks.Apply(false,true);material.mainTexture=streaks;
        if(material.HasProperty("_Color"))material.SetColor("_Color",Color.white);
        if(material.HasProperty("_TintColor"))material.SetColor("_TintColor",Color.white);
        var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        if(sprayMaterial){
            var go=new GameObject("pipe impact spray");go.transform.SetParent(transform,false);Splash=go.AddComponent<ParticleSystem>();Splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            float size=Diameter,flight=Mathf.Sqrt(size);
            var main=Splash.main;main.loop=true;main.playOnAwake=false;main.maxParticles=Mathf.RoundToInt(192*size*size);main.startLifetime=new ParticleSystem.MinMaxCurve(.45f*flight,.9f*flight);main.startSize=new ParticleSystem.MinMaxCurve(.12f*size,.28f*size);main.startSpeed=new ParticleSystem.MinMaxCurve(2*flight,4.5f*flight);main.gravityModifier=1;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startColor=new Color(.9f,.97f,1,.9f);
            var emission=Splash.emission;emission.rateOverTime=70;emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            var shape=Splash.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=.12f*size;shape.angle=65;
            var lifetime=Splash.colorOverLifetime;lifetime.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.08f),new GradientAlphaKey(0,1)});lifetime.color=gradient;
            var collision=Splash.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;collision.collidesWith=LayerMask.GetMask("terrain","piece","static_solid","Default");collision.bounce=.15f;collision.lifetimeLoss=.65f;
            var sr=go.GetComponent<ParticleSystemRenderer>();sr.sharedMaterial=sprayMaterial;sr.renderMode=ParticleSystemRenderMode.Billboard;sr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Trace();Animate();
    }
    void Update(){if(!HasSource)return;if(Time.time>=nextTrace){nextTrace=Time.time+.1f;Trace();}if(Time.time>=nextFrame){nextFrame=Time.time+1f/30;Animate();}}
    void OnEnable(){nextTrace=nextFrame=0;if(Mesh&&HasSource){Trace();Animate();}}
    void OnDisable(){points.Clear();HasImpact=false;if(Mesh)Mesh.Clear();if(Splash)Splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void Trace(){
        if(!Mesh||!HasSource)return;flow=Pipe?Pipe.Flow[PortIndex]:Fountain.VisualFlow;points.Clear();HasImpact=false;
        if(flow<=0){Mesh.Clear();if(Splash)Splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);return;}
        var origin=Pipe?Pipe.Port(PortIndex):Fountain.Mouth;var direction=Pipe?Pipe.Direction(PortIndex):Fountain.Direction;
        speed=PipeFlowModel.JetSpeed(flow);
        JetPath.Trace(origin,direction,flow,Pipe?Pipe.transform:Fountain.transform,points,out HasImpact,out ImpactNormal,out var receiver,out float travel);
        ImpactPoint=points[points.Count-1];
        if(Fountain){
            float age=(float)Fountain.Age;HasImpact=HasImpact&&age>=travel&&age<travel+SyncFountain.Duration;
            // Show only water emitted during the finite burst, travelling along the arc.
            int last=Mathf.Min(points.Count-1,Mathf.FloorToInt(age/Step));
            int first=Mathf.Max(0,Mathf.CeilToInt((age-SyncFountain.Duration)/Step));
            if(last<first||first>=points.Count){points.Clear();Mesh.Clear();}
            else{if(last+1<points.Count)points.RemoveRange(last+1,points.Count-last-1);if(first>0)points.RemoveRange(0,first);}
        }
        if(Splash){Splash.transform.position=ImpactPoint+ImpactNormal*.035f;Splash.transform.rotation=Quaternion.LookRotation(ImpactNormal);var emission=Splash.emission;float size=Diameter;emission.rateOverTime=Mathf.Clamp(45+flow*4,45,160)*size*size;
            if(HasImpact&&!Splash.isPlaying)Splash.Play();else if(!HasImpact)Splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    }
    void Animate(){
        if(!Mesh)return;if(points.Count<2){Mesh.Clear();return;}int count=points.Count*Sides;
        if(vertices==null||vertices.Length!=count){vertices=new Vector3[count];normals=new Vector3[count];uv=new Vector2[count];colors=new Color[count];triangles=new int[(points.Count-1)*Sides*6];}
        float distance=0;
        for(int i=0;i<points.Count;i++){
            if(i>0)distance+=Vector3.Distance(points[i-1],points[i]);
            var tangent=(i+1<points.Count?points[i+1]-points[i]:points[i]-points[i-1]).normalized;
            var right=Vector3.Cross(tangent,Mathf.Abs(tangent.y)<.95f?Vector3.up:Vector3.right).normalized;var up=Vector3.Cross(right,tangent);
            float radius=Mathf.Clamp(Mathf.Sqrt(flow*.0037854118f/(Mathf.PI*Mathf.Max(speed,speed+9.81f*i*Step))),.018f,.22f);
            radius*=Diameter*(Fountain||Pipe&&PipePrefabs.Dragon(Pipe.Kind)?2f:1f);
            float phase=distance*15-Time.time*18;radius*=1+.10f*Mathf.Sin(phase)+.045f*Mathf.Sin(phase*2.7f);
            float envelope=i==0||i==points.Count-1?0:Mathf.Min(distance,1)*radius*.16f;
            var centre=points[i]+right*(Mathf.Sin(phase*.63f)*envelope)+up*(Mathf.Cos(phase*.91f)*envelope);
            for(int side=0;side<Sides;side++){
                float angle=side*Mathf.PI*2/Sides;var normal=right*Mathf.Cos(angle)+up*Mathf.Sin(angle);int at=i*Sides+side;
                vertices[at]=transform.InverseTransformPoint(centre+normal*radius);normals[at]=transform.InverseTransformDirection(normal);uv[at]=new Vector2(side/(float)Sides,distance*.8f-Time.time*2.5f);colors[at]=new Color(.7f,.88f,1,.6f);
                if(i==0)continue;int offset=((i-1)*Sides+side)*6,a=(i-1)*Sides+side,b=(i-1)*Sides+(side+1)%Sides,c=i*Sides+side,d=i*Sides+(side+1)%Sides;
                triangles[offset]=a;triangles[offset+1]=c;triangles[offset+2]=b;triangles[offset+3]=b;triangles[offset+4]=c;triangles[offset+5]=d;
            }
        }
        Mesh.Clear();Mesh.vertices=vertices;Mesh.normals=normals;Mesh.uv=uv;Mesh.colors=colors;Mesh.triangles=triangles;Mesh.RecalculateBounds();
    }
    void OnDestroy(){if(Mesh)Destroy(Mesh);if(material)Destroy(material);if(streaks)Destroy(streaks);}
}
