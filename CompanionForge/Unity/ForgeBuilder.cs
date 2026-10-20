using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ForgeBuilder
{
    [Serializable] public class AnimationSpec { public string role, path, clip; public bool menu, loop, hold; }
    [Serializable] public class MaterialSpec { public string name, @base, normal, metallic, emission; }
    [Serializable] public class Spec
    {
        public string model, output, tint;
        public float scale = 1, yaw, offset_y;
        public float walk_clip_speed = 1.181818f, run_clip_speed = 3.214286f;
        public int texture_limit = 4096, triangle_budget = 80000;
        public AnimationSpec[] animations;
        public MaterialSpec[] materials;
    }
    [Serializable] public class ClipInfo { public string role, path; public string[] clips; }
    [Serializable] public class Report
    {
        public bool ok;
        public int triangles, bones, renderers;
        public float height;
        public string unity;
        public string[] materials, errors, notes;
        public ClipInfo[] animations;
    }

    public static void Build()
    {
        Spec spec = JsonUtility.FromJson<Spec>(File.ReadAllText("forge.json"));
        Directory.CreateDirectory(spec.output);
        Report report = new Report { unity = Application.unityVersion };
        var errors = new List<string>();
        var notes = new List<string>();
        try { BuildInternal(spec, report, errors, notes); }
        catch (Exception e) { errors.Add(e.Message); Debug.LogException(e); }
        report.ok = errors.Count == 0;
        report.errors = errors.ToArray();
        report.notes = notes.ToArray();
        File.WriteAllText(Path.Combine(spec.output, "report.json"), JsonUtility.ToJson(report, true));
        // Validation errors are reported as structured data, with clip choices even on a failed import.
        Debug.Log(report.ok ? "Companion Forge bundle created." : string.Join("\n", errors));
    }

    static ModelImporter ImportHuman(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new Exception("Cannot import model: " + path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.autoGenerateAvatarMappingIfUnspecified = true;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false; // Preserve authored extra-bone transforms for visual secondary motion.
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        return importer;
    }

    static void BuildInternal(Spec s, Report r, List<string> errors, List<string> notes)
    {
        AssetDatabase.Refresh();
        ImportHuman(s.model);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(s.model);
        var sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(s.model).OfType<Avatar>().FirstOrDefault();
        if (!sourceAvatar || !sourceAvatar.isValid || !sourceAvatar.isHuman)
            errors.Add("Model does not have a valid Humanoid avatar. Correct its bones in Blender and re-export, or map them in Unity Rig > Configure and keep the corrected .fbx.meta beside your source FBX before inspecting again.");
        if (source == null) throw new Exception("FBX did not import a character GameObject.");
        var skin = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skin.Length == 0 || !skin.Any(x => x.bones.Length > 0))
            errors.Add("Model needs a skinned mesh bound to a humanoid skeleton; a static mesh cannot animate as a companion.");
        r.bones = skin.SelectMany(x => x.bones).Where(x => x != null).Distinct().Count();
        r.renderers = source.GetComponentsInChildren<Renderer>(true).Length;
        r.triangles = skin.Where(x => x.sharedMesh).Sum(x => x.sharedMesh.triangles.Length / 3)
            + source.GetComponentsInChildren<MeshFilter>(true).Where(x => x.sharedMesh).Sum(x => x.sharedMesh.triangles.Length / 3);
        if (r.triangles > s.triangle_budget)
            errors.Add($"Model has {r.triangles:N0} triangles; the chosen budget is {s.triangle_budget:N0}. Reduce the mesh or deliberately raise the project budget.");
        if (r.triangles == 0) errors.Add("Model has no rendered triangles.");
        r.materials = source.GetComponentsInChildren<Renderer>(true).SelectMany(x => x.sharedMaterials)
            .Where(x => x).Select(x => x.name).Distinct().OrderBy(x => x).ToArray();
        var clips = new Dictionary<string, AnimationClip>();
        var clipReports = new List<ClipInfo>();
        foreach (var a in s.animations ?? Array.Empty<AnimationSpec>())
        {
            if (a.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) ImportHuman(a.path);
            var available = AssetDatabase.LoadAllAssetsAtPath(a.path).OfType<AnimationClip>()
                .Where(x => !x.name.StartsWith("__preview__")).ToArray();
            clipReports.Add(new ClipInfo { role = a.role, path = a.path, clips = available.Select(x => x.name).ToArray() });
            AnimationClip selected = string.IsNullOrWhiteSpace(a.clip)
                ? (available.Length == 1 ? available[0] : null)
                : available.FirstOrDefault(x => x.name == a.clip);
            if (selected == null)
            {
                errors.Add($"{a.role}: choose an exact clip name. Available: {string.Join(", ", available.Select(x => x.name))}");
                continue;
            }
            if (!selected.humanMotion)
                errors.Add($"{a.role}: clip '{selected.name}' is not Humanoid motion. Export humanoid animation with a compatible rig.");
            if (selected.length <= 0) errors.Add(a.role + ": animation has no duration.");
            clips[a.role] = selected;
        }
        r.animations = clipReports.ToArray();
        foreach (var role in new[] { "idle", "walk", "run" })
            if (!clips.ContainsKey(role)) errors.Add(role + ": animation is required.");
        foreach (var m in s.materials ?? Array.Empty<MaterialSpec>())
            if (!r.materials.Contains(m.name)) errors.Add("Material assignment does not exist in the imported model: " + m.name);
        if (errors.Count > 0) return;
        Directory.CreateDirectory("Assets/Generated");
        AssetDatabase.Refresh();
        var root = new GameObject("CompanionVisual");
        var model = UnityEngine.Object.Instantiate(source, root.transform);
        model.name = "Model";
        model.transform.localPosition = new Vector3(0, s.offset_y, 0);
        model.transform.localRotation = Quaternion.Euler(0, s.yaw, 0);
        model.transform.localScale = Vector3.one * s.scale;
        // Imported animation events execute arbitrary method names; native combat remains authoritative.
        foreach (var role in clips.Keys.ToArray())
        {
            var clip = UnityEngine.Object.Instantiate(clips[role]);
            clip.name = role;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var authored = s.animations.First(a => a.role == role);
            settings.loopTime = role == "idle" || role == "walk" || role == "run" || role == "sit" || role == "sleep";
            if (authored.menu) settings.loopTime = authored.loop || authored.hold;
            if ((role == "sleep" || role == "sit" || role == "pose")) { settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true; settings.loopBlendOrientation = true; }
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, "Assets/Generated/" + role + ".anim");
            clips[role] = clip;
        }
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Generated/Companion.controller");
        var layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;
        controller.AddParameter("CF_Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("CF_Attack", AnimatorControllerParameterType.Bool);
        controller.AddParameter("CF_Wave", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("CF_Sit", AnimatorControllerParameterType.Bool);
        controller.AddParameter("CF_Sleep", AnimatorControllerParameterType.Bool);
        controller.AddParameter("CF_Pose", AnimatorControllerParameterType.Bool);
        controller.AddParameter("CF_Menu", AnimatorControllerParameterType.Bool);
        controller.AddParameter(new AnimatorControllerParameter { name = "CF_Rate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.AddState("Locomotion");
        var blend = new BlendTree { name = "Movement", blendType = BlendTreeType.Simple1D,
            blendParameter = "CF_Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(blend, controller);
        blend.AddChild(clips["idle"], 0);
        blend.AddChild(clips["walk"], 1.4f);
        blend.AddChild(clips["run"], 3.8f);
        var children = blend.children;
        children[1].timeScale = 1.4f / Mathf.Max(0.1f, s.walk_clip_speed);
        children[2].timeScale = 3.8f / Mathf.Max(0.1f, s.run_clip_speed);
        blend.children = children;
        locomotion.speedParameter = "CF_Rate"; locomotion.speedParameterActive = true;
        locomotion.motion = blend;
        machine.defaultState = locomotion;
        foreach (var role in new[] { "attack", "wave", "sit", "sleep", "pose" })
        {
            if (!clips.ContainsKey(role)) continue;
            var state = machine.AddState(role);
            state.motion = clips[role];
            if (role == "sleep") state.speed = 0f;
            var entry = machine.AddAnyStateTransition(state);
            entry.hasExitTime = false; entry.duration = .12f; entry.canTransitionToSelf = false;
            if (role != "pose") entry.AddCondition(AnimatorConditionMode.IfNot, 0, "CF_Pose");
            else entry.AddCondition(AnimatorConditionMode.IfNot, 0, "CF_Menu");
            if (role != "sleep") entry.AddCondition(AnimatorConditionMode.IfNot, 0, "CF_Sleep");
            entry.AddCondition(role == "wave" ? AnimatorConditionMode.If : AnimatorConditionMode.If,
                0, role == "attack" ? "CF_Attack" : role == "sit" ? "CF_Sit" : role == "sleep" ? "CF_Sleep" : role == "pose" ? "CF_Pose" : "CF_Wave");
            var exit = state.AddTransition(locomotion);
            exit.duration = .12f;
            if (role == "wave") { exit.hasExitTime = true; exit.exitTime = .95f; }
            else { exit.hasExitTime = false; exit.AddCondition(AnimatorConditionMode.IfNot, 0, role == "attack" ? "CF_Attack" : role == "sleep" ? "CF_Sleep" : role == "pose" ? "CF_Pose" : "CF_Sit"); }
        }
        foreach (var item in s.animations.Where(a => a.menu))
        {
            var state = machine.AddState(item.role);
            state.motion = clips[item.role];
            if (item.hold) state.speed = 0f;
            // Enter explicitly on a selected command; stay until the pose driver releases it.
            var exit = state.AddTransition(locomotion);
            exit.hasExitTime = false; exit.duration = .15f;
            exit.AddCondition(AnimatorConditionMode.IfNot, 0, "CF_Pose");
        }
        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.avatar = sourceAvatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        Color tint;
        ColorUtility.TryParseHtmlString(s.tint, out tint);
        var materialCache = new Dictionary<Material, Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
            {
                if (!original) throw new Exception("A renderer has an empty material slot. Assign every material in your source model.");
                if (materialCache.TryGetValue(original, out var found)) return found;
                var material = new Material(Shader.Find("Standard")) { name = original.name };
                if (original.HasProperty("_MainTex")) material.mainTexture = original.mainTexture;
                if (original.HasProperty("_Color")) material.color = original.color * tint;
                else material.color = tint;
                if (original.HasProperty("_BumpMap") && original.GetTexture("_BumpMap"))
                { material.SetTexture("_BumpMap", original.GetTexture("_BumpMap")); material.EnableKeyword("_NORMALMAP"); }
                var assignment = (s.materials ?? Array.Empty<MaterialSpec>()).FirstOrDefault(x => x.name == original.name);
                if (assignment != null)
                {
                    SetTexture(material, "_MainTex", assignment.@base, false, s.texture_limit);
                    SetTexture(material, "_BumpMap", assignment.normal, true, s.texture_limit);
                    SetTexture(material, "_MetallicGlossMap", assignment.metallic, false, s.texture_limit);
                    SetTexture(material, "_EmissionMap", assignment.emission, false, s.texture_limit);
                    if (!string.IsNullOrEmpty(assignment.normal)) material.EnableKeyword("_NORMALMAP");
                    if (!string.IsNullOrEmpty(assignment.metallic)) material.EnableKeyword("_METALLICGLOSSMAP");
                    if (!string.IsNullOrEmpty(assignment.emission)) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.white); }
                }
                if (!material.mainTexture) errors.Add("Material '" + original.name + "' has no base-color texture. Assign its texture in the Materials step.");
                if (material.mainTexture)
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.mainTexture)) as TextureImporter;
                    if (importer != null)
                    {
                        importer.maxTextureSize = s.texture_limit; importer.mipmapEnabled = true; importer.SaveAndReimport();
                        if (importer.DoesSourceTextureHaveAlpha())
                        {
                            material.SetFloat("_Mode", 1); material.SetFloat("_Cutoff", .4f);
                            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                            material.SetInt("_ZWrite", 1); material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450;
                        }
                    }
                }
                AssetDatabase.CreateAsset(material, "Assets/Generated/material_" + materialCache.Count + ".mat");
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Glossiness", .12f);
                material.SetFloat("_GlossyReflections", 0f);
                material.SetFloat("_SpecularHighlights", 0f);
                material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                materialCache[original] = material;
                return material;
            }).ToArray();
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
        }
        var allRenderers = model.GetComponentsInChildren<Renderer>(true);
        var bounds = allRenderers[0].bounds;
        foreach (var renderer in allRenderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        r.height = bounds.size.y;
        if (r.height < .8f || r.height > 2.8f)
            errors.Add($"Character height is {r.height:F2} metres. Set model scale to bring it between 0.8 and 2.8 metres.");
        notes.Add($"Rendered height: {r.height:F2} metres. Root motion disabled; native Valheim AI drives movement.");
        notes.Add("Runtime uses a 1.8 m humanoid capsule and native weapon reach. Check feet placement and attack alignment in game.");
        if (!clips.ContainsKey("attack")) notes.Add("No attack animation supplied. Add one for visible combat actions.");
        if (errors.Count > 0) { UnityEngine.Object.DestroyImmediate(root); return; }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Generated/CompanionVisual.prefab");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var preview = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, r.height * .55f, -r.height * 1.8f);
        camera.transform.LookAt(new Vector3(0, r.height * .5f, 0));
        camera.backgroundColor = new Color(.13f, .15f, .18f); camera.clearFlags = CameraClearFlags.SolidColor;
        var light = new GameObject("KeyLight").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(35, -30, 0);
        RenderSettings.ambientLight = new Color(.5f, .5f, .5f);
        EditorSceneManager.SaveScene(scene, "Assets/Generated/Preview.unity");
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        var builds = new[] { new AssetBundleBuild { assetBundleName = "companionassets",
            assetNames = new[] { "Assets/Generated/CompanionVisual.prefab" } } };
        var manifest = BuildPipeline.BuildAssetBundles(s.output, builds,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        if (!manifest) throw new Exception("Unity asset bundle build failed. See the Unity log.");
        // A successful build alone does not prove a usable bundle: disabled engine
        // modules can produce an archive that the matching runtime rejects.
        var checkedBundle = AssetBundle.LoadFromFile(Path.Combine(s.output, "companionassets"));
        if (!checkedBundle) throw new Exception("Built bundle cannot load. Ensure com.unity.modules.assetbundle is enabled in Packages/manifest.json.");
        try
        {
            var checkedPrefab = checkedBundle.LoadAsset<GameObject>("assets/generated/companionvisual.prefab");
            if (!checkedPrefab) throw new Exception("Built bundle is missing its companion prefab.");
        }
        finally { checkedBundle.Unload(true); }

    }

    static void SetTexture(Material material, string property, string path, bool normal, int limit)
    {
        if (string.IsNullOrEmpty(path)) return;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new Exception("Cannot import texture: " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = property == "_MainTex" || property == "_EmissionMap";
        importer.maxTextureSize = limit;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
    }
}
