using UnityEngine;
using Jotunn.Managers;
namespace RunicLilyPads
{
    // Continuous textured sheets provide the stream; fine droplets/foam break up its edges.
    public sealed class WaterFlow:MonoBehaviour
    {
        Mesh mesh;Material material;Texture2D texture;ParticleSystem spray;ParticleSystem.Particle[] particles=new ParticleSystem.Particle[32];
        Vector3 left,right,end;bool fall;float phase;
        internal static WaterFlow Create(Transform parent,Vector3 left,Vector3 right,Vector3 end,bool waterfall){
            var go=new GameObject(waterfall?"pond_waterfall":"fountain_water_stream");go.transform.SetParent(parent,false);
            var flow=go.AddComponent<WaterFlow>();flow.left=left;flow.right=right;flow.end=end;flow.fall=waterfall;flow.Build();return flow;
        }
        void Build(){
            var source=PrefabManager.Instance.GetPrefab("vfx_watersplash_bathtub").GetComponentInChildren<ParticleSystemRenderer>(true);
            material=new Material(source.sharedMaterial);texture=new Texture2D(64,128,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            for(int y=0;y<128;y++)for(int x=0;x<64;x++){
                float edge=Mathf.Sin((x+.5f)/64*Mathf.PI),noise=Mathf.PerlinNoise(x*.23f,y*.08f);
                float streak=.45f+.55f*Mathf.Pow(Mathf.Sin(x*.61f+noise*1.8f),2);
                texture.SetPixel(x,y,new Color(.55f+noise*.35f,.75f+noise*.2f,.82f+noise*.18f,edge*streak*(.3f+noise*.5f)));
            }
            texture.Apply();material.mainTexture=texture;material.SetColor("_Color",Color.white);
            if(material.HasProperty("_CamFadeDistance"))material.SetVector("_CamFadeDistance",new Vector4(0,150,25,0));
            if(material.HasProperty("_CameraFadingEnabled"))material.SetFloat("_CameraFadingEnabled",0);
            const int rows=32;int columns=fall?2:9;var v=new Vector3[(rows+1)*columns];var uv=new Vector2[v.Length];var colors=new Color[v.Length];var t=new int[rows*(columns-1)*12];
            var mid=(left+right)*.5f;var half=(right-left)*.5f;
            for(int i=0;i<=rows;i++){
                float q=i/(float)rows;var center=Vector3.Lerp(mid,end,q);center.y=Mathf.Lerp(mid.y,end.y,q*q);
                for(int j=0;j<columns;j++){
                    float u=j/(float)(columns-1);int index=i*columns+j;
                    var width=fall?half*(j==0?-1:1):new Vector3(Mathf.Cos(u*Mathf.PI*2),0,Mathf.Sin(u*Mathf.PI*2))*half.magnitude*(.8f+.2f*q);
                    v[index]=center+width;uv[index]=new Vector2(u,q*2);colors[index]=new Color(1,1,1,.95f);
                    if(i==rows||j==columns-1)continue;int a=index,b=a+1,c=a+columns,d=c+1,k=(i*(columns-1)+j)*12;
                    int[] faces={a,c,b,b,c,d,b,c,a,d,c,b};for(int n=0;n<12;n++)t[k+n]=faces[n];
                }
            }
            mesh=new Mesh{name="Flowing water sheet",vertices=v,uv=uv,colors=colors,triangles=t};mesh.RecalculateNormals();mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            var drops=new GameObject("splash_foam");drops.transform.SetParent(transform,false);spray=drops.AddComponent<ParticleSystem>();spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=spray.main;main.loop=true;main.playOnAwake=false;main.maxParticles=particles.Length;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.simulationSpeed=0;
            var emission=spray.emission;emission.enabled=false;var shape=spray.shape;shape.enabled=false;
            spray.GetComponent<ParticleSystemRenderer>().sharedMaterial=source.sharedMaterial;spray.Play();phase=left.x*2+left.z;
        }
        float terrainCheck;
        void Update(){
            if(fall&&Time.time>=terrainCheck){
                terrainCheck=Time.time+.5f;var v=mesh.vertices;float previous=(left.y+right.y)*.5f;
                for(int i=0;i<v.Length;i+=2){var center=(v[i]+v[i+1])*.5f;var world=transform.TransformPoint(center);
                    if(Heightmap.GetHeight(world,out var ground)){float y=Mathf.Clamp(ground-transform.position.y+.045f,end.y,previous);v[i].y=v[i+1].y=y;previous=y;}
                }
                mesh.vertices=v;mesh.RecalculateBounds();mesh.RecalculateNormals();
            }
            material.mainTextureOffset=new Vector2(0,-Time.time*1.3f+phase);
            for(int i=0;i<particles.Length;i++){
                float age=Mathf.Repeat(Time.time*1.9f+i/(float)particles.Length,1),angle=i*2.39996f;
                float spread=(fall?.16f:.1f)*age;
                particles[i].position=end+new Vector3(Mathf.Cos(angle)*spread,Mathf.Sin(age*Mathf.PI)*.13f,Mathf.Sin(angle)*spread);
                particles[i].startColor=new Color(.85f,.96f,1,(1-age)*.5f);particles[i].startSize=.025f+age*.04f;particles[i].startLifetime=2;particles[i].remainingLifetime=1;
            }
            spray.SetParticles(particles,particles.Length);
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);if(texture)Destroy(texture);}
    }
}
