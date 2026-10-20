using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    internal static class ManagedChestLedger
    {
        internal const string Marker = "runic.inventory.authority.v2";
        internal const string RulesKey = "RunicStorage.ChestRules.v1";
        internal const string SalvageKey = "runic.inventory.salvage.v2";
        private const string CaptureRpc = "RunicOperations_CaptureChest_v2";
        private const string CapturedRpc = "RunicOperations_CapturedChest_v2";
        private const float CaptureWindow = 0.25f;
        private const int MaximumSnapshot = 1024 * 1024;
        private const int MaximumCapturePacket = MaximumSnapshot + 16384;
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.chests.v2", () => new Ledger().Routes());
        internal static void Tick() => LiveChestWorld.Tick();
        internal static void Clear() { LiveChestWorld.Clear(); ((Action)Routes["clear"])(); }
        internal static void Project(ZDO zdo) { }
        internal static byte[] Read(ZDO zdo) => LiveChestWorld.Read(zdo);
        internal static string Endpoint(ZDOID id)
        {
            return LiveChestWorld.Endpoint(id);
        }
        // Native destruction and ordinary opening/saving are never replaced by ledger behavior.
        internal static bool Managed(ZDO zdo) => false;
        internal static byte[] CaptureRemoval(ZDO zdo)
        {
            string token = zdo.GetString(Marker, "");
            if (SharedOperationHost.Read("chest:" + token) != null) return null;
            if (zdo.GetLong(NativeChestInterop.RevisionKey, -1) >= 0)
                throw new OperationRejectedException("chest-ledger-missing");
            var descriptor = NetworkInventoryAccess.Descriptor(zdo);
            if (descriptor == null || !Ledger.NativeSnapshot(zdo, descriptor, out int width, out int height, out byte[] items))
                throw new OperationRejectedException("container-native-snapshot-unavailable");
            var inventory = NativeChestInterop.LoadFlexible(items, width, height);
            return new Image { Prefab = zdo.GetPrefab(), Position = zdo.GetPosition(), Width = inventory.GetWidth(), Height = inventory.GetHeight(),
                Token = token, SourceOrigin = WorldInventoryOrigin.Ensure(zdo), Rules = zdo.GetString(RulesKey, ""),
                AutoRules = zdo.GetString("SmartSort.AutoRules.v1", ""), Items = NativeInventoryCodec.Save(inventory),
                ProductionToken = zdo.GetString("runic.transactions.world-object.token", ""), Creator = zdo.GetLong(ZDOVars.s_creator),
                Salvage = zdo.GetBool(SalvageKey) }.Encode();
        }

        internal sealed class Image
        {
            internal int Prefab, Width, Height;
            internal Vector3 Position;
            internal string Token, Rules = "", AutoRules = "", ProductionToken = "", SourceOrigin = "", NativeReceipts = "";
            internal bool Retired, Salvage;
            internal long Creator;
            internal byte[] Items;
            internal byte[] Encode()
            {
                var packet = new ZPackage(); packet.Write(7); packet.Write(Prefab); packet.Write(Position);
                packet.Write(Width); packet.Write(Height); packet.Write(Token); packet.Write(Rules); packet.Write(AutoRules); packet.Write(Items);
                packet.Write(Retired); packet.Write(Salvage); packet.Write(Creator); packet.Write(ProductionToken); packet.Write(SourceOrigin);
                packet.Write(NativeReceipts);
                return packet.GetArray();
            }
            internal static Image Decode(byte[] bytes)
            {
                var packet = new ZPackage(bytes);
                int version = packet.ReadInt();
                if (version < 2 || version > 7) throw new InvalidDataException("Unsupported chest ledger image");
                var image = new Image { Prefab = packet.ReadInt(), Position = packet.ReadVector3(), Width = packet.ReadInt(), Height = packet.ReadInt(),
                    Token = packet.ReadString(), Rules = packet.ReadString(), AutoRules = version >= 3 ? packet.ReadString() : "", Items = OperationPacket.Bytes(packet, 1024 * 1024) };
                if (version >= 4) { image.Retired = packet.ReadBool(); image.Salvage = packet.ReadBool(); image.Creator = packet.ReadLong(); }
                if (version >= 5) image.ProductionToken = packet.ReadString();
                if (version >= 6) image.SourceOrigin = packet.ReadString();
                if (version >= 7) image.NativeReceipts = packet.ReadString();
                OperationPacket.End(packet);
                if (image.NativeReceipts.Length > 64 * 33 || image.NativeReceipts.Split(',').Any(x => x.Length != 0 && !Guid.TryParseExact(x, "N", out _)))
                    throw new InvalidDataException("Invalid native chest receipt identities");
                if (image.ProductionToken.Length != 0 && !Guid.TryParseExact(image.ProductionToken, "N", out _)) throw new InvalidDataException("Invalid production token");
                if (!Guid.TryParseExact(image.Token, "N", out _) || !NetworkInventoryAccess.Finite(image.Position) ||
                    !NativeInventoryCodec.ValidDimensions(image.Width, image.Height))
                    throw new InvalidDataException("Invalid chest ledger image");
                return image;
            }
        }
        internal static Inventory Inventory(byte[] image)
        {
            Image body = Image.Decode(FileOperationEndpoints.Inventory(image));
            return NativeInventoryCodec.Load(body.Items, body.Width, body.Height);
        }
        internal static byte[] Next(byte[] previous, Inventory inventory, string rules = null, string autoRules = null)
        {
            Image body = Image.Decode(FileOperationEndpoints.Inventory(previous));
            body.Items = NativeInventoryCodec.Save(inventory);
            body.Width = inventory.GetWidth(); body.Height = inventory.GetHeight();
            if (rules != null) body.Rules = rules;
            if (autoRules != null) body.AutoRules = autoRules;
            return FileOperationEndpoints.Next(previous, body.Encode());
        }

        private sealed class Capture
        {
            internal string Nonce;
            internal long Owner;
            internal float Retry, Started;
            internal string Failure;
        }
        private sealed class Ledger
        {
            private readonly Dictionary<ZDOID, Capture> captures = new Dictionary<ZDOID, Capture>();
            private readonly Dictionary<ZDOID, byte[]> known = new Dictionary<ZDOID, byte[]>();
            private readonly Dictionary<string, ZDOID> byEndpoint = new Dictionary<string, ZDOID>();
            private readonly List<Image> identities = new List<Image>();
            private bool indexed;
            private ZRoutedRpc registered;
            private bool projecting;
            internal Dictionary<string, Delegate> Routes() => new Dictionary<string, Delegate>
            {
                ["tick"] = new Action(() => { Register(); RecoverSalvage(); FinishCaptures(); }),
                ["clear"] = new Action(() => { captures.Clear(); known.Clear(); byEndpoint.Clear(); identities.Clear(); indexed = false; projecting = false; }),
                ["project"] = new Action<ZDO>(Project),
                ["read"] = new Func<ZDO, byte[]>(Read)
            };
            private void Register()
            {
                if (!InventoryOperationNetwork.Ready || ZRoutedRpc.instance == null || ReferenceEquals(registered, ZRoutedRpc.instance)) return;
                registered = ZRoutedRpc.instance;
                registered.Register<ZPackage>(CaptureRpc, CaptureRequest);
                registered.Register<ZPackage>(CapturedRpc, CaptureReply);
                WorldInventoryOrigin.Register("chest:", bytes => { var body = Image.Decode(bytes); return Tuple.Create(body.SourceOrigin, body.Prefab, body.Position); },
                    (zdo, endpoint, image) => {
                        var body = Image.Decode(FileOperationEndpoints.Inventory(image));
                        string existing = zdo.GetString(Marker, "");
                        if (existing.Length != 0 && existing != body.Token) throw new InvalidOperationException("Chest origin points to another ledger");
                        zdo.Set(Marker, body.Token); byEndpoint[endpoint] = zdo.m_uid; known[zdo.m_uid] = image;
                        if (!body.Retired) Publish(zdo, body, FileOperationEndpoints.Revision(image));
                    }, (endpoint, image) => {
                        var body = Image.Decode(FileOperationEndpoints.Inventory(image));
                        if (body.Retired || body.Salvage || InventoryWorldLifecycle.ChestRemovalPending(body.Token)) return;
                        var missing = ZDOMan.instance.CreateNewZDO(body.Position, body.Prefab);
                        missing.SetPrefab(body.Prefab); missing.Set(Marker, body.Token); missing.Set(WorldInventoryOrigin.Key, body.SourceOrigin);
                        missing.SetOwner(ZDOMan.GetSessionID()); byEndpoint[endpoint] = missing.m_uid;
                        InventoryWorldLifecycle.RetireMissingChest(missing.m_uid, body.Token);
                    });
                SharedOperationHost.RegisterProjection("chest:", (endpoint, image) =>
                {
                    var body = Image.Decode(FileOperationEndpoints.Inventory(image));
                    identities.RemoveAll(x => x.Token == body.Token); identities.Add(body);
                    if (body.Retired) { RetiredChestContents.Enqueue(body); return; }
                    if (!byEndpoint.TryGetValue(endpoint, out ZDOID id) || ZDOMan.instance.GetZDO(id) == null)
                    {
                        var matches = NetworkInventoryAccess.Nearby(body.Position, 1f)
                            .Where(z => z.GetString(Marker, "") == body.Token).Take(2).ToArray();
                        if (matches.Length == 0 && body.Salvage && FileOperationEndpoints.Revision(image) > 0)
                        {
                            var recovered = ZDOMan.instance.CreateNewZDO(body.Position, body.Prefab);
                            recovered.SetPrefab(body.Prefab); recovered.Persistent = true; recovered.Type = ZDO.ObjectType.Default;
                            recovered.SetOwner(ZDOMan.GetSessionID()); recovered.Set(Marker, body.Token);
                            recovered.Set(ZDOVars.s_creator, body.Creator); recovered.Set(SalvageKey, true);
                            matches = new[] { recovered };
                        }
                        if (matches.Length == 0 || matches.Length != 1 && !body.Salvage) throw new InvalidOperationException("Chest projection identity could not be resolved uniquely");
                        byEndpoint[endpoint] = id = matches[0].m_uid;
                    }
                    var zdo = ZDOMan.instance?.GetZDO(id);
                    if (zdo == null) throw new InvalidOperationException("Chest was removed before projection completed");
                    ValidateIdentity(zdo, body);
                    known[id] = image; Publish(zdo, body, FileOperationEndpoints.Revision(image));
                });
            }
            private void RecoverSalvage()
            {
                if (indexed || ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active) return;
                foreach (var persisted in SharedOperationHost.Enumerate("chest:"))
                {
                    var body = Image.Decode(FileOperationEndpoints.Inventory(persisted.Item2)); identities.Add(body);
                    if (body.Salvage && !body.Retired && FileOperationEndpoints.Revision(persisted.Item2) > 0) SharedOperationHost.RefreshProjection(persisted.Item1);
                }
                indexed = true;
            }
            private byte[] Read(ZDO zdo)
            {
                if (zdo == null || ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active)
                    throw new OperationDeferredException("inventory-authority-starting");
                Register();
                Container descriptor = NetworkInventoryAccess.Descriptor(zdo);
                if (descriptor == null) throw new OperationRejectedException("unsupported-container");
                if (!indexed)
                {
                    foreach (var persisted in SharedOperationHost.Enumerate("chest:"))
                        identities.Add(Image.Decode(FileOperationEndpoints.Inventory(persisted.Item2)));
                    indexed = true;
                }
                string token = zdo.GetString(Marker, "");
                if (token.Length == 0 && identities.Any(x => !x.Retired && x.Prefab == zdo.GetPrefab() && x.Position == zdo.GetPosition()))
                    throw new OperationRejectedException("chest-identity-missing-after-world-rollback");
                byte[] image = token.Length == 0 ? null : SharedOperationHost.Read(Endpoint(zdo.m_uid));
                if (image != null)
                {
                    byEndpoint[Endpoint(zdo.m_uid)] = zdo.m_uid;
                    Image body = Image.Decode(FileOperationEndpoints.Inventory(image));
                    if (body.Retired) throw new OperationRejectedException("chest-was-destroyed");
                    ValidateIdentity(zdo, body); known[zdo.m_uid] = image;
                    NativeChestInterop.Published(zdo, body.Items, body.Width, body.Height, FileOperationEndpoints.Revision(image), body.NativeReceipts);
                    NativeChestInterop.ObserveReplica(zdo);
                    Publish(zdo, body, FileOperationEndpoints.Revision(image)); return image;
                }
                // Older capture code wrote the authority marker on the owner before the
                // server had imported its reply. Recover that unfinished enrollment with
                // the same identity, but never recreate a missing, previously published
                // ledger from a potentially older native world snapshot.
                if (token.Length != 0 && zdo.GetLong(NativeChestInterop.RevisionKey, -1) >= 0)
                    throw new OperationRejectedException("chest-ledger-missing");
                string importToken = token.Length != 0 ? token : captures.TryGetValue(zdo.m_uid, out var previousCapture) ? previousCapture.Nonce : null;
                long owner = zdo.GetOwner();
                if (owner == 0 || owner == ZDOMan.GetSessionID() || ZNet.instance.GetPeer(owner) == null)
                    return ImportNative(zdo, importToken);
                if (!captures.TryGetValue(zdo.m_uid, out var capture))
                {
                    // Admission pressure is not a reason to suspend the player's escrow.
                    // Replicated native state remains a usable source when no capture slot
                    // or compatible live owner is available.
                    if (captures.Count >= 256 || !InventoryOperationNetwork.Compatible(owner))
                        return ImportNative(zdo, importToken);
                    capture = new Capture { Owner = owner, Nonce = token.Length == 0 ? Guid.NewGuid().ToString("N") : token,
                        Started = Time.realtimeSinceStartup };
                    captures[zdo.m_uid] = capture;
                }
                if (token.Length != 0 && capture.Nonce != token) capture.Nonce = token;
                if (capture.Owner != owner) { capture.Owner = owner; capture.Retry = 0; }
                // The window belongs to this chest, not to its current network owner.
                // Ownership changes and retries cannot restart it indefinitely.
                if (capture.Failure != null || Time.realtimeSinceStartup - capture.Started >= CaptureWindow)
                    return ImportNative(zdo, capture.Nonce);
                RequestCapture(zdo, capture);
                // A routed request can complete synchronously on a local transport.
                byte[] captured = SharedOperationHost.Read("chest:" + capture.Nonce);
                if (captured != null) return captured;
                throw new OperationDeferredException("container-enrollment-pending");
            }
            private void RequestCapture(ZDO zdo, Capture capture)
            {
                if (registered != null && Time.realtimeSinceStartup >= capture.Retry && InventoryOperationNetwork.Compatible(capture.Owner))
                {
                    capture.Retry = Time.realtimeSinceStartup + .1f;
                    var packet = new ZPackage(); packet.Write(zdo.m_uid); packet.Write(capture.Nonce);
                    registered.InvokeRoutedRPC(capture.Owner, CaptureRpc, packet);
                }
            }
            private void FinishCaptures()
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active) return;
                foreach (var stale in captures.Where(x => x.Value.Failure != null && Time.realtimeSinceStartup - x.Value.Started > 60).Select(x => x.Key).ToArray())
                    captures.Remove(stale);
                foreach (var pair in captures.Where(x => x.Value.Failure == null).ToArray().Take(32))
                {
                    var zdo = ZDOMan.instance.GetZDO(pair.Key); var capture = pair.Value;
                    if (zdo == null) { captures.Remove(pair.Key); continue; }
                    if (Time.realtimeSinceStartup - capture.Started < CaptureWindow) continue;
                    string token = zdo.GetString(Marker, "");
                    if (token.Length != 0)
                    {
                        if (SharedOperationHost.Read("chest:" + token) != null) { captures.Remove(pair.Key); continue; }
                        if (zdo.GetLong(NativeChestInterop.RevisionKey, -1) >= 0) { capture.Failure = "chest-ledger-missing"; continue; }
                        capture.Nonce = token;
                    }
                    try { ImportNative(zdo, capture.Nonce); }
                    catch (Exception error)
                    {
                        // A subsequent operation can retry the native data after the
                        // chest initializes; the current action gets a final rejection.
                        capture.Failure = error.Message;
                    }
                }
            }
            private byte[] ImportNative(ZDO zdo, string token)
            {
                Container descriptor = NetworkInventoryAccess.Descriptor(zdo);
                if (descriptor == null) throw new OperationRejectedException("unsupported-container");
                try
                {
                    if (!NativeSnapshot(zdo, descriptor, out int width, out int height, out byte[] items))
                        throw new OperationRejectedException("container-native-snapshot-unavailable");
                    return Import(zdo, width, height, items, token);
                }
                catch (OperationRejectedException) { throw; }
                catch (Exception) { throw new OperationRejectedException("container-native-inventory-invalid"); }
            }
            internal static bool NativeSnapshot(ZDO zdo, Container descriptor, out int width, out int height, out byte[] items)
            {
                bool drawer = ManagedDrawerInventory.IsDrawer(descriptor);
                width = drawer ? 1 : descriptor.m_width; height = drawer ? 1 : descriptor.m_height;
                if (drawer) { items = ManagedDrawerInventory.ReadNative(zdo); return true; }
                items = zdo.GetByteArray(ZDOVars.s_items);
                var root = ZNetScene.instance?.FindInstance(zdo.m_uid);
                var chest = root == null ? null : root.GetComponent<Container>();
                var view = chest == null ? null : ContainerAuthority.View(chest);
                if (view != null && view.IsValid() && view.IsOwner() && chest.GetInventory() is Inventory live)
                {
                    // Do not Load over a live owner's unreplicated edits. The only special
                    // case is an uninitialized empty instance, before its first native Load.
                    bool uninitialized = (uint)AccessTools.Field(typeof(Container), "m_lastRevision").GetValue(chest) == uint.MaxValue;
                    if (!uninitialized || live.NrOfItems() != 0 || items == null)
                    { width = live.GetWidth(); height = live.GetHeight(); items = NativeInventoryCodec.Save(live); }
                }
                if (items == null && (descriptor.m_defaultItems == null || descriptor.m_defaultItems.m_drops.Count == 0))
                    items = NativeInventoryCodec.Save(new Inventory("Native empty chest", null, width, height));
                return items != null;
            }
            private byte[] Import(ZDO zdo, int width, int height, byte[] items, string token = null)
            {
                string currentToken = zdo.GetString(Marker, "");
                if (currentToken.Length != 0 && currentToken != token)
                    throw new OperationRejectedException("container-ledger-identity-conflict");
                try
                {
                    Inventory native = NativeChestInterop.LoadFlexible(items, width, height);
                    width = native.GetWidth(); height = native.GetHeight();
                }
                catch (InvalidDataException) { throw new OperationRejectedException("container-native-inventory-invalid"); }
                var body = new Image { Prefab = zdo.GetPrefab(), Position = zdo.GetPosition(), Width = width, Height = height,
                    Token = token ?? Guid.NewGuid().ToString("N"), SourceOrigin = WorldInventoryOrigin.Ensure(zdo), Rules = zdo.GetString(RulesKey, ""), AutoRules = zdo.GetString("SmartSort.AutoRules.v1", ""), Items = items, ProductionToken = zdo.GetString("runic.transactions.world-object.token", ""), Creator = zdo.GetLong(ZDOVars.s_creator) };
                string endpoint = "chest:" + body.Token;
                byte[] image = SharedOperationHost.Import(endpoint, body.Encode());
                body = Image.Decode(FileOperationEndpoints.Inventory(image));
                ValidateIdentity(zdo, body); known[zdo.m_uid] = image; captures.Remove(zdo.m_uid);
                identities.Add(body); byEndpoint[endpoint] = zdo.m_uid;
                Publish(zdo, body, FileOperationEndpoints.Revision(image)); return image;
            }
            private static void ValidateIdentity(ZDO zdo, Image body)
            {
                string token = zdo.GetString(Marker, "");
                if (body.Prefab != zdo.GetPrefab() ||
                    (token.Length != 0 && token != body.Token))
                    throw new OperationRejectedException("container-ledger-identity-conflict");
            }
            private void Project(ZDO zdo)
            {
                if (projecting || zdo == null || ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active) return;
                if (!known.ContainsKey(zdo.m_uid) && !Managed(zdo)) return;
                string endpoint = known.TryGetValue(zdo.m_uid, out byte[] previous)
                    ? "chest:" + Image.Decode(FileOperationEndpoints.Inventory(previous)).Token : Endpoint(zdo.m_uid);
                byte[] image = SharedOperationHost.Read(endpoint);
                if (image == null) throw new InvalidDataException("Managed chest is missing its authoritative inventory ledger");
                Image body = Image.Decode(FileOperationEndpoints.Inventory(image));
                ValidateIdentity(zdo, body); known[zdo.m_uid] = image; byEndpoint[endpoint] = zdo.m_uid;
                NativeChestInterop.Published(zdo, body.Items, body.Width, body.Height, FileOperationEndpoints.Revision(image), body.NativeReceipts);
                NativeChestInterop.ObserveReplica(zdo);
                Publish(zdo, body, FileOperationEndpoints.Revision(image));
            }
            private void Publish(ZDO zdo, Image image, long revision)
            {
                if (projecting) return;
                projecting = true;
                try
                {
                    bool changed = false;
                    NativeChestInterop.Published(zdo, image.Items, image.Width, image.Height, revision, image.NativeReceipts);
                    if (zdo.GetLong(NativeChestInterop.RevisionKey, -1) != revision) { zdo.Set(NativeChestInterop.RevisionKey, revision); changed = true; }
                    if (zdo.GetString(NativeChestInterop.ReceiptsKey, "") != image.NativeReceipts) { zdo.Set(NativeChestInterop.ReceiptsKey, image.NativeReceipts); changed = true; }
                    if (zdo.GetBool(SalvageKey) != image.Salvage) { zdo.Set(SalvageKey, image.Salvage); changed = true; }
                    if (zdo.GetString(Marker, "") != image.Token) { zdo.Set(Marker, image.Token); changed = true; }
                    if (image.SourceOrigin.Length != 0 && zdo.GetString(WorldInventoryOrigin.Key, "") != image.SourceOrigin) { zdo.Set(WorldInventoryOrigin.Key, image.SourceOrigin); changed = true; }
                    string productionToken = image.ProductionToken.Length == 0 ? image.Token : image.ProductionToken;
                    if (zdo.GetString("runic.transactions.world-object.token", "") != productionToken) { zdo.Set("runic.transactions.world-object.token", productionToken); zdo.Set("runic.transactions.world-object.token-present", true); changed = true; }
                    if (zdo.GetString(RulesKey, "") != image.Rules) { zdo.Set(RulesKey, image.Rules); changed = true; }
                    if (zdo.GetString("SmartSort.AutoRules.v1", "") != image.AutoRules) { zdo.Set("SmartSort.AutoRules.v1", image.AutoRules); changed = true; }
                    if (!OwnerOperationQueue.Same(zdo.GetByteArray(ZDOVars.s_items), image.Items))
                    { zdo.Set(ZDOVars.s_items, image.Items); changed = true; }
                    if (ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(zdo)))
                    {
                        string native = ManagedDrawerInventory.NativePayload(NativeInventoryCodec.Load(image.Items, 1, 1));
                        if (zdo.GetString("items", "") != native) { zdo.Set("items", native); changed = true; }
                    }
                    if (changed) ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                }
                finally { projecting = false; }
            }
            private void CaptureRequest(long sender, ZPackage packet)
            {
                ZDOID id = ZDOID.None; string nonce = null;
                try
                {
                    if (!InventoryOperationNetwork.Ready || ZNet.instance?.GetServerPeer()?.m_uid != sender || packet == null || packet.Size() > 128) return;
                    id = packet.ReadZDOID(); nonce = packet.ReadString(); OperationPacket.End(packet);
                    if (!Guid.TryParseExact(nonce, "N", out _)) return;
                    // A connected peer can remain the ZDO owner after teleporting away
                    // and unloading its Container component. The owned ZDO is sufficient.
                    ZDO zdo = ZDOMan.instance?.GetZDO(id);
                    Container descriptor = NetworkInventoryAccess.Descriptor(zdo);
                    if (zdo == null || zdo.GetOwner() != ZDOMan.GetSessionID() || descriptor == null)
                    { SendCaptureReply(sender, id, nonce, 1, 0, 0, 0, null); return; }
                    string existing = zdo.GetString(Marker, "");
                    if (existing.Length != 0 && existing != nonce)
                    { SendCaptureReply(sender, id, nonce, 1, 0, 0, 0, null); return; }
                    if (!NativeSnapshot(zdo, descriptor, out int width, out int height, out byte[] items))
                    { SendCaptureReply(sender, id, nonce, 1, 0, 0, 0, null); return; }
                    // Do not install an authority marker until the server has an image.
                    // Premature markers previously froze writes and orphaned the identity
                    // when the reply was lost or ownership changed before it arrived.
                    SendCaptureReply(sender, id, nonce, 0, zdo.OwnerRevision, width, height, items);
                }
                catch (Exception error)
                {
                    if (!id.IsNone() && Guid.TryParseExact(nonce, "N", out _))
                        SendCaptureReply(sender, id, nonce, 1, 0, 0, 0, null);
                    MutationGate.Report("chest enrollment capture used replicated fallback: " + error.Message);
                }
            }
            private void SendCaptureReply(long receiver, ZDOID id, string nonce, int status, uint ownerRevision, int width, int height, byte[] items)
            {
                var reply = new ZPackage(); reply.Write(1); reply.Write(id); reply.Write(nonce); reply.Write(status);
                if (status == 0)
                {
                    if (items == null || items.Length > MaximumSnapshot) throw new InvalidDataException("Chest snapshot exceeds supported capacity");
                    byte[] compressed;
                    using (var buffer = new MemoryStream())
                    {
                        using (var zip = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true))
                            zip.Write(items, 0, items.Length);
                        compressed = buffer.ToArray();
                    }
                    reply.Write((int)ownerRevision); reply.Write(width); reply.Write(height); reply.Write(compressed);
                }
                if (reply.Size() > MaximumCapturePacket) throw new InvalidDataException("Compressed chest snapshot exceeds supported capacity");
                registered?.InvokeRoutedRPC(receiver, CapturedRpc, reply);
            }
            private void CaptureReply(long sender, ZPackage packet)
            {
                try
                {
                    if (ZNet.instance == null || !ZNet.instance.IsServer() || !InventoryOperationNetwork.Compatible(sender) || packet == null || packet.Size() > MaximumCapturePacket) return;
                    if (packet.ReadInt() != 1) return;
                    ZDOID id = packet.ReadZDOID(); string nonce = packet.ReadString(); int status = packet.ReadInt();
                    if (status < 0 || status > 1) return;
                    if (!captures.TryGetValue(id, out var capture) || capture.Nonce != nonce || capture.Owner != sender) return;
                    ZDO zdo = ZDOMan.instance.GetZDO(id);
                    if (zdo == null || NetworkInventoryAccess.Descriptor(zdo) == null) return;
                    // A fallback may already have imported and consumed this stock. Never
                    // replay a late capture over that authoritative endpoint.
                    if (SharedOperationHost.Read("chest:" + nonce) != null) { captures.Remove(id); return; }
                    if (status != 0)
                    {
                        OperationPacket.End(packet);
                        try { ImportNative(zdo, nonce); }
                        catch (Exception error) { capture.Failure = error.Message; }
                        return;
                    }
                    int ownerRevision = packet.ReadInt(), width = packet.ReadInt(), height = packet.ReadInt();
                    byte[] compressed = OperationPacket.Bytes(packet, MaximumCapturePacket); OperationPacket.End(packet);
                    if (zdo.GetOwner() != sender || zdo.OwnerRevision != (uint)ownerRevision)
                    {
                        // The old owner cannot authorize a stale snapshot after handoff.
                        // Its rejected response still completes enrollment from current
                        // server-native data instead of restarting an endless capture loop.
                        try { ImportNative(zdo, nonce); }
                        catch (Exception error) { capture.Failure = error.Message; }
                        return;
                    }
                    byte[] items;
                    using (var input = new MemoryStream(compressed, false))
                    using (var zip = new DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[4096]; int count;
                        while ((count = zip.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (output.Length + count > MaximumSnapshot) throw new InvalidDataException("Captured chest snapshot exceeds supported capacity");
                            output.Write(buffer, 0, count);
                        }
                        items = output.ToArray();
                    }
                    Import(zdo, width, height, items, nonce);
                }
                catch (Exception error) { MutationGate.Report("chest enrollment response used replicated fallback: " + error.Message); }
            }
        }
    }
}
