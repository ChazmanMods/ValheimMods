using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicCrafting.Domain;
using TMPro;
using UnityEngine;

namespace RunicCrafting.Integration
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance)
        {
            try { ContainerSpatialIndex.Register(__instance); }
            catch (Exception exception) { Plugin.Log?.LogWarning("Could not index a container: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class ContainerDestroyedPatch
    {
        private static void Postfix(Container __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try { ContainerSpatialIndex.Unregister(__instance); }
            catch (Exception exception) { Plugin.Log?.LogWarning("Could not remove a container index entry: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class ContainerCheckForChangesPatch
    {
        private static void Postfix(Container __instance)
        {
            try { ContainerSpatialIndex.Register(__instance); }
            catch (Exception exception) { Plugin.Log?.LogWarning("Could not refresh a container index entry: " + exception.Message); }
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
    internal static class CraftingStationInteractPatch
    {
        private static bool Prefix(CraftingStation __instance, Humanoid user, ref bool __result)
        {
            try
            {
                if (!(user is Player player) || !ValheimReflection.CanMutateLocalPlayer(player)) return true;
                if (CraftingRuntime.CanUseStation(__instance, player, out string reason)) return true;
                player.Message(MessageHud.MessageType.Center, "Station use denied: " + reason);
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Workshop Access failed closed for this station interaction: " + exception);
                if (user is Player player && ValheimReflection.CanMutateLocalPlayer(player) &&
                    Configuration.Enabled.Value && CraftingRuntime.IsInitialized)
                {
                    player.Message(MessageHud.MessageType.Center, "Station permissions are temporarily unavailable");
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        private static void Postfix(CraftingStation __instance, Humanoid user, bool repeat, bool __result)
        {
            if (repeat || !__result || !(user is Player player)) return;
            try
            {
                if (!ReferenceEquals(player.GetCurrentCraftingStation(), __instance)) return;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not verify the crafting station interaction: " + exception.Message);
                return;
            }
            try { CraftingRuntime.ShowStationStatus(__instance, player); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not show the crafting station status: " + exception.Message);
            }
            try { RepairAllRuntime.TryHandleStationOpen(__instance, player); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not complete the crafting station repair follow-up: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Repair))]
    internal static class WearNTearRepairPatch
    {
        private static void Postfix(WearNTear __instance, bool __result)
        {
            try
            {
                if (__result) AreaRepairRuntime.OnVanillaHammerRepair(__instance);
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not start area repair after the hammer repair: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class InventoryGuiDoCraftingPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicInventory")]
        private static bool Prefix(
            InventoryGui __instance,
            Player player,
            Recipe ___m_craftRecipe,
            ItemDrop.ItemData ___m_craftUpgradeItem,
            bool ___m_multiCrafting,
            int ___m_multiCraftAmount)
        {
            try
            {
                return CraftingRuntime.BeforeCraft(
                    __instance,
                    player,
                    ___m_craftRecipe,
                    ___m_craftUpgradeItem,
                    ___m_multiCrafting,
                    ___m_multiCraftAmount);
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Craft preparation failed closed before output creation: " + exception);
                CraftingRuntime.FinishCraft(exception);
                return false;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            CraftingRuntime.FinishCraft(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem),
        new[] { typeof(GameObject), typeof(int) })]
    internal static class InventoryCanAddCraftOutputPatch
    {
        private static void Postfix(
            Inventory __instance,
            GameObject prefab,
            ref bool __result)
        {
            try { __result = CraftingRuntime.AfterCraftCapacityCheck(__instance, prefab, __result); }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Craft reservation failed closed at the vanilla capacity check: " + exception);
                CraftingRuntime.CancelPendingCraft();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem),
        new[]
        {
            typeof(string), typeof(int), typeof(int), typeof(int), typeof(long),
            typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool)
        })]
    internal static class InventoryAddCraftOutputPatch
    {
        private static void Postfix(
            Inventory __instance,
            ItemDrop.ItemData __result)
        {
            try { CraftingRuntime.CraftOutputAdded(__instance, __result); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not record the completed craft output: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "HaveRequirementItems",
        new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
    internal static class PlayerHaveRequirementItemsPatch
    {
        private static bool Prefix(
            Player __instance,
            Recipe piece,
            bool discover,
            int qualityLevel,
            int amount,
            ref bool __result) =>
            CraftingRuntime.BeforeHaveRecipeRequirements(
                __instance,
                piece,
                discover,
                qualityLevel,
                amount,
                ref __result);

        private static void Postfix(
            Player __instance,
            Recipe piece,
            bool discover,
            int qualityLevel,
            int amount,
            ref bool __result)
        {
            try
            {
                CraftingRuntime.AddNearbyRecipeAvailability(
                    __instance,
                    piece,
                    discover,
                    qualityLevel,
                    amount,
                    ref __result);
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Nearby recipe preview was disabled for this check: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements),
        new[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class PlayerHavePieceRequirementsPatch
    {
        private static void Postfix(
            Player __instance,
            Piece piece,
            Player.RequirementMode mode,
            ref bool __result)
        {
            try { CraftingRuntime.AddNearbyPieceAvailability(__instance, piece, mode, ref __result); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Nearby build preview was disabled for this check: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources),
        new[] { typeof(Piece.Requirement[]), typeof(int), typeof(int), typeof(int) })]
    internal static class PlayerConsumeResourcesPatch
    {
        private static bool Prefix(
            Player __instance,
            Piece.Requirement[] requirements,
            int qualityLevel,
            int itemQuality,
            int multiplier) =>
            CraftingRuntime.BeforeVanillaConsume(
                __instance,
                requirements,
                qualityLevel,
                itemQuality,
                multiplier);
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class PlayerUpdatePlacementPatch
    {
        private static void Prefix() => CraftingRuntime.BeginPlacementScope();

        private static Exception Finalizer(Exception __exception)
        {
            CraftingRuntime.FinishPlacementScope(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(Player),
        "PlacePiece",
        new[]
        {
            typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool)
        })]
    internal static class PlayerPlacePieceInstantiationPatch
    {
        private static readonly MethodInfo PlacementCreated =
            typeof(CraftingRuntime).GetMethod(
                nameof(CraftingRuntime.PlacementObjectInstantiated),
                BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(
                typeof(CraftingRuntime).FullName,
                nameof(CraftingRuntime.PlacementObjectInstantiated));

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int index = 0; index < codes.Count - 1; index++)
            {
                if (!IsExactGameObjectInstantiate(codes[index].operand as MethodInfo) ||
                    codes[index + 1].opcode != OpCodes.Stloc_0) continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Player.PlacePiece contains more than one exact placement Instantiate call.");
                match = index;
            }
            if (match < 0)
                throw new MissingMethodException(
                    "Player.PlacePiece no longer contains the audited GameObject Instantiate -> stloc.0 boundary.");

            codes.InsertRange(match + 1, new[]
            {
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, PlacementCreated)
            });
            return codes;
        }

        private static bool IsExactGameObjectInstantiate(MethodInfo method)
        {
            if (method == null || !method.IsGenericMethod ||
                method.DeclaringType != typeof(UnityEngine.Object) ||
                !string.Equals(method.Name, nameof(UnityEngine.Object.Instantiate), StringComparison.Ordinal))
                return false;
            Type[] arguments = method.GetGenericArguments();
            ParameterInfo[] parameters = method.GetParameters();
            return arguments.Length == 1 && arguments[0] == typeof(GameObject) &&
                   method.ReturnType == typeof(GameObject) &&
                   parameters.Select(parameter => parameter.ParameterType).SequenceEqual(
                       new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) });
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class PlayerTryPlacePiecePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            try
            {
                if (CraftingRuntime.BeforePlacePiece(__instance, piece, out string reason)) return true;
                __instance.Message(
                    MessageHud.MessageType.Center, "Nearby building cancelled: " + reason);
                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Build reservation failed closed before placement: " + exception);
                __result = false;
                return false;
            }
        }

        private static void Postfix(bool __result) => CraftingRuntime.PlacePieceReturned(__result);
    }

    [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
    internal static class InventoryGuiRepairOneItemPatch
    {
        private static bool Prefix(InventoryGui __instance)
        {
            try { return !RepairAllRuntime.TryHandle(__instance); }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Repair All failed closed on the authoritative process: " + exception);
                Player player = Player.m_localPlayer;
                if (player != null && ReferenceEquals(player, Player.m_localPlayer) &&
                    player.IsOwner() && Configuration.Enabled.Value &&
                    CraftingRuntime.IsInitialized)
                {
                    player.Message(MessageHud.MessageType.Center, "Repair is temporarily unavailable");
                    return false;
                }
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirement")]
    internal static class InventoryGuiSetupRequirementPatch
    {
        private static void Postfix(
            Transform elementRoot,
            Piece.Requirement req,
            Player player,
            bool craft,
            int quality,
            int craftMultiplier)
        {
            try
            {
                bool available = craft
                    ? CraftingRuntime.TryGetAvailabilityBreakdown(
                        player,
                        CraftingRequirementUiContext.CurrentRecipe,
                        req,
                        quality,
                        craftMultiplier,
                        out int carried,
                        out int nearby,
                        out int required,
                        out NearbyCraftingFeatureState state)
                    : CraftingRuntime.TryGetBuildAvailabilityBreakdown(
                        player,
                        CraftingRequirementUiContext.CurrentPiece,
                        req,
                        out carried,
                        out nearby,
                        out required,
                        out state);
                if (!available) return;
                Transform amountRoot = elementRoot.Find("res_amount");
                TMP_Text amountText = amountRoot != null ? amountRoot.GetComponent<TMP_Text>() : null;
                if (amountText == null) return;
                amountText.text = CraftingRequirementDisplay.FormatCompactAmount(
                    required,
                    carried,
                    nearby,
                    state);
                if (CraftingRequirementDisplay.CombinedTotalSatisfies(
                        required,
                        carried,
                        nearby,
                        state)) amountText.color = Color.white;

                UITooltip tooltip = elementRoot.GetComponent<UITooltip>();
                if (tooltip != null)
                {
                    string existing = tooltip.m_text ?? string.Empty;
                    string breakdown = CraftingRequirementDisplay.FormatTooltipBreakdown(
                        required,
                        carried,
                        nearby,
                        state);
                    tooltip.m_text = existing.Length == 0
                        ? breakdown
                        : existing + "\n" + breakdown;
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not add carried/nearby requirement counts: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "SetupPieceInfo", new[] { typeof(Piece) })]
    internal static class HudSetupPieceInfoPatch
    {
        private static void Prefix(Piece piece, out Piece __state) =>
            __state = CraftingRequirementUiContext.PushPiece(piece);

        private static void Postfix(Piece __state) =>
            CraftingRequirementUiContext.RestorePiece(__state);

        private static Exception Finalizer(Exception __exception, Piece __state)
        {
            CraftingRequirementUiContext.RestorePiece(__state);
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(InventoryGui),
        "SetupRequirementList",
        new[] { typeof(int), typeof(Player), typeof(bool), typeof(int) })]
    internal static class InventoryGuiSetupRequirementListPatch
    {
        private static void Prefix(InventoryGui __instance, out Recipe __state) =>
            __state = CraftingRequirementUiContext.Push(__instance);

        private static void Postfix(Recipe __state) =>
            CraftingRequirementUiContext.Restore(__state);

        private static Exception Finalizer(Exception __exception, Recipe __state)
        {
            CraftingRequirementUiContext.Restore(__state);
            return __exception;
        }
    }
}
