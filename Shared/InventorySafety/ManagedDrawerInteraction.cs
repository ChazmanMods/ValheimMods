using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicAutomation.Operations
{
    internal static class ManagedDrawerInteraction
    {
        private const string Kind = "drawer.interact.v1";
        internal static void Register() { SharedOperationHost.Register(Kind, Authorize, Plan); ManagedDrawerRetrieval.Register(); }
        private static string Authorize(string actor, byte[] bytes)
        {
            try
            {
                var packet = new ZPackage(bytes); var zdo = ZDOMan.instance.GetZDO(packet.ReadZDOID());
                return ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(zdo)) &&
                    NetworkInventoryAccess.CanAccess(actor, zdo, 5, true, false, out _) ? null : "drawer-access-denied";
            }
            catch { return "invalid-drawer-request"; }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
        {
            string denied = Authorize(actor, bytes); if (denied != null) throw new OperationRejectedException(denied);
            var packet = new ZPackage(bytes); var zdo = ZDOMan.instance.GetZDO(packet.ReadZDOID());
            int action = packet.ReadInt(), amount = packet.ReadInt();
            var delivery = NativeInventoryCodec.Load(OperationPacket.Bytes(packet), 32, 32); OperationPacket.End(packet);
            byte[] before = ManagedChestLedger.Read(zdo); var inventory = ManagedChestLedger.Inventory(before);
            if (action == 0)
            {
                foreach (var item in delivery.GetAllItems().ToArray())
                {
                    int move = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(inventory, item));
                    if (move > 0 && !NativeInventoryCodec.Transfer(delivery, inventory, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), move))
                        throw new OperationRejectedException("drawer-deposit-changed");
                }
            }
            else if (action == 1)
            {
                if (delivery.NrOfItems() != 0 || amount < 1 || amount > ushort.MaxValue) throw new OperationRejectedException("invalid-drawer-withdrawal");
                var item = inventory.GetAllItems().FirstOrDefault();
                if (item == null || !NativeInventoryCodec.Transfer(inventory, delivery, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), Math.Min(amount, item.m_stack)))
                    throw new OperationRejectedException("drawer-is-empty");
            }
            else if (action == 2)
            {
                if (delivery.NrOfItems() != 0 || inventory.NrOfItems() != 0) throw new OperationRejectedException("only-empty-drawers-can-be-cleared");
                inventory = ManagedDrawerInventory.Create("", 0, ManagedDrawerInventory.Maximum);
            }
            else throw new OperationRejectedException("invalid-drawer-action");
            return Tuple.Create(new[] { Tuple.Create(ManagedChestLedger.Endpoint(zdo.m_uid), before, ManagedChestLedger.Next(before, inventory)) }, NativeInventoryCodec.Save(delivery));
        }
        internal static bool Begin(Container drawer, Player player, bool deposit, bool alternate, ItemDrop.ItemData explicitItem = null)
        {
            if (player != Player.m_localPlayer || player == null || player.IsDead() || CharacterOperationEscrow.Busy || !InventoryOperationNetwork.Ready) return false;
            var view = drawer.GetComponent<ZNetView>(); if (view == null || !view.IsValid()) return false;
            var inventory = NativeInventoryCodec.Load(ManagedDrawerInventory.ReadNative(view.GetZDO()), 1, 1);
            string assignment = ManagedDrawerInventory.Assignment(inventory);
            var take = new List<Tuple<ItemDrop.ItemData, int>>(); int action, amount = 0;
            if (deposit)
            {
                string itemName = explicitItem?.m_dropPrefab?.name ?? assignment;
                if (itemName.Length == 0 || assignment.Length > 0 && assignment != itemName) return false;
                foreach (var item in player.GetInventory().GetAllItems())
                    if (item.m_dropPrefab?.name == itemName && ManagedDrawerInventory.Representable(item)) take.Add(Tuple.Create(item, item.m_stack));
                if (take.Count == 0) return false;
                action = 0;
            }
            else if (inventory.NrOfItems() == 0) { if (!alternate) return false; action = 2; }
            else
            {
                action = 1; amount = alternate ? 1 : inventory.GetAllItems()[0].m_dropPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
                amount = Math.Min(amount, NativeInventoryCodec.CapacityFor(player.GetInventory(), inventory.GetAllItems()[0]));
                if (amount < 1) return false;
            }
            return CharacterOperationEscrow.Begin(Kind, escrow =>
            { var p = new ZPackage(); p.Write(view.GetZDO().m_uid); p.Write(action); p.Write(amount); p.Write(escrow); return p.GetArray(); }, take.ToArray(),
                (accepted, reason) => { if (!accepted && player == Player.m_localPlayer) player.Message(MessageHud.MessageType.Center, "Drawer: " + reason.Replace('-', ' ')); });
        }
    }
    [HarmonyPatch]
    internal static class DrawerAuthorityActions
    {
        private static Type Drawer => Type.GetType("DrawerContainer, itemdrawers", false);
        private static bool Prepare() => Drawer != null;
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(Drawer, "ProcessInputInternal"); yield return AccessTools.Method(Drawer, "UseItem"); }
        private static bool Prefix(Container __instance, MethodBase __originalMethod, object[] __args, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal) return false;
            if (!InventoryOperationNetwork.Ready) return true;
            __result = __originalMethod.Name == "UseItem" ?
                ManagedDrawerInteraction.Begin(__instance, __args[0] as Player, true, false, __args[1] as ItemDrop.ItemData) :
                ManagedDrawerInteraction.Begin(__instance, __args[0] as Player, (bool)__args[1], (bool)__args[2]);
            return false;
        }
    }
    [HarmonyPatch]
    internal static class DrawerAuthorityNativeWrites
    {
        private static Type Drawer => Type.GetType("DrawerContainer, itemdrawers", false);
        private static bool Prepare() => Drawer != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "Save", "RPC_AddItem", "RPC_AddItemResponse", "RPC_Drop", "RPC_DropResponse", "RPC_Clear", "OnDestroyed_Drawer", "RetreiveItems" })
                yield return AccessTools.Method(Drawer, name);
        }
        private static bool Prefix(Container __instance, MethodBase __originalMethod, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!InventoryOperationNetwork.Ready) return true;
            if (__originalMethod.Name.StartsWith("RPC_", StringComparison.Ordinal)) return false;
            if (__originalMethod.Name == "RetreiveItems")
            { ManagedDrawerRetrieval.Tick(__instance); return false; }
            return !ManagedChestLedger.Managed(__instance.GetComponent<ZNetView>()?.GetZDO());
        }
    }
}
