using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // Production output remains an ordinary visible ItemDrop, with a durable receipt for
    // creation/collection so restarting from an older world save cannot spawn it twice.
    internal static class ManagedWorldDrops
    {
        internal const string Marker = "runic.inventory.world-drop.v1";
        private const string Prefix = "world-drop:", PickupKind = "inventory.pickup-drop.v1", RetireKind = "inventory.retire-drop.v1";
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.world-drops.v1", () => new Session().Routes());
        internal static bool Managed(ZDO zdo) => zdo != null && zdo.GetString(Marker, "").Length > 0;
        internal static void Tick() => ((Action)Routes["tick"])();
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static void Project(ZDO zdo) => ((Action<ZDO>)Routes["project"])(zdo);
        internal static byte[] Adopt(ZDO zdo, byte[] items, string token)
        {
            var inventory = NativeInventoryCodec.Load(items, 1, 1);
            if (inventory.NrOfItems() != 1 || inventory.GetAllItems()[0].m_dropPrefab.name.GetStableHashCode() != zdo.GetPrefab())
                throw new OperationRejectedException("ground-item-snapshot-invalid");
            var body = new Image { Token = token, SourceOrigin = WorldInventoryOrigin.Ensure(zdo), Position = zdo.GetPosition(),
                Prefab = zdo.GetPrefab(), Items = items, Active = true };
            zdo.Set(Marker, token); ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            var image = SharedOperationHost.Read(Prefix + token) ?? SharedOperationHost.Import(Prefix + token, body.Encode());
            Project(zdo); return image;
        }
        internal static Tuple<string, byte[], byte[]> Consume(ZDO zdo, byte[] before, int amount)
        {
            var body = Image.Decode(FileOperationEndpoints.Inventory(before));
            var inventory = NativeInventoryCodec.Load(body.Items, 1, 1); var item = inventory.GetAllItems().Single();
            if (!body.Active || amount < 1 || amount > item.m_stack) throw new OperationRejectedException("ground-item-changed");
            if (amount == item.m_stack) body.Active = false;
            else { item.m_stack -= amount; body.Items = NativeInventoryCodec.Save(inventory); }
            return Tuple.Create(Prefix + body.Token, before, FileOperationEndpoints.Next(before, body.Encode()));
        }
        internal static byte[] Read(ZDO zdo) => SharedOperationHost.Read(Prefix + zdo.GetString(Marker, ""));
        internal static Inventory Inventory(byte[] image) => NativeInventoryCodec.Load(Image.Decode(FileOperationEndpoints.Inventory(image)).Items, 1, 1);
        internal static Tuple<string, byte[], byte[]> Create(string token, Vector3 position, ItemDrop.ItemData item)
        {
            if (!Guid.TryParseExact(token, "N", out _)) throw new InvalidOperationException("Invalid output identity");
            var inventory = new Inventory("Production output", null, 1, 1); var copy = item.Clone(); copy.m_gridPos = Vector2i.zero; inventory.GetAllItems().Add(copy);
            var body = new Image { Token = token, Position = position, Prefab = item.m_dropPrefab.name.GetStableHashCode(), Items = NativeInventoryCodec.Save(inventory), Active = false };
            string endpoint = Prefix + token;
            byte[] before = SharedOperationHost.Read(endpoint) ?? SharedOperationHost.Import(endpoint, body.Encode());
            if (FileOperationEndpoints.Revision(before) != 0) throw new OperationRejectedException("production-output-already-created");
            body.Active = true;
            return Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, body.Encode()));
        }
        private sealed class Image
        {
            internal string Token, SourceOrigin = ""; internal Vector3 Position; internal int Prefab; internal byte[] Items; internal bool Active;
            internal byte[] Encode()
            { var p = new ZPackage(); p.Write(2); p.Write(Token); p.Write(Position); p.Write(Prefab); p.Write(Active); p.Write(Items); p.Write(SourceOrigin); return p.GetArray(); }
            internal static Image Decode(byte[] bytes)
            {
                var p = new ZPackage(bytes); int version = p.ReadInt(); if (version != 1 && version != 2) throw new InvalidOperationException("Unknown world drop schema");
                var body = new Image { Token = p.ReadString(), Position = p.ReadVector3(), Prefab = p.ReadInt(), Active = p.ReadBool(), Items = OperationPacket.Bytes(p) };
                if (version >= 2) body.SourceOrigin = p.ReadString();
                OperationPacket.End(p);
                if (!Guid.TryParseExact(body.Token, "N", out _) || !NetworkInventoryAccess.Finite(body.Position)) throw new InvalidOperationException("Invalid world drop image");
                return body;
            }
        }
        private sealed class Session
        {
            private readonly Dictionary<string, ZDOID> objects = new();
            private bool recovered, registered, projecting;
            private float nextOrphanCheck;
            internal Dictionary<string, Delegate> Routes() => new()
            {
                ["tick"] = new Action(Tick), ["clear"] = new Action(() => { objects.Clear(); recovered = false; projecting = false; }),
                ["project"] = new Action<ZDO>(Project)
            };
            private void Register()
            {
                if (registered) return;
                SharedOperationHost.Register(PickupKind, AuthorizePickup, Pickup);
                ManagedAnimalConsumption.Register();
                SharedOperationHost.Register(RetireKind, (actor, _) => actor == "world-destruction" ? null : "internal-world-operation-only", Retire);
                SharedOperationHost.RegisterProjection(Prefix, Publish);
                InventoryWorldLifecycle.RegisterAdapter("world-drop", zdo => Managed(zdo) ? Tuple.Create(RetireKind, zdo.GetString(Marker, "")) : null, () => { });
                registered = true;
            }
            private void Tick()
            {
                Register();
                if (!InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (recovered)
                {
                    if (Time.realtimeSinceStartup >= nextOrphanCheck)
                    {
                        nextOrphanCheck = Time.realtimeSinceStartup + 5;
                        foreach (var entry in objects.ToArray())
                        {
                            var pending = ZDOMan.instance.GetZDO(entry.Value);
                            if (pending == null || SharedOperationHost.Read(Prefix + entry.Key) != null) continue;
                            if (pending.GetOwner() != ZDOMan.GetSessionID() && ZNet.instance.GetPeer(pending.GetOwner()) != null) continue;
                            pending.Set(Marker, ""); ZDOMan.instance.ForceSendZDO(pending.m_uid); objects.Remove(entry.Key);
                        }
                    }
                    return;
                }
                // World drops can move. Index their durable token over the entire loaded world,
                // rather than guessing from their original production position.
                var all = (Dictionary<ZDOID, ZDO>)AccessTools.Field(typeof(ZDOMan), "m_objectsByID").GetValue(ZDOMan.instance);
                foreach (var zdo in all.Values.Where(Managed))
                {
                    string token = zdo.GetString(Marker, "");
                    if (objects.ContainsKey(token)) throw new InvalidOperationException("Duplicate production drop identity");
                    objects[token] = zdo.m_uid;
                }
                var origins = all.Values.Where(z => z.GetString(WorldInventoryOrigin.Key, "").Length > 0)
                    .GroupBy(z => z.GetString(WorldInventoryOrigin.Key, "")).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
                foreach (var persisted in SharedOperationHost.Enumerate(Prefix))
                {
                    var body = Image.Decode(FileOperationEndpoints.Inventory(persisted.Item2));
                    if (!objects.ContainsKey(body.Token) && body.SourceOrigin.Length > 0 && origins.TryGetValue(body.SourceOrigin, out var original) && original.GetPrefab() == body.Prefab)
                    { original.Set(Marker, body.Token); objects[body.Token] = original.m_uid; }
                    if (FileOperationEndpoints.Revision(persisted.Item2) > 0 || body.SourceOrigin.Length > 0) SharedOperationHost.RefreshProjection(persisted.Item1);
                }
                recovered = true;
            }
            private void Project(ZDO zdo)
            {
                if (projecting || !Managed(zdo) || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                string token = zdo.GetString(Marker, ""); objects[token] = zdo.m_uid;
                byte[] image = SharedOperationHost.Read(Prefix + token);
                if (image != null) Publish(Prefix + token, image);
            }
            private void Publish(string endpoint, byte[] image)
            {
                if (projecting) return;
                var body = Image.Decode(FileOperationEndpoints.Inventory(image));
                var zdo = objects.TryGetValue(body.Token, out var id) ? ZDOMan.instance.GetZDO(id) : null;
                if (!body.Active)
                {
                    if (zdo != null) InventoryWorldLifecycle.RecoverRemoved(zdo.m_uid, RetireKind, body.Token);
                    return;
                }
                projecting = true;
                try
                {
                    if (zdo == null)
                    {
                        zdo = ZDOMan.instance.CreateNewZDO(body.Position, body.Prefab); zdo.SetPrefab(body.Prefab);
                        zdo.Persistent = true; zdo.SetOwner(ZDOMan.GetSessionID()); zdo.Set(Marker, body.Token); objects[body.Token] = zdo.m_uid;
                    }
                    if (zdo.GetPrefab() != body.Prefab) throw new InvalidOperationException("World drop prefab identity changed");
                    var item = NativeInventoryCodec.Load(body.Items, 1, 1).GetAllItems().Single();
                    if (body.SourceOrigin.Length > 0) zdo.Set(WorldInventoryOrigin.Key, body.SourceOrigin);
                    ItemDrop.SaveToZDO(item, zdo); ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                }
                finally { projecting = false; }
            }
            private static string AuthorizePickup(string actor, byte[] bytes)
            {
                try
                {
                    var p = new ZPackage(bytes); var zdo = ZDOMan.instance.GetZDO(p.ReadZDOID());
                    if (!Managed(zdo) || !NetworkInventoryAccess.TryPosition(actor, out _, out var position)) return "drop-unavailable";
                    return (position - zdo.GetPosition()).sqrMagnitude <= 25 ? null : "drop-out-of-range";
                }
                catch { return "invalid-pickup-request"; }
            }
            private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Pickup(string actor, byte[] bytes, Func<string, byte[]> read)
            {
                string denied = AuthorizePickup(actor, bytes); if (denied != null) throw new OperationRejectedException(denied);
                var p = new ZPackage(bytes); var zdo = ZDOMan.instance.GetZDO(p.ReadZDOID());
                var delivery = NativeInventoryCodec.Load(OperationPacket.Bytes(p), 32, 32); OperationPacket.End(p);
                if (delivery.NrOfItems() != 0) throw new OperationRejectedException("unexpected-pickup-items");
                string endpoint = Prefix + zdo.GetString(Marker, ""); byte[] before = read(endpoint);
                if (before == null) throw new OperationDeferredException("drop-enrollment-pending");
                var body = Image.Decode(FileOperationEndpoints.Inventory(before));
                if (!body.Active) throw new OperationRejectedException("drop-already-collected");
                if (!NativeInventoryCodec.AddAll(delivery, NativeInventoryCodec.Load(body.Items, 1, 1))) throw new OperationRejectedException("drop-delivery-too-large");
                body.Active = false;
                return Tuple.Create(new[] { Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, body.Encode())) }, NativeInventoryCodec.Save(delivery));
            }
            private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Retire(string actor, byte[] bytes, Func<string, byte[]> read)
            {
                var p = new ZPackage(bytes); string token = p.ReadString(); OperationPacket.End(p);
                string endpoint = Prefix + token; byte[] before = read(endpoint);
                if (before == null) throw new OperationDeferredException("drop-ledger-missing");
                var body = Image.Decode(FileOperationEndpoints.Inventory(before));
                if (!body.Active) return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
                body.Active = false;
                return Tuple.Create(new[] { Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, body.Encode())) }, Array.Empty<byte>());
            }
        }
        internal static bool Pickup(Player player, ItemDrop drop)
        {
            if (player == null || player != Player.m_localPlayer || player.IsDead() || player.IsTeleporting() || CharacterOperationEscrow.Busy || !InventoryOperationNetwork.Ready) return false;
            drop.Load();
            if (NativeInventoryCodec.CapacityFor(player.GetInventory(), drop.m_itemData) < drop.m_itemData.m_stack) return false;
            var view = drop.GetComponent<ZNetView>(); var item = drop.m_itemData.Clone();
            return CharacterOperationEscrow.Begin(PickupKind, escrow =>
            { var p = new ZPackage(); p.Write(view.GetZDO().m_uid); p.Write(escrow); return p.GetArray(); }, Array.Empty<Tuple<ItemDrop.ItemData, int>>(),
                (accepted, reason) => { if (accepted && player == Player.m_localPlayer) player.ShowPickupMessage(item, item.m_stack); });
        }
    }
    [HarmonyPatch(typeof(Humanoid), "Pickup")]
    internal static class ManagedOutputPickup
    {
        private static bool Prefix(Humanoid __instance, GameObject go, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal) return false;
            var item = go == null ? null : go.GetComponent<ItemDrop>();
            if (!InventoryOperationNetwork.Ready || !ManagedWorldDrops.Managed(item?.GetComponent<ZNetView>()?.GetZDO())) return true;
            __result = ManagedWorldDrops.Pickup(__instance as Player, item); return false;
        }
    }
    [HarmonyPatch(typeof(ItemDrop), "CanPickup")]
    internal static class ManagedOutputCanPickup
    {
        private static void Postfix(ItemDrop __instance, bool autoPickupDelay, ref bool __result)
        {
            if (!InventoryOperationNetwork.Ready || !ManagedWorldDrops.Managed(__instance.GetComponent<ZNetView>()?.GetZDO())) return;
            float spawned = (float)AccessTools.Field(typeof(ItemDrop), "m_spawnTime").GetValue(__instance);
            __result = InventoryOperationNetwork.Ready && (!autoPickupDelay || Time.time - spawned >= .5f);
        }
    }
    [HarmonyPatch(typeof(ItemDrop), "AutoStackItems")]
    internal static class ManagedOutputStacking
    {
        private static bool MayMerge(ZNetView view) => view.IsOwner() && (!InventoryOperationNetwork.Ready || !ManagedWorldDrops.Managed(view.GetZDO()));
        private static bool Prefix(ItemDrop __instance) => !InventoryOperationNetwork.Ready || !ManagedWorldDrops.Managed(__instance.GetComponent<ZNetView>()?.GetZDO());
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var owner = AccessTools.Method(typeof(ZNetView), "IsOwner");
            foreach (var code in instructions)
            {
                if (code.Calls(owner)) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(ManagedOutputStacking), nameof(MayMerge)); }
                yield return code;
            }
        }
    }
    [HarmonyPatch(typeof(ItemDrop), "Save")]
    internal static class ManagedOutputNativeSave
    { private static bool Prefix(ItemDrop __instance) => !InventoryOperationNetwork.Ready || !ManagedWorldDrops.Managed(__instance.GetComponent<ZNetView>()?.GetZDO()); }
}
