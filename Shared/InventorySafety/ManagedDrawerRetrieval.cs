using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    internal static class ManagedDrawerRetrieval
    {
        private const string Kind = "drawer.retrieve.v1";
        private static HashSet<ZDOID> Pending => SharedState.Get("operations.drawer-retrieval.pending.v1", () => new HashSet<ZDOID>());
        internal static void Clear() => Pending.Clear();
        internal static void Register() => SharedOperationHost.RegisterBackground(Kind, (_, _) => null, Plan);
        private static bool Owner(string actor, ZDO drawer)
        {
            if (drawer.GetOwner() == ZDOMan.GetSessionID() && actor == "server-drawer:" + drawer.m_uid) return true;
            if (!NetworkInventoryAccess.TryPosition(actor, out long principal, out _)) return false;
            return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == principal && drawer.GetOwner() == ZDOMan.GetSessionID() ||
                ZNet.instance.GetPeers().Any(p => NetworkInventoryAccess.TryPeerPrincipal(p, out long peerPrincipal, out _) && peerPrincipal == principal && p.m_uid == drawer.GetOwner());
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
        {
            var packet = new ZPackage(bytes); var drawer = ZDOMan.instance.GetZDO(packet.ReadZDOID()); var drop = ZDOMan.instance.GetZDO(packet.ReadZDOID());
            string token = packet.ReadString(); int ownerRevision = packet.ReadInt(); byte[] captured = OperationPacket.Bytes(packet); OperationPacket.End(packet);
            void Reject(string reason)
            {
                if (drop != null && drop.GetString(ManagedWorldDrops.Marker, "") == token && ManagedWorldDrops.Read(drop) == null)
                { drop.Set(ManagedWorldDrops.Marker, ""); ZDOMan.instance.ForceSendZDO(drop.m_uid); }
                throw new OperationRejectedException(reason);
            }
            if (drawer == null || drop == null || !Guid.TryParseExact(token, "N", out _) || !ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(drawer)) || !Owner(actor, drawer)) Reject("drawer-retrieval-owner-changed");
            var plugin = Type.GetType("itemdrawers.ItemDrawerPlugin, itemdrawers", true);
            bool enabled = (bool)AccessTools.Property(plugin, "RetreiveEnabled").GetValue(null);
            float radius = (float)AccessTools.Property(plugin, "RetreiveRadius").GetValue(null);
            if (!enabled || float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0 || radius > 100 ||
                (drop.GetPosition() - drawer.GetPosition() - Vector3.up).sqrMagnitude > radius * radius) Reject("drawer-retrieval-out-of-range");
            byte[] chestBefore = ManagedChestLedger.Read(drawer); var target = ManagedChestLedger.Inventory(chestBefore);
            byte[] dropBefore = ManagedWorldDrops.Read(drop);
            if (dropBefore == null)
            {
                if (drop.GetOwner() != drawer.GetOwner() || drop.OwnerRevision != ownerRevision || drop.GetString(ManagedWorldDrops.Marker, "") != token) Reject("ground-item-owner-changed");
                var prefab = ObjectDB.instance.GetItemPrefab(drop.GetPrefab()); var item = prefab == null ? null : prefab.GetComponent<ItemDrop>()?.m_itemData.Clone();
                if (item == null) Reject("ground-item-prefab-unavailable");
                item.m_dropPrefab = prefab; ItemDrop.LoadFromZDO(item, drop); item.m_gridPos = Vector2i.zero;
                var snapshot = new Inventory("Ground item", null, 1, 1); snapshot.GetAllItems().Add(item);
                if (!OwnerOperationQueue.Same(NativeInventoryCodec.Save(snapshot), captured)) throw new OperationDeferredException("ground-item-snapshot-pending");
                dropBefore = ManagedWorldDrops.Adopt(drop, captured, token);
            }
            var source = ManagedWorldDrops.Inventory(dropBefore); var offered = source.GetAllItems().Single();
            // Auto pickup only fills the drawer's existing assignment.
            if (ManagedDrawerInventory.Assignment(target) != offered.m_dropPrefab.name) Reject("drawer-assignment-changed");
            int amount = Math.Min(offered.m_stack, NativeInventoryCodec.CapacityFor(target, offered));
            if (amount < 1) Reject("drawer-full-or-item-metadata-unsupported");
            var moved = offered.Clone(); moved.m_stack = amount;
            if (!NativeInventoryCodec.AddExact(target, moved)) Reject("drawer-capacity-changed");
            return Tuple.Create(new[] { ManagedWorldDrops.Consume(drop, dropBefore, amount),
                Tuple.Create(ManagedChestLedger.Endpoint(drawer.m_uid), chestBefore, ManagedChestLedger.Next(chestBefore, target)) }, Array.Empty<byte>());
        }
        internal static void Tick(Container drawer)
        {
            var view = drawer.GetComponent<ZNetView>();
            if (!InventoryOperationNetwork.Ready || view == null || !view.IsValid() || !view.IsOwner() || Pending.Contains(view.GetZDO().m_uid)) return;
            var plugin = Type.GetType("itemdrawers.ItemDrawerPlugin, itemdrawers", true);
            if (!(bool)AccessTools.Property(plugin, "RetreiveEnabled").GetValue(null)) return;
            var delay = AccessTools.Field(drawer.GetType(), "_pickupDelay"); int remainingDelay = (int)delay.GetValue(drawer);
            if (remainingDelay > 0) { delay.SetValue(drawer, remainingDelay - 1); return; }
            string assignment = ManagedDrawerInventory.Assignment(NativeInventoryCodec.Load(ManagedDrawerInventory.ReadNative(view.GetZDO()), 1, 1));
            if (assignment.Length == 0) return;
            float radius = (float)AccessTools.Property(plugin, "RetreiveRadius").GetValue(null);
            foreach (var collider in Physics.OverlapSphere(drawer.transform.position + Vector3.up, radius, LayerMask.GetMask("item")))
            {
                var item = collider.attachedRigidbody == null ? null : collider.attachedRigidbody.GetComponent<ItemDrop>();
                var itemView = item == null ? null : item.GetComponent<ZNetView>();
                if (itemView == null || !itemView.IsValid() || item.m_itemData.m_dropPrefab?.name != assignment) continue;
                if (!ManagedWorldDrops.Managed(itemView.GetZDO()) && !itemView.IsOwner()) { item.RequestOwn(); continue; }
                if (!item.CanPickup(true)) continue;
                item.Load(); var source = new Inventory("Ground item", null, 1, 1); var copy = item.m_itemData.Clone(); copy.m_gridPos = Vector2i.zero; source.GetAllItems().Add(copy);
                string token = itemView.GetZDO().GetString(ManagedWorldDrops.Marker, "");
                bool newlyFrozen = token.Length == 0;
                if (newlyFrozen) { token = Guid.NewGuid().ToString("N"); itemView.GetZDO().Set(ManagedWorldDrops.Marker, token); ZDOMan.instance.ForceSendZDO(itemView.GetZDO().m_uid); }
                var p = new ZPackage(); p.Write(view.GetZDO().m_uid); p.Write(itemView.GetZDO().m_uid); p.Write(token); p.Write((int)itemView.GetZDO().OwnerRevision); p.Write(NativeInventoryCodec.Save(source));
                var id = view.GetZDO().m_uid; Pending.Add(id);
                Action<int, string, byte[]> done = (_, _, _) => Pending.Remove(id);
                string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), request = Guid.NewGuid().ToString("N");
                if (ZNet.instance.IsServer() && Player.m_localPlayer == null)
                {
                    var result = SharedOperationHost.Submit(world, "server-drawer:" + id, request, Kind, p.GetArray(), done);
                    if (result.Item1 != (int)OperationStatus.Queued) done(result.Item1, result.Item2, result.Item3);
                }
                else if (!InventoryOperationNetwork.Send(world, request, Kind, p.GetArray(), done))
                {
                    Pending.Remove(id);
                    if (newlyFrozen && itemView.IsValid() && itemView.GetZDO().GetString(ManagedWorldDrops.Marker, "") == token)
                    { itemView.GetZDO().Set(ManagedWorldDrops.Marker, ""); ZDOMan.instance.ForceSendZDO(itemView.GetZDO().m_uid); }
                }
                break;
            }
        }
    }
}
