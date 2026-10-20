using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    [HarmonyPatch]
    internal static class AuthorityStationInteractionClient
    {
        internal static bool ProcessDelivery(Player player, byte[] bytes, Inventory delivery)
        {
            var request = AuthorityStationInteractionRequest.Decode(bytes);
            var instance = ZNetScene.instance?.FindInstance(request.Station);
            var cooking = instance == null ? null : instance.GetComponent<CookingStation>();
            if (cooking == null) return true;
            // Bonus items belong to the character delivery save, alongside its processed
            // receipt. A repeated server response cannot award another cooking bonus.
            if (request.Action == AuthorityStationAction.Collect && delivery.NrOfItems() > 0 &&
                cooking.m_canGiveBonusYield && InventoryGui.instance != null &&
                UnityEngine.Random.value < player.GetSkillFactor(cooking.m_skill) * InventoryGui.instance.m_craftBonusChance)
            {
                var bonus = delivery.GetAllItems()[0].Clone(); bonus.m_stack = InventoryGui.instance.m_craftBonusAmount;
                if (bonus.m_stack > 0 && !NativeInventoryCodec.AddExact(delivery, bonus)) throw new InvalidOperationException("Cooking bonus delivery is full");
            }
            try
            {
                if (cooking.m_skill != Skills.SkillType.None)
                {
                    if (request.Action == AuthorityStationAction.Input) player.RaiseSkill(cooking.m_skill, .4f);
                    else if (request.Action == AuthorityStationAction.Collect) player.RaiseSkill(cooking.m_skill, .6f);
                }
            }
            catch (Exception error) { RunicAutomation.MutationGate.Report("Cooking skill update failed: " + error.Message); }
            return true;
        }
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string method in new[] { "OnAddOre", "OnAddFuel", "OnEmpty" }) yield return AccessTools.Method(typeof(Smelter), method);
            foreach (string method in new[] { "OnInteract", "OnUseItem", "OnAddFuelSwitch" }) yield return AccessTools.Method(typeof(CookingStation), method);
            foreach (Type type in new[] { typeof(Fermenter), typeof(Fireplace) })
                foreach (string method in new[] { "Interact", "UseItem" }) yield return AccessTools.Method(type, method);
            yield return AccessTools.Method(typeof(Beehive), "Interact");
        }
        [HarmonyPriority(-200)]
        private static bool Prefix(Component __instance, MethodBase __originalMethod, object[] __args, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal || !ProductionRuntime.AuthorityManagedStation(ValheimAccess.Zdo(__instance))) return true;
            __result = false;
            var player = __args.OfType<Humanoid>().FirstOrDefault() as Player;
            if (player == null || player != Player.m_localPlayer || player.IsDead()) return false;
            // Fireworks affect only the user's inventory and native effects, not the station ledger.
            var explicitItem = __args.OfType<ItemDrop.ItemData>().FirstOrDefault();
            if (__instance is Fireplace nativeFire && __originalMethod.Name == "UseItem" && explicitItem != null &&
                explicitItem.m_shared.m_name != nativeFire.m_fuelItem?.m_itemData.m_shared.m_name) return true;
            if (__args.OfType<bool>().FirstOrDefault())
            {
                if (!(__instance is Fireplace repeating) || repeating.m_holdRepeatInterval <= 0) return false;
                var lastUse = AccessTools.Field(typeof(Fireplace), "m_lastUseTime");
                if (Time.time - (float)lastUse.GetValue(repeating) < repeating.m_holdRepeatInterval) return false;
            }
            if (!InventoryOperationNetwork.Ready || CharacterOperationEscrow.Busy)
            { player.Message(MessageHud.MessageType.Center, InventoryOperationNetwork.Ready ? CharacterOperationEscrow.Status : "Waiting for matching Runic mods on the server."); return false; }
            var zdo = ValheimAccess.Zdo(__instance);
            if (!PrivateArea.CheckAccess(__instance.transform.position, 0, true)) return false;
            var item = __args.OfType<ItemDrop.ItemData>().FirstOrDefault();
            AuthorityStationAction action = AuthorityStationAction.Input; string method = __originalMethod.Name; bool nearby = false;
            bool Fuel(ItemDrop expected)
            {
                if (expected == null) return false;
                if (item == null) item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == expected.name);
                if (item == null && NearbyInteractionMaterials.Enabled(true)) { nearby = true; return true; }
                return item != null && item.m_dropPrefab.name == expected.name;
            }
            bool Input(Func<string, bool> accepts)
            {
                if (item == null) item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && accepts(i.m_dropPrefab.name));
                if (item == null && __instance is CookingStation && NearbyInteractionMaterials.Enabled(false)) { nearby = true; return true; }
                return item != null && accepts(item.m_dropPrefab.name);
            }
            bool valid = true;
            if (__instance is Fireplace fire)
            {
                if (method == "Interact" && __args.OfType<bool>().Skip(1).FirstOrDefault() && fire.m_canTurnOff) action = AuthorityStationAction.Toggle;
                else { action = AuthorityStationAction.Fuel; valid = Fuel(fire.m_fuelItem); }
            }
            else if (__instance is Smelter smelter)
            {
                if (method == "OnEmpty") action = AuthorityStationAction.Collect;
                else if (method == "OnAddFuel") { action = AuthorityStationAction.Fuel; valid = Fuel(smelter.m_fuelItem); }
                else valid = Input(name => smelter.m_conversion.Any(c => c.m_from != null && c.m_from.name == name));
            }
            else if (__instance is CookingStation cooking)
            {
                if (method == "OnAddFuelSwitch") { action = AuthorityStationAction.Fuel; valid = Fuel(cooking.m_fuelItem); }
                else if (method == "OnInteract" && (bool)AccessTools.Method(typeof(CookingStation), "HaveDoneItem").Invoke(cooking, null)) action = AuthorityStationAction.Collect;
                else
                {
                    valid = (!cooking.m_requireFire || (bool)AccessTools.Method(typeof(CookingStation), "IsFireLit").Invoke(cooking, null)) &&
                        Input(name => cooking.m_conversion.Any(c => c.m_from != null && c.m_from.name == name));
                }
            }
            else if (__instance is Fermenter fermenter)
            {
                if (method == "Interact" && ValheimAccess.FermenterStatus(fermenter) == FermenterSlotStatus.Ready) action = AuthorityStationAction.Collect;
                else
                {
                    ValheimAccess.RefreshFermenterCover(fermenter);
                    valid = ValheimAccess.FermenterCovered(fermenter) && Input(name => fermenter.m_conversion.Any(c => c.m_from != null && c.m_from.name == name));
                }
            }
            else if (__instance is Beehive) action = AuthorityStationAction.Collect;
            else return true;
            if (!valid) { player.Message(MessageHud.MessageType.Center, "No suitable carried item or station is not ready."); return false; }
            bool takesItem = action == AuthorityStationAction.Fuel || action == AuthorityStationAction.Input;
            if (takesItem && !nearby && (item == null || item.m_equipped || item.m_shared.m_questItem || !player.GetInventory().ContainsItem(item))) return false;
            if (__instance is Fireplace repeatedFire) AccessTools.Field(typeof(Fireplace), "m_lastUseTime").SetValue(repeatedFire, Time.time);
            var request = new AuthorityStationInteractionRequest { Station = zdo.m_uid, Action = action, Nearby = nearby };
            int effectSlot = 0;
            if (__instance is CookingStation effectCooking)
                effectSlot = Enumerable.Range(0, effectCooking.m_slots.Length).Where(n => action == AuthorityStationAction.Input ?
                    zdo.GetString(("slot" + n).GetStableHashCode(), "").Length == 0 :
                    zdo.GetString(("slot" + n).GetStableHashCode(), "").Length > 0 && zdo.GetInt(("slotstatus" + n).GetStableHashCode()) > 0).DefaultIfEmpty(0).First();
            __result = CharacterOperationEscrow.Begin(AuthorityStationInteraction.Kind, escrow => { request.Escrow = escrow; return request.Encode(); },
                takesItem && !nearby ? new[] { Tuple.Create(item, 1) } : Array.Empty<Tuple<ItemDrop.ItemData, int>>(),
                (accepted, reason) => {
                    if (!accepted && Player.m_localPlayer == player) player.Message(MessageHud.MessageType.Center, "Station: " + reason.Replace('-', ' '));
                    if (accepted && __instance != null && action != AuthorityStationAction.Toggle)
                        AuthorityProductionEvents.Play(__instance, action == AuthorityStationAction.Input ? AuthorityProductionEvents.Kind.Input :
                            action == AuthorityStationAction.Fuel ? AuthorityProductionEvents.Kind.Fuel : AuthorityProductionEvents.Kind.Output, effectSlot);
                });
            return false;
        }
    }
    [HarmonyPatch]
    internal static class AuthorityStationNativeRpcPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "RPC_AddOre", "RPC_AddFuel", "RPC_EmptyProcessed" }) yield return AccessTools.Method(typeof(Smelter), name);
            foreach (string name in new[] { "RPC_AddItem", "RPC_AddFuel", "RPC_RemoveDoneItem" }) yield return AccessTools.Method(typeof(CookingStation), name);
            foreach (string name in new[] { "RPC_AddItem", "RPC_Tap" }) yield return AccessTools.Method(typeof(Fermenter), name);
            foreach (string name in new[] { "RPC_AddFuel", "RPC_ToggleOn", "RPC_AddFuelAmount", "RPC_SetFuelAmount" }) yield return AccessTools.Method(typeof(Fireplace), name);
            yield return AccessTools.Method(typeof(Beehive), "RPC_Extract");
        }
        private static bool Prefix(Component __instance) => !ProductionRuntime.AuthorityManagedStation(ValheimAccess.Zdo(__instance));
    }
}
