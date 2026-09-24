using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Jotunn.Managers;
namespace RunicLilyPads
{
    public sealed class NaturalSpillway:MonoBehaviour
    {
        internal const int Rows=80,Columns=25;
        static AssetBundle bundle;static Shader shader;static Texture2D noise;
        Mesh mesh;Material water;PondChannel channel;ParticleSystem spray;
        const int PathSamples=9;
        Vector3[] paths;bool[] visibleSpray;
        readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[72];
        internal static void Create(Transform parent,GardenPiece high,GardenPiece low){
            if(!PondChannel.TryCreate(PondNetwork.Footprint(high),PondNetwork.Footprint(low),out var channel))return;
            var go=new GameObject("natural_pond_spillway");go.transform.SetParent(parent,false);go.layer=parent.gameObject.layer;
            var flow=go.AddComponent<NaturalSpillway>();flow.channel=channel;
            try{flow.Build();if(parent.GetComponentInParent<GardenPiece>()==high){
                channel.Point(.5,0,out double x,out double z);var location=new Vector3((float)x,0,(float)z);location.y=PondNetwork.WaterLevel(location);
                if(!float.IsNegativeInfinity(location.y))WaterfallAudio.Create(go.transform,high,low,location);
            }}catch(Exception e){flow.enabled=false;Destroy(go);Plugin.Log.LogError("Cascade could not be created: "+e);}
        }
        static Shader WaterShader(){
            if(shader)return shader;
            using(var input=typeof(Plugin).Assembly.GetManifestResourceStream("WaterGardens.Cascade"))using(var data=new MemoryStream()){
                if(input==null)throw new InvalidDataException("Missing cascade shader resource");input.CopyTo(data);
                bundle=AssetBundle.LoadFromMemory(data.ToArray());if(!bundle)throw new InvalidDataException("Cascade bundle failed to load");
                shader=bundle.LoadAsset<Shader>("Assets/Waterfall/Cascade.shader");
                if(!shader||!shader.isSupported)throw new InvalidDataException("Cascade shader is unsupported");
            }
            noise=new Texture2D(128,128,TextureFormat.RGBA32,true){name="Cascade flow noise",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            float Tile(float x,float y,float sx,float sy){float a=Mathf.Lerp(Mathf.PerlinNoise(x*sx,y*sy),Mathf.PerlinNoise((x-128)*sx,y*sy),x/128);float b=Mathf.Lerp(Mathf.PerlinNoise(x*sx,(y-128)*sy),Mathf.PerlinNoise((x-128)*sx,(y-128)*sy),x/128);return Mathf.Lerp(a,b,y/128);}
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)noise.SetPixel(x,y,new Color(Tile(x,y,.065f,.045f),Tile(x,y,.14f,.11f),0,1));
            noise.Apply();return shader;
        }
        void Build(){
            water=new Material(WaterShader());water.mainTexture=noise;water.SetFloat("_Length",(float)channel.Length*.6f);
            water.renderQueue=Math.Max(3010,transform.parent.GetComponent<MeshRenderer>().sharedMaterial.renderQueue+10);
            // Copy the actual pond surface triangles. Foam cannot lift away from the
            // native water underneath, including along the lip and shoreline.
            mesh=Instantiate(transform.parent.GetComponent<MeshFilter>().sharedMesh);mesh.name="Continuous cascade foam";
            var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];
            for(int i=0;i<vertices.Length;i++){
                var world=transform.TransformPoint(vertices[i]);channel.Project(world.x,world.z,out double t,out double cross);
                uv[i]=new Vector2((float)(cross/(channel.Width*2)+.5),(float)t);
                float slope=(float)((channel.Height(t-.01)-channel.Height(t+.01))/(channel.Length*.02));
                float fade=Mathf.SmoothStep(0,1,(float)t/.2f)*(1-Mathf.SmoothStep(0,1,((float)t-.7f)/.3f));
                colors[i]=new Color(Mathf.Clamp01(slope),1,1,channel.Contains(world.x,world.z,out _)?fade:0);
            }
            // Discard fully transparent triangles instead of drawing whole ponds once per join.
            var sourceIndices=mesh.triangles;var indices=new List<int>();
            for(int i=0;i<sourceIndices.Length;i+=3)if(colors[sourceIndices[i]].a>0||colors[sourceIndices[i+1]].a>0||colors[sourceIndices[i+2]].a>0){indices.Add(sourceIndices[i]);indices.Add(sourceIndices[i+1]);indices.Add(sourceIndices[i+2]);}
            mesh.triangles=indices.ToArray();mesh.uv=uv;mesh.colors=colors;water.SetFloat("_Length",1);            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=water;
            // Pond shapes only change when this surface is rebuilt. Solve spray paths once,
            // not 24 bisections x 72 particles x every frame x every overlapping pair.
            paths=new Vector3[particles.Length*PathSamples];visibleSpray=new bool[particles.Length];
            var owner=GetComponentInParent<GardenPiece>();
            for(int i=0;i<particles.Length;i++)for(int sample=0;sample<PathSamples;sample++){
                float age=sample/(float)(PathSamples-1),u=Mathf.Sin(i*2.39996f)*.88f;
                double t=(i%3==0?.26:i%3==1?.62:.88)+age*.10;
                channel.Point(t,u,out double x,out double z);var point=new Vector3((float)x,0,(float)z);float level=PondNetwork.WaterLevel(point);
                bool covered=owner&&PondNetwork.Distance(owner,point)<=1;
                if(covered)foreach(var pond in GardenPiece.Ponds)if(pond&&pond.Live&&PondNetwork.Covers(pond,owner)&&PondNetwork.Distance(pond,point)<=1){covered=false;break;}
                if(sample==0)visibleSpray[i]=covered&&!float.IsNegativeInfinity(level);
                point.y=float.IsNegativeInfinity(level)?(float)channel.Height(t):level;
                paths[i*PathSamples+sample]=transform.InverseTransformPoint(point+Vector3.up*(.04f-age*.03f));
            }
            var go=new GameObject("cascade_spray");go.transform.SetParent(transform,false);spray=go.AddComponent<ParticleSystem>();spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=spray.main;main.loop=true;main.playOnAwake=false;main.maxParticles=particles.Length;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.simulationSpeed=0;
            var emission=spray.emission;emission.enabled=false;var shape=spray.shape;shape.enabled=false;
            spray.GetComponent<ParticleSystemRenderer>().sharedMaterial=PrefabManager.Instance.GetPrefab("vfx_watersplash_bathtub").GetComponentInChildren<ParticleSystemRenderer>(true).sharedMaterial;spray.Play();
        }
        void Update(){
            water.mainTextureOffset=new Vector2(0,-Time.time*.8f);
            var light=RenderSettings.ambientLight;if(EnvMan.instance&&EnvMan.instance.m_dirLight)light+=EnvMan.instance.m_dirLight.color*EnvMan.instance.m_dirLight.intensity*Mathf.Max(0,-EnvMan.instance.m_dirLight.transform.forward.y);
            var tint=new Color(.72f,.9f,.9f)*light;
            for(int i=0;i<particles.Length;i++){
                float age=Mathf.Repeat(Time.time*1.1f+i*.618034f,1),sample=age*(PathSamples-1);int index=Mathf.Min((int)sample,PathSamples-2);
                particles[i].position=Vector3.LerpUnclamped(paths[i*PathSamples+index],paths[i*PathSamples+index+1],sample-index);
                tint.a=visibleSpray[i]?(1-age)*.23f:0;particles[i].startColor=tint;particles[i].startSize=.035f+age*.035f;particles[i].startLifetime=2;particles[i].remainingLifetime=1;
            }
            spray.SetParticles(particles,particles.Length);
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(water)Destroy(water);}
    }
}



