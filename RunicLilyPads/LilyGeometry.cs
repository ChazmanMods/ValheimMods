using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunicLilyPads
{
    internal static class LilyGeometry
    {
        static Material Material(Material template, Color tint, bool leaf)
        {
            var m = new Material(template) { name = "Lily " + (leaf ? "leaf" : "petal") };
            var tex = new Texture2D(64, 64) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 32, dz = (y - 31.5f) / 32;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float angle = Mathf.Atan2(dz, dx);
                float vein = leaf && Mathf.Abs(Mathf.Sin(angle * 7)) < 0.065f && r > 0.13f ? 0.18f : 0;
                float mottling = ((x * 17 + y * 31) % 13) / 160f;
                tex.SetPixel(x, y, new Color(0.74f + vein + mottling - r * 0.12f, 0.74f + vein + mottling - r * 0.12f, 0.74f + vein + mottling - r * 0.12f));
            }
            tex.Apply();
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", tint);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0);
            return m;
        }
        internal static void Build(Transform parent, Material template, int variant)
        {
            var green = Material(template, new Color(0.30f, 0.53f, 0.14f), true);
            var pale = Material(template, variant == 3 ? new Color(1f, .43f, .66f) : new Color(1f, .95f, .81f), false);
            var gold = Material(template, new Color(1f, .70f, .10f), false);
            if (variant == 3)
            {
                Pad(parent, green, new Vector3(-.34f, 0, -.15f), .57f, 15);
                Pad(parent, green, new Vector3(.39f, .012f, .16f), .46f, 155);
                Pad(parent, green, new Vector3(-.04f, .008f, .52f), .32f, 260);
                Flower(parent, pale, gold, new Vector3(-.29f, .045f, -.17f));
            }
            else
            {
                float radius = variant == 0 ? .38f : .65f;
                Pad(parent, green, Vector3.zero, radius, variant == 1 ? 35 : 0);
                if (variant == 2) Flower(parent, pale, gold, new Vector3(-.10f, .045f, .08f));
            }
        }
        static void Pad(Transform parent, Material material, Vector3 position, float radius, float yaw)
        {
            var vertices = new List<Vector3> { new Vector3(0, .014f, 0) };
            var uv = new List<Vector2> { new Vector2(.5f, .5f) };
            var triangles = new List<int>();
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float a = (18f + i * 324f / segments) * Mathf.Deg2Rad;
                float r = radius * (1 + .035f * Mathf.Sin(a * 5));
                vertices.Add(new Vector3(Mathf.Cos(a) * r, .012f * Mathf.Sin(a * 3), Mathf.Sin(a) * r * .9f));
                uv.Add(new Vector2(.5f + Mathf.Cos(a) * .49f, .5f + Mathf.Sin(a) * .49f));
                if (i > 0) { triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); }
            }
            // A thin underside closes the leaf while preserving its characteristic V-shaped notch.
            int top = vertices.Count;
            for (int i = 0; i < top; i++) { vertices.Add(vertices[i] - Vector3.up * .025f); uv.Add(uv[i]); }
            for (int i = 1; i <= segments; i++) { triangles.Add(top); triangles.Add(top + i); triangles.Add(top + i + 1); }
            for (int i = 0; i < top; i++)
            {
                int next = (i + 1) % top;
                triangles.Add(i); triangles.Add(i + top); triangles.Add(next);
                triangles.Add(next); triangles.Add(i + top); triangles.Add(next + top);
            }
            var go = MeshObject(parent, "notched_pad", vertices.ToArray(), uv.ToArray(), triangles.ToArray(), material);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            // Non-convex mesh matches the notch and is thin enough to remain decorative.
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        }
        static void Flower(Transform parent, Material petal, Material gold, Vector3 center)
        {
            for (int ring = 0; ring < 2; ring++) for (int i = 0; i < 9; i++)
            {
                float a = (i * 40 + ring * 20) * Mathf.Deg2Rad;
                Vector3 along = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 side = new Vector3(-along.z, 0, along.x);
                float length = ring == 0 ? .26f : .18f;
                var v = new[] { center, center + along * length * .45f + side * .065f + Vector3.up * .055f,
                    center + along * length + Vector3.up * (.09f + ring * .09f),
                    center + along * length * .45f - side * .065f + Vector3.up * .055f,
                    center + along * length * .5f + Vector3.up * (.035f + ring * .035f) };
                MeshObject(parent, "pointed_petal", v, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.5f,.5f) },
                    new[] {0,1,4,1,2,4,2,3,4,3,0,4,4,1,0,4,2,1,4,3,2,4,0,3}, petal);
            }
            var verts = new List<Vector3> { center + Vector3.up * .085f };
            var tris = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * .2f;
                verts.Add(center + new Vector3(Mathf.Cos(a) * .066f, .042f, Mathf.Sin(a) * .066f));
                tris.Add(0); tris.Add((i + 1) % 10 + 1); tris.Add(i + 1);
            }
            MeshObject(parent, "golden_center", verts.ToArray(), new Vector2[11], tris.ToArray(), gold);
        }
        static GameObject MeshObject(Transform parent, string name, Vector3[] vertices, Vector2[] uv, int[] triangles, Material material)
        {
            var go = new GameObject(name) { layer = LayerMask.NameToLayer("piece") };
            go.transform.SetParent(parent, false);
            // Separate face vertices so two-sided petals retain valid normals on both sides.
            var faceVertices = new Vector3[triangles.Length];
            var faceUv = new Vector2[triangles.Length];
            var faceIndices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                faceVertices[i] = vertices[triangles[i]];
                faceUv[i] = uv[triangles[i]];
                faceIndices[i] = i;
            }
            var mesh = new Mesh { name = "RunicLily_" + name, vertices = faceVertices, uv = faceUv, triangles = faceIndices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
        internal static Sprite Icon(int variant)
        {
            var tex = new Texture2D(64,64) { filterMode = FilterMode.Point };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float r = Mathf.Sqrt(dx*dx+dy*dy);
                bool notch = dx > 0 && Mathf.Abs(dy) < dx * .25f;
                Color c = r < (variant == 0 ? 20 : 28) && !notch ? new Color(.29f,.53f,.12f) : Color.clear;
                if (variant >= 2 && r < 13 + 3 * Mathf.Cos(Mathf.Atan2(dy,dx)*9)) c = variant == 2 ? new Color(1,.95f,.8f) : new Color(1,.43f,.66f);
                if (variant >= 2 && r < 5) c = new Color(1,.7f,.1f);
                tex.SetPixel(x,y,c);
            }
            tex.Apply();
            return Sprite.Create(tex,new Rect(0,0,64,64),new Vector2(.5f,.5f));
        }
    }
}

