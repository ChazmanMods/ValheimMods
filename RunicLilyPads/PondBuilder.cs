using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace RunicLilyPads
{
    internal static class PondBuilder
    {
        const int Segments=32;
        internal static void Build(GardenPiece owner,Transform parent,bool live)
        {
            var s=owner.Settings;var points=new Vector3[Segments+1];points[0]=Vector3.zero;
            for(int i=0;i<Segments;i++){
                double angle=i*Math.PI*2/Segments,r=PondMath.Radius(owner.Shape,angle)*s.Width*.5;
                points[i+1]=new Vector3((float)(Math.Cos(angle)*r),0,(float)(Math.Sin(angle)*r*PondMath.Aspect(owner.Shape)));
            }
            var triangles=new int[Segments*3];var uv=new Vector2[points.Length];
            for(int i=0;i<points.Length;i++)uv[i]=new Vector2(points[i].x,points[i].z)*.2f;
            for(int i=0;i<Segments;i++){triangles[i*3]=0;triangles[i*3+1]=(i+1)%Segments+1;triangles[i*3+2]=i+1;}
            var mesh=live?PondNetwork.Surface(owner):new Mesh{name="WaterGardens pond surface",vertices=points,triangles=triangles,uv=uv};mesh.RecalculateNormals();mesh.RecalculateBounds();owner.OwnSurface(mesh);
            var surface=new GameObject("pond_water");surface.transform.SetParent(parent,false);surface.transform.localPosition=Vector3.up*(live?-.15f:.04f);surface.layer=LayerMask.NameToLayer(live?"Water":"ghost");
            surface.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(GardenPrefabs.Water);owner.OwnSurface(material);var renderer=surface.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            ConfigureClearWater(material);
            material.SetFloat("_UseGlobalWind",0);material.SetFloatArray("_depth",new[]{s.Depth/10,s.Depth/10,s.Depth/10,s.Depth/10});
            if(!live)return;
            PondNetwork.Waterfalls(owner,surface.transform);
            for(int i=0;i<Segments;i++){
                Vector3 a=points[i+1],b=points[(i+1)%Segments+1];
                var go=new GameObject("pond_water_volume");go.SetActive(false);go.transform.SetParent(surface.transform,false);go.layer=LayerMask.NameToLayer("WaterVolume");
                var prism=Prism(a,b,s.Depth+.2f);owner.OwnSurface(prism);
                var col=go.AddComponent<MeshCollider>();col.sharedMesh=prism;col.convex=true;col.isTrigger=true;
                var water=go.AddComponent<WaterVolume>();water.m_waterSurface=renderer;water.m_forceDepth=s.Depth/10;water.m_useGlobalWind=false;water.m_surfaceOffset=0;
                go.SetActive(true);
                // Water-layer triggers let the hammer's normal water ray place lily pads here.
                var hit=new GameObject("water_placement_surface");hit.transform.SetParent(surface.transform,false);hit.layer=LayerMask.NameToLayer("Water");
                var thin=Prism(a,b,.03f);owner.OwnSurface(thin);var c=hit.AddComponent<MeshCollider>();c.sharedMesh=thin;c.convex=true;c.isTrigger=true;
            }
        }
        internal static void ConfigureClearWater(Material material)
        {
            // Cave water turns gray and opaque within one meter. Use the native shader's
            // outdoor depth range so the bed remains visible throughout our 0.4-3m basins.
            material.SetFloat("_DepthFade",20f);
            material.SetColor("_ColorTop",new Color(.315f,.524f,.361f,1));
            material.SetColor("_ColorBottom",new Color(.098f,.196f,.169f,1));
            material.SetColor("_ColorBottomShallow",new Color(.196f,.176f,.106f,1));
            material.SetColor("_SurfaceColor",new Color(.5f,.5f,.5f,1));
            material.SetFloat("_NormalPower",.35f);
            material.SetFloat("_NormalScale",.04f);
            material.SetFloat("_RefractionScale",.05f);
            material.SetFloat("_FoamDepth",.05f);
            material.SetColor("_FoamColor",new Color(.84f,.84f,.84f,.2f));
        }
        static Mesh Prism(Vector3 a,Vector3 b,float depth)
        {
            Vector3 down=Vector3.down*depth;
            var m=new Mesh{name="pond sector",vertices=new[]{Vector3.zero,b,a,down,b+down,a+down},triangles=new[]{0,1,2,3,5,4,0,3,4,0,4,1,1,4,5,1,5,2,2,5,3,2,3,0}};
            m.RecalculateNormals();m.RecalculateBounds();return m;
        }
    }
    [HarmonyPatch(typeof(Heightmap),"ApplyModifiers")]
    static class PondTerrain
    {
        internal static bool Sample(Vector3 point,out float height){
            var h=Heightmap.FindHeightmap(point);height=0;if(!h)return false;
            var local=point-h.transform.position;
            float gx=local.x/h.m_scale+h.m_width*.5f,gz=local.z/h.m_scale+h.m_width*.5f;
            int x=Mathf.Clamp(Mathf.FloorToInt(gx),0,h.m_width-1),z=Mathf.Clamp(Mathf.FloorToInt(gz),0,h.m_width-1);
            float u=Mathf.Clamp01(gx-x),v=Mathf.Clamp01(gz-z);
            float a=h.GetHeight(x,z),b=h.GetHeight(x+1,z),c=h.GetHeight(x,z+1),d=h.GetHeight(x+1,z+1);
            // Match Valheim's two terrain triangles, rather than its nearest-vertex
            // GetWorldHeight API, which otherwise turns a smooth stream into stairs.
            height=(u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v))+h.transform.position.y;
            return true;
        }
        static void Postfix(Heightmap __instance,List<float> ___m_heights,Texture2D ___m_paintMask)
        {
            if(__instance.IsDistantLod)return;
            int width=__instance.m_width;float spacing=__instance.m_scale,half=width*spacing*.5f;
            Vector3 origin=__instance.transform.position;
            var footprints=new List<PondFootprint>();
            foreach(var pond in GardenPiece.Ponds)if(pond&&pond.Live){
                float radius=pond.Settings.Width*.5f+3.5f;
                if(Mathf.Abs(origin.x-pond.transform.position.x)<=half+radius&&Mathf.Abs(origin.z-pond.transform.position.z)<=half+radius)footprints.Add(PondNetwork.Footprint(pond));
            }
            if(footprints.Count==0)return;
            var groups=PondMergeMath.Groups(footprints);
            for(int z=0;z<=width;z++)for(int x=0;x<=width;x++){
                int index=z*(width+1)+x;var world=origin+new Vector3(x*spacing-half,0,z*spacing-half);
                                double original=___m_heights[index]+origin.y,result=double.PositiveInfinity;
                foreach(var group in groups)if(PondMergeMath.Affects(group,world.x,world.z))result=Math.Min(result,PondMergeMath.Bed(group,world.x,world.z,original));
                if(!double.IsPositiveInfinity(result))___m_heights[index]=(float)result-origin.y;
            }
            for(int z=0;z<___m_paintMask.height;z++)for(int x=0;x<___m_paintMask.width;x++){
                var world=origin+new Vector3((x+.5f)*spacing-half,0,(z+.5f)*spacing-half);double distance=2;
                foreach(var p in footprints)distance=Math.Min(distance,p.Distance(world.x,world.z));
                if(distance<1.01){var color=___m_paintMask.GetPixel(x,z);float t=Mathf.Clamp01((1.01f-(float)distance)/.08f);___m_paintMask.SetPixel(x,z,Color.Lerp(color,new Color(1,0,0,0),t));}
            }
            if(GardenPiece.Ponds.Count>0)___m_paintMask.Apply();
        }
        internal static void Refresh(Vector3 position,float radius=10)
        {
            foreach(var h in Heightmap.GetAllHeightmaps()){
                float half=h.m_width*h.m_scale*.5f;
                if(Mathf.Abs(h.transform.position.x-position.x)<=half+radius&&Mathf.Abs(h.transform.position.z-position.z)<=half+radius)h.Poke(1);
            }
            if(ClutterSystem.instance)ClutterSystem.instance.ResetGrass(position,radius);
        }
    }
    [HarmonyPatch(typeof(WaterVolume),"GetWaterSurface")]
    static class PondSlopedLiquid
    {
        static void Postfix(WaterVolume __instance,Vector3 point,ref float __result){
            var owner=__instance.GetComponentInParent<GardenPiece>();
            if(!owner||!owner.Live||owner.Shape<0)return;
            float surface=PondNetwork.WaterLevel(point);
            if(!float.IsNegativeInfinity(surface))__result=surface;
        }
    }
}


