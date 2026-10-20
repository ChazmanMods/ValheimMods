using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace ArcaneDecorFlowCraft;

[BepInPlugin(Guid,"ArcaneDecor: FlowCraft","0.3.1")]
[BepInDependency(Jotunn.Main.ModGuid)]
[BepInIncompatibility("chazman.ArcaneWaterWorks")]
public sealed class Plugin:BaseUnityPlugin
{
    public const string Guid="chazman.ArcaneDecorFlowCraft";
    internal static Material Water;
    internal static ConfigEntry<KeyboardShortcut> PipeTilt,PipeRoll;
    Harmony harmony;
    void Awake(){
        PipeTilt=Config.Bind("Plumbing","TiltPipe",new KeyboardShortcut(KeyCode.PageUp),"Tilt the pipe placement preview by 45 degrees.");
        PipeRoll=Config.Bind("Plumbing","RollPipe",new KeyboardShortcut(KeyCode.PageDown),"Roll the pipe placement preview by 90 degrees.");
        gameObject.AddComponent<Plumbing.PipeNetwork>();
        gameObject.AddComponent<Plumbing.FountainTriggers>();
        harmony=new Harmony(Guid);harmony.PatchAll();
        PrefabManager.OnVanillaPrefabsAvailable+=Register;
    }
    void Register(){
        PrefabManager.OnVanillaPrefabsAvailable-=Register;
        try{
            Water=PrefabManager.Instance.GetPrefab("WaterCube_cave").GetComponentInChildren<WaterVolume>(true).m_waterSurface.sharedMaterial;
            Plumbing.PipePrefabs.Register();
            Plumbing.SyncFountain.Register();
            Logger.LogInfo("ArcaneDecor: FlowCraft 0.3.1: eighteen plumbing pieces, pulse fountain and twelve pipe recipes registered.");
        }catch(Exception ex){Logger.LogError("FlowCraft registration failed: "+ex);}
    }
    void OnDestroy(){PrefabManager.OnVanillaPrefabsAvailable-=Register;harmony?.UnpatchSelf();}
}
