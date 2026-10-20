using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal static class MultiAccessChestView
{
    private static readonly HashSet<ZDOID> Authorized = new();
    private static readonly FieldInfo Current = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
    private static readonly FieldInfo LastRevision = AccessTools.Field(typeof(Container), "m_lastRevision");
    private static int nativeOpening;
    private static Container runicView, showing;
    private static Container rejectedSnapshotChest;
    private static uint rejectedSnapshotRevision;
    internal static bool Supported(Container chest)
    {
        if (!(PluginConfig.Enabled?.Value ?? false) || !InventoryOperationNetwork.Ready || chest == null ||
            chest.GetType() != typeof(Container)) return false;
        var descriptor = NetworkInventoryAccess.Descriptor(RunicAutomation.ContainerAuthority.View(chest)?.GetZDO());
        var inventory = chest.GetInventory();
        // A container mod owns its live inventory shape. Do not shrink that inventory to
        // a prefab or ledger shape merely to display it through the Runic view.
        return descriptor != null && inventory != null && NativeInventoryCodec.ValidDimensions(inventory.GetWidth(), inventory.GetHeight());
    }
    internal static bool IsRunicView(Container chest) => Supported(chest) && runicView == chest &&
        Authorized.Contains(RunicAutomation.ContainerAuthority.View(chest).GetZDO().m_uid);
    internal static bool NativeOpening => nativeOpening != 0;
    internal static Container Opened => InventoryGui.instance == null ? null : Current.GetValue(InventoryGui.instance) as Container;
    internal static bool CanView(Container chest)
    {
        if (!IsRunicView(chest)) return chest != null && chest.IsOwner();
        var player = Player.m_localPlayer; var zdo = RunicAutomation.ContainerAuthority.View(chest)?.GetZDO();
        return player != null && zdo != null && Authorized.Contains(zdo.m_uid) &&
            (player.transform.position - chest.transform.position).sqrMagnitude <= 25f &&
            (bool)AccessTools.Method(typeof(Container), "CheckAccess").Invoke(chest, new object[] { player.GetPlayerID() }) &&
            (!chest.m_checkGuardStone || PrivateArea.CheckAccess(chest.transform.position, 0, false));
    }
    internal static void Open(Container chest)
    {
        var zdo = RunicAutomation.ContainerAuthority.View(chest)?.GetZDO();
        if (!Supported(chest) || zdo == null || Player.m_localPlayer == null || ZNet.instance?.GetWorld() == null) return;
        if (!InventoryOperationNetwork.Ready) { OpenNative(chest, Player.m_localPlayer); return; }
        CancelOpen();
        // Looking inside a chest is not an inventory transaction. Show the current
        // native replica immediately, as vanilla does; only actual edits need routing.
        Authorized.Add(zdo.m_uid); runicView = chest;
        if (!CanView(chest)) { CancelOpen(); OpenNative(chest, Player.m_localPlayer); return; }
        if (!RunicAutomation.ContainerAuthority.View(chest).IsOwner())
            AccessTools.Method(typeof(Container), "Load").Invoke(chest, Array.Empty<object>());
        showing = chest;
        try { InventoryGui.instance.Show(chest); }
        finally { showing = null; }
    }
    internal static void Tick()
    {
        if (runicView != null && !Supported(runicView))
        {
            var chest = runicView;
            if (Opened == chest) OpenNative(chest, Player.m_localPlayer);
            else CancelOpen();
        }
    }
    private static void OpenNative(Container chest, Player actor)
    {
        CancelOpen();
        if (chest == null || actor == null || actor != Player.m_localPlayer || actor.IsDead() || actor.IsTeleporting() ||
            (actor.transform.position - chest.transform.position).sqrMagnitude > 25f) return;
        nativeOpening++;
        try { chest.Interact(actor, false, false); }
        finally { nativeOpening--; }
    }
    internal static void ObserveShow(Container chest)
    {
        // A foreign/native Show is allowed to open the same chest after a Runic view.
        // Only our scoped Show grants the altered controls and ownership checks.
        if (showing == null || showing != chest) CancelOpen();
    }
    internal static void Apply(Container chest, byte[] bytes, int width, int height)
    {
        NativeChestInterop.ApplySnapshot(chest, bytes, width, height);
    }
    internal static void RefreshVisible(Container chest)
    {
        // Native Container checks replicated changes only once a second. An owned vanilla
        // chest already changes its in-memory inventory during a click; a shared chest
        // receives that change from the server. Read it before the next grid draw instead
        // of leaving the old contents visible until the native polling timer fires.
        if (!IsRunicView(chest)) return;
        var zdo = RunicAutomation.ContainerAuthority.View(chest)?.GetZDO();
        if (zdo == null || RunicAutomation.ContainerAuthority.View(chest).IsOwner() || zdo.DataRevision == (uint)LastRevision.GetValue(chest)) return;
        if (rejectedSnapshotChest == chest && rejectedSnapshotRevision == zdo.DataRevision) return;
        var bytes = zdo.GetByteArray(ZDOVars.s_items);
        if (bytes == null) return;
        try { AccessTools.Method(typeof(Container), "Load").Invoke(chest, Array.Empty<object>()); }
        catch (Exception error)
        {
            rejectedSnapshotChest = chest; rejectedSnapshotRevision = zdo.DataRevision;
            Plugin.Log?.LogError("Visible chest snapshot rejected: " + error);
        }
    }
    internal static void ClearDrag() => AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(InventoryGui.instance, new object[] { null, null, 1 });
    internal static void TakeAll(Container chest)
    {
        if (!IsRunicView(chest)) return;
        StorageOperationClient.Submit(new StorageAuthorityRequest { Action = AuthorityStorageAction.TakeAll,
            Chest = RunicAutomation.ContainerAuthority.View(chest).GetZDO().m_uid }, Array.Empty<Tuple<ItemDrop.ItemData, int>>());
    }
    internal static void StackAll(Container chest)
    {
        var player = Player.m_localPlayer; if (player == null || !IsRunicView(chest)) return;
        var items = player.GetInventory().GetAllItems().ToArray();
        if (!StorageItemProtection.TryCapture(items, out var protection, out _)) return;
        var take = new List<Tuple<ItemDrop.ItemData, int>>();
        for (int n = 0; n < items.Length; n++)
            if (!items[n].m_equipped && !player.IsItemEquiped(items[n]) && !items[n].m_shared.m_questItem &&
                !StoragePlayerProtection.IsReserved(player, items[n]) &&
                (!PluginConfig.ProtectHotbar.Value || items[n].m_gridPos.y != 0) && protection.StateAt(n) != Engine.StorageProtectionState.Locked)
                take.Add(Tuple.Create(items[n], items[n].m_stack));
        if (take.Count == 0) return;
        StorageOperationClient.Submit(new StorageAuthorityRequest { Action = AuthorityStorageAction.StackMatching,
            Chest = RunicAutomation.ContainerAuthority.View(chest).GetZDO().m_uid }, take.ToArray());
    }
    internal static void CancelOpen()
    {
        Authorized.Clear(); runicView = null;
    }
    internal static void HandleHide(bool wasVisible)
    { if (wasVisible) CancelOpen(); }
    internal static void Clear() { CancelOpen(); rejectedSnapshotChest = null; rejectedSnapshotRevision = 0; MultiAccessChestPresence.Clear(); }
}

