using System;
using System.IO;
using UnityEngine;
namespace ArcaneWaterWorks;
internal static class RiverSurface {
    static AssetBundle bundle;static Shader shader;static bool failed;
    internal static float Clock=>ZNet.instance?ZNet.instance.GetWrappedDayTimeSeconds():Time.time;
    internal static bool Apply(Material material){
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return false;
        if(!shader&&!failed)try{
            using(var stream=typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneWaterWorks.RiverFlow"))using(var bytes=new MemoryStream()){
                if(stream==null)throw new InvalidDataException("River flow shader resource missing");stream.CopyTo(bytes);
                bundle=AssetBundle.LoadFromMemory(bytes.ToArray());shader=bundle?bundle.LoadAsset<Shader>("Assets/RiverFlow.shader"):null;
                if(!shader||!shader.isSupported)throw new InvalidDataException("River flow shader unsupported");
            }
        }catch(Exception e){failed=true;Plugin.Log.LogError("River surface shader: "+e);}
        if(!shader)return false;
        material.shader=shader;material.renderQueue=3050;material.SetFloat("_NormalPower",.32f);Refresh(material);return true;
    }
    internal static void Refresh(Material material){
        var source=Pools.OutdoorWater.Source();if(!source)return;
        foreach(string key in new[]{"_Normal","_NormalFine","_FoamTex"})if(source.HasProperty(key))material.SetTexture(key,source.GetTexture(key));
        foreach(string key in new[]{"_ColorTop","_ColorBottom"})if(source.HasProperty(key))material.SetColor(key,source.GetColor(key));
        material.SetFloat("_RiverTime",Clock);material.SetFloat("_SeaLevel",OceanJoin.Sea);
    }
    internal static float Amplitude(float depth,float shore,float seaHeight,float pondEdge,Vector2 flow){
        float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01(shore/.5f));
        float wet=Mathf.SmoothStep(0,1,Mathf.Clamp01(depth/.65f));
        float ocean=Mathf.SmoothStep(0,1,Mathf.Clamp01(seaHeight/2));
        float pond=Mathf.SmoothStep(0,1,Mathf.Clamp01(pondEdge/4));
        return (.03f+.09f*Mathf.Clamp01((flow.magnitude-.7f)/1.8f))*edge*wet*ocean*pond;
    }
    internal static float Level(Spring spring,Vector3 world,float mean){
        var p=world-spring.transform.position;
        spring.Surface(p.x,p.z,out float distance,out _,out _);
        var members=spring.SurfaceMembers??new System.Collections.Generic.List<Spring>{spring};
        RiverAppearance.Sample(new Vector3(world.x,mean,world.z),members,out var flow);
        float depth=spring.Route.Depth;
        if(RiverTerrain.SamplePlanned(spring,world,out float ground))depth=mean-ground;
        float shore=Mathf.Min(spring.Route.Width*.5f+1.5f-distance,depth-.015f);
        float amplitude=Amplitude(depth,shore,mean-OceanJoin.Sea,Pools.RiverGardens.PondEdge(world),flow);
        return mean+(OceanJoin.Level(world,mean)-mean)*OceanJoin.BankWeight(shore,depth)+RiverWaveMath.Offset(world.x,world.z,flow.x,flow.y,amplitude,Clock);
    }
}
