using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal sealed class WorkshopAccessRuntime
    {
        internal const string StationUseKey = "runic.crafting.station-use";
        internal const string LocalMaterialsKey = "runic.crafting.local-materials";
        internal const string ApprovedKey = "runic.crafting.approved";
        internal const string GroupIdKey = "runic.crafting.group-id";

        private readonly WorkshopAccessEvaluator _evaluator = new WorkshopAccessEvaluator();

        internal WorkshopAccessDecision Evaluate(
            CraftingStation station,
            Player player,
            WorkshopAction action)
        {
            if (station == null || !ValheimReflection.CanMutateLocalPlayer(player))
                return new WorkshopAccessDecision(false, action, "local-player-owner-required");

            ZDO zdo = ValheimReflection.StationZdo(station);
            if (zdo == null || !zdo.IsValid())
                return new WorkshopAccessDecision(false, action, "station-record-unavailable");

            Piece piece = station.GetComponent<Piece>();
            string ownerId = (piece == null ? 0L : piece.GetCreator())
                .ToString(CultureInfo.InvariantCulture);
            string subjectId = player.GetPlayerID().ToString(CultureInfo.InvariantCulture);
            WorkshopPolicyKind stationPolicy = ReadPolicy(
                zdo, StationUseKey, Configuration.DefaultStationUse.Value);
            WorkshopPolicyKind materialPolicy = ReadPolicy(
                zdo, LocalMaterialsKey, Configuration.DefaultLocalMaterialUse.Value);
            string groupId = ReadGroupId(zdo);
            bool providerAvailable = false;
            bool groupMember = false;
            WorkshopPolicyKind selected = action == WorkshopAction.StationUse
                ? stationPolicy
                : materialPolicy;
            if (selected == WorkshopPolicyKind.Group)
                providerAvailable = TryResolveGroup(groupId, player.GetPlayerID(), out groupMember);

            WardResolution ward = ValheimReflection.ResolveWard(station.transform.position);
            bool wardAllows = !ward.HasWard || ward.Allowed && !ward.Ambiguous;
            var profile = new WorkshopAccessProfile(
                ownerId,
                stationPolicy,
                materialPolicy,
                ReadApproved(zdo));
            return _evaluator.Evaluate(
                profile,
                action,
                subjectId,
                new WorkshopAccessContext(wardAllows, providerAvailable, groupMember));
        }

        internal static string ReadGroupId(ZDO zdo)
        {
            string value = zdo?.GetString(GroupIdKey, string.Empty) ?? string.Empty;
            return IsCanonicalGroupId(value) ? value : string.Empty;
        }

        private static WorkshopPolicyKind ReadPolicy(
            ZDO zdo,
            string key,
            WorkshopPolicyKind fallback)
        {
            if (zdo == null) return fallback;
            string value = zdo.GetString(key, string.Empty);
            return TryParsePolicy(value, out WorkshopPolicyKind policy) ? policy : fallback;
        }

        internal static bool TryParsePolicy(string value, out WorkshopPolicyKind policy)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "everyone": policy = WorkshopPolicyKind.Everyone; return true;
                case "approved": policy = WorkshopPolicyKind.Approved; return true;
                case "owner": policy = WorkshopPolicyKind.Owner; return true;
                case "nobody": policy = WorkshopPolicyKind.Nobody; return true;
                case "ward": policy = WorkshopPolicyKind.Ward; return true;
                case "ward.exceptions": policy = WorkshopPolicyKind.WardWithExceptions; return true;
                case "group": policy = WorkshopPolicyKind.Group; return true;
                default: policy = default; return false;
            }
        }

        internal static string PolicyWireName(WorkshopPolicyKind policy)
        {
            switch (policy)
            {
                case WorkshopPolicyKind.Everyone: return "everyone";
                case WorkshopPolicyKind.Approved: return "approved";
                case WorkshopPolicyKind.Owner: return "owner";
                case WorkshopPolicyKind.Nobody: return "nobody";
                case WorkshopPolicyKind.Ward: return "ward";
                case WorkshopPolicyKind.WardWithExceptions: return "ward.exceptions";
                case WorkshopPolicyKind.Group: return "group";
                default: return "nobody";
            }
        }

        internal static bool IsCanonicalGroupId(string value) =>
            value != null && value.Length == 32 &&
            Guid.TryParseExact(value, "N", out Guid groupId) && groupId != Guid.Empty &&
            string.Equals(value, groupId.ToString("N"), StringComparison.Ordinal);

        private static IEnumerable<string> ReadApproved(ZDO zdo)
        {
            if (zdo == null) return Array.Empty<string>();
            return zdo.GetString(ApprovedKey, string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long playerId) && playerId != 0L)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static bool TryResolveGroup(
            string groupId,
            long playerId,
            out bool member)
        {
            member = false;
            if (!IsCanonicalGroupId(groupId) || playerId == 0L) return false;
            try
            {
                Type api = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "RunicPortals.Api.GroupIntegrationApi", throwOnError: false))
                    .FirstOrDefault(type => type != null);
                MethodInfo method = api?.GetMethod(
                    "TryIsMember",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(string), typeof(long), typeof(bool).MakeByRefType() },
                    modifiers: null);
                if (method == null) return false;
                object[] arguments = { groupId, playerId, false };
                bool available = method.Invoke(null, arguments) is bool result && result;
                member = available && arguments[2] is bool resolved && resolved;
                return available;
            }
            catch
            {
                member = false;
                return false;
            }
        }
    }
}
