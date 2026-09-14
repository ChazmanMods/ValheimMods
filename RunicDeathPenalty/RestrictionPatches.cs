using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicDeathPenalty
{
    // Decline before native inventory mutation. Do not veto RemoveItem: doing so after an action
    // has committed can create free crafting outputs or eat materials without producing anything.
    [HarmonyPatch]
    internal static class RestrictionPatches
    {
        internal static readonly Dictionary<Type, string[]> Boundaries = new Dictionary<Type,string[]>
        {
            {typeof(Humanoid), new[]{"UseItem","EquipItem","Pickup"}},
            {typeof(Player), new[]{"CanConsumeItem","ConsumeItem","EatFood","TryPlacePiece","PlacePiece","HaveRequirements"}},
            {typeof(Attack), new[]{"Start","UseAmmo"}},
            {typeof(InventoryGui), new[]{"DoCrafting"}},
            {typeof(Smelter), new[]{"OnAddOre","OnAddFuel","RPC_AddOre","RPC_AddFuel"}},
            {typeof(CookingStation), new[]{"CookItem","OnAddFuelSwitch","RPC_AddItem","RPC_AddFuel"}},
            {typeof(Fermenter), new[]{"AddItem","RPC_AddItem"}},
            {typeof(OfferingBowl), new[]{"UseItem","Interact"}},
            {typeof(ItemStand), new[]{"UseItem"}},
            {typeof(ShieldGenerator), new[]{"OnAddFuel"}},
            {typeof(Pickable), new[]{"Interact","RPC_Pick"}},
            {typeof(PickableItem), new[]{"Interact","RPC_Pick"}},
            {typeof(Beehive), new[]{"Interact","RPC_Extract"}},
            {typeof(Fish), new[]{"Pickup","RPC_Pickup"}},
            {typeof(TreeBase), new[]{"Damage","RPC_Damage"}},
            {typeof(TreeLog), new[]{"Damage","RPC_Damage"}},
            {typeof(MineRock), new[]{"Damage","RPC_Hit"}},
            {typeof(MineRock5), new[]{"Damage","RPC_Damage"}},
            {typeof(Destructible), new[]{"Damage","RPC_Damage"}},
            {typeof(StoreGui), new[]{"BuySelectedItem"}}
        };
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var pair in Boundaries)
                foreach (string name in pair.Value)
                {
                    var methods = AccessTools.GetDeclaredMethods(pair.Key).Where(m => m.Name == name && (m.ReturnType == typeof(bool) || m.ReturnType == typeof(void))).ToArray();
                    if (methods.Length == 0) throw new MissingMethodException(pair.Key.Name, name);
                    foreach (var method in methods) yield return method;
                }
        }
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicProduction", "chazman.RunicCrafting", "chazman.RunicInventory", "chazman.RunicInteraction")]
        static bool Prefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!Plugin.Active) return true;
            try { return Allow(__instance, __originalMethod.Name, __args); }
            catch (Exception e) { Plugin.Log.LogError("Action declined at " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": " + e); return false; }
        }
        static bool Allow(object instance, string method, object[] args)
        {
            if (!Plugin.Policy.Use && !Plugin.Policy.Harvest) return true;
            var item = args.OfType<ItemDrop.ItemData>().FirstOrDefault();
            if (instance is TreeBase || instance is TreeLog || instance is MineRock || instance is MineRock5)
                return Locations.Harvest((Component)instance);
            if (instance is Destructible d)
                return !d.GetComponent<DropOnDestroyed>() || d.GetComponent<Piece>() || Locations.Harvest(d);
            if (instance is Pickable pick) return Locations.Harvest(pick) && Acquire(pick.m_itemPrefab ? pick.m_itemPrefab.name : "");
            if (instance is PickableItem pi) return Locations.Harvest(pi) && Acquire(pi.m_itemPrefab ? pi.m_itemPrefab.name : "");
            if (instance is Beehive hive) return Locations.Harvest(hive) && Acquire(hive.m_honeyItem.name);
            if (instance is Fish fish) return Locations.Harvest(fish) && Acquire(fish.m_pickupItem.name);
            if (instance is StoreGui store)
            {
                var trade = AccessTools.Field(typeof(StoreGui), "m_selectedItem").GetValue(store) as Trader.TradeItem;
                return trade?.m_prefab == null || Acquire(trade.m_prefab.name);
            }
            if (instance is Humanoid humanoid)
            {
                if (!(humanoid is Player)) return true;
                if (method == "Pickup")
                {
                    var obj = args.OfType<GameObject>().FirstOrDefault(); var drop = obj ? obj.GetComponent<ItemDrop>() : null;
                    if (!drop || !Plugin.Policy.Harvest) return true;
                    bool existing = drop.GetComponent<ZNetView>()?.GetZDO()?.GetBool("RDP.playerDropped") ?? false;
                    int birthBiome = drop.GetComponent<ZNetView>()?.GetZDO()?.GetInt("RDP.originBiome", -1) ?? -1;
                    bool originAllowed = birthBiome < 0 || !Plugin.Policy.Restricted(Locations.BiomeTier((Heightmap.Biome)birthBiome));
                    return existing || (originAllowed && Locations.Harvest(drop) && Acquire(ItemCatalog.Prefab(drop.m_itemData)));
                }
                if (method == "HaveRequirements" || method == "TryPlacePiece" || method == "PlacePiece")
                {
                    var piece = args.OfType<Piece>().FirstOrDefault();
                    var recipe = args.OfType<Recipe>().FirstOrDefault();
                    if (piece) return ItemCatalog.Piece(piece);
                    if (recipe)
                    {
                        if (args.Length > 1 && args[1] is bool discover && discover) return true;
                        int quality = args.Length > 2 && args[2] is int q ? q : 1;
                        return ItemCatalog.Recipe(recipe, quality, humanoid.GetInventory());
                    }
                }
                return ItemCatalog.Use(item);
            }
            if (instance is Attack attack)
            {
                var actor = args.OfType<Humanoid>().FirstOrDefault() ?? AccessTools.Field(typeof(Attack), "m_character").GetValue(attack) as Humanoid;
                if (!(actor is Player)) return true;
                var weapon = item ?? AccessTools.Field(typeof(Attack), "m_weapon").GetValue(attack) as ItemDrop.ItemData;
                if (!ItemCatalog.Use(weapon)) return false;
                var ammo = AccessTools.Method(typeof(Attack), "FindAmmo").Invoke(null, new object[]{actor,weapon}) as ItemDrop.ItemData;
                return ItemCatalog.Use(ammo);
            }
            if (instance is InventoryGui gui)
            {
                var recipe = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe").GetValue(gui) as Recipe;
                var upgrade = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem").GetValue(gui) as ItemDrop.ItemData;
                return !Player.m_localPlayer || (ItemCatalog.Use(upgrade) && ItemCatalog.Recipe(recipe, upgrade == null ? 1 : upgrade.m_quality + 1, Player.m_localPlayer.GetInventory()));
            }
            if (!Plugin.Policy.Use) return true;
            if (instance is Smelter smelter)
            {
                if (method.Contains("Fuel")) return !smelter.m_fuelItem || ItemCatalog.Allowed(smelter.m_fuelItem.name);
                string prefab = args.OfType<string>().FirstOrDefault();
                if (item == null && prefab == null && args.OfType<Humanoid>().FirstOrDefault() is Humanoid user)
                    item = AccessTools.Method(typeof(Smelter), "FindCookableItem").Invoke(smelter, new object[]{user.GetInventory()}) as ItemDrop.ItemData;
                prefab = prefab ?? ItemCatalog.Prefab(item);
                return Conversion(prefab, smelter.m_conversion.Select(c => (c.m_from,c.m_to)));
            }
            if (instance is CookingStation cooking)
            {
                if (method.Contains("Fuel"))
                {
                    var fuel = AccessTools.Field(typeof(CookingStation), "m_fuelItem").GetValue(cooking) as ItemDrop;
                    return !fuel || ItemCatalog.Allowed(fuel.name);
                }
                return Conversion(args.OfType<string>().FirstOrDefault() ?? ItemCatalog.Prefab(item), cooking.m_conversion.Select(c => (c.m_from,c.m_to)));
            }
            if (instance is Fermenter fermenter)
            {
                string prefab = ItemCatalog.Prefab(item);
                if (method.StartsWith("RPC") && args.Length > 1 && args[1] is int hash)
                    prefab = ObjectDB.instance.GetItemPrefab(hash)?.name ?? "";
                return Conversion(prefab, fermenter.m_conversion.Select(c => (c.m_from,c.m_to)));
            }
            if (instance is OfferingBowl bowl)
                return ItemCatalog.Use(item) && (!bowl.m_bossItem || ItemCatalog.Allowed(bowl.m_bossItem.name));
            if (instance is ShieldGenerator shield && item == null)
            {
                var user = args.OfType<Humanoid>().FirstOrDefault();
                var fuel = shield.m_fuelItems.FirstOrDefault(f => user && user.GetInventory().HaveItem(f.m_itemData.m_shared.m_name));
                return !fuel || ItemCatalog.Allowed(fuel.name);
            }
            return ItemCatalog.Use(item);
        }
        internal static bool Acquire(string name) => !Plugin.Policy.Harvest || ItemCatalog.Allowed(name);
        static bool Conversion(string input, IEnumerable<(ItemDrop from, ItemDrop to)> conversions)
        {
            if (string.IsNullOrEmpty(input)) return true; // Vanilla handles no suitable input without mutation.
            if (!ItemCatalog.Allowed(input)) return false;
            foreach (var c in conversions) if (c.from && c.from.name == input && c.to && !ItemCatalog.Allowed(c.to.name)) return false;
            return true;
        }
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAmmoItem))]
    static class AmmoSelection
    {
        static void Postfix(Inventory __instance, string ammoName, string matchPrefabName, ref ItemDrop.ItemData __result)
        {
            if (!Plugin.Active || !Plugin.Policy.Use || !Player.m_localPlayer || __instance != Player.m_localPlayer.GetInventory() || ItemCatalog.Use(__result, false)) return;
            __result = __instance.GetAllItems().Where(i => i.m_shared.m_ammoType == ammoName && (matchPrefabName == null || ItemCatalog.Prefab(i) == matchPrefabName) &&
                (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) && ItemCatalog.Use(i, false))
                .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).FirstOrDefault();
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    static class PlayerDropContext
    {
        [ThreadStatic] internal static bool Dropping;
        static void Prefix(Humanoid __instance, out bool __state) { __state = Dropping; Dropping = __instance is Player; }
        static Exception Finalizer(bool __state, Exception __exception) { Dropping = __state; return __exception; }
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
    static class MarkExistingDrop
    {
        static void Postfix(ItemDrop __result)
        {
            if (PlayerDropContext.Dropping && __result) __result.GetComponent<ZNetView>()?.GetZDO()?.Set("RDP.playerDropped",true);
        }
    }
    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    static class RememberDropOrigin
    {
        static void Postfix(ItemDrop __instance)
        {
            if (WorldGenerator.instance == null) return;
            var view = __instance.GetComponent<ZNetView>(); var zdo = view ? view.GetZDO() : null;
            if (zdo != null && view.IsOwner() && zdo.GetInt("RDP.originBiome",-1) < 0)
                zdo.Set("RDP.originBiome",(int)WorldGenerator.instance.GetBiome(__instance.transform.position));
        }
    }
}
