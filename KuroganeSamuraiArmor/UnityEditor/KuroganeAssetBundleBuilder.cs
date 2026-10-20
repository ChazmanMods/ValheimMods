using System.IO;
using UnityEditor;
using UnityEngine;

public static class KuroganeAssetBundleBuilder
{
    private const string BundleName = "kuroganeassets";
    private const string OutputRoot = @"E:\Valheim Mods\KuroganeSamuraiArmor\UnityAssetBundles";

    [MenuItem("Valheim/Kurogane Samurai Armor/Build AssetBundle")]
    public static void BuildAssetBundle()
    {
        Directory.CreateDirectory(OutputRoot);

        string[] assetNames =
        {
            "Assets/Custom/SamuraiArmor/Prefabs/KuroganeMaskedHelmetVisual.prefab",
            "Assets/Custom/SamuraiArmor/Materials/KuroganeMaskedHelmet_Metal.mat",
            "Assets/Custom/SamuraiArmor/Materials/KuroganeMaskedHelmet_Mask.mat",
            "Assets/Custom/SamuraiArmor/MaskedHelmet/KuroganeKabutoHelmet.fbx",
            "Assets/Custom/SamuraiArmor/MaskedHelmet/Leather_Back_Samurai_Helmet.png",
            "Assets/Custom/SamuraiArmor/MaskedHelmet/oni_mask_design_06.png",
            "Assets/Custom/SamuraiArmor/Icons/kurogane_menpo_kabuto_icon.png",
            "Assets/Custom/SamuraiArmor/kurogane_armor_spec.json"
        };

        Debug.Log($"Building Kurogane AssetBundle '{BundleName}' with {assetNames.Length} explicit assets.");

        BuildPipeline.BuildAssetBundles(
            OutputRoot,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = assetNames
                }
            },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        AssetDatabase.RemoveUnusedAssetBundleNames();
        Debug.Log($"Built Kurogane AssetBundle '{BundleName}' in {OutputRoot}");
    }

    private static void ClearBundleLabels()
    {
        foreach (string bundleName in AssetDatabase.GetAllAssetBundleNames())
        {
            foreach (string assetPath in AssetDatabase.GetAssetPathsFromAssetBundle(bundleName))
            {
                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer != null)
                {
                    importer.assetBundleName = null;
                }
            }
        }

        AssetDatabase.RemoveUnusedAssetBundleNames();
    }

    private static void MarkBundle(string folder)
    {
        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
        {
            MarkAsset(AssetDatabase.GUIDToAssetPath(guid));
        }
    }

    private static void MarkAsset(string assetPath)
    {
        AssetImporter importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null)
        {
            return;
        }

        importer.assetBundleName = BundleName;
    }
}
