using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

[assembly: AssemblyCompany("Chazman")]
[assembly: AssemblyProduct("Kurogane Samurai Armor")]

namespace KuroganeSamuraiArmor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class KuroganeSamuraiArmorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.chazman.valheim.kuroganesamuraiarmor";
        public const string PluginName = "Kurogane Samurai Armor";
        public const string PluginVersion = "0.1.35";

        private const string SetName = "SetKuroganeSamuraiArmor";
        private const string BundleFileName = "kuroganeassets";
        private const string MaskedHelmetVisualAssetName = "KuroganeMaskedHelmetVisual";
        private const string MaskStyleTextureFileName = "kurogane_mask_styles.png";
        private const int MaskStyleCount = 9;
        private const string MenpoVariantZdoKey = "ChazmanKuroganeMenpoVariant";
        private static readonly int MenpoPrefabHash = "HelmetKuroganeMenpoKabuto".GetStableHashCode();
        private static readonly Vector3 MaskedHelmetBaseOffset = Vector3.zero;
        private static KuroganeSamuraiArmorPlugin _instance;
        private static string _assetDir;
        private static bool _registered;
        private static bool _recipesRegistered;
        private static readonly MethodInfo PlayerUpdateKnownRecipesListMethod = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");
        private static readonly MethodInfo ObjectDBUpdateRegistersMethod = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static readonly FieldInfo HumanoidHelmetItemField = AccessTools.Field(typeof(Humanoid), "m_helmetItem");
        private static readonly FieldInfo VisEquipmentHelmetInstanceField = AccessTools.Field(typeof(VisEquipment), "m_helmetItemInstance");
        private static readonly FieldInfo VisEquipmentCurrentHelmetHashField = AccessTools.Field(typeof(VisEquipment), "m_currentHelmetItemHash");
        private static readonly FieldInfo VisEquipmentNViewField = AccessTools.Field(typeof(VisEquipment), "m_nview");
        private static AssetBundle _assetBundle;
        private static readonly ConditionalWeakTable<VisEquipment, HelmetVariantState> HelmetVariantStates =
            new ConditionalWeakTable<VisEquipment, HelmetVariantState>();
        private Harmony _harmony;
        private static ConfigEntry<float> _maskedHelmetOffsetX;
        private static ConfigEntry<float> _maskedHelmetOffsetY;
        private static ConfigEntry<float> _maskedHelmetOffsetZ;
        private static ConfigEntry<float> _maskedHelmetRotationX;
        private static ConfigEntry<float> _maskedHelmetRotationY;
        private static ConfigEntry<float> _maskedHelmetRotationZ;
        private static ConfigEntry<float> _maskedHelmetScale;

        private static readonly string[] KuroganeItemPrefabNames =
        {
            "HelmetKuroganeKabuto",
            "HelmetKuroganeMenpoKabuto",
            "ArmorKuroganeDoMaruChest",
            "ArmorKuroganeHaidateLegs",
            "CapeKuroganeJinbaori"
        };

        private static readonly string[] KuroganeKnownRecipeNames =
        {
            "Kurogane Kabuto",
            "Kurogane Menpō Kabuto",
            "Kurogane Dō-Maru",
            "Kurogane Haidate",
            "Kurogane Jinbaori",
            "HelmetKuroganeKabuto",
            "HelmetKuroganeMenpoKabuto",
            "ArmorKuroganeDoMaruChest",
            "ArmorKuroganeHaidateLegs",
            "CapeKuroganeJinbaori"
        };

        private void Awake()
        {
            _instance = this;
            _assetDir = Path.Combine(Path.GetDirectoryName(Info.Location), "assets");
            BindConfig();

            if (!Directory.Exists(_assetDir))
            {
                Logger.LogError("Missing assets directory: " + _assetDir);
                return;
            }

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            PrefabManager.OnVanillaPrefabsAvailable += RegisterItems;
            ItemManager.OnItemsRegisteredFejd += RestrictMenpoStylesToMask;
            ItemManager.OnItemsRegistered += RegisterRecipes;
            Logger.LogInfo(PluginName + " loaded. Waiting for Jotunn vanilla prefab registration.");
        }

        private void BindConfig()
        {
            const string section = "Masked Helmet Visual";
            _maskedHelmetOffsetX = Config.Bind(section, "FineTuneX", 0f, "Additional local X offset for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetOffsetY = Config.Bind(section, "FineTuneY", 0f, "Additional local Y offset for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetOffsetZ = Config.Bind(section, "FineTuneZ", 0f, "Additional local Z offset for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetRotationX = Config.Bind(section, "FineTuneRotationX", 0f, "Local X rotation in degrees for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetRotationY = Config.Bind(section, "FineTuneRotationY", 0f, "Local Y rotation in degrees for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetRotationZ = Config.Bind(section, "FineTuneRotationZ", 0f, "Local Z rotation in degrees for the Kurogane Menpo Kabuto visual.");
            _maskedHelmetScale = Config.Bind(section, "FineTuneScale", 1f, "Local uniform scale for the Kurogane Menpo Kabuto visual.");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterItems;
            ItemManager.OnItemsRegisteredFejd -= RestrictMenpoStylesToMask;
            ItemManager.OnItemsRegistered -= RegisterRecipes;
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }

        private static void RegisterItems()
        {
            if (_registered)
            {
                return;
            }

            try
            {
                SE_Stats setEffect = CreateSetEffect();
                SE_Stats featherFallEffect = CreateFeatherFallEffect();
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(setEffect, false));
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(featherFallEffect, false));

                RegisterArmorItem(
                    sourcePrefabName: "HelmetFlametal",
                    newPrefabName: "HelmetKuroganeKabuto",
                    displayName: "Kurogane Kabuto",
                    description: "Protects your thoughts. Not your choices.",
                    iconFile: "kurogane_kabuto_icon.png",
                    itemType: ItemDrop.ItemData.ItemType.Helmet,
                    armor: 48f,
                    armorPerLevel: 2f,
                    durability: 1200f,
                    durabilityPerLevel: 200f,
                    weight: 3f,
                    movement: 0f,
                    setEffect: setEffect,
                    requirements: KuroganeHelmetRequirements());

                RegisterArmorItem(
                    sourcePrefabName: "HelmetFlametal",
                    newPrefabName: "HelmetKuroganeMenpoKabuto",
                    displayName: "Kurogane Menpō Kabuto",
                    description: "For when your face wants plausible deniability.",
                    iconFile: "kurogane_menpo_kabuto_icon.png",
                    itemType: ItemDrop.ItemData.ItemType.Helmet,
                    armor: 48f,
                    armorPerLevel: 2f,
                    durability: 1200f,
                    durabilityPerLevel: 200f,
                    weight: 3f,
                    movement: 0f,
                    setEffect: setEffect,
                    requirements: KuroganeHelmetRequirements(),
                    visualPrefabName: MaskedHelmetVisualAssetName);

                RegisterArmorItem(
                    sourcePrefabName: "ArmorFlametalChest",
                    newPrefabName: "ArmorKuroganeDoMaruChest",
                    displayName: "Kurogane Dō-Maru",
                    description: "For when bare-chested bravery stops working.",
                    iconFile: "kurogane_domaru_icon.png",
                    itemType: ItemDrop.ItemData.ItemType.Chest,
                    armor: 52f,
                    armorPerLevel: 2f,
                    durability: 1500f,
                    durabilityPerLevel: 200f,
                    weight: 10f,
                    movement: -0.02f,
                    setEffect: setEffect,
                    requirements: KuroganeChestRequirements());

                RegisterArmorItem(
                    sourcePrefabName: "ArmorFlametalLegs",
                    newPrefabName: "ArmorKuroganeHaidateLegs",
                    displayName: "Kurogane Haidate",
                    description: "Keeps your knees from filing complaints.",
                    iconFile: "kurogane_haidate_icon.png",
                    itemType: ItemDrop.ItemData.ItemType.Legs,
                    armor: 50f,
                    armorPerLevel: 2f,
                    durability: 1400f,
                    durabilityPerLevel: 200f,
                    weight: 10f,
                    movement: -0.02f,
                    setEffect: setEffect,
                    requirements: KuroganeLegsRequirements());

                CustomItem cloak = RegisterArmorItem(
                    sourcePrefabName: "CapeFeather",
                    newPrefabName: "CapeKuroganeJinbaori",
                    displayName: "Kurogane Jinbaori",
                    description: "Dramatic billowing sold separately.",
                    iconFile: "kurogane_jinbaori_icon.png",
                    itemType: ItemDrop.ItemData.ItemType.Shoulder,
                    armor: 8f,
                    armorPerLevel: 1f,
                    durability: 1200f,
                    durabilityPerLevel: 200f,
                    weight: 4f,
                    movement: 0f,
                    setEffect: setEffect,
                    requirements: KuroganeCloakRequirements());

                AddDamageModifier(cloak.ItemDrop, HitData.DamageType.Fire, HitData.DamageModifier.Resistant);
                cloak.ItemDrop.m_itemData.m_shared.m_equipStatusEffect = featherFallEffect;

            _registered = true;
            _instance.Logger.LogInfo("Registered Kurogane Samurai Armor through Jotunn.");
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError(ex);
            }
        }

        private static void RegisterRecipes()
        {
            if (ObjectDB.instance == null)
            {
                return;
            }

            try
            {
                RestrictMenpoStylesToMask();

                if (_recipesRegistered)
                {
                    return;
                }

                CraftingStation forge = GetForgeStation();
                if (forge == null)
                {
                    _instance.Logger.LogWarning("Could not resolve the real Forge crafting station. Kurogane recipes were not added.");
                    return;
                }

                AddRecipe("HelmetKuroganeKabuto", forge, KuroganeHelmetRequirements());

                AddRecipe("HelmetKuroganeMenpoKabuto", forge, KuroganeHelmetRequirements());
                AddAaaCraftingMenpoVariantRecipes(forge);

                AddRecipe("ArmorKuroganeDoMaruChest", forge, KuroganeChestRequirements());

                AddRecipe("ArmorKuroganeHaidateLegs", forge, KuroganeLegsRequirements());

                AddRecipe("CapeKuroganeJinbaori", forge, KuroganeCloakRequirements());

                _recipesRegistered = true;
                _instance.Logger.LogInfo("Registered 5 explicit Kurogane Forge recipes.");

                if (Player.m_localPlayer != null)
                {
                    PlayerUpdateKnownRecipesListMethod?.Invoke(Player.m_localPlayer, null);
                    AddKnownKuroganeRecipes(Player.m_localPlayer);
                }
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError(ex);
            }
        }

        private static void RestrictMenpoStylesToMask()
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab("HelmetKuroganeMenpoKabuto");
            if (prefab == null)
            {
                _instance.Logger.LogWarning("Could not find HelmetKuroganeMenpoKabuto while configuring mask styles.");
                return;
            }

            int styledMaskRenderers = 0;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                bool isMask = IsBundledHelmetRenderer(renderer.transform) &&
                              renderer.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    SetFloatIfPresent(material, "_Style", 0f);
                    SetFloatIfPresent(material, "_UseStyles", isMask ? 1f : 0f);
                    if (isMask)
                    {
                        material.EnableKeyword("_USESTYLES_ON");
                    }
                    else
                    {
                        material.DisableKeyword("_USESTYLES_ON");
                    }
                }

                ItemStyle itemStyle = renderer.GetComponent<ItemStyle>();
                if (isMask)
                {
                    styledMaskRenderers++;
                }
                else if (itemStyle != null)
                {
                    DestroyImmediate(itemStyle);
                }
            }

            ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                itemDrop.m_itemData.m_shared.m_variants = MaskStyleCount;
            }

            _instance.Logger.LogInfo(
                "Configured " + MaskStyleCount + " Kurogane Menpo styles on " +
                styledMaskRenderers + " mask renderer(s); helmet materials remain fixed.");
        }

        private static CustomItem RegisterArmorItem(
            string sourcePrefabName,
            string newPrefabName,
            string displayName,
            string description,
            string iconFile,
            ItemDrop.ItemData.ItemType itemType,
            float armor,
            float armorPerLevel,
            float durability,
            float durabilityPerLevel,
            float weight,
            float movement,
            StatusEffect setEffect,
            RequirementSpec[] requirements,
            string visualPrefabName = null)
        {
            ItemConfig config = new ItemConfig
            {
                Name = displayName,
                Description = description,
                Icon = LoadSprite(iconFile),
                Amount = 1,
                Enabled = true,
                Weight = weight
            };

            if (visualPrefabName == MaskedHelmetVisualAssetName)
            {
                config.Icons = LoadMaskStyleSprites();
                config.StyleTex = LoadTexture(MaskStyleTextureFileName, true);
            }

            _instance.Logger.LogInfo("Registering item " + newPrefabName + ". Recipe will be attached explicitly at Forge level 1: " + DescribeRequirements(requirements));

            CustomItem customItem;
            if (string.IsNullOrEmpty(visualPrefabName))
            {
                customItem = new CustomItem(newPrefabName, sourcePrefabName, config);
            }
            else
            {
                GameObject preparedPrefab = PrefabManager.Instance.CreateClonedPrefab(newPrefabName, sourcePrefabName);
                if (preparedPrefab == null)
                {
                    throw new InvalidOperationException("Jotunn could not clone '" + sourcePrefabName + "' into '" + newPrefabName + "'.");
                }

                ReplaceAttachVisual(preparedPrefab, visualPrefabName);
                customItem = new CustomItem(preparedPrefab, false, config);
            }

            if (customItem.ItemPrefab == null || customItem.ItemDrop == null)
            {
                throw new InvalidOperationException("Jotunn could not clone '" + sourcePrefabName + "' into '" + newPrefabName + "'.");
            }

            ItemDrop item = customItem.ItemDrop;
            ItemDrop.ItemData.SharedData shared = item.m_itemData.m_shared;

            shared.m_name = displayName;
            shared.m_description = description;
            shared.m_itemType = itemType;
            if (itemType == ItemDrop.ItemData.ItemType.Helmet)
            {
                shared.m_helmetHideHair = ItemDrop.ItemData.HelmetHairType.Hidden;
                shared.m_helmetHideBeard = ItemDrop.ItemData.HelmetHairType.Hidden;
            }
            shared.m_maxQuality = 4;
            shared.m_weight = weight;
            shared.m_movementModifier = movement;
            shared.m_armor = armor;
            shared.m_armorPerLevel = armorPerLevel;
            shared.m_maxDurability = durability;
            shared.m_durabilityPerLevel = durabilityPerLevel;
            shared.m_setName = SetName;
            shared.m_setSize = 4;
            shared.m_setStatusEffect = setEffect;
            shared.m_teleportable = true;
            shared.m_value = 0;
            shared.m_maxStackSize = 1;

            Material material = CreateKuroganeMaterial(shared.m_armorMaterial ?? FindFirstMaterial(customItem.ItemPrefab), itemType, newPrefabName + "_Material");
            shared.m_armorMaterial = material;
            TintRendererMaterials(customItem.ItemPrefab, itemType);

            item.m_itemData.m_dropPrefab = customItem.ItemPrefab;
            item.m_itemData.m_durability = durability;

            ItemManager.Instance.AddItem(customItem);
            return customItem;
        }

        private static void ReplaceAttachVisual(GameObject itemPrefab, string visualPrefabName)
        {
            GameObject visualPrefab = LoadBundledPrefab(visualPrefabName);
            if (visualPrefab == null)
            {
                _instance.Logger.LogWarning("Could not load bundled visual prefab '" + visualPrefabName + "'. The item will keep the vanilla cloned helmet visual.");
                return;
            }

            Transform attach = itemPrefab.transform.Find("attach");
            if (attach == null)
            {
                _instance.Logger.LogWarning("Could not find attach transform on " + itemPrefab.name + ".");
                return;
            }

            Material vanillaHelmetMaterial = FindFirstMaterial(attach.gameObject);

            foreach (Renderer renderer in attach.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            Transform visualParent = attach;
            Transform existingVisual = visualParent.Find(visualPrefabName);
            if (existingVisual != null)
            {
                DestroyImmediate(existingVisual.gameObject);
            }

            GameObject visual = Instantiate(visualPrefab, visualParent, false);
            visual.name = visualPrefabName;
            visual.transform.localPosition = GetMaskedHelmetOffset();
            visual.transform.localRotation = Quaternion.Euler(GetMaskedHelmetRotation());
            visual.transform.localScale = Vector3.one * Mathf.Max(0.01f, _maskedHelmetScale.Value);

            ApplyBundledHelmetMaterials(visual, vanillaHelmetMaterial);

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            Vector3 finalOffset = visual.transform.localPosition;
            Vector3 finalRotation = visual.transform.localEulerAngles;
            _instance.Logger.LogInfo(
                "Attached bundled visual '" + visualPrefabName + "' to " + itemPrefab.name +
                " under " + visualParent.name +
                " at local position " + finalOffset.ToString("F3") +
                ", local rotation " + finalRotation.ToString("F1") +
                ", local scale " + visual.transform.localScale.ToString("F3") + ".");
        }

        private static void ApplyBundledHelmetMaterials(GameObject visual, Material vanillaTemplate)
        {
            if (vanillaTemplate == null)
            {
                _instance.Logger.LogWarning("Could not find the vanilla helmet material used to render the bundled Kurogane textures.");
                return;
            }

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                Material[] bundledMaterials = renderer.sharedMaterials;
                Material[] gameMaterials = new Material[bundledMaterials.Length];

                for (int i = 0; i < bundledMaterials.Length; i++)
                {
                    Material bundled = bundledMaterials[i];
                    Texture diffuse = GetTextureIfPresent(bundled, "_MainTex") ?? GetTextureIfPresent(bundled, "_BaseMap");
                    Material gameMaterial = new Material(vanillaTemplate)
                    {
                        name = (bundled != null ? bundled.name : renderer.name) + "_Valheim"
                    };

                    SetTextureIfPresent(gameMaterial, "_MainTex", diffuse);
                    SetTextureIfPresent(gameMaterial, "_BaseMap", diffuse);
                    SetTextureIfPresent(gameMaterial, "_BumpMap", null);
                    SetTextureIfPresent(gameMaterial, "_MetallicGlossMap", null);
                    SetColorIfPresent(gameMaterial, "_Color", Color.white);
                    SetColorIfPresent(gameMaterial, "_BaseColor", Color.white);
                    SetFloatIfPresent(gameMaterial, "_Metallic", 0.08f);
                    SetFloatIfPresent(gameMaterial, "_Glossiness", 0.32f);
                    SetFloatIfPresent(gameMaterial, "_Smoothness", 0.32f);
                    gameMaterials[i] = gameMaterial;

                    _instance.Logger.LogInfo(
                        "Applied bundled texture '" + (diffuse != null ? diffuse.name : "<missing>") +
                        "' to " + renderer.name + " using shader '" +
                        (gameMaterial.shader != null ? gameMaterial.shader.name : "<missing>") + "'.");
                }

                renderer.sharedMaterials = gameMaterials;
            }
        }

        private static Transform FindHelmetVisualTransform(Transform attach)
        {
            Transform directDefault = attach.Find("default");
            if (directDefault != null)
            {
                return directDefault;
            }

            foreach (MeshRenderer renderer in attach.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.transform != attach)
                {
                    return renderer.transform;
                }
            }

            return null;
        }

        private static Vector3 GetMaskedHelmetOffset()
        {
            return MaskedHelmetBaseOffset + new Vector3(
                _maskedHelmetOffsetX != null ? _maskedHelmetOffsetX.Value : 0f,
                _maskedHelmetOffsetY != null ? _maskedHelmetOffsetY.Value : 0f,
                _maskedHelmetOffsetZ != null ? _maskedHelmetOffsetZ.Value : 0f);
        }

        private static Vector3 GetMaskedHelmetRotation()
        {
            return new Vector3(
                _maskedHelmetRotationX != null ? _maskedHelmetRotationX.Value : 0f,
                _maskedHelmetRotationY != null ? _maskedHelmetRotationY.Value : 0f,
                _maskedHelmetRotationZ != null ? _maskedHelmetRotationZ.Value : 0f);
        }

        private static GameObject LoadBundledPrefab(string assetName)
        {
            AssetBundle bundle = LoadAssetBundle();
            if (bundle == null)
            {
                return null;
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(assetName);
            if (prefab == null)
            {
                foreach (string bundledAssetName in bundle.GetAllAssetNames())
                {
                    if (Path.GetFileNameWithoutExtension(bundledAssetName).Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        prefab = bundle.LoadAsset<GameObject>(bundledAssetName);
                        break;
                    }
                }
            }

            if (prefab == null)
            {
                _instance.Logger.LogWarning("AssetBundle '" + BundleFileName + "' does not contain GameObject '" + assetName + "'.");
            }
            return prefab;
        }

        private static AssetBundle LoadAssetBundle()
        {
            if (_assetBundle != null)
            {
                return _assetBundle;
            }

            string path = Path.Combine(_assetDir, BundleFileName);
            if (!File.Exists(path))
            {
                _instance.Logger.LogWarning("Missing Kurogane AssetBundle: " + path);
                return null;
            }

            _assetBundle = AssetBundle.LoadFromFile(path);
            if (_assetBundle == null)
            {
                _instance.Logger.LogWarning("Unity could not load Kurogane AssetBundle: " + path);
            }
            return _assetBundle;
        }

        private static SE_Stats CreateSetEffect()
        {
            SE_Stats effect = ScriptableObject.CreateInstance<SE_Stats>();
            effect.name = "SE_Kurogane_DragonAndTiger";
            effect.m_name = "Dragon and Tiger";
            effect.m_tooltip = "The dragon refuses to tire. The tiger refuses to walk. Neither is especially worried about falling.";
            effect.m_runStaminaUseModifier = -0.8f;
            effect.m_jumpStaminaUseModifier = -0.8f;
            effect.m_attackStaminaUseModifier = -0.8f;
            effect.m_blockStaminaUseModifier = -0.8f;
            effect.m_speedModifier = 0.8f;
            return effect;
        }

        private static SE_Stats CreateFeatherFallEffect()
        {
            SE_Stats effect = ScriptableObject.CreateInstance<SE_Stats>();
            effect.name = "SE_Kurogane_FeatherFall";
            effect.m_name = "Feather Fall";
            effect.m_tooltip = "Fall like a feather. Land like you meant to do that.";
            effect.m_jumpStaminaUseModifier = -0.2f;
            effect.m_maxMaxFallSpeed = 5f;
            effect.m_fallDamageModifier = -1f;
            return effect;
        }

        private static CraftingStation GetForgeStation()
        {
            Recipe forgeRecipe = ObjectDB.instance.m_recipes.FirstOrDefault(recipe =>
                recipe != null &&
                recipe.m_item != null &&
                recipe.m_item.name == "HelmetBronze" &&
                recipe.m_craftingStation != null);

            if (forgeRecipe != null)
            {
                return forgeRecipe.m_craftingStation;
            }

            GameObject stationPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("forge") : null;
            return stationPrefab != null ? stationPrefab.GetComponent<CraftingStation>() : null;
        }

        private static void AddRecipe(string itemPrefabName, CraftingStation station, RequirementSpec[] requirements)
        {
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemPrefabName);
            ItemDrop item = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (item == null)
            {
                _instance.Logger.LogWarning("Missing Kurogane item prefab for recipe: " + itemPrefabName);
                return;
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + itemPrefabName;
            recipe.m_item = item;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_repairStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_requireOnlyOneIngredient = false;
            recipe.m_resources = ToPieceRequirements(requirements);

            ObjectDB.instance.m_recipes.RemoveAll(existing =>
                existing == null ||
                existing.name == recipe.name ||
                (existing.m_item != null && existing.m_item.name == itemPrefabName));
            ObjectDB.instance.m_recipes.Add(recipe);

            _instance.Logger.LogInfo("Added explicit recipe " + recipe.name + " using station " + station.name + ": " + DescribeRequirements(requirements));
        }

        private static void AddAaaCraftingMenpoVariantRecipes(CraftingStation station)
        {
            bool aaaCraftingLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly.GetName().Name.Equals("AzuAntiArthriticCrafting", StringComparison.OrdinalIgnoreCase));
            if (!aaaCraftingLoaded)
            {
                return;
            }

            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab("HelmetKuroganeMenpoKabuto");
            ItemDrop item = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (item == null)
            {
                _instance.Logger.LogWarning("AAA Crafting compatibility could not find HelmetKuroganeMenpoKabuto.");
                return;
            }

            const string baseRecipeName = "Recipe_HelmetKuroganeMenpoKabuto";
            ObjectDB.instance.m_recipes.RemoveAll(recipe =>
                recipe != null && recipe.name.StartsWith(baseRecipeName + "_var", StringComparison.Ordinal));

            for (int variant = 1; variant < MaskStyleCount; variant++)
            {
                Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = baseRecipeName + "_var" + variant;
                recipe.m_item = item;
                recipe.m_amount = 1;
                recipe.m_enabled = true;
                recipe.m_craftingStation = station;
                recipe.m_repairStation = station;
                recipe.m_minStationLevel = 1;
                recipe.m_requireOnlyOneIngredient = false;
                recipe.m_resources = ToPieceRequirements(KuroganeHelmetRequirements());
                ObjectDB.instance.m_recipes.Add(recipe);
            }

            ObjectDBUpdateRegistersMethod?.Invoke(ObjectDB.instance, null);
            _instance.Logger.LogInfo("Added 8 AAA Crafting compatibility recipes for Kurogane Menpo styles #2-#9.");
        }

        private static Piece.Requirement[] ToPieceRequirements(RequirementSpec[] specs)
        {
            List<Piece.Requirement> requirements = new List<Piece.Requirement>();
            foreach (RequirementSpec spec in specs)
            {
                GameObject resourcePrefab = ObjectDB.instance.GetItemPrefab(spec.PrefabName);
                ItemDrop resource = resourcePrefab != null ? resourcePrefab.GetComponent<ItemDrop>() : null;
                if (resource == null)
                {
                    _instance.Logger.LogWarning("Missing recipe resource '" + spec.PrefabName + "'.");
                    continue;
                }

                requirements.Add(new Piece.Requirement
                {
                    m_resItem = resource,
                    m_amount = spec.Amount,
                    m_amountPerLevel = spec.AmountPerLevel,
                    m_recover = false
                });
            }

            return requirements.ToArray();
        }

        private static string DescribeRequirements(RequirementSpec[] specs)
        {
            List<string> parts = new List<string>();
            foreach (RequirementSpec spec in specs)
            {
                parts.Add(spec.PrefabName + " x" + spec.Amount + " +" + spec.AmountPerLevel + "/level");
            }
            return string.Join(", ", parts.ToArray());
        }

        private static Material CreateKuroganeMaterial(Material template, ItemDrop.ItemData.ItemType itemType, string name)
        {
            if (template == null)
            {
                template = new Material(Shader.Find("Standard"));
            }

            Material material = new Material(template) { name = name };
            ApplyKuroganeSurface(material, itemType);
            return material;
        }

        private static void TintRendererMaterials(GameObject root, ItemDrop.ItemData.ItemType itemType)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsBundledHelmetRenderer(renderer.transform))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    materials[i] = CreateKuroganeMaterial(materials[i], itemType, materials[i].name + "_Kurogane");
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static bool IsBundledHelmetRenderer(Transform transform)
        {
            while (transform != null)
            {
                if (transform.name == MaskedHelmetVisualAssetName)
                {
                    return true;
                }
                transform = transform.parent;
            }

            return false;
        }

        private static void ApplyKuroganeSurface(Material material, ItemDrop.ItemData.ItemType itemType)
        {
            Color tint = itemType == ItemDrop.ItemData.ItemType.Shoulder
                ? new Color(0.11f, 0.13f, 0.14f, 1f)
                : new Color(0.42f, 0.47f, 0.50f, 1f);

            SetColorIfPresent(material, "_Color", tint);
            SetColorIfPresent(material, "_EmissionColor", Color.black);
            SetFloatIfPresent(material, "_Metallic", itemType == ItemDrop.ItemData.ItemType.Shoulder ? 0.1f : 0.75f);
            SetFloatIfPresent(material, "_Glossiness", itemType == ItemDrop.ItemData.ItemType.Shoulder ? 0.35f : 0.62f);
            SetFloatIfPresent(material, "_Smoothness", itemType == ItemDrop.ItemData.ItemType.Shoulder ? 0.35f : 0.62f);
        }

        private static Material FindFirstMaterial(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        return material;
                    }
                }
            }
            return null;
        }

        private static void AddDamageModifier(ItemDrop item, HitData.DamageType type, HitData.DamageModifier modifier)
        {
            item.m_itemData.m_shared.m_damageModifiers.RemoveAll(pair => pair.m_type == type);
            item.m_itemData.m_shared.m_damageModifiers.Add(new HitData.DamageModPair { m_type = type, m_modifier = modifier });
        }

        private static void SetTextureIfPresent(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static Texture GetTextureIfPresent(Material material, string property)
        {
            return material != null && material.HasProperty(property) ? material.GetTexture(property) : null;
        }

        private static void SetColorIfPresent(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static TextureSet LoadTextureSet(string prefix)
        {
            return new TextureSet
            {
                Diffuse = LoadTexture(prefix + "_d.png", true),
                Normal = LoadTexture(prefix + "_n.png", false),
                Mask = LoadTexture(prefix + "_m.png", false)
            };
        }

        private static Texture2D LoadTexture(string fileName, bool srgb)
        {
            string path = Path.Combine(_assetDir, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path);
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, !srgb);
            ImageConversion.LoadImage(texture, File.ReadAllBytes(path));
            texture.name = Path.GetFileNameWithoutExtension(fileName);
            return texture;
        }

        private static Sprite LoadSprite(string fileName)
        {
            Texture2D texture = LoadTexture(fileName, true);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite[] LoadMaskStyleSprites()
        {
            Sprite[] sprites = new Sprite[MaskStyleCount];
            for (int i = 0; i < sprites.Length; i++)
            {
                sprites[i] = LoadSprite("kurogane_mask_style_" + (i + 1).ToString("00") + ".png");
            }
            return sprites;
        }

        private static RequirementSpec[] R(params RequirementSpec[] specs)
        {
            return specs;
        }

        private static RequirementSpec[] KuroganeHelmetRequirements()
        {
            return R(
                Req("Silver", 20, 10),
                Req("Iron", 10, 5),
                Req("LeatherScraps", 8, 4),
                Req("DeerHide", 4, 0));
        }

        private static RequirementSpec[] KuroganeChestRequirements()
        {
            return R(
                Req("Silver", 30, 15),
                Req("Iron", 20, 10),
                Req("LeatherScraps", 12, 6),
                Req("DeerHide", 8, 0));
        }

        private static RequirementSpec[] KuroganeLegsRequirements()
        {
            return R(
                Req("Silver", 25, 12),
                Req("Iron", 15, 8),
                Req("LeatherScraps", 10, 5),
                Req("DeerHide", 6, 0));
        }

        private static RequirementSpec[] KuroganeCloakRequirements()
        {
            return R(
                Req("Silver", 15, 8),
                Req("Iron", 8, 4),
                Req("LeatherScraps", 10, 5),
                Req("DeerHide", 6, 0));
        }

        private static RequirementSpec Req(string prefabName, int amount, int amountPerLevel)
        {
            return new RequirementSpec(prefabName, amount, amountPerLevel);
        }

        private enum MaterialMode
        {
            Standard,
            Chest,
            Legs
        }

        private sealed class TextureSet
        {
            public Texture2D Diffuse;
            public Texture2D Normal;
            public Texture2D Mask;
        }

        private sealed class RequirementSpec
        {
            public RequirementSpec(string prefabName, int amount, int amountPerLevel)
            {
                PrefabName = prefabName;
                Amount = amount;
                AmountPerLevel = amountPerLevel;
            }

            public readonly string PrefabName;
            public readonly int Amount;
            public readonly int AmountPerLevel;
        }

        private sealed class HelmetVariantState
        {
            public int Variant;
            public int AppliedVariant = -1;
        }

        private static ZNetView GetEquipmentNView(VisEquipment equipment)
        {
            return VisEquipmentNViewField?.GetValue(equipment) as ZNetView;
        }

        private static GameObject GetEquippedHelmetInstance(VisEquipment equipment)
        {
            return VisEquipmentHelmetInstanceField?.GetValue(equipment) as GameObject;
        }

        private static void AddKnownKuroganeRecipes(Player player)
        {
            HashSet<string> knownRecipes = GetKnownRecipeSet(player);
            if (knownRecipes == null)
            {
                return;
            }

            int added = 0;
            foreach (string recipeName in KuroganeKnownRecipeNames)
            {
                if (knownRecipes.Add(recipeName))
                {
                    added++;
                }
            }

            if (ObjectDB.instance != null)
            {
                foreach (string prefabName in KuroganeItemPrefabNames)
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                    ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    string sharedName = item != null && item.m_itemData != null && item.m_itemData.m_shared != null
                        ? item.m_itemData.m_shared.m_name
                        : null;

                    if (!string.IsNullOrEmpty(sharedName) && knownRecipes.Add(sharedName))
                    {
                        added++;
                    }
                }
            }

            if (added > 0)
            {
                _instance.Logger.LogInfo("Unlocked " + added + " Kurogane recipe name(s) for the local player.");
            }
        }

        private static HashSet<string> GetKnownRecipeSet(Player player)
        {
            if (player == null || PlayerUpdateKnownRecipesListPatch.KnownRecipesField == null)
            {
                return null;
            }

            return PlayerUpdateKnownRecipesListPatch.KnownRecipesField.GetValue(player) as HashSet<string>;
        }

        [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
        private static class PlayerUpdateKnownRecipesListPatch
        {
            internal static readonly FieldInfo KnownRecipesField = AccessTools.Field(typeof(Player), "m_knownRecipes");

            private static void Postfix(Player __instance)
            {
                AddKnownKuroganeRecipes(__instance);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        private static class VisEquipmentUpdatePatch
        {
            private static void Prefix(VisEquipment __instance)
            {
                ZNetView nview = GetEquipmentNView(__instance);
                if (nview != null && nview.GetZDO() is ZDO zdo)
                {
                    HelmetVariantStates.GetOrCreateValue(__instance).Variant =
                        Mathf.Clamp(zdo.GetInt(MenpoVariantZdoKey), 0, MaskStyleCount - 1);
                }
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
        private static class VisEquipmentSetHelmetItemPatch
        {
            private static void Prefix(VisEquipment __instance, string name)
            {
                int variant = 0;
                Humanoid humanoid = __instance.GetComponent<Humanoid>();
                ItemDrop.ItemData helmetItem = humanoid != null
                    ? HumanoidHelmetItemField?.GetValue(humanoid) as ItemDrop.ItemData
                    : null;
                if (name == "HelmetKuroganeMenpoKabuto" && helmetItem != null)
                {
                    variant = Mathf.Clamp(helmetItem.m_variant, 0, MaskStyleCount - 1);
                }

                HelmetVariantState state = HelmetVariantStates.GetOrCreateValue(__instance);
                state.Variant = variant;
                if (name != "HelmetKuroganeMenpoKabuto")
                {
                    state.AppliedVariant = -1;
                }
                ZNetView nview = GetEquipmentNView(__instance);
                if (nview != null && nview.GetZDO() is ZDO zdo)
                {
                    zdo.Set(MenpoVariantZdoKey, variant);
                }

                if (name == "HelmetKuroganeMenpoKabuto")
                {
                    _instance.Logger.LogInfo("Equipping Kurogane Menpo variant index " + variant + ".");
                }
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquipped")]
        private static class VisEquipmentSetHelmetEquippedPatch
        {
            private static void Prefix(VisEquipment __instance, int hash)
            {
                if (hash == MenpoPrefabHash)
                {
                    HelmetVariantState state = HelmetVariantStates.GetOrCreateValue(__instance);
                    if (state.AppliedVariant != state.Variant)
                    {
                        VisEquipmentCurrentHelmetHashField?.SetValue(__instance, int.MinValue);
                    }
                }
            }

            private static void Postfix(VisEquipment __instance, int hash, bool __result)
            {
                GameObject helmetInstance = GetEquippedHelmetInstance(__instance);
                if (!__result || hash != MenpoPrefabHash || helmetInstance == null)
                {
                    return;
                }

                HelmetVariantState state = HelmetVariantStates.GetOrCreateValue(__instance);
                int variant = state.Variant;
                int styleCount = 0;
                foreach (ItemStyle style in helmetInstance.GetComponentsInChildren<ItemStyle>(true))
                {
                    style.Setup(variant);
                    styleCount++;
                }
                state.AppliedVariant = variant;
                _instance.Logger.LogInfo(
                    "Applied Kurogane Menpo variant index " + variant + " to " + styleCount + " mask style component(s).");
            }
        }
    }
}
