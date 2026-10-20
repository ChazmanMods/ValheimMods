using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

public sealed partial class NativeAccessProbe
{
    private static ZNetView delayedCookingView;
    private static object[] delayedCookingPayload;
    private static bool CaptureCooking(ZNetView __instance, string method, object[] parameters)
    {
        if (__instance != delayedCookingView || method != "RPC_AddItem") return true;
        delayedCookingPayload = (object[])parameters.Clone();
        return false;
    }

    private void RunManualOutcome()
    {
        Log("MANUAL_OUTCOME native installed methods; one process; injected interleaving, NOT real network concurrency");
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        var bag = player.GetInventory();
        foreach(var item in bag.GetAllItems().ToArray()) player.UnequipItem(item,false);
        bag.RemoveAll(); // New isolated character only.
        var obj = Instantiate(ZNetScene.instance.GetPrefab("piece_cookingstation"),player.transform.position+Vector3.forward*2,Quaternion.identity);
        var station = obj.GetComponent<CookingStation>();
        var view = obj.GetComponent<ZNetView>();
        var from = station.m_conversion.First().m_from;
        var prefab = ObjectDB.instance.GetItemPrefab(from.gameObject.name);
        var ingredient = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        ingredient.m_dropPrefab = prefab; ingredient.m_stack = 1;
        Require(bag.AddItem(ingredient),"manual fixture ingredient failed");
        var add = AccessTools.Method(typeof(CookingStation),"RPC_AddItem");
        var free = AccessTools.Method(typeof(CookingStation),"GetFreeSlot");
        int Occupied() => Enumerable.Range(0,station.m_slots.Length).Count(i => view.GetZDO().GetString("slot"+i,"") != "");
        for(int i=0;i<station.m_slots.Length-1;i++) add.Invoke(station,new object[]{ZNet.GetUID(),prefab.name,false});
        Require((int)free.Invoke(station,null) >= 0,"expected last empty slot");
        int beforeSlots=Occupied();
        delayedCookingView=view;
        bool submitted;
        try { submitted=(bool)AccessTools.Method(typeof(CookingStation),"CookItem").Invoke(station,new object[]{player,ingredient}); }
        finally { delayedCookingView=null; }
        Log("MANUAL_CAPTURE submitted="+submitted+" containsOriginal="+bag.ContainsItem(ingredient)+" captured="+(delayedCookingPayload!=null)+" item="+prefab.name+" occupied="+Occupied());
        Require(submitted && !bag.ContainsItem(ingredient) && delayedCookingPayload != null,"native caller did not consume before submission");
        Require(Occupied()==beforeSlots,"captured request unexpectedly applied");
        Log("MANUAL_CHECK_A REPRODUCED native CookItem returned=true ingredientDebited=1 pendingRequest=true occupied="+beforeSlots);
        // A competing accepted input fills the last slot before the held request.
        add.Invoke(station,new object[]{910001L,prefab.name,false});
        Require((int)free.Invoke(station,null)==-1,"competing input did not fill final slot");
        var before=new ZPackage(); view.GetZDO().Serialize(before);
        add.Invoke(station,new object[]{ZNet.GetUID(),delayedCookingPayload[0],delayedCookingPayload[1]});
        var after=new ZPackage(); view.GetZDO().Serialize(after);
        Require(before.GetArray().SequenceEqual(after.GetArray()),"full target unexpectedly accepted held request");
        Require(!bag.ContainsItem(ingredient),"native code unexpectedly returned ingredient");
        Log("MANUAL_CHECK_B REPRODUCED full target ignored held RPC_AddItem; our effect=0 our ingredientDebit=1; no correlated native acceptance/rejection reply; injected competition");
        // Positive neighboring target execution, without an injected competitor.
        var second=Instantiate(ZNetScene.instance.GetPrefab("piece_cookingstation"),player.transform.position+Vector3.right*2,Quaternion.identity).GetComponent<CookingStation>();
        var accepted=ingredient.Clone(); accepted.m_stack=1; Require(bag.AddItem(accepted),"positive fixture failed");
        Require((bool)AccessTools.Method(typeof(CookingStation),"CookItem").Invoke(second,new object[]{player,accepted}),"positive native cook failed");
        Require(!bag.ContainsItem(accepted) && (int)free.Invoke(second,null)==1,"positive native cost/effect mismatch");
        Log("MANUAL_CHECK_C PASS uncontended local native call consumed one and occupied exactly one slot; no network proof claimed");
    }
}
