using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicProduction.Core
{
    internal enum ReplenishmentProducerKind
    {
        DirectRecipe = 1,
        TimedCooking = 2,
        Fermenter = 3
    }

    internal sealed class ReplenishmentRequirement
    {
        internal ReplenishmentRequirement(string prefabId, int amount)
        {
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                throw new ArgumentException("An exact ASCII prefab ID is required.", nameof(prefabId));
            if (amount <= 0 || amount > StockDomainValidation.MaximumItemAmount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            PrefabId = prefabId;
            Amount = amount;
        }

        internal string PrefabId { get; }
        internal int Amount { get; }
    }

    /// <summary>Stable identity of the exact replenishment link authorized by this plan.</summary>
    internal sealed class ReplenishmentLinkBinding
    {
        internal ReplenishmentLinkBinding(
            string linkId,
            string stationId,
            string targetId,
            int revision)
            : this(linkId, stationId, targetId, string.Empty, 0, revision)
        {
        }

        internal ReplenishmentLinkBinding(
            string linkId,
            string stationId,
            string targetId,
            string targetToken,
            int targetPrefabHash,
            int revision)
        {
            LinkId = StockDomainValidation.RequireStableText(linkId, nameof(linkId), 200);
            StationId = StockDomainValidation.RequireStableText(stationId, nameof(stationId), 200);
            TargetId = StockDomainValidation.RequireStableText(targetId, nameof(targetId), 200);
            if (!StockDomainValidation.IsLegacyOrWorldObjectIdentity(
                    targetToken,
                    targetPrefabHash))
                throw new ArgumentException(
                    "A canonical target token and non-zero prefab hash must be supplied together.",
                    nameof(targetToken));
            if (!string.IsNullOrEmpty(targetToken) &&
                !string.Equals(targetId, targetToken, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A persistent binding's target ID must be its canonical target token.",
                    nameof(targetId));
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            TargetToken = targetToken ?? string.Empty;
            TargetPrefabHash = targetPrefabHash;
            Revision = revision;
        }

        internal string LinkId { get; }
        internal string StationId { get; }
        internal string TargetId { get; }
        internal string TargetToken { get; }
        internal int TargetPrefabHash { get; }
        internal int Revision { get; }
    }

    /// <summary>
    /// Immutable proof that one exact output was authorized against one producer definition.
    /// The signature is copied on ingress and egress so callers cannot mutate an authorization.
    /// </summary>
    internal sealed class ReplenishmentTargetAuthorization
    {
        internal const int MaximumRequirements = 16;
        internal const int ProducerSignatureBytes = 32;

        private readonly byte[] _producerSignature;
        private readonly ReadOnlyCollection<ReplenishmentRequirement> _requirements;

        internal ReplenishmentTargetAuthorization(
            string outputPrefabId,
            ReplenishmentProducerKind producerKind,
            string producerId,
            byte[] producerSignature,
            int outputAmount,
            string requiredStationName,
            int requiredStationLevel,
            long authorizedPlayerId,
            string authorizedPlayerName,
            IEnumerable<ReplenishmentRequirement> requirements)
        {
            if (!StockDomainValidation.IsExactPrefabId(outputPrefabId))
                throw new ArgumentException("An exact ASCII output prefab ID is required.", nameof(outputPrefabId));
            if (!Enum.IsDefined(typeof(ReplenishmentProducerKind), producerKind))
                throw new ArgumentOutOfRangeException(nameof(producerKind));
            ProducerId = StockDomainValidation.RequireStableText(
                producerId, nameof(producerId), 256);
            if (producerSignature == null || producerSignature.Length != ProducerSignatureBytes)
                throw new ArgumentException(
                    $"The producer signature must contain exactly {ProducerSignatureBytes} bytes.",
                    nameof(producerSignature));
            if (outputAmount <= 0 || outputAmount > StockDomainValidation.MaximumItemAmount)
                throw new ArgumentOutOfRangeException(nameof(outputAmount));
            if (authorizedPlayerId == 0L)
                throw new ArgumentOutOfRangeException(nameof(authorizedPlayerId));

            if (producerKind == ReplenishmentProducerKind.DirectRecipe)
            {
                RequiredStationName = StockDomainValidation.RequireStableText(
                    requiredStationName, nameof(requiredStationName), 128);
                if (requiredStationLevel < 1 ||
                    requiredStationLevel > StockDomainValidation.MaximumStationLevel)
                    throw new ArgumentOutOfRangeException(nameof(requiredStationLevel));
            }
            else
            {
                if (!string.IsNullOrEmpty(requiredStationName) || requiredStationLevel != 0)
                    throw new ArgumentException(
                        "Only direct recipes may carry a crafting-station name and level.");
                RequiredStationName = string.Empty;
            }

            var requirementCopy = new List<ReplenishmentRequirement>();
            if (requirements != null)
            {
                foreach (ReplenishmentRequirement requirement in requirements)
                {
                    if (requirement == null)
                        throw new ArgumentException("Requirements cannot contain null entries.", nameof(requirements));
                    requirementCopy.Add(requirement);
                    if (requirementCopy.Count > MaximumRequirements)
                        throw new ArgumentOutOfRangeException(nameof(requirements));
                }
            }
            if (requirementCopy.Count == 0)
                throw new ArgumentException(
                    "A replenishment producer must consume at least one requirement.",
                    nameof(requirements));

            OutputPrefabId = outputPrefabId;
            ProducerKind = producerKind;
            _producerSignature = (byte[])producerSignature.Clone();
            OutputAmount = outputAmount;
            RequiredStationLevel = requiredStationLevel;
            AuthorizedPlayerId = authorizedPlayerId;
            AuthorizedPlayerName = StockDomainValidation.RequireStableText(
                authorizedPlayerName, nameof(authorizedPlayerName), 128);
            _requirements = requirementCopy.AsReadOnly();
        }

        internal string OutputPrefabId { get; }
        internal ReplenishmentProducerKind ProducerKind { get; }
        internal string ProducerId { get; }
        internal int OutputAmount { get; }
        internal string RequiredStationName { get; }
        internal int RequiredStationLevel { get; }
        internal long AuthorizedPlayerId { get; }
        internal string AuthorizedPlayerName { get; }
        internal IReadOnlyList<ReplenishmentRequirement> Requirements => _requirements;

        internal byte[] CopyProducerSignature() => (byte[])_producerSignature.Clone();

        internal bool ProducerSignatureMatches(byte[] candidate)
        {
            if (candidate == null || candidate.Length != _producerSignature.Length) return false;
            int difference = 0;
            for (int index = 0; index < _producerSignature.Length; index++)
                difference |= _producerSignature[index] ^ candidate[index];
            return difference == 0;
        }
    }

    /// <summary>
    /// Immutable, link-bound replenishment plan. Targets are canonicalized by exact output prefab
    /// ID using ordinal comparison, making selection and persistence independent of scan order.
    /// </summary>
    internal sealed class ReplenishmentPlan
    {
        internal const int MaximumTargets = 32;

        private readonly ReadOnlyCollection<ReplenishmentTargetAuthorization> _targets;

        internal ReplenishmentPlan(
            string planId,
            int revision,
            int cursor,
            string stationPrefabId,
            ReplenishmentProducerKind adapterKind,
            ReplenishmentLinkBinding link,
            long authorizedPlayerId,
            string authorizedPlayerName,
            IEnumerable<ReplenishmentTargetAuthorization> targets)
        {
            PlanId = StockDomainValidation.RequireStableText(planId, nameof(planId), 200);
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            if (!StockDomainValidation.IsExactPrefabId(stationPrefabId))
                throw new ArgumentException("An exact ASCII station prefab ID is required.", nameof(stationPrefabId));
            if (!Enum.IsDefined(typeof(ReplenishmentProducerKind), adapterKind))
                throw new ArgumentOutOfRangeException(nameof(adapterKind));
            Link = link ?? throw new ArgumentNullException(nameof(link));
            if (authorizedPlayerId == 0L)
                throw new ArgumentOutOfRangeException(nameof(authorizedPlayerId));
            AuthorizedPlayerId = authorizedPlayerId;
            AuthorizedPlayerName = StockDomainValidation.RequireStableText(
                authorizedPlayerName,
                nameof(authorizedPlayerName),
                128);

            var targetCopy = new List<ReplenishmentTargetAuthorization>();
            if (targets != null)
            {
                foreach (ReplenishmentTargetAuthorization target in targets)
                {
                    if (target == null)
                        throw new ArgumentException("Targets cannot contain null entries.", nameof(targets));
                    if (target.ProducerKind != adapterKind)
                        throw new ArgumentException(
                            "Every target must use the plan's concrete station adapter.",
                            nameof(targets));
                    if (target.AuthorizedPlayerId != AuthorizedPlayerId ||
                        !string.Equals(
                            target.AuthorizedPlayerName,
                            AuthorizedPlayerName,
                            StringComparison.Ordinal))
                        throw new ArgumentException(
                            "Every target must use the plan's exact authorization principal.",
                            nameof(targets));
                    targetCopy.Add(target);
                    if (targetCopy.Count > MaximumTargets)
                        throw new ArgumentOutOfRangeException(nameof(targets));
                }
            }
            targetCopy.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.OutputPrefabId, right.OutputPrefabId));
            for (int index = 1; index < targetCopy.Count; index++)
                if (string.Equals(
                        targetCopy[index - 1].OutputPrefabId,
                        targetCopy[index].OutputPrefabId,
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "A plan cannot authorize the same exact output prefab more than once.",
                        nameof(targets));

            if (cursor < 0 ||
                targetCopy.Count == 0 && cursor != 0 ||
                targetCopy.Count > 0 && cursor >= targetCopy.Count)
                throw new ArgumentOutOfRangeException(nameof(cursor));

            Revision = revision;
            Cursor = cursor;
            StationPrefabId = stationPrefabId;
            AdapterKind = adapterKind;
            _targets = targetCopy.AsReadOnly();
        }

        internal string PlanId { get; }
        internal int Revision { get; }
        internal int Cursor { get; }
        internal string StationPrefabId { get; }
        internal ReplenishmentProducerKind AdapterKind { get; }
        internal ReplenishmentLinkBinding Link { get; }
        internal long AuthorizedPlayerId { get; }
        internal string AuthorizedPlayerName { get; }
        internal IReadOnlyList<ReplenishmentTargetAuthorization> Targets => _targets;

        internal ReplenishmentPlan WithCursor(int nextCursor) =>
            new ReplenishmentPlan(
                PlanId,
                Revision,
                nextCursor,
                StationPrefabId,
                AdapterKind,
                Link,
                AuthorizedPlayerId,
                AuthorizedPlayerName,
                _targets);
    }

    internal static class StockDomainValidation
    {
        internal const int MaximumIdentifierCharacters = 128;
        internal const int MaximumItemAmount = 1000000;
        internal const int MaximumStationLevel = 1000;
        internal const int WorldObjectTokenCharacters = 32;

        internal static bool IsWorldObjectToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != WorldObjectTokenCharacters)
                return false;
            foreach (char character in value)
            {
                bool decimalDigit = character >= '0' && character <= '9';
                bool lowerHexLetter = character >= 'a' && character <= 'f';
                if (!decimalDigit && !lowerHexLetter) return false;
            }
            return true;
        }

        internal static bool IsLegacyOrWorldObjectIdentity(
            string targetToken,
            int targetPrefabHash) =>
            string.IsNullOrEmpty(targetToken)
                ? targetPrefabHash == 0
                : targetPrefabHash != 0 && IsWorldObjectToken(targetToken);

        internal static bool IsExactPrefabId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumIdentifierCharacters)
                return false;
            foreach (char character in value)
            {
                bool asciiLetter = character >= 'A' && character <= 'Z' ||
                                   character >= 'a' && character <= 'z';
                bool asciiDigit = character >= '0' && character <= '9';
                if (!asciiLetter && !asciiDigit &&
                    character != '_' && character != '-' && character != '.') return false;
            }
            return true;
        }

        internal static string RequireStableText(
            string value,
            string parameterName,
            int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable value is required.", parameterName);
            if (value.Length > maximumCharacters)
                throw new ArgumentOutOfRangeException(parameterName);
            foreach (char character in value)
                if (char.IsControl(character))
                    throw new ArgumentException("Stable values cannot contain control characters.", parameterName);
            return value;
        }
    }
}
