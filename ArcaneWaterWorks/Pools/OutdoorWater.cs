using UnityEngine;

namespace ArcaneWaterWorks.Pools
{
    internal static class OutdoorWater
    {
        // Borrow properties from live terrain water, but never modify its material.
        internal static Material Source(){
            foreach(var volume in WaterVolume.Instances)
                if(volume&&volume.m_heightmap&&!volume.m_heightmap.IsDistantLod&&volume.m_waterSurface&&volume.m_waterSurface.sharedMaterial)
                    return volume.m_waterSurface.sharedMaterial;
            return GardenPrefabs.Water;
        }
        internal static bool Apply(Material target){
            var source=Source();if(!source||!target)return false;
            target.shader=source.shader;target.CopyPropertiesFromMaterial(source);
            // Keep outdoor wind-driven ripples, but no ocean-sized displacement.
            // _depth controls wave amplitude, not the actual channel/pond depth.
            target.SetFloat("_UseGlobalWind",1);
            target.SetFloatArray("_depth",new[]{0f,0f,0f,0f});
            // Built pieces need fine moving detail even without ocean displacement.
            // Use the same ripple scale on every pond and river to avoid a frozen
            // looking sheet beside the adjoining animated surface.
            target.SetFloat("_NormalScale",1);
            target.SetFloat("_NormalPower",1);
            // Custom surfaces already sample their curves explicitly; additional
            // ocean tessellation adds GPU work without improving those curves.
            if(target.HasProperty("_Tess"))target.SetFloat("_Tess",1);
            return true;
        }
        internal static void Bind(MeshRenderer renderer){
            // Pieces can arrive before terrain water during scene streaming.
            if(Source()==GardenPrefabs.Water)renderer.gameObject.AddComponent<OutdoorWaterBinding>();
        }
    }
    internal sealed class OutdoorWaterBinding:MonoBehaviour
    {
        float next;
        void Update(){
            if(Time.time<next)return;next=Time.time+.5f;
            if(OutdoorWater.Source()==GardenPrefabs.Water)return;
            var renderer=GetComponent<MeshRenderer>();if(!renderer){Destroy(this);return;}
            var material=renderer.sharedMaterial;var depth=material.GetFloatArray("_depth");
            OutdoorWater.Apply(material);if(depth.Length>0)material.SetFloatArray("_depth",depth);
            Destroy(this);
        }
    }
}
