using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    // Observe the actual native product after Spawn and its installed patches finish.
    // Never manufacture a replacement from the station's unmodified prefab definition.
    internal static class NativeProductionOutput
    {
        private sealed class Pending { internal Component Station; internal ItemDrop Drop; internal int Frame; }
        private static readonly List<Pending> PendingDrops = new();
        internal static Component Current;
        internal static bool Collects(Component station)
        {
            var zdo = ValheimAccess.Zdo(station);
            return InventoryOperationNetwork.Ready && ProductionConfig.Enabled.Value && ValheimAccess.IsNativeOwner(station) && zdo != null &&
                new[] { ProductionLinkRole.Output, ProductionLinkRole.Replenishment }.Any(role =>
                    ProductionRoleLinkCatalogStore.ReadWithLegacy(zdo, role, out var links) == StoredRecordState.Valid && links.Links.Count > 0);
        }
        internal static bool CollectorWaiting(Component station)
        {
            if (PendingDrops.Any(p => p.Station == station && p.Drop != null)) return true;
            byte[] saved = ValheimAccess.Zdo(station)?.GetByteArray(AuthorityStationState.NativeOutputKey);
            return saved != null && saved.Length > 0 && NativeInventoryCodec.Load(saved, 32, 32).NrOfItems() > 0;
        }
        internal static void Harvest(Component station)
        {
            if (!Collects(station)) return;
            long sender = ZDOMan.GetSessionID();
            if (station is CookingStation cooking)
                AccessTools.Method(typeof(CookingStation), "RPC_RemoveDoneItem").Invoke(cooking, new object[] { sender, cooking.transform.position, 1 });
            else if (station is Fermenter fermenter)
                AccessTools.Method(typeof(Fermenter), "RPC_Tap").Invoke(fermenter, new object[] { sender });
            else if (station is SapCollector sap && !CollectorWaiting(sap))
                AccessTools.Method(typeof(SapCollector), "RPC_Extract").Invoke(sap, new object[] { sender });
            else if (station is Beehive hive)
                AccessTools.Method(typeof(Beehive), "RPC_Extract").Invoke(hive, new object[] { sender });
        }
        internal static void Created(ItemDrop item)
        {
            if (Current != null && item != null && !PendingDrops.Any(p => p.Drop == item))
                PendingDrops.Add(new Pending { Station = Current, Drop = item, Frame = Time.frameCount + 2 });
        }
        internal static void Tick()
        {
            foreach (var pending in PendingDrops.Where(p => p.Frame <= Time.frameCount).ToArray())
            {
                PendingDrops.Remove(pending);
                try
                {
                    if (pending.Station == null || pending.Drop == null || !Collects(pending.Station) ||
                        !ValheimAccess.IsNativeOwner(pending.Drop)) continue;
                    var zdo = ValheimAccess.Zdo(pending.Station);
                    var saved = zdo.GetByteArray(AuthorityStationState.NativeOutputKey);
                    var inventory = saved == null ? new Inventory("Pending production output", null, 32, 32) : NativeInventoryCodec.Load(saved, 32, 32);
                    var item = pending.Drop.m_itemData.Clone();
                    if (!NativeInventoryCodec.AddExact(inventory, item)) continue; // Leave the actual ground item available.
                    byte[] next = NativeInventoryCodec.Save(inventory);
                    zdo.Set(AuthorityStationState.NativeOutputKey, next);
                    ZNetScene.instance.Destroy(pending.Drop.gameObject);
                    ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                }
                catch (Exception error) { ProductionRuntime.FailHook("native production output", error); }
            }
        }
        internal static Inventory RemovalContents(WearNTear piece)
        {
            if (!ValheimAccess.IsNativeOwner(piece)) return null;
            var zdo = ValheimAccess.Zdo(piece);
            var bytes = zdo?.GetByteArray(AuthorityStationState.NativeOutputKey);
            return bytes == null || bytes.Length == 0 ? null : NativeInventoryCodec.Load(bytes, 32, 32);
        }
    }

    [HarmonyPatch]
    internal static class NativeTimedOutputScope
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CookingStation), "SpawnItem");
            yield return AccessTools.Method(typeof(Fermenter), "DelayedTap");
            yield return AccessTools.Method(typeof(Beehive), "RPC_Extract");
            yield return AccessTools.Method(typeof(SapCollector), "RPC_Extract");
        }
        private static void Prefix(Component __instance, out Component __state)
        { __state = NativeProductionOutput.Current; NativeProductionOutput.Current = NativeProductionOutput.Collects(__instance) ? __instance : null; }
        private static void Finalizer(Component __state) => NativeProductionOutput.Current = __state;
    }

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    internal static class NativeProductionDropAwake
    { private static void Postfix(ItemDrop __instance) => NativeProductionOutput.Created(__instance); }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(ItemDrop), typeof(bool) })]
    internal static class NativeProductionCreatedPatch
    {
        private static void Postfix(ItemDrop item) => NativeProductionOutput.Created(item);
    }

    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class NativeProductionRemovalPatch
    {
        // This is the actual destruction path, never scene unloading / OnDestroy.
        private static void Prefix(WearNTear __instance, out Tuple<Inventory, Vector3> __state) =>
            __state = Tuple.Create(NativeProductionOutput.RemovalContents(__instance), __instance.transform.position + Vector3.up);
        private static void Postfix(bool blockDrop, bool __runOriginal, Tuple<Inventory, Vector3> __state)
        {
            if (!__runOriginal || blockDrop || __state?.Item1 == null) return;
            foreach (var item in __state.Item1.GetAllItems()) ItemDrop.DropItem(item, item.m_stack, __state.Item2, Quaternion.identity);
        }
    }
}
