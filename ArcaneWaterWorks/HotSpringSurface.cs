using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;

// Reuse the river's opaque-depth intersection and safe refraction, with a calm,
// undisplaced surface. This component owns no saved state or liquid/terrain logic.
internal sealed class HotSpringSurface:MonoBehaviour
{
    Material material;
    internal static void Attach(GardenPiece pond,MeshRenderer renderer){
        if(!RiverSurface.Apply(renderer.sharedMaterial))return;
        var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
        int count=mesh.vertexCount;
        var flow=new Vector2[count];var shore=new Vector2[count];
        var contact=new Vector2[count];var appearance=new Color[count];
        var vertices=mesh.vertices;
        for(int i=0;i<count;i++){
            var world=renderer.transform.TransformPoint(vertices[i]);
            float distance=GardenIntegration.Distance(pond,world);
            // The hidden skirt fades only at its outer edge; actual rock/bank
            // boundaries are clipped per pixel using opaque scene depth.
            float edge=(1-distance)*pond.Settings.Width*.5f+(pond.Live?1.5f:0);
            flow[i]=new Vector2(.12f,.07f);shore[i]=new Vector2(edge,0);
            contact[i]=new Vector2(pond.Settings.Depth,4);
            appearance[i]=new Color(0,0,0,1); // no crest foam or vertex waves
        }
        mesh.uv2=flow;mesh.uv3=shore;mesh.uv4=contact;mesh.colors=appearance;
        var binding=renderer.gameObject.AddComponent<HotSpringSurface>();binding.material=renderer.sharedMaterial;
        binding.Refresh();
    }
    void Refresh(){
        if(!material)return;
        RiverSurface.Refresh(material);
        // A pool is its own water body, including at/below sea level. River mouth
        // crossfading must not make a hot spring disappear.
        material.SetFloat("_SeaLevel",-10000);
        material.SetFloat("_NormalPower",.22f);
        if(GameCamera.instance)GameCamera.instance.GetComponent<Camera>().depthTextureMode|=DepthTextureMode.Depth;
    }
    void LateUpdate()=>Refresh();
}
