using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class ManualInteractionRuntime
    {
        private static readonly MethodInfo Done = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly MethodInfo FreeSlot = AccessTools.Method(typeof(CookingStation), "GetFreeSlot");
        private static readonly MethodInfo FireLit = AccessTools.Method(typeof(CookingStation), "IsFireLit");
        private static readonly MethodInfo Cookable = AccessTools.Method(typeof(CookingStation), "FindCookableItem");
        private static readonly MethodInfo CookingFuel = AccessTools.Method(typeof(CookingStation), "GetFuel");
        private static readonly MethodInfo SmelterFuel = AccessTools.Method(typeof(Smelter), "GetFuel");
        [ThreadStatic] private static bool _preparing;

        internal static void PrepareCooking(CookingStation station, Humanoid user)
        {
            if (!Configuration.CookFromContainers.Value || !CanPrepare(station, user, out Player player)) return;
            if (!ManualInteractionPolicy.ShouldLoadCooking((bool)Done.Invoke(station, null),
                (int)FreeSlot.Invoke(station, null), station.m_requireFire,
                !station.m_requireFire || (bool)FireLit.Invoke(station, null),
                Cookable.Invoke(station, new object[] { player.GetInventory() }) != null)) return;
            if (station.m_conversion == null || station.m_conversion.Count > 256) return;
            foreach (CookingStation.ItemConversion conversion in station.m_conversion)
                if (conversion?.m_from != null && Pull(station, player, conversion.m_from)) return;
        }

        internal static void PrepareCookingFuel(CookingStation station, Humanoid user, ItemDrop.ItemData item)
        {
            if (item != null || !Configuration.RefuelFromContainers.Value || !station.m_useFuel ||
                !CanPrepare(station, user, out Player player)) return;
            float fuel = (float)CookingFuel.Invoke(station, null);
            if (ManualInteractionPolicy.HasFuelRoom(fuel, station.m_maxFuel)) Pull(station, player, station.m_fuelItem);
        }

        internal static void PrepareSmelterFuel(Smelter station, Humanoid user, ItemDrop.ItemData item)
        {
            if (item != null || !Configuration.RefuelFromContainers.Value ||
                !CanPrepare(station, user, out Player player)) return;
            float fuel = (float)SmelterFuel.Invoke(station, null);
            if (ManualInteractionPolicy.HasFuelRoom(fuel, station.m_maxFuel)) Pull(station, player, station.m_fuelItem);
        }

        internal static void PrepareFire(Fireplace station, Humanoid user, bool hold, bool alt, float lastUse)
        {
            if (!Configuration.RefuelFromContainers.Value || !station.m_canRefill || station.m_infiniteFuel ||
                !CanPrepare(station, user, out Player player)) return;
            float fuel = station.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel);
            if (ManualInteractionPolicy.ShouldRefuelFire(station.m_canRefill, station.m_infiniteFuel,
                hold, alt, station.m_canTurnOff, fuel, station.m_maxFuel,
                station.m_holdRepeatInterval, Time.time - lastUse)) Pull(station, player, station.m_fuelItem);
        }

        private static bool CanPrepare(Component station, Humanoid user, out Player player)
        {
            player = user as Player;
            if (_preparing || !Configuration.Enabled.Value || !CraftingRuntime.IsInitialized ||
                CraftingRuntime.HasMaterialOperation || station == null ||
                !ValheimReflection.CanMutateLocalPlayer(player)) return false;
            ZNetView view = station.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || player.GetInventory() == null ||
                (player.transform.position - station.transform.position).sqrMagnitude > 100f ||
                !PrivateArea.CheckAccess(station.transform.position, 0f, flash: false, wardCheck: false)) return false;
            return true;
        }

        private static bool Pull(Component station, Player player, ItemDrop fuelOrInput)
        {
            if (fuelOrInput?.m_itemData?.m_shared == null ||
                player.GetInventory().HaveItem(fuelOrInput.m_itemData.m_shared.m_name)) return false;
            string resource = ValheimReflection.ResourceId(fuelOrInput);
            if (string.IsNullOrEmpty(resource)) return false;
            _preparing = true;
            try
            {
                var requirements = new[] { new MaterialRequirement(resource, 1) };
                var query = new ContainerQueryRuntime(new WorkshopAccessRuntime());
                var sources = query.ResolveSources(player, station.GetComponent<CraftingStation>(),
                    station.transform.position, Configuration.SafeInteractionRange, requirements,
                    "runic.crafting.manual-use", true, out _, requireWritable: true);
                foreach (ValheimMaterialSource source in sources.OfType<ValheimMaterialSource>())
                {
                    if (source.Snapshot().Kind != MaterialSourceKind.NearbyContainer) continue;
                    if (source.TryStageOne(player.GetInventory(), resource, ItemAllowed, out string reason))
                    {
                        CraftingDiagnostics.TraceAction("manual-use", "one-item-staged:" + resource);
                        return true;
                    }
                    if (reason == "backpack-space-required")
                    {
                        player.Message(MessageHud.MessageType.TopLeft, "Runic Crafting: make backpack space for one cooking/fuel item.");
                        return false;
                    }
                }
                CraftingDiagnostics.TraceGate("manual-use", "no-eligible-source:" + resource);
                return false;
            }
            finally { _preparing = false; }
        }

        private static bool ItemAllowed(ItemDrop.ItemData item)
        {
            if (item == null || item.m_equipped || item.m_shared == null || item.m_shared.m_questItem) return false;
            Type api = Type.GetType("RunicInventory.Api.InventoryIntegrationApi, RunicInventory", false);
            if (api == null) return !Chainloader.PluginInfos.ContainsKey("chazman.RunicInventory");
            MethodInfo method = api.GetMethod("TryGetProtection", new[] { typeof(object), typeof(int).MakeByRefType() });
            if (method == null) return false;
            object[] args = { item, 0 };
            // NotApplicable (false) is not a provider failure; standalone/fallback play remains supported.
            bool governed = (bool)method.Invoke(null, args);
            return ManualInteractionPolicy.AllowsProtection(governed, (int)args[1]);
        }

        internal static void Guard(Action prepare)
        {
            try { prepare(); }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Nearby cooking/refueling preparation stopped; vanilla interaction remains available: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnInteract", typeof(Humanoid))]
    internal static class NearbyCookingInteractionPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CookingStation __instance, Humanoid user, bool __runOriginal)
        { if (__runOriginal) ManualInteractionRuntime.Guard(() => ManualInteractionRuntime.PrepareCooking(__instance, user)); }
    }
    [HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch", typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData))]
    internal static class NearbyOvenFuelPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CookingStation __instance, Humanoid user, ItemDrop.ItemData item, bool __runOriginal)
        { if (__runOriginal) ManualInteractionRuntime.Guard(() => ManualInteractionRuntime.PrepareCookingFuel(__instance, user, item)); }
    }
    [HarmonyPatch(typeof(Smelter), "OnAddFuel", typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData))]
    internal static class NearbySmelterFuelPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Smelter __instance, Humanoid user, ItemDrop.ItemData item, bool __runOriginal)
        { if (__runOriginal) ManualInteractionRuntime.Guard(() => ManualInteractionRuntime.PrepareSmelterFuel(__instance, user, item)); }
    }
    [HarmonyPatch(typeof(Fireplace), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))]
    internal static class NearbyFireFuelPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, float ___m_lastUseTime, bool __runOriginal)
        { if (__runOriginal) ManualInteractionRuntime.Guard(() => ManualInteractionRuntime.PrepareFire(__instance, user, hold, alt, ___m_lastUseTime)); }
    }
}
