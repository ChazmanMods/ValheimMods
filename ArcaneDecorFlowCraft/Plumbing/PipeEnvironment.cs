using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

internal static class PipeEnvironment
{
    // Sample the current terrain mesh, including changes made by the hoe or other mods.
    // Triangle interpolation also handles buried intakes without a physics ray from underground.
    internal static bool SampleGround(Vector3 p,out float height){
        height=0;var map=Heightmap.FindHeightmap(p);if(!map||map.IsDistantLod)return false;
        var local=p-map.transform.position;
        float gx=local.x/map.m_scale+map.m_width*.5f,gz=local.z/map.m_scale+map.m_width*.5f;
        int x=Mathf.Clamp(Mathf.FloorToInt(gx),0,map.m_width-1),z=Mathf.Clamp(Mathf.FloorToInt(gz),0,map.m_width-1);
        float u=Mathf.Clamp01(gx-x),v=Mathf.Clamp01(gz-z);
        float a=map.GetHeight(x,z),b=map.GetHeight(x+1,z),c=map.GetHeight(x,z+1),d=map.GetHeight(x+1,z+1);
        height=(u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v))+map.transform.position.y;
        return true;
    }
}
