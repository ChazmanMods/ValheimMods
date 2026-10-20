using System.IO;
using UnityEditor;
using UnityEngine;

public static class KuroganeMaskedHelmetBuilder
{
    private const string SourceRoot = "Assets/Imported/Valheim/GameElements/Items/armor/_res/FlametalArmor/model";
    private const string MaskedRoot = "Assets/Custom/SamuraiArmor/MaskedHelmet";
    private const string MaterialRoot = "Assets/Custom/SamuraiArmor/Materials";
    private const string PrefabRoot = "Assets/Custom/SamuraiArmor/Prefabs";
    private const string SourceModelPath = MaskedRoot + "/KuroganeKabutoHelmet.fbx";
    private const string OutputPrefabPath = PrefabRoot + "/KuroganeMaskedHelmetVisual.prefab";

    [MenuItem("Valheim/Kurogane Samurai Armor/Build Masked Helmet Visual")]
    public static void Build()
    {
        Directory.CreateDirectory(MaterialRoot);
        Directory.CreateDirectory(PrefabRoot);
        Directory.CreateDirectory(MaskedRoot);

        ConfigureTexture(MaskedRoot + "/Leather_Back_Samurai_Helmet.png", false);
        ConfigureTexture(MaskedRoot + "/oni_mask_design_06.png", false);
        AssetDatabase.Refresh();

        Material helmetMaterial = CreateMaterial(
            "KuroganeMaskedHelmet_Metal",
            SourceRoot + "/FlametalArmor_Mat.mat",
            MaskedRoot + "/Leather_Back_Samurai_Helmet.png",
            SourceRoot + "/FlametalArmor_n.png",
            new Color(0.42f, 0.47f, 0.50f, 1f));

        Material maskMaterial = CreateMaterial(
            "KuroganeMaskedHelmet_Mask",
            SourceRoot + "/FlametalArmor_Mat.mat",
            MaskedRoot + "/oni_mask_design_06.png",
            SourceRoot + "/FlametalArmor_n.png",
            new Color(0.75f, 0.33f, 0.28f, 1f));

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
        if (source == null)
        {
            Debug.LogError("Missing masked helmet FBX: " + SourceModelPath);
            return;
        }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (root == null)
        {
            root = Object.Instantiate(source);
        }

        try
        {
            root.name = "KuroganeMaskedHelmetVisual";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                    string rendererName = renderer.name.ToLowerInvariant();
                    materials[i] = materialName.Contains("oni") || materialName.Contains("mask") || rendererName.Contains("mask")
                        ? maskMaterial
                        : helmetMaterial;
                }

                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built masked Kurogane helmet visual prefab: " + OutputPrefabPath);
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

    private static Material CreateMaterial(string materialName, string templatePath, string albedoPath, string normalPath, Color tint)
    {
        Material template = AssetDatabase.LoadAssetAtPath<Material>(templatePath);
        Material material = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        material.name = materialName;

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        SetColor(material, "_Color", tint);
        SetTexture(material, "_MainTex", albedo);
        SetTexture(material, "_BumpMap", normal);
        SetFloat(material, "_Metallic", 0.7f);
        SetFloat(material, "_Glossiness", 0.58f);
        SetFloat(material, "_Smoothness", 0.58f);

        string path = MaterialRoot + "/" + materialName + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(material, existing);
            existing.name = materialName;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SetTexture(Material material, string property, Texture texture)
    {
        if (texture != null && material.HasProperty(property))
        {
            material.SetTexture(property, texture);
        }
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }
}
