using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

public sealed partial class NativeAccessProbe
{
    private Container targetChest;
    private Container pendingChest;
    private long expectedOwner;
    private string lastCommand;
    private float nextSnapshot, requestedAt, releaseAt;
    private bool networkReady, granted, ownsLogicalAccess;
    private string previousSnapshot;
    private bool consumeOne;
    private float ownerPacketAt;
    private uint ownerPacketRevision;
    private int consumedThisAccess;

    private static void ObserveReplication(ZRpc rpc, ZPackage pkg)
    {
        if (instance == null || instance.pendingChest == null || rpc != ZNet.instance?.GetServerPeer()?.m_rpc) return;
        var copy = new ZPackage(pkg.GetArray());
        int invalidated = copy.ReadInt();
        for (int i = 0; i < invalidated; i++) copy.ReadZDOID();
        var body = new ZPackage();
        var expected = instance.pendingChest.GetComponent<ZNetView>().GetZDO().m_uid;
        while (true)
        {
            var id = copy.ReadZDOID(); if (id.IsNone()) break;
            ushort ownerRevision = copy.ReadUShort(); uint dataRevision = copy.ReadUInt();
            long owner = copy.ReadLong(); copy.ReadVector3(); copy.ReadPackage(ref body);
            if (id != expected || owner != ZNet.GetUID()) continue;
            instance.ownerPacketAt = Time.realtimeSinceStartup;
            instance.ownerPacketRevision = dataRevision;
            instance.Log("SERVER_OWNER_PACKET source=" + id + " owner=" + owner + " ownerRevision=" + ownerRevision + " dataRevision=" + dataRevision);
        }
    }

