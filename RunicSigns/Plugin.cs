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
    public const string Version = "1.2.7";
    private Harmony _harmony;
    internal static BepInEx.Logging.ManualLogSource Log;
    private void Awake()
    {
        Log = Logger;
        SignPlacement.Bind(Config);
        ModalGameplayInput.IsOpen = () => SignEditor.BlockGameplay;
        _harmony = new Harmony(Guid);
        _harmony.PatchAll();
        gameObject.AddComponent<SignEditor>();
        Logger.LogInfo("RunicSigns " + Version + " loaded: F8 live label editor, [ / ] sign sizing.");
    }
    private void OnDestroy()
    {
        SignEditor.Close();
        foreach (var preview in Object.FindObjectsByType<SignPlacementPreview>(FindObjectsSortMode.None)) Object.Destroy(preview);
        foreach (var runtime in Object.FindObjectsByType<SignRuntime>(FindObjectsSortMode.None)) Object.Destroy(runtime);
        _harmony?.UnpatchSelf();
        Runic.Shared.EmojiRenderer.Release();
        ModalGameplayInput.Reset();
    }
}
