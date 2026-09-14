using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RunicDeathPenalty
{
    internal static class WorldLoot
    {
        internal static readonly ConditionalWeakTable<Inventory, Container> Containers = new ConditionalWeakTable<Inventory, Container>();
        internal static bool IsFresh(Inventory inventory)
        {
            if (inventory == null || !Containers.TryGetValue(inventory,out var container) || !container || container.GetComponent<TombStone>()) return false;
            var piece=container.GetComponent<Piece>();
            return !piece || piece.GetCreator()==0;
        }
    }
    [HarmonyPatch(typeof(Container), "Awake")]
    static class TrackContainer
    {
        static void Postfix(Container __instance)
        {
            var inventory=__instance.GetInventory();
            if(inventory!=null) { WorldLoot.Containers.Remove(inventory); WorldLoot.Containers.Add(inventory,__instance); }
        }
    }
    [HarmonyPatch]
    static class WorldLootTransfer
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Inventory)).Where(m=>m.Name=="MoveItemToThis"||m.Name=="MoveAll");
        static bool Prefix(Inventory __instance, Inventory fromInventory, object[] __args)
        {
            if(!Plugin.Active || !Plugin.Policy.Harvest || !Player.m_localPlayer || __instance!=Player.m_localPlayer.GetInventory() || !WorldLoot.IsFresh(fromInventory)) return true;
            var item=__args.OfType<ItemDrop.ItemData>().FirstOrDefault();
            // Bulk transfers are all-or-nothing; individual allowed stacks can still be taken.
            return item!=null ? RestrictionPatches.Acquire(ItemCatalog.Prefab(item)) : fromInventory.GetAllItems().All(i=>RestrictionPatches.Acquire(ItemCatalog.Prefab(i)));
        }
    }
}
