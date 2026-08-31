using System;
using System.Collections.Generic;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    /// <summary>Atomically publishes one bounded, signed replenishment plan on its station ZDO.</summary>
    internal static class ReplenishmentPlanStore
    {
        private const int LegacySchemaVersion = 1;
        private const int StableIdentitySchemaVersion = 2;
        private const int SchemaVersion = 3;
        private const int MaximumEncodedCharacters = 65536;
        private const int MaximumDecodedBytes = 49152;
        private static readonly string RecordKey =
            Plugin.ModuleId + ".stock.plan.record";

        internal static string StorageKey => RecordKey;

        internal static StoredRecordState Read(ZDO zdo, out ReplenishmentPlan plan) =>
            zdo == null
                ? ReturnAbsent(out plan)
                : Parse(zdo.GetString(RecordKey, string.Empty), out plan);

        internal static void Save(ZDO zdo, ReplenishmentPlan plan)
        {
            if (zdo == null) throw new ArgumentNullException(nameof(zdo));
            zdo.Set(RecordKey, Serialize(plan));
        }

        internal static void Clear(ZDO zdo)
        {
            if (zdo != null) zdo.Set(RecordKey, string.Empty);
        }

        internal static string Serialize(ReplenishmentPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var package = new ZPackage();
            package.Write(SchemaVersion);
            package.Write(plan.PlanId);
            package.Write(plan.Revision);
            package.Write(plan.Cursor);
            package.Write(plan.StationPrefabId);
            package.Write((int)plan.AdapterKind);
            package.Write(plan.AuthorizedPlayerId);
            package.Write(plan.AuthorizedPlayerName);
            WriteLink(package, plan.Link);
            package.Write(plan.Targets.Count);
            foreach (ReplenishmentTargetAuthorization target in plan.Targets)
            {
                package.Write(target.OutputPrefabId);
                package.Write((int)target.ProducerKind);
                package.Write(target.ProducerId);
                package.Write(target.CopyProducerSignature());
                package.Write(target.OutputAmount);
                package.Write(target.RequiredStationName);
                package.Write(target.RequiredStationLevel);
                package.Write(target.AuthorizedPlayerId);
                package.Write(target.AuthorizedPlayerName);
                package.Write(target.Requirements.Count);
                foreach (ReplenishmentRequirement requirement in target.Requirements)
                {
                    package.Write(requirement.PrefabId);
                    package.Write(requirement.Amount);
                }
            }
            byte[] bytes = package.GetArray();
            if (bytes.Length == 0 || bytes.Length > MaximumDecodedBytes)
                throw new InvalidOperationException("The replenishment plan exceeds its persisted size bound.");
            string encoded = Convert.ToBase64String(bytes);
            if (encoded.Length > MaximumEncodedCharacters)
                throw new InvalidOperationException("The replenishment plan exceeds its encoded size bound.");
            return encoded;
        }

        internal static StoredRecordState Parse(string encoded, out ReplenishmentPlan plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(encoded)) return StoredRecordState.Absent;
            if (encoded.Length > MaximumEncodedCharacters) return StoredRecordState.Invalid;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length == 0 || bytes.Length > MaximumDecodedBytes)
                    return StoredRecordState.Invalid;
                var package = new ZPackage(bytes);
                int schema = package.ReadInt();
                if (schema != LegacySchemaVersion &&
                    schema != StableIdentitySchemaVersion &&
                    schema != SchemaVersion)
                    return StoredRecordState.Invalid;
                string planId = package.ReadString();
                int revision = package.ReadInt();
                int cursor = package.ReadInt();
                string stationPrefab = package.ReadString();
                var adapterKind = (ReplenishmentProducerKind)package.ReadInt();
                long authorizedPlayerId = schema >= SchemaVersion
                    ? package.ReadLong()
                    : 0L;
                string authorizedPlayerName = schema >= SchemaVersion
                    ? package.ReadString()
                    : string.Empty;
                ReplenishmentLinkBinding link = ReadLink(package, schema);
                int count = package.ReadInt();
                if (count < 0 || count > ReplenishmentPlan.MaximumTargets)
                    return StoredRecordState.Invalid;
                var targets = new List<ReplenishmentTargetAuthorization>(count);
                for (int index = 0; index < count; index++)
                {
                    string outputPrefab = package.ReadString();
                    var producerKind = (ReplenishmentProducerKind)package.ReadInt();
                    string producerId = package.ReadString();
                    byte[] signature = package.ReadByteArray();
                    int outputAmount = package.ReadInt();
                    string requiredStationName = package.ReadString();
                    int requiredStationLevel = package.ReadInt();
                    long targetPlayerId = package.ReadLong();
                    string targetPlayerName = package.ReadString();
                    int requirementCount = package.ReadInt();
                    if (requirementCount <= 0 ||
                        requirementCount > ReplenishmentTargetAuthorization.MaximumRequirements)
                        return StoredRecordState.Invalid;
                    var requirements = new List<ReplenishmentRequirement>(requirementCount);
                    for (int requirementIndex = 0;
                         requirementIndex < requirementCount;
                         requirementIndex++)
                    {
                        requirements.Add(new ReplenishmentRequirement(
                            package.ReadString(), package.ReadInt()));
                    }
                    targets.Add(new ReplenishmentTargetAuthorization(
                        outputPrefab,
                        producerKind,
                        producerId,
                        signature,
                        outputAmount,
                        requiredStationName,
                        requiredStationLevel,
                        targetPlayerId,
                        targetPlayerName,
                        requirements));
                }
                if (schema < SchemaVersion &&
                    !TryDeriveLegacyPrincipal(
                        targets,
                        out authorizedPlayerId,
                        out authorizedPlayerName))
                    return StoredRecordState.Invalid;
                if (package.GetPos() != package.Size()) return StoredRecordState.Invalid;
                plan = new ReplenishmentPlan(
                    planId,
                    revision,
                    cursor,
                    stationPrefab,
                    adapterKind,
                    link,
                    authorizedPlayerId,
                    authorizedPlayerName,
                    targets);
                return StoredRecordState.Valid;
            }
            catch
            {
                plan = null;
                return StoredRecordState.Invalid;
            }
        }

        internal static bool MatchesLink(
            ReplenishmentPlan plan,
            StoredProductionLink link,
            string stationId) =>
            plan != null && link != null &&
            link.Role == Contracts.ProductionLinkRole.Replenishment &&
            string.Equals(plan.Link.LinkId, link.LinkId, StringComparison.Ordinal) &&
            string.Equals(plan.Link.StationId, stationId, StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(plan.Link.TargetToken)
                ? string.Equals(
                    plan.Link.TargetId,
                    link.Target.ToString(),
                    StringComparison.Ordinal)
                : string.Equals(
                    plan.Link.TargetToken,
                    link.TargetToken,
                    StringComparison.Ordinal) &&
                  plan.Link.TargetPrefabHash == link.TargetPrefabHash &&
                  string.Equals(
                    plan.Link.TargetId,
                    link.TargetToken,
                    StringComparison.Ordinal)) &&
            plan.Link.Revision == link.Revision;

        internal static ReplenishmentLinkBinding Bind(
            StoredProductionLink link,
            string stationId)
        {
            if (link == null || link.Role != Contracts.ProductionLinkRole.Replenishment)
                throw new ArgumentException("An exact replenishment link is required.", nameof(link));
            if (!StockDomainValidation.IsLegacyOrWorldObjectIdentity(
                    link.TargetToken,
                    link.TargetPrefabHash))
                throw new ArgumentException(
                    "The replenishment target identity is malformed.",
                    nameof(link));
            string targetId = string.IsNullOrEmpty(link.TargetToken)
                ? link.Target.ToString()
                : link.TargetToken;
            return new ReplenishmentLinkBinding(
                link.LinkId,
                stationId,
                targetId,
                link.TargetToken,
                link.TargetPrefabHash,
                link.Revision);
        }

        /// <summary>
        /// Prepares the plan half of one legacy singleton/catalog identity migration without
        /// changing any authorization, fairness, station, or revision evidence. The caller may
        /// publish the returned bytes only as part of the same guarded migration as the upgraded
        /// stored link.
        /// </summary>
        internal static bool TryPrepareLegacyTargetRebind(
            ReplenishmentPlan plan,
            StoredProductionLink legacyLink,
            StoredProductionLink upgradedLink,
            out ReplenishmentPlan reboundPlan,
            out string encodedPlan)
        {
            reboundPlan = null;
            encodedPlan = string.Empty;
            if (plan == null || legacyLink == null || upgradedLink == null ||
                !string.IsNullOrEmpty(legacyLink.TargetToken) ||
                legacyLink.TargetPrefabHash != 0 ||
                string.IsNullOrEmpty(upgradedLink.TargetToken) ||
                !StockDomainValidation.IsWorldObjectToken(upgradedLink.TargetToken) ||
                upgradedLink.TargetPrefabHash == 0 ||
                upgradedLink.Target.IsNone() ||
                !SameLinkEvidenceExceptTargetIdentity(legacyLink, upgradedLink) ||
                !MatchesLink(plan, legacyLink, plan.Link.StationId))
                return false;

            try
            {
                var reboundBinding = new ReplenishmentLinkBinding(
                    plan.Link.LinkId,
                    plan.Link.StationId,
                    upgradedLink.TargetToken,
                    upgradedLink.TargetToken,
                    upgradedLink.TargetPrefabHash,
                    plan.Link.Revision);
                reboundPlan = new ReplenishmentPlan(
                    plan.PlanId,
                    plan.Revision,
                    plan.Cursor,
                    plan.StationPrefabId,
                    plan.AdapterKind,
                    reboundBinding,
                    plan.AuthorizedPlayerId,
                    plan.AuthorizedPlayerName,
                    plan.Targets);
                encodedPlan = Serialize(reboundPlan);
                return true;
            }
            catch
            {
                reboundPlan = null;
                encodedPlan = string.Empty;
                return false;
            }
        }

        private static bool SameLinkEvidenceExceptTargetIdentity(
            StoredProductionLink legacyLink,
            StoredProductionLink upgradedLink) =>
            legacyLink.Role == upgradedLink.Role &&
            legacyLink.ExpectedPosition == upgradedLink.ExpectedPosition &&
            legacyLink.OwnerId == upgradedLink.OwnerId &&
            legacyLink.StationOwnerId == upgradedLink.StationOwnerId &&
            legacyLink.TargetOwnerId == upgradedLink.TargetOwnerId &&
            legacyLink.Revision == upgradedLink.Revision &&
            string.Equals(
                legacyLink.LinkId,
                upgradedLink.LinkId,
                StringComparison.Ordinal);

        private static StoredRecordState ReturnAbsent(out ReplenishmentPlan plan)
        {
            plan = null;
            return StoredRecordState.Absent;
        }

        private static void WriteLink(ZPackage package, ReplenishmentLinkBinding link)
        {
            package.Write(link.LinkId);
            package.Write(link.StationId);
            package.Write(link.TargetId);
            package.Write(link.TargetToken);
            package.Write(link.TargetPrefabHash);
            package.Write(link.Revision);
        }

        private static ReplenishmentLinkBinding ReadLink(ZPackage package, int schema)
        {
            string linkId = package.ReadString();
            string stationId = package.ReadString();
            string targetId = package.ReadString();
            string targetToken = schema >= StableIdentitySchemaVersion
                ? package.ReadString()
                : string.Empty;
            int targetPrefabHash = schema >= StableIdentitySchemaVersion
                ? package.ReadInt()
                : 0;
            int revision = package.ReadInt();
            return new ReplenishmentLinkBinding(
                linkId,
                stationId,
                targetId,
                targetToken,
                targetPrefabHash,
                revision);
        }

        private static bool TryDeriveLegacyPrincipal(
            IReadOnlyList<ReplenishmentTargetAuthorization> targets,
            out long playerId,
            out string playerName)
        {
            playerId = 0L;
            playerName = string.Empty;
            if (targets == null || targets.Count == 0) return false;
            ReplenishmentTargetAuthorization first = targets[0];
            if (first == null || first.AuthorizedPlayerId == 0L ||
                string.IsNullOrWhiteSpace(first.AuthorizedPlayerName)) return false;
            for (int index = 1; index < targets.Count; index++)
            {
                ReplenishmentTargetAuthorization target = targets[index];
                if (target == null ||
                    target.AuthorizedPlayerId != first.AuthorizedPlayerId ||
                    !string.Equals(
                        target.AuthorizedPlayerName,
                        first.AuthorizedPlayerName,
                        StringComparison.Ordinal)) return false;
            }
            playerId = first.AuthorizedPlayerId;
            playerName = first.AuthorizedPlayerName;
            return true;
        }
    }
}
