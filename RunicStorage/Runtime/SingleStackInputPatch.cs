using System;
using BepInEx.Configuration;
using HarmonyLib;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

[HarmonyPatch(typeof(InventoryGui), "OnSelectedItem",
    new[] { typeof(InventoryGrid), typeof(ItemDrop.ItemData), typeof(Vector2i), typeof(InventoryGrid.Modifier) })]
internal static class SingleStackInputPatch
{
    // Run after other mods' guards. Once this chord owns a click, never fall through
    // to vanilla transfer/split, even when nothing could be deposited.
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1, bool __runOriginal)
    {
        if (!__runOriginal || Plugin.ActiveActions == null ||
            !(PluginConfig.Enabled?.Value ?? false) || !Input.GetMouseButton(0) ||
            !Held(PluginConfig.QuickStackSingleModifier.Value)) return true;

        Player player = Player.m_localPlayer;
        if (player == null || __0 == null || __1 == null ||
            !ReferenceEquals(__0.GetInventory(), player.GetInventory())) return true;

        try
        {
            if (Plugin.SearchPanelOpen) return false;
            var decision = StorageActionRouter.Route(
                new StorageActionRequest(StorageActionKind.QuickStackSingle, StorageInputOrigin.Keyboard),
                Plugin.CaptureRouteContext());
            if (decision.Outcome == StorageRouteOutcome.Execute)
                Plugin.ActiveActions.QuickStackSingle(player.GetInventory(), __1);
            else
            {
                string feedback = StorageActionDiagnostics.RouteFeedback(decision.Reason);
                if (feedback.Length != 0) player.Message(MessageHud.MessageType.Center, feedback);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError("Single-stack Quick Stack stopped: " + ex);
            player.Message(MessageHud.MessageType.Center,
                global::Runic.Localization.RunicText.Get("text_02145dcee5cc"));
        }
        return false;
    }

    private static bool Held(KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None || !KeyHeld(shortcut.MainKey)) return false;
        foreach (KeyCode modifier in shortcut.Modifiers)
            if (!KeyHeld(modifier)) return false;
        return true;
    }

    private static bool KeyHeld(KeyCode key) => key switch
    {
        KeyCode.LeftControl or KeyCode.RightControl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
        KeyCode.LeftShift or KeyCode.RightShift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
        KeyCode.LeftAlt or KeyCode.RightAlt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
        _ => Input.GetKey(key)
    };
}
