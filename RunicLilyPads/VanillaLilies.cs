using UnityEngine;
using UnityEngine.Rendering;

namespace RunicLilyPads
{
    internal static class VanillaLilies
    {
        internal static Mesh Mesh;
        internal static Material Material;

        internal static bool Load()
        {
            var prefab = Jotunn.Managers.PrefabManager.Instance.GetPrefab("instanced_waterlilies");
            if (!prefab) return false;
            var instance = prefab.GetComponent<InstanceRenderer>();
            if (!instance || !instance.m_mesh || !instance.m_material) return false;
            Mesh = instance.m_mesh;
            // Preserve vanilla textures and shader, but don't let decorative clutter shrink at distance.
            Material = new Material(instance.m_material) { name = "Runic vanilla waterlilies" };
            Material.DisableKeyword("_DISTANCESCALE_ON");
            if (Material.HasProperty("_DistanceScale")) Material.SetFloat("_DistanceScale", 0);
            return true;
        }

        internal static void Build(Transform parent, int variant)
        {
            if (variant == 0) Add(parent, Vector3.zero, .75f, 0);
            if (variant == 1) Add(parent, Vector3.zero, 1.3f, 35);
            if (variant == 2)
            {
                Add(parent, new Vector3(-.35f, 0, -.10f), .95f, 15);
                Add(parent, new Vector3(.38f, 0, .20f), .75f, 160);
            }
            if (variant == 3)
            {
                Add(parent, new Vector3(-.43f, 0, -.28f), 1.1f, 0);
                Add(parent, new Vector3(.46f, 0, -.08f), .90f, 125);
                Add(parent, new Vector3(-.02f, 0, .53f), .75f, 250);
            }
        }

        static void Add(Transform parent, Vector3 position, float width, float yaw)
        {
            var bounds = Mesh.bounds;
            float scale = width / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            var group = new GameObject("vanilla_lily_patch");
            group.transform.SetParent(parent, false);
            group.transform.localPosition = position;
            group.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            group.transform.localScale = Vector3.one * scale;
            var model = new GameObject("vanilla_waterlilies") { layer = LayerMask.NameToLayer("piece") };
            model.transform.SetParent(group.transform, false);
            // The mesh includes long downward stems. Center only X/Z; align its leaf
            // canopy to the waterline rather than lifting half the stems above water.
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.max.y, -bounds.center.z);
            model.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            // Alpha-cutout leaves use a thin selection box; mesh bounds include transparent pixels.
            var collider = model.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(bounds.center.x, bounds.max.y - .03f / scale, bounds.center.z);
            collider.size = new Vector3(bounds.size.x, .06f / scale, bounds.size.z);
        }
    }
}
