using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RunicLilyPads
{
    [BepInPlugin(Guid, "ArcaneDecor: WaterGardens", "1.0.2")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // Stable identity retained for upgrades from the original lily-pad test mod.
        public const string Guid = "chazman.RunicLilyPads";
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<float> MasterVolume;
        internal static BepInEx.Configuration.ConfigEntry<float> TallWaterfallHeight;
        static BepInEx.Configuration.ConfigEntry<float> DefaultWidth, DefaultDepth;
        internal static GardenSettings DefaultSettings()=>new GardenSettings { Width=DefaultWidth.Value,Depth=DefaultDepth.Value };
        Harmony harmony;
        void Awake()
        {
            Log=Logger;
            GardenLanguage.Load();
            TallWaterfallHeight=Config.Bind("Audio","TallWaterfallHeight",1.5f,new BepInEx.Configuration.ConfigDescription(global::Runic.Localization.RunicText.Get("text_30168a2408a8"),new BepInEx.Configuration.AcceptableValueRange<float>(.25f,10f)));
            MasterVolume=Config.Bind("Audio","MasterVolume",1f,new BepInEx.Configuration.ConfigDescription(global::Runic.Localization.RunicText.Get("text_46db60eb2268"),new BepInEx.Configuration.AcceptableValueRange<float>(0,1)));
            DefaultWidth=Config.Bind("Ponds","DefaultWidth",6f,new BepInEx.Configuration.ConfigDescription(global::Runic.Localization.RunicText.Get("text_11f0402a6233"),new BepInEx.Configuration.AcceptableValueRange<float>(4,24)));
            DefaultDepth=Config.Bind("Ponds","DefaultDepth",1.2f,new BepInEx.Configuration.ConfigDescription(global::Runic.Localization.RunicText.Get("text_8ab7fdf8f372"),new BepInEx.Configuration.AcceptableValueRange<float>(.4f,3)));
            ArcaneDecorWaterGardens.Input.ModalGameplayInput.IsOpen=()=>GardenEditor.IsOpen||FeatureEditor.IsOpen||GardenTreeEditor.IsOpen;
            gameObject.AddComponent<GardenEditor>();
            gameObject.AddComponent<FeatureEditor>();gameObject.AddComponent<GardenTreeEditor>();
            gameObject.AddComponent<PondNetwork>();
            harmony = new Harmony(Guid);
            harmony.PatchAll();
            PrefabManager.OnVanillaPrefabsAvailable += Register;
        }
        void OnDestroy()
        {
            GardenEditor.Close();
            FeatureEditor.Close();GardenTreeEditor.Close();
            ArcaneDecorWaterGardens.Input.ModalGameplayInput.Reset();
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            harmony?.UnpatchSelf();
        }
        void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            try { GardenPrefabs.RegisterAll(); } catch(Exception e) { Logger.LogError("Water garden registration: "+e); }
            GardenModels.RegisterLilies();
        }
    }

    public sealed class LilyFloat : MonoBehaviour
    {
        internal const float SurfaceClearance = 0.015f;
        Transform visual;
        void Awake() { visual = transform.Find("lily_visual"); }
        void LateUpdate()
        {
            if (!visual || !ZoneSystem.instance) return;
            float water = WaterAt(transform.position);
            if (water < -1000f) return;
            // Keep the saved/networked anchor fixed. Each peer animates only the visual and selection colliders.
            var p = visual.position;
            p.y = water + SurfaceClearance;
            visual.position = p;
            visual.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        }
        internal static float WaterAt(Vector3 p)
        {
            float garden=PondNetwork.WaterLevel(p);if(!float.IsNegativeInfinity(garden))return garden;
            // Probe inside the water volume, including when the placement ray starts above a wave.
            p.y -= 4f;
            return Floating.GetLiquidLevel(p, 1f, LiquidType.Water);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    static class WaterPlacement
    {
        static void Postfix(GameObject ___m_placementGhost,ref Player.PlacementStatus ___m_placementStatus)
        {
            if (!___m_placementGhost || !___m_placementGhost.activeSelf || !___m_placementGhost.GetComponent<LilyFloat>()) return;
            var p = ___m_placementGhost.transform.position;
            float water = LilyFloat.WaterAt(p);
            if (water < -1000f || (Heightmap.GetHeight(p,out var ground)&&ground>=water)) { ___m_placementStatus=Player.PlacementStatus.Invalid; ___m_placementGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(true); return; }
            p.y = water + LilyFloat.SurfaceClearance;
            ___m_placementGhost.transform.position = p;
        }
    }
}