    private static bool IncomingOpen(Container __instance, long uid, bool granted)
    {
        if (instance == null || !instance.NetworkMode || instance.pendingChest != __instance) return true;
        instance.Log("NATIVE_OPEN_RESPONSE sender=" + uid + " expectedOwner=" + instance.expectedOwner + " granted=" + granted + " elapsedMs=" + ((Time.realtimeSinceStartup-instance.requestedAt)*1000));
        // Diagnostic response interception prevents a forced visible chest panel.
        // No debit is authorized. Ownership/freshness observations follow separately.
        if (uid != instance.expectedOwner) { instance.Log("UNEXPECTED_RESPONSE_SENDER no acquisition"); return false; }
        instance.granted = granted;
        if (!granted) instance.pendingChest = null;
        return false;
    }
    private void NetworkUpdate()
    {
        try
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                if (Time.realtimeSinceStartup - started > 210) { Log("INCOMPLETE actual client join timeout"); Application.Quit(2); }
                return;
            }
            if (!networkReady)
            {
                networkReady = true;
                Player.m_localPlayer.SetGodMode(true);
                AccessTools.Field(typeof(Player), "m_enableAutoPickup").SetValue(null, false);
                Log("REAL_CLIENT_JOINED pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " world=" + ZNet.instance.GetWorldUID() + " localPeer=" + ZNet.GetUID() + " character=" + Player.m_localPlayer.GetPlayerID());
            }
            if (targetChest == null && File.Exists(Path.Combine(Root,"target.txt")))
            {
                var parts = File.ReadAllText(Path.Combine(Root,"target.txt")).Trim().Split(':');
                var restored = ZNetScene.instance.FindInstance(new ZDOID(long.Parse(parts[0]),uint.Parse(parts[1])));
                if (restored != null) { targetChest = restored.GetComponent<Container>(); Log("FIXTURE_RELOADED " + ChestState()); }
            }
            if (pendingChest != null && granted)
            {
                var view = pendingChest.GetComponent<ZNetView>();
                if (view.IsOwner() && !ownsLogicalAccess && (expectedOwner == ZNet.GetUID() || ownerPacketAt >= requestedAt))
                {
                    Require(!pendingChest.IsInUse(), "local chest became busy before diagnostic execution");
                    Require(!pendingChest.m_checkGuardStone || PrivateArea.CheckAccess(pendingChest.transform.position,0f,false), "ward changed before execution");
                    Require((bool)Call(pendingChest,"CheckAccess",Player.m_localPlayer.GetPlayerID()), "permission changed before execution");
                    Require(Vector3.Distance(Player.m_localPlayer.transform.position,pendingChest.transform.position) <= 20f, "outside diagnostic range");
                    Call(pendingChest, "Load");
                    Log("OWNER_AND_LOAD_OBSERVED " + ChestState() + " elapsedMs=" + ((Time.realtimeSinceStartup-requestedAt)*1000) + "; observation alone not freshness proof");
                    pendingChest.SetInUse(true);
                    ownsLogicalAccess = true;
                    releaseAt = Time.realtimeSinceStartup + .25f;
                    if (consumeOne)
                    {
                        int before = Wood(pendingChest.GetInventory());
                        var slice = pendingChest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Wood" && i.m_stack > 0);
                        Require(slice != null && pendingChest.GetInventory().RemoveItem(slice,1), "diagnostic Wood debit failed");
                        consumedThisAccess = 1;
                        Require(Wood(pendingChest.GetInventory()) == before-1, "diagnostic debit count mismatch");
                        consumeOne = false;
                        Log("DIAGNOSTIC_CONSUMPTION wood=1 before=" + before + " after=" + Wood(pendingChest.GetInventory()) + "; no crafted output claimed; packetRevision=" + ownerPacketRevision);
                    }
                }
                if (ownsLogicalAccess && Time.realtimeSinceStartup >= releaseAt)
                {
                    if (view.IsOwner()) pendingChest.SetInUse(false);
                    Log("NATIVE_ACCESS_RELEASED consumedWood=" + consumedThisAccess + " " + ChestState());
                    ownsLogicalAccess = false; pendingChest = null; granted = false;
                }
            }
            if (Time.realtimeSinceStartup > nextSnapshot)
            {
                nextSnapshot = Time.realtimeSinceStartup + .25f;
                string state = ChestState();
                if (state != previousSnapshot) { previousSnapshot = state; Log("OBSERVE " + state); }
            }
            string file = Path.Combine(Root, "command.txt");
            string command;
            try { command = File.Exists(file) ? File.ReadAllText(file).Trim() : ""; } catch(IOException) { return; }
            if (command.Length == 0 || command == lastCommand) return;
            lastCommand = command;
            string verb = command.Split(' ')[0];
            Log("COMMAND " + command);
            if (verb == "seed") SeedChest();
            else if (verb == "acquire") BeginAcquire();
            else if (verb == "debit") BeginAcquire(true);
            else if (verb == "snapshot") Log("SNAPSHOT " + ChestState());
            else if (verb == "saveworld") { ZNet.instance.SaveWorldAndPlayerProfiles(); Log("NATIVE_SAVE_REQUESTED await server completion log"); }
            else if (verb == "savequit")
            {
                if (ownsLogicalAccess && pendingChest != null && pendingChest.IsOwner()) pendingChest.SetInUse(false);
                AccessTools.Method(typeof(Game), "Shutdown").Invoke(Game.instance, new object[] {true});
                Application.Quit();
            }
        }
        catch (Exception error)
        {
            if (ownsLogicalAccess && pendingChest != null && pendingChest.IsOwner()) pendingChest.SetInUse(false);
            ownsLogicalAccess = false;
            Log("NETWORK_PROBE_ERROR no speculative refund; consumedWood=" + consumedThisAccess + " " + error);
            enabled = false;
        }
    }
    private void SeedChest()
    {
        Require(targetChest == null, "fixture already seeded");
        var player = Player.m_localPlayer;
        var site = player.transform.position + Vector3.forward * 5;
        site.y = ZoneSystem.instance.GetGroundHeight(site) + .1f;
        var obj = Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), site, Quaternion.identity);
        obj.GetComponent<Piece>().SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
        obj.GetComponent<WearNTear>().OnPlaced();
        targetChest = obj.GetComponent<Container>();
        var prefab = ObjectDB.instance.GetItemPrefab("Wood");
        var wood = prefab.GetComponent<ItemDrop>().m_itemData.Clone(); wood.m_dropPrefab = prefab; wood.m_stack = 10;
        Require(targetChest.GetInventory().AddItem(wood), "seed failed");
        var zdo = obj.GetComponent<ZNetView>().GetZDO();
        File.WriteAllText(Path.Combine(Root,"target.txt"), zdo.m_uid.ToString());
        Log("SEEDED wood=10 position=" + site + " " + ChestState());
    }
    private void BeginAcquire(bool debit = false)
    {
        Require(targetChest != null && pendingChest == null, "one outstanding diagnostic request only");
        Require(!targetChest.m_checkGuardStone || PrivateArea.CheckAccess(targetChest.transform.position,0f,false), "ward denied");
        Require((bool)Call(targetChest,"CheckAccess",Player.m_localPlayer.GetPlayerID()), "native permission denied");
        var view = targetChest.GetComponent<ZNetView>();
        expectedOwner = view.GetZDO().GetOwner(); pendingChest = targetChest;
        granted = false; consumeOne = debit; consumedThisAccess = 0; requestedAt = Time.realtimeSinceStartup; ownerPacketAt = -1;
        Log("NATIVE_REQUEST_OPEN expectedOwner=" + expectedOwner + " " + ChestState());
        view.InvokeRPC("RPC_RequestOpen", Player.m_localPlayer.GetPlayerID());
    }
    private string ChestState()
    {
        if (targetChest == null) return "no-target";
        var zdo = targetChest.GetComponent<ZNetView>().GetZDO();
        return "source=" + zdo.m_uid + " owner=" + zdo.GetOwner() + " ownerRevision=" + zdo.OwnerRevision + " dataRevision=" + zdo.DataRevision +
            " localOwner=" + targetChest.IsOwner() + " localInUse=" + targetChest.IsInUse() + " replicatedInUse=" + zdo.GetInt(ZDOVars.s_inUse,0) + " wood=" + Wood(targetChest.GetInventory());
    }
}
