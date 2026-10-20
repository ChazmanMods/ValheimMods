using System.Collections.Generic;
using UnityEngine;
namespace RunicLilyPads
{
    // Native vegetation shaders consume EnvMan's global wind on the GPU.
    // Never copy/deform/upload every vertex for every placed plant each frame.
    public sealed class GardenWind:MonoBehaviour
    {
        public float Strength=.035f;
        public bool TransformSway;
        static readonly Dictionary<Material,Material> Materials=new Dictionary<Material,Material>();
        internal static Material NativeTemplate(){
            var prefab=Jotunn.Managers.PrefabManager.Instance.GetPrefab("YggaShoot1");
            foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))foreach(var material in renderer.sharedMaterials)
                if(material&&material.shader.name=="Custom/Vegetation")return material;
            throw new System.InvalidOperationException("Native vegetation wind material is unavailable");
        }
        internal static void ConfigureImported(Material material){
            material.shader=NativeTemplate().shader;
            material.shaderKeywords=System.Array.Empty<string>();
            material.SetFloat("_Height",1);
            material.SetFloat("_UV2Height",0);
            material.SetFloat("_SwaySpeed",60);
            material.SetFloat("_SwayDistance",.4f);
            material.SetFloat("_RippleSpeed",100);
            material.SetFloat("_RippleDistance",.04f);
            material.SetFloat("_RippleDeadzoneMin",0);
            material.SetFloat("_RippleDeadzoneMax",1);
            material.SetFloat("_PushDistance",0);
            material.SetFloat("_Cull",0);
            material.SetFloat("_Cutoff",.1f);
            material.SetFloat("_CamCull",0);
            material.SetFloat("_MossAlpha",0);
            material.SetFloat("_BumpScale",0);
            material.SetFloat("_SphereNormals",0);
            material.SetFloat("_Glossiness",.12f);
            material.enableInstancing=true;
        }
        void Start(){
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){enabled=false;return;}
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true)){
                var sources=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<sources.Length;i++){
                    var source=sources[i];if(!source)continue;
                    // Imported models are already configured once in the asset cache.
                    if(source.shader.name=="Custom/Vegetation"&&source.HasProperty("_SwayDistance")&&source.GetFloat("_SwayDistance")>0)continue;
                    if(!Materials.TryGetValue(source,out var material)){
                        material=new Material(source){name=source.name+" garden foliage",enableInstancing=true};
                        if(!material.HasProperty("_SwayDistance"))ConfigureImported(material);
                        else if(material.GetFloat("_SwayDistance")<=0)material.SetFloat("_SwayDistance",.4f);
                        material.DisableKeyword("_DISTANCESCALE_ON");
                        if(material.HasProperty("_DistanceScale"))material.SetFloat("_DistanceScale",0);
                        if(material.HasProperty("_CamCull"))material.SetFloat("_CamCull",0);
                        Materials[source]=material;
                    }
                    sources[i]=material;changed=true;
                }
                if(changed)renderer.sharedMaterials=sources;
            }
            enabled=false;
        }
    }
}
