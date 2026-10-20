using System.IO;
using UnityEditor;
using UnityEngine;

public static class KuroganeBundleBuilder
{
    private const string BundleName = "kuroganeassets";
    private const string Root = "Assets/KuroganeMaskedHelmet";
    private const string SourceRoot = @"E:\Valheim Mods\Blender Work";
    private const string OutputRoot = @"E:\Valheim Mods\KuroganeSamuraiArmor\package\plugins\KuroganeSamuraiArmor\assets";
    private const string VisualPrefabPath = Root + "/KuroganeMaskedHelmetVisual.prefab";

    public static void Build()
    {
        Directory.CreateDirectory(OutputRoot);
        CopySourceAsset("KuroganeKabutoHelmet.fbx");
        CopySourceAsset("Leather_Back_Samurai_Helmet.png");
        CopySourceAsset("oni_mask_design_06.png");
        CopySourceAsset("KuroganeHelmetIcon.png", "kurogane_menpo_kabuto_icon.png");
        ConfigureTexture(Root + "/Leather_Back_Samurai_Helmet.png", false);
        ConfigureTexture(Root + "/oni_mask_design_06.png", false);
        ConfigureTexture(Root + "/kurogane_menpo_kabuto_icon.png", false);
        AssetDatabase.Refresh();

        Material metal = CreateMaterial(
            "KuroganeMaskedHelmet_Metal",
            Root + "/Leather_Back_Samurai_Helmet.png",
            Color.white,
            0.08f,
            0.32f);

        Material mask = CreateMaterial(
            "KuroganeMaskedHelmet_Mask",
            Root + "/oni_mask_design_06.png",
            Color.white,
            0.05f,
            0.38f);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/KuroganeKabutoHelmet.fbx");
        if (model == null)
        {
            Debug.LogError("Missing Kurogane helmet FBX.");
            EditorApplication.Exit(1);
            return;
        }

        GameObject sourceRoot = Object.Instantiate(model);
        GameObject root = new GameObject("KuroganeMaskedHelmetVisual");
        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            LogHierarchy(sourceRoot.transform, string.Empty);

            foreach (MeshFilter sourceFilter in sourceRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
                Mesh sourceMesh = sourceFilter.sharedMesh;
                if (sourceRenderer == null || sourceMesh == null)
                {
                    continue;
                }

                GameObject child = new GameObject(sourceFilter.name);
                child.transform.SetParent(root.transform, false);

                Mesh bakedMesh = BakeMesh(sourceMesh, sourceFilter.transform.localToWorldMatrix, sourceFilter.name);
                string bakedPath = Root + "/" + sourceFilter.name + "_baked.asset";
                AssetDatabase.DeleteAsset(bakedPath);
                AssetDatabase.CreateAsset(bakedMesh, bakedPath);

                child.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = CreateMaterialSlots(
                    sourceRenderer.sharedMaterials.Length,
                    IsMask(sourceRenderer, sourceFilter) ? mask : metal);
                Debug.Log("Baked " + sourceFilter.name + " bounds center " + bakedMesh.bounds.center + " size " + bakedMesh.bounds.size);
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(sourceRoot);
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[]
            {
                VisualPrefabPath,
                Root + "/kurogane_menpo_kabuto_icon.png",
                Root + "/Leather_Back_Samurai_Helmet.png",
                Root + "/oni_mask_design_06.png"
            }
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            OutputRoot,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        if (manifest == null || !File.Exists(Path.Combine(OutputRoot, BundleName)))
        {
            Debug.LogError("Failed to build " + BundleName);
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("Built " + Path.Combine(OutputRoot, BundleName));
    }

    private static bool IsMask(Renderer renderer, Component component)
    {
        string rendererName = renderer.name.ToLowerInvariant();
        string componentName = component.name.ToLowerInvariant();
        foreach (Material material in renderer.sharedMaterials)
        {
            string materialName = material != null ? material.name.ToLowerInvariant() : string.Empty;
            if (materialName.Contains("oni") || materialName.Contains("mask"))
            {
                return true;
            }
        }

        return rendererName.Contains("mask") || componentName.Contains("mask");
    }

    private static Material[] CreateMaterialSlots(int count, Material material)
    {
        Material[] materials = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = material;
        }

        return materials;
    }

    private static void LogHierarchy(Transform transform, string indent)
    {
        Debug.Log(indent + transform.name + " localPosition=" + transform.localPosition + " localRotation=" + transform.localEulerAngles + " localScale=" + transform.localScale);
        foreach (Transform child in transform)
        {
            LogHierarchy(child, indent + "  ");
        }
    }

    private static Mesh BakeMesh(Mesh source, Matrix4x4 matrix, string meshName)
    {
        Mesh mesh = Object.Instantiate(source);
        mesh.name = meshName + "_baked";

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        }
        mesh.vertices = vertices;

        Matrix4x4 normalMatrix = matrix.inverse.transpose;
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        }
        mesh.normals = normals;

        Vector4[] tangents = mesh.tangents;
        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 tangent = normalMatrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
            tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
        }
        mesh.tangents = tangents;

        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateMaterial(string name, string texturePath, Color color, float metallic, float smoothness)
    {
        Material material = new Material(Shader.Find("Standard"))
        {
            name = name,
            color = color
        };

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null)
        {
            material.SetTexture("_MainTex", texture);
        }

        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_EmissionColor", Color.black);
        material.DisableKeyword("_EMISSION");

        string path = Root + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(material, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void CopySourceAsset(string sourceName, string destinationName = null)
    {
        string sourcePath = Path.Combine(SourceRoot, sourceName);
        if (!File.Exists(sourcePath))
        {
            Debug.LogWarning("Missing source asset: " + sourcePath);
            return;
        }

        string destinationPath = Path.Combine(Application.dataPath, "KuroganeMaskedHelmet", destinationName ?? sourceName);
        File.Copy(sourcePath, destinationPath, true);
    }

    private static void ConfigureTexture(string path, bool normalMap)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
        {
            return;
        }

        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normalMap;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }
}
