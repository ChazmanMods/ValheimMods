using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyCompany("Chazman")]
[assembly: AssemblyProduct("Arcane Warriors")]

namespace ArcaneWarriors
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class ArcaneWarriorsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.chazman.valheim.arcanewarriors";
        public const string PluginName = "Arcane Warriors";
        public const string PluginVersion = "0.7.1";

        private const string SetName = "SetArcaneWarrior";
        private const string BundleFileName = "arcanewarriorsassets";
        private const string ChestVisualName = "ArcaneWarriorChestVisual";
        private const string LegsVisualName = "ArcaneWarriorLegsVisual";
        private const string HoodVisualName = "ArcaneWarriorHoodVisual";
        private const string SwordVisualName = "SwordOfTheAncientsVisual";
        private const string HumClipName = "Idle_hum";
        private const string DeployClipName = "lightsaber-ignition";
        private const string RetractClipName = "Lightsaber_shut_off";
        private const string SwingClipName = "Lightsaber_swing";
        private static ArcaneWarriorsPlugin _instance;
        private static bool _itemsRegistered;
        private static bool _recipesRegistered;
        private static SE_Stats _setEffect;
        private static AssetBundle _assetBundle;
        private Harmony _harmony;
        private AudioSource _humSource;
        private AudioSource _eventSource;
        private AudioClip _deployClip;
        private AudioClip _retractClip;
        private AudioClip[] _swingClips;
        private Player _audioPlayer;
        private bool _humWasActive;
        private readonly List<Renderer> _ignitionRenderers = new List<Renderer>();
        private readonly List<Light> _ignitionLights = new List<Light>();
        private float _ignitionDelayRemaining;
        private ItemDrop.ItemData _pendingSwordUnequip;
        private bool _allowSwordUnequip;
        private static readonly FieldInfo PerfectBlockEffectField =
            AccessTools.Field(typeof(Humanoid), "m_perfectBlockEffect");
        private static readonly FieldInfo BlockTimerField =
            AccessTools.Field(typeof(Humanoid), "m_blockTimer");
        private static readonly FieldInfo PerfectBlockIntervalField =
            AccessTools.Field(typeof(Humanoid), "m_perfectBlockInterval");

        private sealed class BlockEffectState
        {
            internal EffectList Original;
            internal bool Replaced;
            internal bool Saber;
            internal bool Perfect;
        }

        private void Awake()
        {
            _instance = this;
            PrefabManager.OnVanillaPrefabsAvailable += RegisterItems;
            ItemManager.OnItemsRegistered += RegisterRecipes;
            InstallAudioPatches();
            Logger.LogInfo(PluginName + " loaded. Waiting for vanilla prefabs.");
        }

        private void Update()
        {
            UpdateSwordAudio();
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterItems;
            ItemManager.OnItemsRegistered -= RegisterRecipes;
            _harmony?.UnpatchSelf();
        }

        private static void RegisterItems()
        {
            if (_itemsRegistered)
            {
                return;
            }

            try
            {
                _setEffect = CreateSetEffect();
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(_setEffect, false));

                RegisterArmor(
                    "HelmetFenring",
                    "HelmetArcaneWarrior",
                    "Arcane Warrior Cowl",
                    "A quiet mind is useful. A dramatic hood is optional.",
                    ItemDrop.ItemData.ItemType.Helmet,
                    18f,
                    0.20f,
                    HoodVisualName,
                    Req("LinenThread", 12, 6), Req("JuteBlue", 4, 2), Req("Softtissue", 4, 2), Req("eitr", 3, 2));

                RegisterArmor(
                    "ArmorFenringChest",
                    "ArmorArcaneWarriorChest",
                    "Arcane Warrior Vestments",
                    "Looks peaceful. Handles disagreements decisively.",
                    ItemDrop.ItemData.ItemType.Chest,
                    22f,
                    0.40f,
                    ChestVisualName,
                    Req("LinenThread", 24, 12), Req("JuteBlue", 8, 4), Req("Softtissue", 8, 4), Req("eitr", 6, 3));

                RegisterArmor(
                    "ArmorFenringLegs",
                    "ArmorArcaneWarriorLegs",
                    "Arcane Warrior Legwraps",
                    "For crossing realms without tripping over destiny.",
                    ItemDrop.ItemData.ItemType.Legs,
                    20f,
                    0.40f,
                    LegsVisualName,
                    Req("LinenThread", 18, 9), Req("JuteBlue", 6, 3), Req("Softtissue", 6, 3), Req("eitr", 4, 2));

                RegisterSword();
                _itemsRegistered = true;
                _instance.Logger.LogInfo("Registered the Arcane Warrior set and Sword of the Ancients.");
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError(ex);
            }
        }

        private static void RegisterArmor(
            string sourcePrefabName,
            string prefabName,
            string displayName,
            string description,
            ItemDrop.ItemData.ItemType itemType,
            float armor,
            float eitrRegen,
            string visualName,
            params RequirementSpec[] requirements)
        {
            ItemConfig config = CreateConfig(sourcePrefabName, displayName, description, 2f);
            GameObject preparedPrefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, sourcePrefabName);
            if (preparedPrefab == null)
            {
                throw new InvalidOperationException("Could not clone " + sourcePrefabName + ".");
            }

            Material armorMaterial = InstallSkinnedVisual(preparedPrefab, visualName, itemType);
            CustomItem item = new CustomItem(preparedPrefab, false, config);
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;

            shared.m_name = displayName;
            shared.m_description = description;
            shared.m_itemType = itemType;
            shared.m_maxQuality = 4;
            shared.m_armor = armor;
            shared.m_armorPerLevel = 2f;
            shared.m_maxDurability = 900f;
            shared.m_durabilityPerLevel = 150f;
            shared.m_weight = itemType == ItemDrop.ItemData.ItemType.Chest ? 4f : 2f;
            shared.m_movementModifier = 0f;
            shared.m_eitrRegenModifier = eitrRegen;
            shared.m_setName = SetName;
            shared.m_setSize = 3;
            shared.m_setStatusEffect = _setEffect;
            shared.m_teleportable = true;
            shared.m_maxStackSize = 1;

            if (itemType == ItemDrop.ItemData.ItemType.Helmet)
            {
                shared.m_helmetHideHair = ItemDrop.ItemData.HelmetHairType.Hidden;
                shared.m_helmetHideBeard = ItemDrop.ItemData.HelmetHairType.Default;
            }

            if (armorMaterial != null)
            {
                shared.m_armorMaterial = armorMaterial;
            }
            item.ItemDrop.m_itemData.m_dropPrefab = item.ItemPrefab;
            item.ItemDrop.m_itemData.m_durability = shared.m_maxDurability;
            ItemManager.Instance.AddItem(item);
            RecipeSpecs[prefabName] = requirements;
        }

        private static void RegisterSword()
        {
            const string sourcePrefabName = "SwordMistwalker";
            const string prefabName = "SwordOfTheAncients";
            ItemConfig config = CreateConfig(
                sourcePrefabName,
                "Sword of the Ancients",
                "An old current of power, persuaded into the shape of a sword.",
                1.8f);

            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, sourcePrefabName);
            if (prefab == null)
            {
                throw new InvalidOperationException("Could not clone " + sourcePrefabName + ".");
            }

            InstallSwordVisual(prefab);
            CustomItem item = new CustomItem(prefab, false, config);
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_name = "Sword of the Ancients";
            shared.m_description = "An old current of power, persuaded into the shape of a sword.";
            shared.m_maxQuality = 4;
            shared.m_weight = 1.8f;
            shared.m_movementModifier = 0f;
            shared.m_maxDurability = 250f;
            shared.m_durabilityPerLevel = 75f;
            shared.m_itemType = ItemDrop.ItemData.ItemType.OneHandedWeapon;
            shared.m_animationState = ItemDrop.ItemData.AnimationState.OneHanded;
            shared.m_damages.m_slash = 62.5f;
            shared.m_damages.m_spirit = 26.25f;
            shared.m_damages.m_lightning = 11.25f;
            shared.m_damagesPerLevel.m_slash = 62.5f;
            shared.m_damagesPerLevel.m_spirit = 26.25f;
            shared.m_damagesPerLevel.m_lightning = 11.25f;
            shared.m_attackForce = 45f;
            shared.m_blockPower = 52f;
            shared.m_blockPowerPerLevel = 6f;
            shared.m_teleportable = true;
            shared.m_maxStackSize = 1;
            InstallLightningImpactEffects(shared);

            item.ItemDrop.m_itemData.m_dropPrefab = item.ItemPrefab;
            item.ItemDrop.m_itemData.m_durability = shared.m_maxDurability;
            ItemManager.Instance.AddItem(item);
            RecipeSpecs[prefabName] = new[]
            {
                Req("YggdrasilWood", 10, 5),
                Req("Silver", 20, 10),
                Req("BlackCore", 2, 1),
                Req("eitr", 15, 8)
            };
        }

        private static ItemConfig CreateConfig(string sourcePrefabName, string name, string description, float weight)
        {
            ItemConfig config = new ItemConfig
            {
                Name = name,
                Description = description,
                Amount = 1,
                Enabled = true,
                Weight = weight
            };

            GameObject source = PrefabManager.Instance.GetPrefab(sourcePrefabName);
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop != null && sourceDrop.m_itemData.m_shared.m_icons != null && sourceDrop.m_itemData.m_shared.m_icons.Length > 0)
            {
                config.Icon = sourceDrop.m_itemData.m_shared.m_icons[0];
            }

            return config;
        }

        private static SE_Stats CreateSetEffect()
        {
            SE_Stats effect = ScriptableObject.CreateInstance<SE_Stats>();
            effect.name = "SE_ArcaneWarrior_QuietCurrent";
            effect.m_name = "Quiet Current";
            effect.m_tooltip = "The world slows down just enough to make you look prepared.";
            effect.m_runStaminaUseModifier = -0.20f;
            effect.m_jumpStaminaUseModifier = -0.20f;
            effect.m_attackStaminaUseModifier = -0.15f;
            effect.m_speedModifier = 0.10f;
            return effect;
        }

        private static Material InstallSkinnedVisual(
            GameObject itemPrefab,
            string visualName,
            ItemDrop.ItemData.ItemType itemType)
        {
            Transform attach = itemPrefab.transform.Find("attach_skin");
            if (attach == null)
            {
                throw new InvalidOperationException(itemPrefab.name + " has no attach_skin transform.");
            }

            Material template = itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_armorMaterial;
            if (template == null)
            {
                template = FindFirstMaterial(attach.gameObject);
            }

            GameObject visualPrefab = LoadBundledPrefab(visualName);
            if (visualPrefab == null)
            {
                throw new InvalidOperationException("Missing bundled visual " + visualName + ".");
            }

            List<GameObject> oldChildren = new List<GameObject>();
            foreach (Transform child in attach)
            {
                oldChildren.Add(child.gameObject);
            }
            foreach (GameObject child in oldChildren)
            {
                DestroyImmediate(child);
            }

            GameObject visual = Instantiate(visualPrefab);
            visual.name = visualName;
            while (visual.transform.childCount > 0)
            {
                visual.transform.GetChild(0).SetParent(attach, false);
            }
            DestroyImmediate(visual);

            Material cream = CreateClothMaterial(template, "ArcaneCreamCloth", new Color(0.76f, 0.69f, 0.56f, 1f), 0.08f);
            Material brown = CreateClothMaterial(template, "ArcaneBrownCloth", new Color(0.13f, 0.055f, 0.035f, 1f), 0.11f);
            Material leather = CreateClothMaterial(template, "ArcaneLeather", new Color(0.24f, 0.075f, 0.025f, 1f), 0.22f);

            Material primaryArmorMaterial = null;
            foreach (Renderer renderer in attach.GetComponentsInChildren<Renderer>(true))
            {
                string rendererName = renderer.name.ToLowerInvariant();
                Texture bundledTexture = renderer.sharedMaterial != null
                    ? renderer.sharedMaterial.mainTexture
                    : null;
                Material selected;
                if (bundledTexture != null)
                {
                    selected = CreateBundledClothMaterial(
                        template,
                        "ArcaneArmor_" + renderer.name,
                        bundledTexture);
                }
                else
                {
                    selected = rendererName.Contains("leather")
                        ? leather
                        : rendererName.Contains("brown") || rendererName.Contains("hood") || rendererName.Contains("outerrobe")
                            ? brown
                            : cream;
                }
                renderer.sharedMaterials = Enumerable.Repeat(selected, Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
                if (primaryArmorMaterial == null && bundledTexture != null &&
                    !rendererName.Contains("cape"))
                {
                    primaryArmorMaterial = selected;
                }
            }

            attach.gameObject.SetActive(false);
            _instance.Logger.LogInfo("Installed custom skinned visual " + visualName + " on " + itemPrefab.name + ".");
            return primaryArmorMaterial ??
                   (itemType == ItemDrop.ItemData.ItemType.Helmet ? brown : cream);
        }

        private static void InstallSwordVisual(GameObject itemPrefab)
        {
            Transform attach = itemPrefab.transform.Find("attach");
            if (attach == null)
            {
                throw new InvalidOperationException("SwordMistwalker clone has no attach transform.");
            }

            Material template = FindFirstMaterial(attach.gameObject);
            foreach (MeshRenderer renderer in attach.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.enabled = false;
            }
            foreach (SkinnedMeshRenderer renderer in attach.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.enabled = false;
            }

            GameObject visualPrefab = LoadBundledPrefab(SwordVisualName);
            if (visualPrefab == null)
            {
                throw new InvalidOperationException("Missing bundled visual " + SwordVisualName + ".");
            }

            GameObject visual = Instantiate(visualPrefab, attach, false);
            visual.name = SwordVisualName;
            visual.transform.localPosition = new Vector3(0f, 0f, -0.083f);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Material metal = CreateSurfaceMaterial(template, "AncientSilver", new Color(0.28f, 0.32f, 0.34f, 1f), 0.82f, 0.60f);
            Material darkMetal = CreateSurfaceMaterial(template, "AncientGrip", new Color(0.025f, 0.03f, 0.035f, 1f), 0.45f, 0.42f);
            Material bladeCore = CreateEnergyMaterial(template, "AncientGreenCore", new Color(0.55f, 1f, 0.64f, 1f), false);
            Material bladeGlow = CreateEnergyMaterial(template, "AncientGreenGlow", new Color(0.0f, 1f, 0.22f, 0.32f), true);

            foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                string rendererName = renderer.name.ToLowerInvariant();
                renderer.enabled = true;
                renderer.sharedMaterial = rendererName.Contains("energycore")
                    ? bladeCore
                    : rendererName.Contains("energy")
                        ? bladeGlow
                        : rendererName.Contains("grip")
                            ? darkMetal
                            : metal;
            }

            foreach (ParticleSystem particles in attach.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 1f, 0.25f, 1f));
            }
            foreach (TrailRenderer trail in attach.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.startColor = new Color(0.45f, 1f, 0.55f, 0.9f);
                trail.endColor = new Color(0.0f, 0.75f, 0.16f, 0.05f);
            }
            foreach (Light light in attach.GetComponentsInChildren<Light>(true))
            {
                light.color = new Color(0.05f, 1f, 0.22f, 1f);
                light.intensity = 2.1f;
                light.range = 4.2f;
            }

            _instance.Logger.LogInfo("Installed slim green Sword of the Ancients visual.");
        }

        private static void InstallLightningImpactEffects(ItemDrop.ItemData.SharedData target)
        {
            GameObject lightningPrefab = PrefabManager.Instance.GetPrefab("SwordNiedhoggLightning");
            ItemDrop lightningDrop = lightningPrefab != null ? lightningPrefab.GetComponent<ItemDrop>() : null;
            if (lightningDrop == null)
            {
                _instance.Logger.LogWarning("Could not find SwordNiedhoggLightning; retaining the source sword hit effects.");
                return;
            }

            target.m_hitEffect = lightningDrop.m_itemData.m_shared.m_hitEffect;
            target.m_hitTerrainEffect = lightningDrop.m_itemData.m_shared.m_hitTerrainEffect;
            target.m_blockEffect = lightningDrop.m_itemData.m_shared.m_hitEffect;
            _instance.Logger.LogInfo("Installed native lightning-enchantment contact effects.");
        }

        private void UpdateSwordAudio()
        {
            Player player = Player.m_localPlayer;
            bool deployed = player != null && IsSwordOfTheAncients(player.RightItem);

            UpdateIgnitionVisual();

            // The sword intentionally remains equipped while its shutdown clip plays.
            // Do not let that temporary equipped state re-arm the normal transition
            // detector or it will fire a second shutdown after the delayed unequip.
            if (_pendingSwordUnequip != null)
            {
                return;
            }

            if (player != _audioPlayer && player != null)
            {
                _humWasActive = false;
                EnsureSwordAudio(player);
            }

            if (deployed == _humWasActive)
            {
                return;
            }

            _humWasActive = deployed;
            if (deployed)
            {
                EnsureSwordAudio(player);
                PlayEvent(_deployClip, 0.74f);
                BeginIgnitionVisual(player);
                Logger.LogInfo("Sword of the Ancients deployed; matched hum started.");
            }
            else
            {
                if (_humSource != null)
                {
                    _humSource.Stop();
                }
                PlayEvent(_retractClip, 0.72f, 2f);
                Logger.LogInfo("Sword of the Ancients retracted; matched hum stopped.");
            }
        }

        private void BeginDelayedSwordUnequip(
            Humanoid humanoid,
            ItemDrop.ItemData item,
            bool triggerEquipEffects)
        {
            if (_pendingSwordUnequip == item)
            {
                return;
            }

            _pendingSwordUnequip = item;
            _humWasActive = false;
            _humSource?.Stop();
            PlayEvent(_retractClip, 0.72f, 2f);
            StartCoroutine(FinishDelayedSwordUnequip(
                humanoid,
                item,
                triggerEquipEffects,
                _retractClip != null ? _retractClip.length * 0.5f : 0f));
            Logger.LogInfo("Sword of the Ancients stow requested; delaying unequip until shutdown finishes.");
        }

        private IEnumerator FinishDelayedSwordUnequip(
            Humanoid humanoid,
            ItemDrop.ItemData item,
            bool triggerEquipEffects,
            float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            _allowSwordUnequip = true;
            try
            {
                if (humanoid != null && item != null)
                {
                    humanoid.UnequipItem(item, triggerEquipEffects);
                }
            }
            finally
            {
                _allowSwordUnequip = false;
                _pendingSwordUnequip = null;
            }
        }

        private void BeginIgnitionVisual(Player player)
        {
            _ignitionRenderers.Clear();
            _ignitionLights.Clear();
            _ignitionDelayRemaining = 0.05f;

            if (player == null)
            {
                return;
            }

            Transform visibleSword = player.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate =>
                    candidate.name.Equals(SwordVisualName, StringComparison.Ordinal) &&
                    candidate.gameObject.activeInHierarchy);
            if (visibleSword == null)
            {
                return;
            }

            foreach (Renderer renderer in visibleSword.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf("energy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                renderer.enabled = false;
                _ignitionRenderers.Add(renderer);
            }
            foreach (Light light in visibleSword.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
                _ignitionLights.Add(light);
            }
        }

        private void UpdateIgnitionVisual()
        {
            if (_ignitionDelayRemaining <= 0f)
            {
                return;
            }

            _ignitionDelayRemaining -= Time.unscaledDeltaTime;
            if (_ignitionDelayRemaining > 0f)
            {
                return;
            }

            foreach (Renderer renderer in _ignitionRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            foreach (Light light in _ignitionLights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }
            _ignitionRenderers.Clear();
            _ignitionLights.Clear();
            if (_humSource != null && _humSource.clip != null && !_humSource.isPlaying)
            {
                _humSource.Play();
            }
        }

        private void EnsureSwordAudio(Player player)
        {
            if (_audioPlayer == player && _humSource != null && _eventSource != null)
            {
                return;
            }

            _audioPlayer = player;
            _humSource = CreateSpatialAudioSource(player.gameObject, "SwordOfTheAncients_Hum", true);
            _eventSource = CreateSpatialAudioSource(player.gameObject, "SwordOfTheAncients_Events", false);
            _humSource.clip = LoadBundledAsset<AudioClip>(HumClipName);
            _deployClip = LoadBundledAsset<AudioClip>(DeployClipName);
            _retractClip = LoadBundledAsset<AudioClip>(RetractClipName);
            _swingClips = new[]
            {
                LoadBundledAsset<AudioClip>(SwingClipName)
            }.Where(clip => clip != null).ToArray();
            if (_humSource.clip == null)
            {
                Logger.LogWarning("Sword of the Ancients idle hum could not be loaded.");
                return;
            }
            _humSource.clip.LoadAudioData();
            _deployClip?.LoadAudioData();
            _retractClip?.LoadAudioData();
            foreach (AudioClip clip in _swingClips)
            {
                clip.LoadAudioData();
            }
            Logger.LogInfo("Loaded the matched Sword of the Ancients audio family: hum, deploy, retract, and " + _swingClips.Length + " swing clips.");
        }

        private static AudioSource CreateSpatialAudioSource(GameObject owner, string name, bool loop)
        {
            AudioSource source = owner.AddComponent<AudioSource>();
            source.name = name;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = loop ? 0f : 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = 18f;
            source.dopplerLevel = 0.15f;
            source.volume = loop ? 0.58f : 0.58f;
            return source;
        }

        private void PlayEvent(AudioClip clip, float volume, float pitch = 1f)
        {
            if (_eventSource != null && clip != null)
            {
                _eventSource.pitch = pitch;
                _eventSource.PlayOneShot(clip, volume);
            }
        }

        private void TriggerSwordSwing(Humanoid humanoid)
        {
            Player player = humanoid as Player;
            if (player == null || player != Player.m_localPlayer || !IsSwordOfTheAncients(player.RightItem))
            {
                return;
            }

            EnsureSwordAudio(player);
            if (_swingClips.Length == 0)
            {
                return;
            }
            AudioClip clip = _swingClips[UnityEngine.Random.Range(0, _swingClips.Length)];
            _eventSource.pitch = UnityEngine.Random.Range(0.88f, 1.12f);
            _eventSource.PlayOneShot(clip, UnityEngine.Random.Range(0.58f, 0.78f));
        }

        private void PlaySaberParrySound()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            EnsureSwordAudio(player);
            if (_swingClips == null || _swingClips.Length == 0)
            {
                return;
            }

            _eventSource.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            _eventSource.PlayOneShot(
                _swingClips[UnityEngine.Random.Range(0, _swingClips.Length)],
                0.72f);
        }

        private void InstallAudioPatches()
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(Humanoid), "OnWeaponTrailStart");
                MethodInfo postfix = AccessTools.Method(typeof(ArcaneWarriorsPlugin), nameof(OnWeaponTrailStarted));
                if (original == null || postfix == null)
                {
                    Logger.LogWarning("Could not resolve the weapon-trail audio hook; continuous and transition sounds will still work.");
                    return;
                }
                _harmony = new Harmony(PluginGuid);
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                MethodInfo unequip = AccessTools.Method(
                    typeof(Humanoid),
                    "UnequipItem",
                    new[] { typeof(ItemDrop.ItemData), typeof(bool) });
                MethodInfo beforeUnequip = AccessTools.Method(
                    typeof(ArcaneWarriorsPlugin),
                    nameof(BeforeUnequipItem));
                MethodInfo blockAttack = AccessTools.Method(
                    typeof(Humanoid),
                    "BlockAttack",
                    new[] { typeof(HitData), typeof(Character) });
                MethodInfo beforeBlock = AccessTools.Method(
                    typeof(ArcaneWarriorsPlugin),
                    nameof(BeforeBlockAttack));
                MethodInfo afterBlock = AccessTools.Method(
                    typeof(ArcaneWarriorsPlugin),
                    nameof(AfterBlockAttack));
                if (unequip == null || beforeUnequip == null ||
                    blockAttack == null || beforeBlock == null || afterBlock == null)
                {
                    throw new MissingMethodException(
                        "Could not resolve the Sword of the Ancients stow or block hooks.");
                }

                _harmony.Patch(unequip, prefix: new HarmonyMethod(beforeUnequip));
                _harmony.Patch(
                    blockAttack,
                    prefix: new HarmonyMethod(beforeBlock),
                    postfix: new HarmonyMethod(afterBlock));
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not install the weapon-trail audio hook. Other sword audio will remain active. " + ex);
            }
        }

        private static void OnWeaponTrailStarted(Humanoid __instance)
        {
            _instance?.TriggerSwordSwing(__instance);
        }

        private static bool BeforeUnequipItem(
            Humanoid __instance,
            ItemDrop.ItemData item,
            bool triggerEquipEffects)
        {
            if (_instance == null ||
                _instance._allowSwordUnequip ||
                __instance != Player.m_localPlayer ||
                !IsSwordOfTheAncients(item))
            {
                return true;
            }

            _instance.BeginDelayedSwordUnequip(__instance, item, triggerEquipEffects);
            return false;
        }

        private static void BeforeBlockAttack(Humanoid __instance, ref BlockEffectState __state)
        {
            __state = new BlockEffectState();
            Player player = __instance as Player;
            ItemDrop.ItemData blocker = player != null ? player.RightItem : null;
            __state.Saber = IsSwordOfTheAncients(blocker);
            if (!__state.Saber ||
                blocker.m_shared.m_blockEffect == null ||
                PerfectBlockEffectField == null)
            {
                return;
            }

            if (BlockTimerField != null && PerfectBlockIntervalField != null)
            {
                float blockTimer = (float)BlockTimerField.GetValue(__instance);
                float perfectInterval = (float)PerfectBlockIntervalField.GetValue(__instance);
                __state.Perfect = blockTimer < perfectInterval;
            }
            __state.Original = PerfectBlockEffectField.GetValue(__instance) as EffectList;
            __state.Replaced = true;
            PerfectBlockEffectField.SetValue(__instance, blocker.m_shared.m_blockEffect);
        }

        private static void AfterBlockAttack(
            Humanoid __instance,
            bool __result,
            BlockEffectState __state)
        {
            if (__state != null && __state.Replaced && PerfectBlockEffectField != null)
            {
                PerfectBlockEffectField.SetValue(__instance, __state.Original);
            }
            if (__result && __state != null && __state.Saber && __state.Perfect)
            {
                _instance?.PlaySaberParrySound();
            }
        }


        private static bool IsSwordOfTheAncients(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null &&
                   item.m_dropPrefab.name.StartsWith("SwordOfTheAncients", StringComparison.Ordinal);
        }


        private static Material CreateClothMaterial(Material template, string name, Color color, float variation)
        {
            Material material = CreateSurfaceMaterial(template, name, Color.white, 0.02f, 0.24f);
            Texture2D texture = CreateClothTexture(name + "_Texture", color, variation);
            SetTexture(material, "_MainTex", texture);
            SetTexture(material, "_BaseMap", texture);
            SetColor(material, "_Color", Color.white);
            SetColor(material, "_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        private static Material CreateBundledClothMaterial(
            Material template,
            string name,
            Texture texture)
        {
            Material material = CreateSurfaceMaterial(template, name, Color.white, 0f, 0.18f);
            SetTexture(material, "_MainTex", texture);
            SetTexture(material, "_BaseMap", texture);
            SetColor(material, "_Color", Color.white);
            SetColor(material, "_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        private static Material CreateSurfaceMaterial(Material template, string name, Color color, float metallic, float smoothness)
        {
            Shader shader = template != null ? template.shader : Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("Could not resolve a runtime shader for " + name + ".");
            }
            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = name;
            material.color = color;
            SetColor(material, "_Color", color);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Glossiness", smoothness);
            SetFloat(material, "_Smoothness", smoothness);
            return material;
        }

        private static Texture2D CreateClothTexture(string name, Color baseColor, float variation)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float weave = ((x / 3 + y / 5) % 2 == 0 ? 1f : -1f) * variation;
                    float grain = Mathf.PerlinNoise(x * 0.19f, y * 0.23f) * variation - variation * 0.5f;
                    float multiplier = Mathf.Clamp01(1f + weave + grain);
                    pixels[y * size + x] = new Color(baseColor.r * multiplier, baseColor.g * multiplier, baseColor.b * multiplier, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Material CreateEnergyMaterial(Material template, string name, Color color, bool transparent)
        {
            Material material = CreateSurfaceMaterial(template, name, color, 0f, 1f);
            material.color = color;
            material.EnableKeyword("_EMISSION");
            SetColor(material, "_EmissionColor", new Color(color.r * 5f, color.g * 5f, color.b * 5f, color.a));

            if (transparent)
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }

            return material;
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

        private static GameObject LoadBundledPrefab(string name)
        {
            return LoadBundledAsset<GameObject>(name);
        }

        private static T LoadBundledAsset<T>(string name) where T : UnityEngine.Object
        {
            AssetBundle bundle = LoadAssetBundle();
            if (bundle == null)
            {
                return null;
            }

            T asset = bundle.LoadAsset<T>(name);
            if (asset != null)
            {
                return asset;
            }

            string matchingPath = bundle.GetAllAssetNames().FirstOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));
            return matchingPath != null ? bundle.LoadAsset<T>(matchingPath) : null;
        }

        private static AssetBundle LoadAssetBundle()
        {
            if (_assetBundle != null)
            {
                return _assetBundle;
            }

            string path = Path.Combine(Path.GetDirectoryName(_instance.Info.Location), "assets", BundleFileName);
            if (!File.Exists(path))
            {
                _instance.Logger.LogError("Missing Arcane Warriors asset bundle: " + path);
                return null;
            }

            _assetBundle = AssetBundle.LoadFromFile(path);
            if (_assetBundle == null)
            {
                _instance.Logger.LogError("Unity could not load Arcane Warriors asset bundle: " + path);
            }
            return _assetBundle;
        }

        private static readonly Dictionary<string, RequirementSpec[]> RecipeSpecs =
            new Dictionary<string, RequirementSpec[]>(StringComparer.Ordinal);

        private static void RegisterRecipes()
        {
            if (_recipesRegistered || ObjectDB.instance == null)
            {
                return;
            }

            try
            {
                CraftingStation station = GetGaldrTable();
                if (station == null)
                {
                    _instance.Logger.LogWarning("Could not resolve the Galdr Table. Arcane Warrior recipes were not added.");
                    return;
                }

                foreach (KeyValuePair<string, RequirementSpec[]> entry in RecipeSpecs)
                {
                    AddRecipe(entry.Key, station, entry.Value);
                }

                _recipesRegistered = true;
                _instance.Logger.LogInfo("Registered " + RecipeSpecs.Count + " Galdr Table recipes.");
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError(ex);
            }
        }

        private static CraftingStation GetGaldrTable()
        {
            Recipe mageRecipe = ObjectDB.instance.m_recipes.FirstOrDefault(recipe =>
                recipe != null && recipe.m_item != null && recipe.m_item.name == "HelmetMage" && recipe.m_craftingStation != null);
            if (mageRecipe != null)
            {
                return mageRecipe.m_craftingStation;
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_magetable") : null;
            return prefab != null ? prefab.GetComponent<CraftingStation>() : null;
        }

        private static void AddRecipe(string prefabName, CraftingStation station, RequirementSpec[] specs)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (item == null)
            {
                _instance.Logger.LogWarning("Missing item prefab for recipe: " + prefabName);
                return;
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + prefabName;
            recipe.m_item = item;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_repairStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = ToRequirements(specs);

            ObjectDB.instance.m_recipes.RemoveAll(existing => existing == null || existing.name == recipe.name);
            ObjectDB.instance.m_recipes.Add(recipe);
        }

        private static Piece.Requirement[] ToRequirements(IEnumerable<RequirementSpec> specs)
        {
            List<Piece.Requirement> requirements = new List<Piece.Requirement>();
            foreach (RequirementSpec spec in specs)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(spec.PrefabName);
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (item == null)
                {
                    _instance.Logger.LogWarning("Missing recipe resource '" + spec.PrefabName + "'.");
                    continue;
                }

                requirements.Add(new Piece.Requirement
                {
                    m_resItem = item,
                    m_amount = spec.Amount,
                    m_amountPerLevel = spec.AmountPerLevel,
                    m_recover = false
                });
            }
            return requirements.ToArray();
        }

        private static RequirementSpec Req(string prefabName, int amount, int amountPerLevel)
        {
            return new RequirementSpec(prefabName, amount, amountPerLevel);
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetTexture(Material material, string property, Texture value)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private sealed class RequirementSpec
        {
            public readonly string PrefabName;
            public readonly int Amount;
            public readonly int AmountPerLevel;

            public RequirementSpec(string prefabName, int amount, int amountPerLevel)
            {
                PrefabName = prefabName;
                Amount = amount;
                AmountPerLevel = amountPerLevel;
            }
        }
    }
}
