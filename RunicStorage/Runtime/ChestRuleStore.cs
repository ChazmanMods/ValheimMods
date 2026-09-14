using System;
using System.Linq;
using HarmonyLib;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal static class ChestRuleStore
{
    internal const string Key = "RunicStorage.ChestRules.v1";
    internal static bool Eligible(Container chest) => chest && chest.GetComponentInParent<Piece>() &&
        !chest.GetComponentInParent<Ship>() && chest.m_wagon == null &&
        !chest.GetComponentInParent<ArmorStand>() && !chest.GetComponentInParent<ItemStand>();

    internal static string Raw(Container chest)
    {
        var view = ValheimContainerIdentity.NetworkView(chest);
        return view && view.IsValid() ? view.GetZDO().GetString(Key, "") : "";
    }
    internal static bool Read(Container chest, out ChestRules rules) => ChestRules.TryDecode(Raw(chest), out rules);

    internal static bool CanEdit(Container chest)
    {
        var player = Player.m_localPlayer;
        return (PluginConfig.Enabled?.Value ?? false) && Eligible(chest) && player && player.IsOwner() && !player.IsDead() &&
            Vector3.Distance(player.transform.position, chest.transform.position) <= 5f &&
            ValheimContainerService.CanDiscover(chest, player.GetPlayerID(), true, true) &&
            StorageContainerAuthority.TryGetExactOpenedLocalOwnerInventory(chest, player, out _);
    }

    internal static bool Save(Container chest, ChestRules rules, string expected, out string error)
    {
        error = "Chest access changed. Close and reopen its rules.";
        if (!CanEdit(chest)) return false;
        var view = ValheimContainerIdentity.NetworkView(chest);
        if (!view || !view.IsOwner() || Raw(chest) != expected) return false;
        try
        {
            // One bounded ZDO value keeps label and routing changes together.
            string encoded = rules.Encode();
            view.GetZDO().Set(Key, encoded);
            error = "Saved.";
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    internal static void Learn(Container chest)
    {
        if (!(PluginConfig.Enabled?.Value ?? false) || !Eligible(chest)) return;
        var view = ValheimContainerIdentity.NetworkView(chest);
        if (!view || !view.IsValid() || !view.IsOwner() || !Read(chest, out var rules) || !rules.Remember) return;
        string previous = Raw(chest);
        rules.Learn(chest.GetInventory().GetAllItems().Select(ValheimContainerIdentity.ResourceId));
        string updated = rules.Encode();
        if (previous != updated) view.GetZDO().Set(Key, updated);
    }

    internal static void Attach(Container chest)
    {
        if (Eligible(chest) && !chest.GetComponent<ChestExteriorLabel>()) chest.gameObject.AddComponent<ChestExteriorLabel>();
    }
}

[HarmonyPatch(typeof(Container), "Save")]
internal static class ChestRememberSavePatch
{
    private static void Postfix(Container __instance)
    {
        try
        {
            // Native inventory callbacks also run during a transfer that may roll back.
            // Learn from its settled contents on the next frame, not an intermediate insertion.
            ChestRuleStore.Attach(__instance);
            var label = __instance.GetComponent<ChestExteriorLabel>();
            if (label) label.RequestLearning();
        }
        catch (Exception ex) { Plugin.Log?.LogWarning("Chest memory update skipped: " + ex.Message); }
    }
}
