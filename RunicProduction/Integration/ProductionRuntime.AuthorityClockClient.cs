using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        private static readonly HashSet<ZDOID> PendingAuthorityClocks = new();
        internal static void ClearAuthorityPending() { PendingAuthorityClocks.Clear(); PendingAuthorityRecipes.Clear(); RecipeClockStations.Clear(); }
        internal static bool AuthorityManagedStation(ZDO zdo)
        { return false; } // Timers, manual interactions and destruction belong to the native station.
        internal static bool HasLiveProductionLinks(ZDO zdo)
        {
            if (!InventoryOperationNetwork.Ready || zdo == null) return false;
            if (zdo.GetString(AuthorityStationLedger.Marker, "").Length != 0 || zdo.GetBool(AuthorityStationLedger.EnrollingKey)) return true;
            if (!ProductionConfig.Enabled.Value) return false;
            foreach (var role in new[] { ProductionLinkRole.Input, ProductionLinkRole.Fuel, ProductionLinkRole.Output, ProductionLinkRole.Replenishment })
                if (ProductionRoleLinkCatalogStore.ReadWithLegacy(zdo, role, out var catalog) == StoredRecordState.Valid && catalog.Links.Count > 0) return true;
            return false;
        }
        private static AuthorityStationEnvironment AuthorityEnvironment(Component component)
        {
            bool roof = true, fire = true, smoke = false, bees = true, burning = true; float wind = 1;
            if (component is Smelter smelter)
            {
                roof = !smelter.m_requiresRoof || Cover.IsUnderRoof(smelter.m_roofCheckPoint.position);
                smoke = smelter.m_smokeSpawner != null && smelter.m_smokeSpawner.IsBlocked();
                wind = smelter.m_windmill == null ? 1 : Mathf.Clamp01(smelter.m_windmill.GetPowerOutput());
            }
            else if (component is CookingStation cooking)
                fire = !cooking.m_requireFire || (bool)AccessTools.Method(typeof(CookingStation), "IsFireLit").Invoke(cooking, null);
            else if (component is Fermenter fermenter)
            { Cover.GetCoverForPoint(fermenter.m_roofCheckPoint.position, out float cover, out roof); roof &= cover >= .7f; }
            else if (component is Beehive hive)
                bees = (bool)AccessTools.Method(typeof(Beehive), "CheckBiome").Invoke(hive, null) &&
                    (bool)AccessTools.Method(typeof(Beehive), "HaveFreeSpace").Invoke(hive, null);
            else if (component is Fireplace fireplace) burning = fireplace.IsBurning();
            return new AuthorityStationEnvironment(roof, fire, smoke, bees, burning, wind);
        }
        private static bool QueueAuthorityClock(Component component)
        {
            var zdo = ValheimAccess.Zdo(component);
            if (zdo == null || !ValheimAccess.IsNativeOwner(component) || !HasLiveProductionLinks(zdo) || PendingAuthorityClocks.Contains(zdo.m_uid)) return false;
            if (!ProductionEndpointIdentity.TryGetOrEnsureToken(ValheimAccess.View(component), out _)) return false;
            NativeProductionOutput.Harvest(component);
            var environment = AuthorityEnvironment(component); var snapshot = AuthorityStationState.Capture(zdo);
            var p = new ZPackage(); p.Write(2); p.Write(zdo.m_uid); p.Write((int)zdo.OwnerRevision); p.Write(zdo.GetLong(AuthorityStationLedger.RevisionKey));
            p.Write(environment.Sheltered); p.Write(environment.FireLit); p.Write(environment.SmokeBlocked);
            p.Write(environment.BeesHappy); p.Write(environment.Burning); p.Write(environment.WindPower); p.Write(snapshot.Encode());
            ZDOID id = zdo.m_uid; PendingAuthorityClocks.Add(id);
            Action<int, string, byte[]> completed = (status, reason, events) => {
                PendingAuthorityClocks.Remove(id);
                if (status == (int)OperationStatus.Committed)
                {
                    try { AuthorityProductionEvents.Play(component, events); }
                    catch (Exception error) { FailHook("committed production effects", error); }
                }
                if (component != null) Stop(component, ProductionStopCode.Ready, status == (int)OperationStatus.Committed ? "station transaction committed" : reason);
            };
            string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), request = Guid.NewGuid().ToString("N");
            if (ZNet.instance.IsServer() && Player.m_localPlayer == null)
            {
                var submitted = SharedOperationHost.Submit(world, "server-station:" + snapshot.Token, request, AuthorityClockKind, p.GetArray(), completed);
                if (submitted.Item1 != (int)OperationStatus.Queued) completed(submitted.Item1, submitted.Item2, submitted.Item3);
                return submitted.Item1 == (int)OperationStatus.Queued;
            }
            bool sent = InventoryOperationNetwork.Send(world, request, AuthorityClockKind, p.GetArray(), completed);
            if (!sent) PendingAuthorityClocks.Remove(id); return sent;
        }
    }
    // Suppress only native inventory/timer mutation. Native cover checks, fire visuals,
    // bee effects and cooking slot rendering still run on the original simulation owner.
    internal static class AuthorityNativeStationClockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Smelter), "UpdateSmelter");
            yield return AccessTools.Method(typeof(CookingStation), "UpdateCooking");
            yield return AccessTools.Method(typeof(Fermenter), "UpdateCover");
            yield return AccessTools.Method(typeof(Beehive), "UpdateBees");
            yield return AccessTools.Method(typeof(Fireplace), "UpdateFireplace");
        }
        private static bool NativeMayAdvance(ZNetView view) => view.IsOwner() && !ProductionRuntime.AuthorityManagedStation(view.GetZDO());
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = instructions.ToList(); var owner = AccessTools.Method(typeof(ZNetView), nameof(ZNetView.IsOwner));
            int count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(owner)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(AuthorityNativeStationClockPatch), nameof(NativeMayAdvance)); count++; }
            if (count != 1) throw new InvalidOperationException("Native station clock boundary changed: " + __originalMethod);
            return code;
        }
    }
}
