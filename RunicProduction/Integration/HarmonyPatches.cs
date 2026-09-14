using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicProduction.Integration
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ProductionHandoffContainerPatch
    {
        private static void Postfix(Container __instance)
        {
            try { ProductionChestHandoff.Register(__instance); }
            catch (Exception exception) { ProductionRuntime.FailHook("Container handoff registration", exception); }
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class ProductionHandoffWorldExitPatch
    {
        private static void Postfix() => ProductionChestHandoff.Clear();
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class ProductionPlayerUpdateInputPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicBuildCamera")]
        private static void Prefix(Player __instance)
        {
            try { ProductionRuntime.TickLocalPlayer(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Player.Update link input", exception);
            }
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class ProductionConsumedButtonPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!ProductionLinkInput.ShouldSuppress(name, Time.frameCount)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class ProductionConsumedButtonDownPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!ProductionLinkInput.ShouldSuppress(name, Time.frameCount)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ProductionSetControlsInputPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(Player),
            "SetControls",
            new[]
            {
                typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool)
            });

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicBuildCamera", "chazman.RunicInventory")]
        private static void Prefix(
            Player __instance,
            ref bool attack,
            ref bool attackHold,
            ref bool secondaryAttack,
            ref bool secondaryAttackHold,
            ref bool block,
            ref bool blockHold)
        {
            try
            {
                // FixedUpdate/SetControls may precede Player.Update. Sample and accept the edge
                // here too so the first combat booleans are neutralized in the same frame.
                ProductionRuntime.TickLocalPlayer(__instance);
            }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Player.SetControls link input", exception);
            }
            ProductionLinkInput.SuppressSetControls(
                __instance,
                Time.frameCount,
                ref attack,
                ref attackHold,
                ref secondaryAttack,
                ref secondaryAttackHold,
                ref block,
                ref blockHold);
        }
    }

    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class ProductionSmelterAwakePatch
    {
        private static void Postfix(Smelter __instance)
        {
            try { ProductionRuntime.Register(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Smelter.Awake", exception);
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class ProductionCookingAwakePatch
    {
        private static void Postfix(CookingStation __instance)
        {
            try { ProductionRuntime.Register(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("CookingStation.Awake", exception);
            }
        }
    }

    [HarmonyPatch(typeof(CraftingStation), "Start")]
    internal static class ProductionRecipeStartPatch
    {
        private static void Postfix(CraftingStation __instance)
        {
            try { ProductionRuntime.Register(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("CraftingStation.Start", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class ProductionFermenterAwakePatch
    {
        private static void Postfix(Fermenter __instance)
        {
            try { ProductionRuntime.Register(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Fermenter.Awake", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Fireplace), "Awake")]
    internal static class ProductionFireplaceAwakePatch
    {
        private static void Postfix(Fireplace __instance)
        {
            try { ProductionRuntime.Register(__instance); }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Fireplace.Awake", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class ProductionSmelterSpawnPatch
    {
        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            try
            {
                return !ProductionRuntime.TryDepositProducedOutput(
                    __instance, ore, stack);
            }
            catch (Exception exception)
            {
                ProductionRuntime.FailStation(
                    __instance, "Smelter.Spawn", exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
    internal static class ProductionSmelterInputHoverPatch
    {
        private static void Postfix(Smelter __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Input,
                ref __result);
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverAddFuel")]
    internal static class ProductionSmelterFuelHoverPatch
    {
        private static void Postfix(Smelter __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Fuel,
                ref __result);
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverEmptyOre")]
    internal static class ProductionSmelterOutputHoverPatch
    {
        private static void Postfix(Smelter __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Output,
                ref __result);
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
    internal static class ProductionCookingHoverPatch
    {
        private static void Postfix(CookingStation __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Input,
                ref __result);
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText))]
    internal static class ProductionRecipeHoverPatch
    {
        private static void Postfix(CraftingStation __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Input,
                ref __result);
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
    internal static class ProductionFermenterHoverPatch
    {
        private static void Postfix(Fermenter __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Input,
                ref __result);
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    internal static class ProductionFireplaceHoverPatch
    {
        private static void Postfix(Fireplace __instance, ref string __result) =>
            ProductionRuntime.AppendHover(
                __instance,
                Contracts.ProductionLinkRole.Fuel,
                ref __result);
    }

    [HarmonyPatch(typeof(Player), "Interact",
        typeof(UnityEngine.GameObject), typeof(bool), typeof(bool))]
    [HarmonyPriority(Priority.First)]
    internal static class ProductionPlayerInteractPatch
    {
        private static bool Prefix(
            Player __instance,
            UnityEngine.GameObject go,
            bool hold,
            bool alt)
        {
            try
            {
                if (!ProductionRuntime.TryHandleInteraction(
                        __instance, go, hold, alt, out _)) return true;
                return false;
            }
            catch (Exception exception)
            {
                ProductionRuntime.FailHook("Player.Interact", exception);
                return true;
            }
        }
    }
}
