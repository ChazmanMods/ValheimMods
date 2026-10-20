using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class BuildRiver {
    public static void Build() {
        PlayerSettings.colorSpace=ColorSpace.Linear;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RiverFlow.shader");
        if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("River shader failed to compile");
        string output=Path.GetFullPath("../Assets");Directory.CreateDirectory(output);
        var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="riverflow",assetNames=new[]{"Assets/RiverFlow.shader"}}},BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
        if(!manifest||ShaderUtil.ShaderHasError(shader))throw new Exception("River shader bundle failed");
        var verification=AssetBundle.LoadFromFile(Path.Combine(output,"riverflow"));
        if(!verification)throw new Exception("Editor cannot reload its own river bundle");
        if(!verification.LoadAsset<Shader>("Assets/RiverFlow.shader"))throw new Exception("Built river shader is missing");
        verification.Unload(true);
        Debug.Log("ARCANE RIVER SHADER BUILD PASS");
    }
}