[HarmonyPatch(typeof(Container), "Interact")]
internal static class MultiAccessOpenPatch
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || MultiAccessChestView.NativeOpening || !MultiAccessChestView.Supported(__instance)) return true;
        __result = false;
        if (hold || character != Player.m_localPlayer) return false;
        MultiAccessChestView.Open(__instance); __result = true; return false;
    }
}
[HarmonyPatch(typeof(InventoryGui), "Show")]
internal static class MultiAccessObserveNativeShow
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Container container, bool __runOriginal)
    {
        if (__runOriginal) MultiAccessChestView.ObserveShow(container);
        else MultiAccessChestView.CancelOpen();
    }
}
[HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
internal static class MultiAccessViewOwnershipPatch
{
    private static void Prefix(Container ___m_currentContainer, bool __runOriginal)
    { if (__runOriginal) MultiAccessChestView.RefreshVisible(___m_currentContainer); }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var native = AccessTools.Method(typeof(Container), "IsOwner");
        var view = AccessTools.Method(typeof(MultiAccessChestView), "CanView");
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = view; replaced++; }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Native chest view ownership call did not match expected IL");
    }
}
[HarmonyPatch(typeof(Container), "Load")]
internal static class MultiAccessReadSnapshotPatch
{
    private static bool Prefix(Container __instance, ref bool __result, uint ___m_lastRevision, bool __runOriginal)
    {
        var zdo = RunicAutomation.ContainerAuthority.View(__instance)?.GetZDO();
        if (!__runOriginal || !MultiAccessChestView.IsRunicView(__instance) || !ManagedChestLedger.Managed(zdo)) return true;
        __result = false;
        if (zdo.DataRevision == ___m_lastRevision) return false;
        var bytes = zdo.GetByteArray(ZDOVars.s_items);
        if (bytes == null) return false;
        try
        {
            MultiAccessChestView.Apply(__instance, bytes, __instance.GetInventory().GetWidth(), __instance.GetInventory().GetHeight());
            __result = true;
        }
        catch (Exception error) { Plugin.Log?.LogError("Managed chest snapshot rejected: " + error); }
        return false;
    }
}
[HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
internal static class MultiAccessTakeAllButton
{
    private static bool Prefix(bool __runOriginal)
    {
        var chest = MultiAccessChestView.Opened; if (!__runOriginal || !MultiAccessChestView.IsRunicView(chest)) return true;
        MultiAccessChestView.ClearDrag(); MultiAccessChestView.TakeAll(chest); return false;
    }
}
[HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
internal static class MultiAccessStackAllButton
{
    private static bool Prefix(bool __runOriginal)
    {
        var chest = MultiAccessChestView.Opened; if (!__runOriginal || !MultiAccessChestView.IsRunicView(chest)) return true;
        MultiAccessChestView.ClearDrag(); MultiAccessChestView.StackAll(chest); return false;
    }
}
[HarmonyPatch(typeof(Container), "StackAll")]
internal static class MultiAccessStackAllInteraction
{
    private static bool Prefix(Container __instance, bool __runOriginal)
    { if (!__runOriginal || !MultiAccessChestView.IsRunicView(__instance)) return true; MultiAccessChestView.StackAll(__instance); return false; }
}
[HarmonyPatch(typeof(Container), "TakeAll")]
internal static class MultiAccessTakeAllInteraction
{
    private static bool Prefix(Container __instance, ref bool __result, bool __runOriginal)
    { if (!__runOriginal || !MultiAccessChestView.IsRunicView(__instance)) return true; MultiAccessChestView.TakeAll(__instance); __result = true; return false; }
}
[HarmonyPatch(typeof(ZNet), "OnDestroy")]
internal static class MultiAccessViewEndWorld
{ private static void Postfix() => MultiAccessChestView.Clear(); }

[HarmonyPatch(typeof(InventoryGui), "Hide")]
internal static class MultiAccessCancelPendingOpen
{
    private static readonly MethodInfo IsVisible = AccessTools.Method(
        AccessTools.Field(typeof(InventoryGui), "m_animator").FieldType, "GetBool", new[] { typeof(string) });
    private static void Prefix(object ___m_animator, bool __runOriginal)
    {
        if (__runOriginal) MultiAccessChestView.HandleHide(
            ___m_animator != null && (bool)IsVisible.Invoke(___m_animator, new object[] { "visible" }) || MultiAccessChestView.Opened != null);
    }
}
[HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
internal static class MultiAccessCloseNativeContainer
{
    private static void Postfix(bool __runOriginal)
    { if (__runOriginal) MultiAccessChestView.CancelOpen(); }
}
