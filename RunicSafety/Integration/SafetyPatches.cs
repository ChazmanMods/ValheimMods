using HarmonyLib;

namespace RunicSafety.Integration
{
    [HarmonyPatch(typeof(Player), "RemovePiece", new System.Type[0])]
    internal static class PlayerRemovePiecePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizePieceRemoval(__instance);
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.SetText), new[] { typeof(string) })]
    [HarmonyAfter("chazman.RunicInteraction")]
    internal static class TeleportWorldSetTextPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TeleportWorld __instance, string text) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizePortalOverwrite(__instance, text);
    }

    [HarmonyPatch(typeof(Incinerator), "OnIncinerate",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class IncineratorOnIncineratePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(Incinerator __instance, Humanoid user) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeIncineratorClient(__instance, user);
    }

    [HarmonyPatch(typeof(Incinerator), "RPC_RequestIncinerate", new[] { typeof(long), typeof(long) })]
    internal static class IncineratorRequestPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Incinerator __instance, long uid)
        {
            if (!Plugin.RuntimeReady ||
                Plugin.CurrentRuntime.AuthorizeIncineratorOwner(__instance, uid)) return true;
            ValheimContracts.SendIncineratorFailure(__instance, uid);
            return false;
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnAddOre",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class SmelterAddOrePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(
            Smelter __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeSmelterOre(__instance, user, item);
    }

    [HarmonyPatch(typeof(Smelter), "OnAddFuel",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class SmelterAddFuelPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(
            Smelter __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeSmelterFuel(__instance, user, item);
    }

    [HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class CookingStationAddFuelPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory", "chazman.RunicProduction")]
        private static bool Prefix(
            CookingStation __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeCookingFuel(__instance, user, item);
    }

    [HarmonyPatch(typeof(CookingStation), "OnUseItem",
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class CookingStationUseItemPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicProduction")]
        private static bool Prefix(
            CookingStation __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeCookingFood(__instance, user, item);
    }

    [HarmonyPatch(typeof(Fermenter), "AddItem",
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class FermenterAddItemPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory", "chazman.RunicProduction")]
        private static bool Prefix(
            Fermenter __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeFermenter(__instance, user, item);
    }

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem),
        new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) })]
    internal static class ItemStandUseItemPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(
            ItemStand __instance,
            Humanoid user,
            ItemDrop.ItemData item) =>
            !Plugin.RuntimeReady || Plugin.CurrentRuntime.AuthorizeItemStand(__instance, user, item);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone), new System.Type[0])]
    internal static class PlayerCreateTombstonePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, out TombstoneAuditState __state)
        {
            __state = Plugin.RuntimeReady
                ? Plugin.CurrentRuntime.BeginTombstoneAudit(__instance)
                : default;
        }

        [HarmonyPostfix]
        private static void Postfix(Player __instance, TombstoneAuditState __state)
        {
            if (Plugin.RuntimeReady)
                Plugin.CurrentRuntime.CompleteTombstoneAudit(__instance, __state);
        }
    }
}
