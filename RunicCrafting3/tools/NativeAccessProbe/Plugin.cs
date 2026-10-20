using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

// Validation helper only. Native handler execution with simulated peer IDs is
// deliberately NOT reported as multiplayer evidence or an access implementation.
[BepInPlugin("chazman.validation.crafting3.native", "Crafting 3 Native Access Probe", "0.0.1")]
public sealed partial class NativeAccessProbe : BaseUnityPlugin
{
    private static NativeAccessProbe instance;
    private static readonly List<string> replies = new List<string>();
    private static bool capture;
    private bool booted, ran;
    private float started;
    private Harmony patches;
    private string Root => Paths.GameRootPath;
    private bool NetworkMode => File.Exists(Path.Combine(Root, "network-target.txt"));
    private void Awake()
    {
        instance = this;
        started = Time.realtimeSinceStartup;
        string save = Path.Combine(Root, "test-saves");
        Directory.CreateDirectory(save);
        Utils.SetSaveDataPath(save);
        Utils.persistantDataPath = save;
        patches = new Harmony("chazman.validation.crafting3.native");
        patches.Patch(AccessTools.Method(typeof(FejdStartup), "Start"),
            postfix: new HarmonyMethod(typeof(NativeAccessProbe), nameof(Startup)));
        patches.Patch(AccessTools.Method(typeof(ZNetView), "InvokeRPC", new[] {typeof(long), typeof(string), typeof(object[])}),
            prefix: new HarmonyMethod(typeof(NativeAccessProbe), nameof(CaptureReply)));
        patches.Patch(AccessTools.Method(typeof(ZNetView), "InvokeRPC", new[] {typeof(string), typeof(object[])}),
            prefix: new HarmonyMethod(typeof(NativeAccessProbe), nameof(CaptureCooking)));
        if (NetworkMode)
        {
            patches.Patch(AccessTools.Method(typeof(Container), "RPC_OpenResponse"), prefix: new HarmonyMethod(typeof(NativeAccessProbe), nameof(IncomingOpen)));
            patches.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: new HarmonyMethod(typeof(NativeAccessProbe), nameof(ObserveReplication)));
        }
        Log(NetworkMode ? "EVIDENCE_CLASS real network crafter probe; no mutation authorized by an owner flag alone" : "EVIDENCE_CLASS native-installed-methods single-process simulated-peer-ids NO real network proof");
    }
    private static bool CaptureReply(ZNetView __instance, long targetID, string method, object[] parameters)
    {
        if (__instance == delayedCookingView && method == "RPC_AddItem")
        { delayedCookingPayload = (object[])parameters.Clone(); return false; }
        if (!capture || !(method == "RPC_OpenResponse" || method == "RPC_TakeAllResponse" || method == "RPC_StackResponse")) return true;
        replies.Add(targetID + ":" + method + ":" + parameters[0]);
        return false;
    }
    private static void Startup(FejdStartup __instance)
    {
        if (instance.booted) return;
        instance.booted = true;
        const string name = "Crafting3NativeProbe";
        var profile = new PlayerProfile(name, FileHelpers.FileSource.Local);
        if (!profile.Load()) { profile.SetName(name); profile.m_firstSpawn = false; profile.Save(); }
        Game.SetProfile(name, FileHelpers.FileSource.Local);
        if (instance.NetworkMode)
        {
            string[] target = File.ReadAllLines(Path.Combine(instance.Root, "network-target.txt"));
            AccessTools.Property(typeof(FejdStartup), "ServerPassword").SetValue(null, target[2]);
            AccessTools.Field(typeof(FejdStartup), "m_joinServer").SetValue(__instance,
                new ServerJoinData(new ServerJoinDataDedicated(target[0] + ":" + target[1])));
            AccessTools.Field(typeof(FejdStartup), "m_instantStart").SetValue(__instance, true);
            __instance.JoinServer();
            instance.Log("BOOT network client isolated profile=" + name);
            return;
        }
        var world = World.GetCreateWorld("Crafting3NativeProbe", FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, world.m_name, "", world);
        ZNet.ResetServerHost();
        AccessTools.Field(typeof(FejdStartup), "m_startingWorld").SetValue(__instance, true);
        AccessTools.Field(typeof(FejdStartup), "m_instantStart").SetValue(__instance, true);
        AccessTools.Method(typeof(FejdStartup), "TransitionToMainScene").Invoke(__instance, null);
        instance.Log("BOOT isolated local host world=" + world.m_name);
    }
    private void Log(string line)
    {
        line = DateTime.UtcNow.ToString("O") + " " + line;
        Logger.LogInfo(line);
        File.AppendAllText(Path.Combine(Root, "native-access-evidence.log"), line + "\n");
    }
    private static object Call(Container chest, string method, params object[] args) =>
        AccessTools.Method(typeof(Container), method).Invoke(chest, args);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static int Wood(Inventory inv) => inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Wood").Sum(i => i.m_stack);
    private void Update()
    {
        if (NetworkMode) { NetworkUpdate(); return; }
        if (ran) return;
        if (Player.m_localPlayer == null || ZNetScene.instance == null || ObjectDB.instance == null)
        {
            if (Time.realtimeSinceStartup - started > 180) { ran = true; Log("INCOMPLETE native host startup timeout"); Application.Quit(2); }
            return;
        }
        ran = true;
        try
        {
            if (File.Exists(Path.Combine(Root,"manual-only.txt"))) RunManualOutcome(); else Run();
            Log("PROBE_COMPLETED access release gate remains UNPROVEN");
        }
        catch (Exception error) { Log("PROBE_ERROR " + error); }
        finally
        {
            capture = false;
            AccessTools.Method(typeof(Game), "Shutdown").Invoke(Game.instance, new object[] { true });
            Application.Quit();
        }
    }
    private void Run()
    {
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        var bag = player.GetInventory();
        foreach (var item in bag.GetAllItems().ToArray()) player.UnequipItem(item, false);
        bag.RemoveAll(); // Newly created, isolated test character only.
        var obj = Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), player.transform.position + Vector3.forward * 2, Quaternion.identity);
        var chest = obj.GetComponent<Container>();
        var view = obj.GetComponent<ZNetView>();
        var zdo = view.GetZDO();
        long local = ZNet.GetUID(), peerB = local == 910001 ? 910002 : 910001, peerC = 910003;
        var inv = chest.GetInventory();
        inv.RemoveAll();
        var prefab = ObjectDB.instance.GetItemPrefab("Wood");
        var wood = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        wood.m_dropPrefab = prefab; wood.m_stack = 10;
        Require(inv.AddItem(wood), "fixture stock failed");
        capture = true;

        // Verify ordinary busy denial; then test its delayed-grant neighbor.
        chest.SetInUse(true);
        replies.Clear();
        Call(chest, "RPC_RequestOpen", peerB, player.GetPlayerID());
        Call(chest, "RPC_RequestTakeAll", peerB, player.GetPlayerID());
        Require(replies.Count == 2 && replies.All(x => x.EndsWith(":False")), "native busy request was not denied");
        Require(Wood(inv) == 10 && Wood(bag) == 0, "busy denial changed stock");
        Log("CHECK_1 PASS native busy Open and TakeAll denied, chest=10 bag=0");
        chest.SetInUse(false);

        AccessTools.Field(typeof(Container), "m_lastTakeAllTime").SetValue(chest, Time.time - 3f);
        replies.Clear();
        Call(chest, "RPC_RequestTakeAll", peerB, player.GetPlayerID());
        Require(replies.Single().EndsWith(":True") && zdo.GetOwner() == local && !chest.IsInUse(), "grant unexpectedly established exclusion");
        Log("CHECK_2 REPRODUCED TakeAll grant leaves ownership unchanged and no in-use reservation");

        replies.Clear();
        Call(chest, "RPC_RequestOpen", peerC, player.GetPlayerID());
        Require(replies.Single().EndsWith(":True") && zdo.GetOwner() == peerC, "intervening open not granted");
        // Model the new owner's local in-use flag; no real second process exists.
        AccessTools.Field(typeof(Container), "m_inUse").SetValue(chest, true);
        zdo.Set(ZDOVars.s_inUse, 1);
        Call(chest, "RPC_TakeAllResponse", local, true);
        Require(zdo.GetOwner() == local && Wood(inv) == 0 && Wood(bag) == 10, "delayed TakeAll did not reproduce ownership override");
        Log("CHECK_3 REPRODUCED delayed native TakeAllResponse reclaims owner despite intervening Open/in-use; chest=0 bag=10; simulated roles only");

        // Fresh ZDO payload can coexist with stale materialized Inventory while
        // in-use; an owner flag and granted response do not attest freshness.
        var fresh = new Inventory("fresh", null, 5, 2);
        var freshWood = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        freshWood.m_dropPrefab = prefab; freshWood.m_stack = 7;
        Require(fresh.AddItem(freshWood), "fresh fixture failed");
        var package = new ZPackage(); fresh.Save(package);
        zdo.Set(ZDOVars.s_items, package.GetArray());
        Require(!(bool)Call(chest, "Load") && Wood(inv) == 0, "in-use should block loading new payload");
        chest.SetInUse(false);
        Require((bool)Call(chest, "Load") && Wood(inv) == 7, "released Load should materialize payload");
        Log("CHECK_4 REPRODUCED in-use Load leaves stale inventory=0 with ZDO stock=7; release then Load yields 7");
        // No duplication claim: check 4 deliberately injected a fixture payload.
        ProbeObsoleteWriter(chest, zdo, peerB);
        capture = false;
        Log("LOG_REVIEW_REQUIRED; no crafting mutation implementation enabled; no production worlds opened");
    }
    private void ProbeObsoleteWriter(Container chest, ZDO zdo, long oldOwner)
    {
        // A constructed old-owner packet with a higher data revision exercises
        // the real receive handler. It is NOT an observed network interleaving.
        var stalePayload = new ZPackage();
        zdo.Serialize(stalePayload); // Seven Wood in the old image.
        Require(chest.GetInventory().RemoveItem(chest.GetInventory().GetAllItems().Single(), 1), "debit fixture failed");
        Require(Wood(chest.GetInventory()) == 6, "debit fixture should leave six");
        uint committedRevision = zdo.DataRevision;
        ushort committedOwnerRevision = zdo.OwnerRevision;
        Require(committedOwnerRevision > 0, "fixture has no previous owner revision");
        var packet = new ZPackage();
        packet.Write(0); packet.Write(zdo.m_uid);
        packet.Write((ushort)(committedOwnerRevision - 1));
        packet.Write(checked(committedRevision + 1));
        packet.Write(oldOwner); packet.Write(zdo.GetPosition());
        packet.Write(stalePayload); packet.Write(ZDOID.None); packet.SetPos(0);
        var peer = new ZNetPeer(null, false) { m_uid = oldOwner };
        var peerType = typeof(ZDOMan).GetNestedType("ZDOPeer", System.Reflection.BindingFlags.NonPublic);
        object peerState = Activator.CreateInstance(peerType, true);
        AccessTools.Field(peerType, "m_peer").SetValue(peerState, peer);
        var peers = (System.Collections.IList)AccessTools.Field(typeof(ZDOMan), "m_peers").GetValue(ZDOMan.instance);
        peers.Add(peerState);
        try
        {
            AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData").Invoke(ZDOMan.instance, new object[] {peer.m_rpc, packet});
            Require(zdo.GetOwner() == oldOwner && zdo.OwnerRevision == committedOwnerRevision - 1,
                "obsolete owner packet unexpectedly rejected");
            Call(chest, "Load");
            Require(Wood(chest.GetInventory()) == 7, "stale seven-item image was not accepted");
            Log("CHECK_5 REPRODUCED native ZDOData accepts constructed obsolete-owner/newer-data packet; committed stock=6 overwritten stock=7; ownerRevision=" + committedOwnerRevision + "->" + zdo.OwnerRevision + "; simulated transport only");
        }
        finally { peers.Remove(peerState); }
    }
    private void OnDestroy() => patches?.UnpatchSelf();
}
