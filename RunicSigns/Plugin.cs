using BepInEx;
using HarmonyLib;
using RunicSigns.Input;
using RunicSigns.Runtime;
using UnityEngine;

namespace RunicSigns;

[BepInPlugin(Guid, "RunicSigns", Version)]
[BepInIncompatibility("chazman.BetterSigns")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "chazman.RunicSigns";
    public const string Version = "1.0.3";
    private Harmony _harmony;
    internal static BepInEx.Logging.ManualLogSource Log;
    private void Awake()
    {
        Log = Logger;
        ModalGameplayInput.IsOpen = () => SignEditor.BlockGameplay;
        _harmony = new Harmony(Guid);
        _harmony.PatchAll();
        gameObject.AddComponent<SignEditor>();
        Logger.LogInfo("RunicSigns " + Version + " loaded: vanilla signs, synchronized labels and size 0.25–4x.");
    }
    private void OnDestroy()
    {
        SignEditor.Close();
        foreach (var runtime in Object.FindObjectsByType<SignRuntime>(FindObjectsSortMode.None)) Object.Destroy(runtime);
        _harmony?.UnpatchSelf();
        ModalGameplayInput.Reset();
    }
}
