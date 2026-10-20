using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace CompanionForge
{
    [BepInPlugin("local.companionforge.runtime", "Companion Forge", "1.2.7")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static readonly Dictionary<string, Pack> Packs = new Dictionary<string, Pack>();
        private readonly List<AssetBundle> bundles = new List<AssetBundle>();
        private ConfigEntry<KeyboardShortcut> summon;
        private ConfigEntry<string> selected;
        private ConfigEntry<KeyboardShortcut> poseKey;
        internal static ConfigEntry<float> ChestBounceAmount;
        private Harmony harmony;
        private CompanionAnimationMenu menu;
        private static readonly Func<Player, bool> CanTakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        private void Awake()
        {
            summon = Config.Bind("Controls", "Summon", new KeyboardShortcut(KeyCode.F8), "Summon the selected companion beside you.");
            poseKey = Config.Bind("Controls", "Pose", new KeyboardShortcut(KeyCode.J, KeyCode.LeftAlt), "Open the animation menu for your nearby following companion.");
            // Migrate only our former default: Alt+C is Runic Storage's consolidate shortcut.
            if (poseKey.Value.Equals(new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt)))
                poseKey.Value = new KeyboardShortcut(KeyCode.J, KeyCode.LeftAlt);
            menu = gameObject.AddComponent<CompanionAnimationMenu>();
            selected = Config.Bind("Companion", "Selected ID", "", "Stable ID of the companion to summon. Empty selects the first pack.");
            ChestBounceAmount = Config.Bind("Appearance", "Chest bounce multiplier", 1f,
                new ConfigDescription("Adjust chest bounce and hot-tub float for companions rigged with chest markers. 0 disables it. The authored maximum travel still applies.", new AcceptableValueRange<float>(0f, 2f)));
            // Disk work and bundle loading happen once at startup, never on an interaction or AI tick.
            var paths = Directory.GetFiles(Paths.PluginPath, "companion.json", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (!Path.GetFileName(Path.GetDirectoryName(path)).StartsWith("CompanionForge_", StringComparison.Ordinal)) continue;
                try
                {
                    var pack = Pack.Read(File.ReadAllText(path));
                    if (pack == null || !pack.adult || string.IsNullOrEmpty(pack.id) || pack.prefab != "CF_" + pack.id)
                        throw new Exception("Invalid adult companion pack metadata.");
                    if (Packs.ContainsKey(pack.prefab)) throw new Exception("Duplicate companion ID: " + pack.id);
                    var bundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(path), "companionassets"));
                    if (!bundle) throw new Exception("Character bundle could not be loaded.");
                    pack.visual = bundle.LoadAsset<GameObject>(pack.bundlePrefab);
                    if (!pack.visual) { bundle.Unload(true); throw new Exception("Character visual prefab is missing from the bundle."); }
                    bundles.Add(bundle);
                    Packs.Add(pack.prefab, pack);
                }
                catch (Exception e) { Logger.LogError("Could not load " + path + ": " + e.Message); }
            }
            CreatureManager.OnVanillaCreaturesAvailable += Register;
            harmony = new Harmony("local.companionforge.runtime");
            harmony.PatchAll();
        }

        private void Register()
        {
            CreatureManager.OnVanillaCreaturesAvailable -= Register;
            foreach (var pair in Packs)
            {
                try
                {
                    var pack = pair.Value;
                    var creature = new CustomCreature(pack.prefab, "Dverger", new CreatureConfig
                    { Name = pack.name, Faction = Character.Faction.Players, Group = "CompanionForge" });
                    var prefab = creature.Prefab;
                    var tame = prefab.GetComponent<Tameable>() ?? prefab.AddComponent<Tameable>();
                    tame.m_commandable = true;
                    tame.m_startsTamed = true;
                    // Strip rewards: summoning a companion must never be a source of free loot.
                    var drops = prefab.GetComponent<CharacterDrop>();
                    if (drops != null) drops.m_drops.Clear();
                    // Do not reveal the template creature when this custom character dies.
                    var nativeCharacter = prefab.GetComponent<Character>();
                    nativeCharacter.m_deathEffects = new EffectList { m_effectPrefabs =
                        Array.FindAll(nativeCharacter.m_deathEffects.m_effectPrefabs,
                            effect => effect.m_prefab == null || effect.m_prefab.GetComponent<Ragdoll>() == null) };
                    var chatter = prefab.GetComponent<NpcTalk>();
                    if (chatter != null) chatter.enabled = false;
                    var child = Instantiate(pack.visual, prefab.transform);
                    child.name = "CompanionForgeVisual";
                    prefab.AddComponent<CompanionVisual>();
                    prefab.AddComponent<CompanionBed>();
                    prefab.AddComponent<CompanionPose>();
                    prefab.AddComponent<CompanionStairs>();
                    // Match an upright humanoid body rather than retaining the short,
                    // wide Dverger capsule, particularly between steep stair rails.
                    var capsule = prefab.GetComponent<CapsuleCollider>();
                    if (capsule != null) { capsule.radius=.3f; capsule.height=1.8f; capsule.center=new Vector3(0,.9f,0); }
                    prefab.GetComponent<Character>().m_name = pack.name;
                    prefab.GetComponent<Character>().m_speed = 1.4f;
                    prefab.GetComponent<Character>().m_walkSpeed = 1.4f;
                    prefab.GetComponent<Character>().m_runSpeed = 3.8f;
                    if (!CreatureManager.Instance.AddCreature(creature)) throw new Exception("Jötunn rejected creature registration.");
                    Logger.LogInfo("Registered companion " + pack.name + " (" + pack.id + ") with " + (pack.animationMenu?.Length ?? 0) + " authored animations.");
                }
                catch (Exception e) { Logger.LogError("Could not register " + pair.Key + ": " + e); }
            }
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (menu != null && menu.IsOpen) { if (poseKey.Value.IsDown()) menu.Close(); return; }
            if (player == null || ZNetScene.instance == null || !CanTakeInput(player)) return;
            if (poseKey.Value.IsDown()) { menu.Open(player); return; }
            if (!summon.Value.IsDown()) return;
            Pack pack = null;
            foreach (var candidate in Packs.Values)
                if (string.IsNullOrEmpty(selected.Value) || candidate.id == selected.Value) { pack = candidate; break; }
            if (pack == null) { player.Message(MessageHud.MessageType.Center, Packs.Count == 0 ? "No companion pack loaded. Check the mod installation." : "Select an installed companion ID in the configuration"); return; }
            var prefab = ZNetScene.instance.GetPrefab(pack.prefab);
            if (prefab == null) return;
            var spawn = player.transform.position + player.transform.forward * 2f;
            var instance = Instantiate(prefab, spawn, player.transform.rotation);
            var character = instance.GetComponent<Character>();
            character.SetTamed(true);
            // Vanilla's tamed command flow retains its normal ownership/RPC behavior.
            instance.GetComponent<Tameable>().Command(player, false);
            player.Message(MessageHud.MessageType.Center, pack.name + " joins you");
        }

        private void OnDestroy()
        {
            if (menu != null) menu.Close();
            CreatureManager.OnVanillaCreaturesAvailable -= Register;
            harmony?.UnpatchSelf();
            foreach (var bundle in bundles) bundle.Unload(false);
            Packs.Clear();
        }
    }

    public sealed class CompanionVisual : MonoBehaviour
    {
        internal Pack Pack;
        private Character character;
        private Humanoid humanoid;
        private Animator animator;
        private bool lastAttack;
        private static readonly int Speed = Animator.StringToHash("CF_Speed");
        private static readonly int Attack = Animator.StringToHash("CF_Attack");
        private static readonly int Wave = Animator.StringToHash("CF_Wave");
        private static readonly int Rate = Animator.StringToHash("CF_Rate");
        private bool hasRate;
        private CompanionBed rest;
        private CompanionPose pose;

        private void Awake()
        {
            character = GetComponent<Character>();
            humanoid = GetComponent<Humanoid>();
            var key = gameObject.name.Replace("(Clone)", "");
            Plugin.Packs.TryGetValue(key, out Pack);
            if (Pack != null) character.m_name = Pack.name;
            var visual = transform.Find("CompanionForgeVisual");
            if (visual == null) return;
            animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null) hasRate = Array.Exists(animator.parameters, p => p.nameHash == Rate);
            // Keep the native animator and attachment skeleton running for Valheim's combat timing.
            // Disable only this clone's native renderers; vanilla creatures and player equipment are untouched.
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(visual)) renderer.enabled = false;
            var equipment = GetComponent<VisEquipment>();
            if (equipment != null) equipment.enabled = false;
        }

        private void Start()
        {
            rest = GetComponent<CompanionBed>();
            pose = GetComponent<CompanionPose>();
            if (animator != null && animator.isHuman && rest != null)
            {
                var gaze = animator.GetComponent<CompanionTubGaze>() ?? animator.gameObject.AddComponent<CompanionTubGaze>();
                gaze.Configure(rest);
            }
            var motionVisual = transform.Find("CompanionForgeVisual");
            if (motionVisual != null && Pack?.secondaryMotion != null && Pack.secondaryMotion.enabled)
            {
                var motion = motionVisual.GetComponent<CompanionSecondaryMotion>() ?? motionVisual.gameObject.AddComponent<CompanionSecondaryMotion>();
                motion.Configure(Pack.secondaryMotion, rest);
            }
            // Gear objects can be created by Humanoid.Awake after our Awake; hide them once, after initialization.
            var visual = transform.Find("CompanionForgeVisual");
            if (visual == null) return;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(visual)) renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (animator == null || character == null) return;
            if ((rest != null && rest.Resting) || (pose != null && pose.Performing)) return;
            var velocity = character.GetVelocity();
            velocity.y = 0;
            float speed = velocity.sqrMagnitude < .0144f ? 0f : velocity.magnitude;
            animator.SetFloat(Speed, speed, .10f, Time.deltaTime);
            if (hasRate) animator.SetFloat(Rate, speed < .08f ? 1f : Mathf.Clamp(speed / (speed < 1.4f ? 1.4f : speed > 3.8f ? 3.8f : speed), .25f, 1.75f));
            bool attacking = humanoid != null && humanoid.InAttack();
            if (attacking != lastAttack) { animator.SetBool(Attack, attacking); lastAttack = attacking; }
        }

        internal void Say(Humanoid user, bool affectionate)
        {
            if (Pack == null || !(user is Player player)) return;
            player.Message(MessageHud.MessageType.Center, character.GetHoverName() + ": " + (affectionate ? Pack.affection : Pack.greeting));
            if (animator != null) animator.SetTrigger(Wave);
        }
    }

    [HarmonyPatch(typeof(Tameable), "Interact")]
    static class TalkPatch
    {
        private static readonly Action<Tameable> Rename = AccessTools.MethodDelegate<Action<Tameable>>(AccessTools.Method(typeof(Tameable), "SetName"));
        static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            var visual = __instance.GetComponent<CompanionVisual>();
            if (visual == null || hold) return true;
            if (alt) return true; // Preserve Valheim's own rename modifier and controller mapping.
            if (Input.GetKey(KeyCode.LeftAlt)) { Rename(__instance); __result = true; return false; }
            if (!Input.GetKey(KeyCode.LeftControl)) return true;
            visual.Say(user, false);
            __result = true;
            return false;
        }
    }
}
