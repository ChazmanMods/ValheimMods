using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    internal static class ManagedAnimalConsumption
    {
        private const string Kind = "inventory.animal-consume.v1";
        private static HashSet<ZDOID> Pending => SharedState.Get("operations.animal-consume.pending.v1", () => new HashSet<ZDOID>());
        internal static void Clear() => Pending.Clear();
        internal static void Register() => SharedOperationHost.RegisterBackground(Kind, (_, _) => null, Plan);
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
        {
            var packet = new ZPackage(bytes); var animal = ZDOMan.instance.GetZDO(packet.ReadZDOID());
            var drop = ZDOMan.instance.GetZDO(packet.ReadZDOID()); OperationPacket.End(packet);
            if (animal == null || !ManagedWorldDrops.Managed(drop)) throw new OperationRejectedException("animal-or-food-unavailable");
            bool owner = animal.GetOwner() == ZDOMan.GetSessionID() && actor == "server-animal:" + animal.m_uid;
            if (!owner && NetworkInventoryAccess.TryPosition(actor, out long principal, out _))
                owner = Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == principal && animal.GetOwner() == ZDOMan.GetSessionID() ||
                    ZNet.instance.GetPeers().Any(p => NetworkInventoryAccess.TryPeerPrincipal(p, out long peerPrincipal, out _) && peerPrincipal == principal && p.m_uid == animal.GetOwner());
            var ai = ZNetScene.instance.GetPrefab(animal.GetPrefab())?.GetComponent<MonsterAI>();
            var food = ZNetScene.instance.GetPrefab(drop.GetPrefab())?.GetComponent<ItemDrop>();
            if (!owner || ai == null || food == null || !ai.m_consumeItems.Any(i => i != null && i.m_itemData.m_shared.m_name == food.m_itemData.m_shared.m_name) ||
                (animal.GetPosition() - drop.GetPosition()).sqrMagnitude > (ai.m_consumeRange + 2) * (ai.m_consumeRange + 2))
                throw new OperationRejectedException("animal-cannot-consume-food");
            byte[] before = ManagedWorldDrops.Read(drop);
            if (before == null) throw new OperationDeferredException("food-enrollment-pending");
            return Tuple.Create(new[] { ManagedWorldDrops.Consume(drop, before, 1) }, Array.Empty<byte>());
        }
        internal static bool Remove(ItemDrop item, MonsterAI ai, Humanoid humanoid)
        {
            var view = item?.GetComponent<ZNetView>();
            if (!ManagedWorldDrops.Managed(view?.GetZDO())) return item.RemoveOne();
            var animalView = ai.GetComponent<ZNetView>();
            if (!InventoryOperationNetwork.Ready || animalView == null || !animalView.IsValid() || !animalView.IsOwner()) return false;
            var id = animalView.GetZDO().m_uid;
            if (!Pending.Add(id)) return false;
            var foodPrefab = item.m_itemData.m_dropPrefab.GetComponent<ItemDrop>();
            var packet = new ZPackage(); packet.Write(id); packet.Write(view.GetZDO().m_uid);
            Action<int, string, byte[]> done = (status, _, _) =>
            {
                Pending.Remove(id);
                if (status != (int)OperationStatus.Committed || ai == null || humanoid == null || !animalView.IsValid() || !animalView.IsOwner()) return;
                ai.m_onConsumedItem?.Invoke(item != null ? item : foodPrefab);
                humanoid.m_consumeItemEffects.Create(ai.transform.position, Quaternion.identity);
                ((ZSyncAnimation)AccessTools.Field(typeof(BaseAI), "m_animator").GetValue(ai)).SetTrigger("consume");
                AccessTools.Field(typeof(MonsterAI), "m_consumeTarget").SetValue(ai, null);
            };
            string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), request = Guid.NewGuid().ToString("N");
            if (ZNet.instance.IsServer() && Player.m_localPlayer == null)
            {
                var result = SharedOperationHost.Submit(world, "server-animal:" + id, request, Kind, packet.GetArray(), done);
                if (result.Item1 != (int)OperationStatus.Queued) done(result.Item1, result.Item2, result.Item3);
            }
            else if (!InventoryOperationNetwork.Send(world, request, Kind, packet.GetArray(), done)) Pending.Remove(id);
            return false;
        }
    }
    [HarmonyPatch(typeof(MonsterAI), "UpdateConsumeItem")]
    internal static class ManagedAnimalMeal
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                if (code.Calls(AccessTools.Method(typeof(ItemDrop), "RemoveOne")))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ManagedAnimalConsumption), "Remove"));
                }
                else yield return code;
            }
        }
    }
    [HarmonyPatch(typeof(ItemDrop), "RemoveOne")]
    internal static class ManagedOutputRemoveOne
    {
        private static bool Prefix(ItemDrop __instance, ref bool __result)
        {
            if (!ManagedWorldDrops.Managed(__instance.GetComponent<ZNetView>()?.GetZDO())) return true;
            __result = false; return false;
        }
    }
}
