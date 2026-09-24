using System;
using BepInEx.Configuration;
using HarmonyLib;
using RunicSigns.Core;
using UnityEngine;

namespace RunicSigns.Runtime;

internal static class SignPlacement
{
    private static ConfigEntry<float> _scale;
    private static ConfigEntry<KeyboardShortcut> _larger, _smaller, _edit;
    private static SignSettings _draft = new() { OffsetUnit = 1 };
    internal static string Caption { get; private set; }
    internal static bool HasCaption => Caption != null;
    internal static SignSettings CurrentSettings { get { var s = _draft.Clone(); s.Scale = CurrentScale; return s; } }
    internal static void UseDraft(SignSettings settings, string caption) { _draft = settings.Clone(); _scale.Value = settings.Scale; Caption = caption; }
    private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(
        AccessTools.Method(typeof(Player), "TakeInput"));
    private static int _inputFrame = -1;

    internal static void Bind(ConfigFile config)
    {
        _draft = new SignSettings { OffsetUnit = 1 }; Caption = null;
        _edit = config.Bind("Placement", "EditLabel", new KeyboardShortcut(KeyCode.F8), global::Runic.Localization.RunicText.Get("text_e5b77760801e"));
        _scale = config.Bind("Placement", "SignScale", 1f,
            new ConfigDescription(global::Runic.Localization.RunicText.Get("text_5a4c2c8da1a5"),
                new AcceptableValueRange<float>(.25f, 4f)));
        _larger = config.Bind("Placement", "IncreaseSize", new KeyboardShortcut(KeyCode.RightBracket),
            global::Runic.Localization.RunicText.Get("text_b2d33be175cf"));
        _smaller = config.Bind("Placement", "DecreaseSize", new KeyboardShortcut(KeyCode.LeftBracket),
            global::Runic.Localization.RunicText.Get("text_7c429c089d54"));
    }

    internal static float CurrentScale => SignSettings.InRange(_scale?.Value ?? 1f, .25f, 4f) ? _scale?.Value ?? 1f : 1f;

    internal static bool IsLocalPlacement(Sign sign) => Player.m_localPlayer &&
        Player.m_localPlayer.IsOwner() && sign.GetComponent<Piece>() &&
        sign.GetComponent<Piece>().GetCreator() == Player.m_localPlayer.GetPlayerID();

    internal static void Preview(Player player, GameObject ghost, bool readInput)
    {
        if (!player || player != Player.m_localPlayer || !ghost || !ghost.GetComponent<Sign>() ||
            Utils.GetPrefabName(ghost) != "sign") return;
        // Never change a live world object if another mod supplies an unexpected ghost.
        var view = ghost.GetComponent<ZNetView>();
        if (view && view.IsValid()) return;
        var preview = ghost.GetComponent<SignPlacementPreview>() ?? ghost.AddComponent<SignPlacementPreview>();
        if (readInput && _inputFrame != Time.frameCount && !SignEditor.BlockGameplay && TakeInput(player))
        {
            _inputFrame = Time.frameCount;
            if (_edit.Value.IsDown()) { SignEditor.OpenPlacement(ghost.GetComponent<Sign>()); return; }
            bool larger = _larger.Value.IsDown(), smaller = _smaller.Value.IsDown();
            if (larger != smaller) _scale.Value = Mathf.Clamp(CurrentScale + (larger ? .25f : -.25f), .25f, 4f);
        }
        if (!SignEditor.IsPlacementTarget(ghost)) preview.Apply(CurrentSettings, Caption, player);
    }
}

internal sealed class SignPlacementPreview : MonoBehaviour
{
    private SignAppearance _appearance;
    private float _shownScale = -1;
    private string _shown;
    private void Awake() { _appearance = new SignAppearance(transform, GetComponent<Sign>().m_textWidget); }
    internal void Apply(SignSettings settings, string caption, Player player)
    {
        string key = settings.Encode() + "\n" + caption;
        if (key != _shown) { _shown = key; _appearance.Apply(settings, caption); }
        if (_shownScale == settings.Scale) return;
        _shownScale = settings.Scale;
        player.Message(MessageHud.MessageType.TopLeft, global::Runic.Localization.RunicText.Format("text_1c71591837d8", settings.Scale));
    }
    internal void ShowDraft(SignSettings settings, string caption) { _shown = null; _appearance.Apply(settings, caption); }
    private void OnDestroy() => _appearance?.Dispose();
}

[HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
internal static class SignPlacementSetupPatch
{
    private static void Postfix(Player __instance, GameObject ___m_placementGhost) =>
        SignPlacement.Preview(__instance, ___m_placementGhost, false);
}

[HarmonyPatch(typeof(Player), "UpdatePlacementGhost", typeof(bool))]
internal static class SignPlacementUpdatePatch
{
    // Set geometry before vanilla evaluates snapping, contact offsets and placement validity.
    private static bool Prefix(Player __instance, GameObject ___m_placementGhost)
    {
        SignPlacement.Preview(__instance, ___m_placementGhost, true);
        // Freeze the ghost at its last aimed position while the mouse operates the editor.
        return !SignEditor.IsPlacementTarget(___m_placementGhost);
    }
}
