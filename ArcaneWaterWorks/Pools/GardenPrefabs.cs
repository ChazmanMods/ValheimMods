using System;
using System.Linq;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object=UnityEngine.Object;
namespace ArcaneWaterWorks.Pools
{
    internal static class GardenPrefabs
    {
        internal static Sprite Icon;
        internal static GameObject Fireflies;
        internal static Material Water;
        internal static GameObject Rock;
        internal static GameObject Root(string id,string name,string description,int stone=2)
        {
            var root=PrefabManager.Instance.CreateClonedPrefab(id,"wood_floor");
            for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach(var c in root.GetComponents<Collider>())Object.DestroyImmediate(c);
            var visual=new GameObject("garden_visual");visual.transform.SetParent(root.transform,false);
            var p=root.GetComponent<Piece>();
            // The clone's Floor/Building tags bypass its custom Water Garden category.
            // All garden pieces (including lilies and legacy aliases) start without native usage tags.
            p.m_usage = (Piece.UsageTagFlags)0;
            p.m_groundPiece=false;p.m_groundOnly=true;p.m_waterPiece=false;p.m_noInWater=false;p.m_clipEverything=true;p.m_noClipping=false;p.m_icon=null;
            var wear=root.GetComponent<WearNTear>();wear.m_supports=false;wear.m_health=100;wear.m_burnable=false;
            GardenLifecycle.Configure(root,visual);
            return root;
        }
        internal static void Register(GameObject root,string name,string description,int stone=2,bool legacy=false)
        {
            if(legacy){var piece=root.GetComponent<Piece>();piece.m_name=name;piece.m_description=description;piece.m_enabled=false;
                piece.m_resources=new[]{new Piece.Requirement{m_resItem=PrefabManager.Instance.GetPrefab("Stone").GetComponent<ItemDrop>(),m_amount=stone,m_recover=true}};
                PrefabManager.Instance.AddPrefab(new CustomPrefab(root,false));return;}
            var c=new PieceConfig{Enabled=true,Name=name,Description=description,PieceTable="Hammer",Category="Arcane Natural Waters"};c.AddRequirement("Stone",stone);
            if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,c)))throw new Exception("Registration rejected: "+root.name);
            Plugin.Log.LogInfo("Registered "+root.name);
        }
        internal static GameObject AddVisual(GameObject prefab,Transform parent,float width,bool solid,string branch=null,float height=0,bool tree=false)
        {
            if(!prefab)throw new Exception("Missing visual asset");
            var group=new GameObject("native_visual");group.transform.SetParent(parent,false);
            Transform source=branch!=null?prefab.transform.Find(branch):prefab.transform;
            if(!source)throw new Exception("Missing branch "+branch);
            var lodRenderers=new HashSet<Renderer>();var bestRenderers=new HashSet<Renderer>();
            foreach(var lod in source.GetComponentsInChildren<LODGroup>(true)){
                var levels=lod.GetLODs();for(int i=0;i<levels.Length;i++)foreach(var r in levels[i].renderers)if(r){lodRenderers.Add(r);if(i==0)bestRenderers.Add(r);}
            }
            var instance=source.GetComponent<InstanceRenderer>();
            if(instance)AddMesh(group.transform,instance.m_mesh, new[]{instance.m_material},Vector3.zero,Quaternion.identity,instance.m_scale);
            else foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true)) {
                var renderer=filter.GetComponent<MeshRenderer>();if(!filter.sharedMesh||!renderer)continue;
                // Use highest-detail meshes; decorative copies have no growth, loot or AI scripts.
                if(lodRenderers.Contains(renderer)&&!bestRenderers.Contains(renderer))continue;
                if(!lodRenderers.Contains(renderer)&&filter.name.IndexOf("lod",StringComparison.OrdinalIgnoreCase)>=0&&!filter.name.Equals("Lod0",StringComparison.OrdinalIgnoreCase))continue;
                AddMesh(group.transform,filter.sharedMesh,renderer.sharedMaterials,source.InverseTransformPoint(filter.transform.position),Quaternion.Inverse(source.rotation)*filter.transform.rotation,Divide(filter.transform.lossyScale,source.lossyScale));
            }
            var renderers=group.GetComponentsInChildren<MeshRenderer>();if(renderers.Length==0)throw new Exception("No usable meshes in "+prefab.name);
            Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            var center=group.transform.InverseTransformPoint(b.center);var size=b.size;
            float factor=tree?height/Mathf.Max(.01f,size.y):width/Mathf.Max(.01f,Mathf.Max(size.x,size.z));
            if(tree)foreach(var original in source.GetComponentsInChildren<Collider>(true)){
                if(original.isTrigger||!original.enabled)continue;
                var go=new GameObject("native_trunk_collider"){layer=LayerMask.NameToLayer("piece")};go.transform.SetParent(group.transform,false);
                go.transform.localPosition=source.InverseTransformPoint(original.transform.position);go.transform.localRotation=Quaternion.Inverse(source.rotation)*original.transform.rotation;go.transform.localScale=Divide(original.transform.lossyScale,source.lossyScale);
                if(original is CapsuleCollider capsule){var c=go.AddComponent<CapsuleCollider>();c.center=capsule.center;c.radius=capsule.radius;c.height=capsule.height;c.direction=capsule.direction;}
                else if(original is BoxCollider boxSource){var c=go.AddComponent<BoxCollider>();c.center=boxSource.center;c.size=boxSource.size;}
                else if(original is SphereCollider sphere){var c=go.AddComponent<SphereCollider>();c.center=sphere.center;c.radius=sphere.radius;}
                else if(original is MeshCollider mesh){var c=go.AddComponent<MeshCollider>();c.sharedMesh=mesh.sharedMesh;c.convex=mesh.convex;}
            }
            if(!tree)foreach(Transform child in group.transform)child.localPosition-=new Vector3(center.x,center.y-size.y*.5f,center.z);
            group.transform.localScale=tree?Vector3.one*factor:new Vector3(factor,height>0?height/Mathf.Max(.01f,size.y):factor,factor);
            group.layer=LayerMask.NameToLayer("piece");
            // Trees use native trunk colliders for interaction and removal. A canopy-sized
            // trigger also participates in placement penetration checks, blocking nearby builds.
            if(!tree){
                var box=group.AddComponent<BoxCollider>();box.center=new Vector3(0,size.y*.5f,0);box.size=new Vector3(size.x,Mathf.Max(.06f,size.y),size.z);
                // Trigger foliage stays targetable by the hammer without blocking movement.
                box.isTrigger=!solid;
            }
            return group;
        }
        static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
        static void AddMesh(Transform parent,Mesh mesh,Material[] materials,Vector3 p,Quaternion q,Vector3 scale)
        {
            var go=new GameObject("native_mesh");go.layer=LayerMask.NameToLayer("piece");go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=q;go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;
        }
    }
}

