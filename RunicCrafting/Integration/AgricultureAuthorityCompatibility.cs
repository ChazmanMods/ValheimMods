using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using UnityEngine;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    // Reserve a whole confirmed batch, then replay Agriculture's own terrain, spacing,
    // stamina and tool checks. Its debit/rollback objects operate on character reservations.
    [HarmonyPatch]
    internal static class AgricultureAuthorityCompatibility
    {
        private static bool Prepare() => Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", false) != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = Type.GetType("RunicAgriculture.Integration.AgricultureRuntime, RunicAgriculture", false);
            var method = type == null ? null : AccessTools.Method(type, "ExecuteBatch");
            if (method != null) yield return method;
        }
        private static bool Prefix(object __instance, MethodBase __originalMethod, Player player, Piece piece,
            string expectedCropId, IReadOnlyList<Vector3> requested, Quaternion rotation, ref int __result)
        {
            if (!InventoryOperationNetwork.Ready || AuthorityCraftExecution.Active || player != Player.m_localPlayer || piece == null ||
                player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()) || requested.Count == 0) return true;
            var requirements = (piece.m_resources ?? Array.Empty<Piece.Requirement>()).Where(r => r?.m_resItem != null)
                .Select(r => new MaterialRequirement(ValheimReflection.ResourceId(r.m_resItem), Math.Max(1, r.GetAmount(0))))
                .Where(r => !string.IsNullOrEmpty(r.ResourceId)).ToArray();
            var reserved = AuthorityBuildingExecution.Materials(player);
            if (requirements.All(r => ValheimReflection.CountRequirementItems(player.GetInventory(), r.ResourceId) >= (long)r.Quantity * requested.Count)) return true;
            __result = 0;
            if (CharacterOperationEscrow.Busy) return false;
            var service = Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", true);
            int available = (int)AccessTools.Method(service, "AvailablePlantings").Invoke(null, new object[] { player, piece, Configuration.SafeStationlessBuildRange });
            int count = Math.Min(requested.Count, available);
            if (count <= 0) return true;
            var costs = requirements.Select(r => new MaterialRequirement(r.ResourceId, checked(r.Quantity * count))).ToArray();
            var positions = requested.ToArray();
            int completedCount = 0;
            Action execute = () => AuthorityBuildingExecution.RunReserved(player, () =>
            {
                AccessTools.Field(__instance.GetType(), "_seedBudgetExpiresAt")?.SetValue(__instance, 0f);
                int planted = (int)__originalMethod.Invoke(__instance, new object[] { player, piece, expectedCropId, positions, rotation });
                completedCount = planted;
                if (planted > 0)
                {
                    var replant = AccessTools.Field(__instance.GetType(), "_replant")?.GetValue(__instance);
                    if (replant != null) AccessTools.Method(replant.GetType(), "Clear")?.Invoke(replant, null);
                }
            });
            if (costs.All(r => ValheimReflection.CountRequirementItems(player.GetInventory(), r.ResourceId) +
                ValheimReflection.CountRequirementItems(reserved, r.ResourceId) >= r.Quantity)) execute();
            else AuthorityCraftExecution.ReserveBuilding(player, ZDOID.None, costs, reserved, execute);
            // A synchronous reuse of saved reservations must report the native count;
            // otherwise Agriculture offers an already completed replant batch again.
            __result = completedCount;
            return false;
        }
    }
    [HarmonyPatch]
    internal static class AgricultureReservedSources
    {
        private static Type Service => Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", false);
        private static bool Prepare() => Service != null;
        private static MethodBase TargetMethod() => AccessTools.Method(Service, "ResolveSources");
        private static bool Prefix(Player player, ref object __result)
        {
            if (!InventoryOperationNetwork.Ready || player != Player.m_localPlayer) return true;
            Type source = Service.GetNestedType("ResourceSource", BindingFlags.NonPublic | BindingFlags.Public);
            var sources = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(source));
            var constructor = AccessTools.Constructor(source, new[] { typeof(Inventory), typeof(Container), typeof(ZDOID), typeof(float) });
            sources.Add(constructor.Invoke(new object[] { player.GetInventory(), null, ZDOID.None, 0f }));
            if (AuthorityCraftExecution.Active) sources.Add(constructor.Invoke(new object[] { AuthorityBuildingExecution.Materials(player), null, ZDOID.None, 0f }));
            __result = sources; return false;
        }
    }
    [HarmonyPatch]
    internal static class AgricultureReservedSourceAuthority
    {
        private static Type Service => Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", false);
        private static bool Prepare() => Service != null;
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(Service, "SourceIsCurrent"); yield return AccessTools.Method(Service, "SourceAuthorityStillCurrent"); }
        private static bool Prefix(object __0, Player player, ref bool __result)
        {
            if (!InventoryOperationNetwork.Ready || player != Player.m_localPlayer) return true;
            var inventory = AccessTools.Property(__0.GetType(), "Inventory").GetValue(__0) as Inventory;
            if (!ReferenceEquals(inventory, AuthorityBuildingExecution.Materials(player))) return true;
            __result = AuthorityCraftExecution.Active && !player.IsDead(); return false;
        }
    }
    [HarmonyPatch]
    internal static class AgricultureAuthorityPreview
    {
        private static bool Prepare() => Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", false) != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = Type.GetType("RunicAgriculture.Integration.NearbySeedResourceService, RunicAgriculture", false);
            var method = type == null ? null : AccessTools.Method(type, "AvailablePlantings");
            if (method != null) yield return method;
        }
        private static bool Prefix(Player player, Piece piece, float rangeMeters, ref int __result)
        {
            if (!InventoryOperationNetwork.Ready || player != Player.m_localPlayer || piece == null) return true;
            var requirements = (piece.m_resources ?? Array.Empty<Piece.Requirement>()).Where(r => r?.m_resItem != null)
                .Select(r => new MaterialRequirement(ValheimReflection.ResourceId(r.m_resItem), Math.Max(1, r.GetAmount(0))))
                .Where(r => !string.IsNullOrEmpty(r.ResourceId)).ToArray();
            var query = new ContainerQueryRuntime(new WorkshopAccessRuntime());
            var sources = query.ResolveSources(player, null, player.transform.position,
                Math.Min(rangeMeters, Configuration.SafeStationlessBuildRange), requirements, "runic.crafting.agriculture-preview", true, out _, requireWritable: false);
            var snapshots = sources.Select(s => s.Snapshot()).ToArray();
            __result = requirements.Length == 0 ? int.MaxValue : requirements.Min(r => (int)Math.Min(int.MaxValue,
                snapshots.Sum(s => (long)s.Available(r.ResourceId)) / r.Quantity));
            return false;
        }
    }
}
