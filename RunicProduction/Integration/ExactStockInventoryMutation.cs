using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal sealed class StockSnapshotPublicationException : InvalidOperationException
    {
        internal StockSnapshotPublicationException(
            string message,
            bool rollbackProven,
            Exception inner)
            : base(message, inner)
        {
            RollbackProven = rollbackProven;
        }

        internal bool RollbackProven { get; }
    }

    internal enum StockSourcePreparationFailureKind
    {
        None = 0,
        InvalidInput = 1,
        InvalidRequirements = 2,
        SnapshotUnavailable = 3,
        ReserveProtected = 4,
        IngredientUnavailable = 5
    }

    internal sealed class StockOutputDefinition
    {
        internal StockOutputDefinition(
            string prefabId,
            int amount,
            int quality,
            int variant,
            long crafterId,
            string crafterName,
            bool cheated = false)
        {
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                throw new ArgumentException("An exact output prefab ID is required.", nameof(prefabId));
            if (amount <= 0 || amount > StockDomainValidation.MaximumItemAmount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (quality < 1 || quality > 1000) throw new ArgumentOutOfRangeException(nameof(quality));
            if (variant < 0 || variant > 1000000) throw new ArgumentOutOfRangeException(nameof(variant));
            PrefabId = prefabId;
            Amount = amount;
            Quality = quality;
            Variant = variant;
            Cheated = cheated;
            CrafterId = crafterId;
            if (crafterId == 0L)
            {
                if (!string.IsNullOrEmpty(crafterName))
                    throw new ArgumentException(
                        "Vanilla-default output metadata requires both crafter ID zero and an empty name.",
                        nameof(crafterName));
                CrafterName = string.Empty;
            }
            else
            {
                CrafterName = StockDomainValidation.RequireStableText(
                    crafterName, nameof(crafterName), 128);
            }
        }

        internal string PrefabId { get; }
        internal int Amount { get; }
        internal int Quality { get; }
        internal int Variant { get; }
        internal long CrafterId { get; }
        internal string CrafterName { get; }
        internal bool Cheated { get; }
    }

    /// <summary>An immutable exact serialization and hash of one Valheim inventory.</summary>
    internal sealed class StockInventoryState
    {
        internal const int MaximumPayloadBytes = 262144;
        private readonly byte[] _payload;
        private ItemDrop.ItemData[] _drawerItems;
        internal ItemDrop.ItemData[] CopyDrawerItems() => _drawerItems?.Select(item => item.Clone()).ToArray();

        internal StockInventoryState(byte[] payload, string fingerprint)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaximumPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload));
            string actual = Fingerprint(payload);
            if (!string.Equals(actual, fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("The inventory payload does not match its fingerprint.", nameof(fingerprint));
            _payload = (byte[])payload.Clone();
            FingerprintValue = fingerprint;
        }

        internal string FingerprintValue { get; }
        internal int PayloadLength => _payload.Length;
        internal byte[] CopyPayload() => (byte[])_payload.Clone();

        internal bool Matches(Inventory inventory) =>
            inventory != null && string.Equals(
                FingerprintValue,
                ExactStockInventoryMutation.Fingerprint(inventory),
                StringComparison.Ordinal);

        internal static StockInventoryState Capture(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var package = new ZPackage();
            inventory.Save(package);
            var drawerItems = ProductionContainerCompatibility.CaptureItems(inventory);
            // Native item serialization stores stack counts in a ushort. Include full
            // drawer quantities in the fingerprint so very large stacks cannot alias.
            if (drawerItems != null) foreach (var item in drawerItems) package.Write(item.m_stack);
            byte[] payload = package.GetArray();
            return new StockInventoryState(payload, Fingerprint(payload)) {
                _drawerItems = drawerItems
            };
        }

        internal static string Fingerprint(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaximumPayloadBytes)
                return string.Empty;
            try { return Convert.ToBase64String(new ZPackage(payload).GenerateHash()); }
            catch { return string.Empty; }
        }
    }

    internal sealed class StockInventoryTransition
    {
        internal StockInventoryTransition(
            StockInventoryState before,
            StockInventoryState after,
            bool consumedCheated = false)
        {
            Before = before ?? throw new ArgumentNullException(nameof(before));
            After = after ?? throw new ArgumentNullException(nameof(after));
            ConsumedCheated = consumedCheated;
            if (string.Equals(
                    before.FingerprintValue,
                    after.FingerprintValue,
                    StringComparison.Ordinal))
                throw new ArgumentException("A positive stock transition must change inventory state.");
        }

        internal StockInventoryState Before { get; }
        internal StockInventoryState After { get; }
        internal bool ConsumedCheated { get; }

        internal bool MatchesBefore(Inventory inventory) => Before.Matches(inventory);
        internal bool MatchesAfter(Inventory inventory) => After.Matches(inventory);
        internal void ApplyBefore(Inventory inventory) =>
            ExactStockInventoryMutation.ApplySnapshot(inventory, Before);
        internal void ApplyAfter(Inventory inventory) =>
            ExactStockInventoryMutation.ApplySnapshot(inventory, After);
    }

    internal sealed class ExactStockMutationPlan
    {
        internal ExactStockMutationPlan(
            StockInventoryTransition source,
            StockInventoryTransition destination,
            IEnumerable<ReplenishmentRequirement> requirements,
            StockOutputDefinition output)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            var copy = new List<ReplenishmentRequirement>();
            if (requirements != null) copy.AddRange(requirements);
            if (copy.Count == 0 || copy.Any(requirement => requirement == null))
                throw new ArgumentException(
                    "A complete exact stock plan requires positive requirements.",
                    nameof(requirements));
            Requirements = new ReadOnlyCollection<ReplenishmentRequirement>(copy);
            Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal StockInventoryTransition Source { get; }
        internal StockInventoryTransition Destination { get; }
        internal IReadOnlyList<ReplenishmentRequirement> Requirements { get; }
        internal StockOutputDefinition Output { get; }
    }

    internal sealed class ExactStockStagingPlan
    {
        internal ExactStockStagingPlan(
            int sourceIndex,
            string sourceId,
            string prefabId,
            int amount,
            StockInventoryTransition source,
            StockInventoryTransition destination,
            NearbyIngredientStagingPlan aggregatePlan)
        {
            if (sourceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            SourceId = StockDomainValidation.RequireStableText(
                sourceId, nameof(sourceId), 200);
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                throw new ArgumentException("An exact staged prefab is required.", nameof(prefabId));
            if (amount <= 0 || amount > StockDomainValidation.MaximumItemAmount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            SourceIndex = sourceIndex;
            PrefabId = prefabId;
            Amount = amount;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            AggregatePlan = aggregatePlan ?? throw new ArgumentNullException(nameof(aggregatePlan));
        }

        internal int SourceIndex { get; }
        internal string SourceId { get; }
        internal string PrefabId { get; }
        internal int Amount { get; }
        internal StockInventoryTransition Source { get; }
        internal StockInventoryTransition Destination { get; }
        internal NearbyIngredientStagingPlan AggregatePlan { get; }
    }

    internal sealed class ExactStockCompositeSourceEntry
    {
        internal ExactStockCompositeSourceEntry(
            int sourceIndex,
            StockInventoryTransition transition)
        {
            if (sourceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            SourceIndex = sourceIndex;
            Transition = transition ?? throw new ArgumentNullException(nameof(transition));
        }

        internal int SourceIndex { get; }
        internal StockInventoryTransition Transition { get; }
    }

    /// <summary>Exact before/after source snapshots for one recipe split across several chests.</summary>
    internal sealed class ExactStockCompositeSourcePlan
    {
        internal ExactStockCompositeSourcePlan(
            int sourceCount,
            IEnumerable<ExactStockCompositeSourceEntry> entries)
        {
            if (sourceCount < 1 || sourceCount > NearbyIngredientContainerIndex.HardMaximumSourceChests)
                throw new ArgumentOutOfRangeException(nameof(sourceCount));
            var copy = entries?.ToList() ?? throw new ArgumentNullException(nameof(entries));
            if (copy.Count == 0 || copy.Count > sourceCount ||
                copy.Any(value => value == null || value.SourceIndex >= sourceCount) ||
                copy.Select(value => value.SourceIndex).Distinct().Count() != copy.Count)
                throw new ArgumentException("Composite source transitions must be unique and bounded.", nameof(entries));
            SourceCount = sourceCount;
            Entries = new ReadOnlyCollection<ExactStockCompositeSourceEntry>(copy);
        }

        internal int SourceCount { get; }
        internal IReadOnlyList<ExactStockCompositeSourceEntry> Entries { get; }
        internal bool ConsumedCheated => Entries.Any(entry => entry.Transition.ConsumedCheated);
    }

    /// <summary>
    /// Plans exact-prefab mutations on inventory clones. Applying a transition suppresses change
    /// callbacks during Inventory.Load and publishes one final change, so the container ZDO can
    /// persist only the complete before or complete after snapshot.
    /// </summary>
    internal static class ExactStockInventoryMutation
    {
        private static readonly MethodInfo InventoryAddAtMethod = AccessTools.Method(
            typeof(Inventory),
            "AddItem",
            new[]
            {
                typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool)
            }) ??
            throw new MissingMethodException(
                typeof(Inventory).FullName,
                "AddItem(ItemData,int,int,int,bool)");

        internal static bool TryPrepare(
            Inventory source,
            Inventory destination,
            IEnumerable<ReplenishmentRequirement> requirements,
            IngredientReservePolicy reserves,
            StockOutputDefinition output,
            out ExactStockMutationPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (!TryPrepareSource(
                    source, requirements, reserves,
                    out StockInventoryTransition sourceTransition,
                    out IReadOnlyList<ReplenishmentRequirement> normalized,
                    out failure)) return false;
            if (!TryPrepareDestination(
                    destination, output,
                    out StockInventoryTransition destinationTransition,
                    out failure)) return false;
            plan = new ExactStockMutationPlan(
                sourceTransition, destinationTransition, normalized, output);
            return true;
        }

        internal static bool TryPrepareSource(
            Inventory source,
            IEnumerable<ReplenishmentRequirement> requirements,
            IngredientReservePolicy reserves,
            out StockInventoryTransition transition,
            out IReadOnlyList<ReplenishmentRequirement> normalizedRequirements,
            out string failure)
            => TryPrepareSourceDetailed(
                source,
                requirements,
                reserves,
                out transition,
                out normalizedRequirements,
                out _,
                out failure);

        internal static bool TryPrepareSourceDetailed(
            Inventory source,
            IEnumerable<ReplenishmentRequirement> requirements,
            IngredientReservePolicy reserves,
            out StockInventoryTransition transition,
            out IReadOnlyList<ReplenishmentRequirement> normalizedRequirements,
            out StockSourcePreparationFailureKind failureKind,
            out string failure)
        {
            transition = null;
            normalizedRequirements = Array.Empty<ReplenishmentRequirement>();
            failureKind = StockSourcePreparationFailureKind.None;
            failure = string.Empty;
            if (source == null || reserves == null || !reserves.IsValid)
            {
                failureKind = StockSourcePreparationFailureKind.InvalidInput;
                failure = "stock.source-or-reserve-policy-unavailable";
                return false;
            }
            if (!TryNormalizeRequirements(requirements, out List<ReplenishmentRequirement> normalized, out failure))
            {
                failureKind = StockSourcePreparationFailureKind.InvalidRequirements;
                return false;
            }

            StockInventoryState before = StockInventoryState.Capture(source);
            Inventory shadow;
            try
            {
                shadow = Clone(source, before);
            }
            catch
            {
                failureKind = StockSourcePreparationFailureKind.SnapshotUnavailable;
                failure = "stock.source-snapshot-not-roundtrippable";
                return false;
            }
            bool consumedCheated = false;
            foreach (ReplenishmentRequirement requirement in normalized)
            {
                int available = CountExact(shadow, requirement.PrefabId);
                if (!reserves.AllowsConsumption(
                        requirement.PrefabId, available, requirement.Amount))
                {
                    failureKind = StockSourcePreparationFailureKind.ReserveProtected;
                    failure = "stock.ingredient-reserve-protected:" + requirement.PrefabId;
                    return false;
                }
                if (!RemoveExact(
                        shadow,
                        requirement.PrefabId,
                        requirement.Amount,
                        out bool requirementCheated))
                {
                    failureKind = StockSourcePreparationFailureKind.IngredientUnavailable;
                    failure = "stock.ingredient-unavailable:" + requirement.PrefabId;
                    return false;
                }
                consumedCheated |= requirementCheated;
            }
            StockInventoryState after = StockInventoryState.Capture(shadow);
            try
            {
                Clone(shadow, after);
            }
            catch
            {
                failureKind = StockSourcePreparationFailureKind.SnapshotUnavailable;
                failure = "stock.source-result-not-roundtrippable";
                return false;
            }
            transition = new StockInventoryTransition(before, after, consumedCheated);
            normalizedRequirements = new ReadOnlyCollection<ReplenishmentRequirement>(normalized);
            return true;
        }

        internal static bool TryPrepareDestination(
            Inventory destination,
            StockOutputDefinition output,
            out StockInventoryTransition transition,
            out string failure)
        {
            transition = null;
            failure = string.Empty;
            if (destination == null || output == null)
            {
                failure = "stock.destination-or-output-unavailable";
                return false;
            }
            GameObject outputPrefab = ObjectDB.instance?.GetItemPrefab(output.PrefabId);
            ItemDrop item = outputPrefab?.GetComponent<ItemDrop>();
            if (item == null || !string.Equals(outputPrefab.name, output.PrefabId, StringComparison.Ordinal))
            {
                failure = "stock.output-prefab-unavailable";
                return false;
            }

            StockInventoryState before = StockInventoryState.Capture(destination);
            Inventory shadow;
            try
            {
                shadow = Clone(destination, before);
            }
            catch
            {
                failure = "stock.destination-snapshot-not-roundtrippable";
                return false;
            }
            int exactBefore = CountExact(shadow, output.PrefabId, matchWorldLevel: false);
            if (!TryAddExactOutput(shadow, outputPrefab, item, output))
            {
                failure = "stock.complete-output-batch-does-not-fit";
                return false;
            }
            int exactAfter = CountExact(shadow, output.PrefabId, matchWorldLevel: false);
            if (exactAfter - exactBefore != output.Amount)
            {
                failure = "stock.output-metadata-or-count-mismatch";
                return false;
            }
            StockInventoryState after = StockInventoryState.Capture(shadow);
            try
            {
                Clone(shadow, after);
            }
            catch
            {
                failure = "stock.destination-result-not-roundtrippable";
                return false;
            }
            transition = new StockInventoryTransition(before, after);
            return true;
        }

        internal static bool TryPrepareCompositeSources(
            IReadOnlyList<Inventory> orderedSources,
            IEnumerable<ReplenishmentRequirement> requirements,
            IngredientReservePolicy reserves,
            out ExactStockCompositeSourcePlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (orderedSources == null || orderedSources.Count == 0 ||
                orderedSources.Count > NearbyIngredientContainerIndex.HardMaximumSourceChests ||
                reserves == null || !reserves.IsValid ||
                orderedSources.Any(value => value == null) ||
                orderedSources.Distinct().Count() != orderedSources.Count)
            {
                failure = "stock.composite-sources-invalid";
                return false;
            }
            if (!TryNormalizeRequirements(
                    requirements,
                    out List<ReplenishmentRequirement> normalized,
                    out failure)) return false;
            try
            {
                var before = new StockInventoryState[orderedSources.Count];
                var shadows = new Inventory[orderedSources.Count];
                var changed = new bool[orderedSources.Count];
                var consumedCheated = new bool[orderedSources.Count];
                for (int index = 0; index < orderedSources.Count; index++)
                {
                    before[index] = StockInventoryState.Capture(orderedSources[index]);
                    shadows[index] = Clone(orderedSources[index], before[index]);
                }

                foreach (ReplenishmentRequirement requirement in normalized)
                {
                    int remaining = requirement.Amount;
                    for (int index = 0; index < shadows.Length && remaining > 0; index++)
                    {
                        int current = CountExact(shadows[index], requirement.PrefabId);
                        if (!reserves.TryGetProtectedReserve(
                                requirement.PrefabId, out int reserve))
                        {
                            failure = "stock.composite-reserve-unavailable:" + requirement.PrefabId;
                            return false;
                        }
                        int available = Math.Max(0, current - reserve);
                        int take = Math.Min(remaining, available);
                        if (take == 0) continue;
                        if (!RemoveExact(
                                shadows[index],
                                requirement.PrefabId,
                                take,
                                out bool sourceCheated))
                        {
                            failure = "stock.composite-remove-failed:" + requirement.PrefabId;
                            return false;
                        }
                        consumedCheated[index] |= sourceCheated;
                        changed[index] = true;
                        remaining -= take;
                    }
                    if (remaining != 0)
                    {
                        failure = "stock.composite-ingredient-unavailable:" + requirement.PrefabId;
                        return false;
                    }
                }

                var entries = new List<ExactStockCompositeSourceEntry>();
                for (int index = 0; index < shadows.Length; index++)
                {
                    if (!changed[index]) continue;
                    StockInventoryState after = StockInventoryState.Capture(shadows[index]);
                    Clone(shadows[index], after);
                    entries.Add(new ExactStockCompositeSourceEntry(
                        index,
                            new StockInventoryTransition(
                                before[index], after, consumedCheated[index])));
                }
                plan = new ExactStockCompositeSourcePlan(orderedSources.Count, entries);
                return true;
            }
            catch (Exception exception)
            {
                failure = "stock.composite-snapshot-failure:" + exception.GetType().Name;
                plan = null;
                return false;
            }
        }

        /// <summary>
        /// Proves that the Input chest can receive every missing reserve-plus-batch ingredient
        /// from the ordered sources, then returns exact before/after snapshots for only the first
        /// bounded transfer. Source order is already deterministic and authorization is the
        /// caller's responsibility. All work before ApplyAfter occurs on round-tripped shadows.
        /// </summary>
        internal static bool TryPrepareStaging(
            Inventory input,
            IReadOnlyList<Inventory> orderedSources,
            IReadOnlyList<string> orderedSourceIds,
            IEnumerable<ReplenishmentRequirement> requirements,
            IngredientReservePolicy reserves,
            int pullBatchSize,
            out ExactStockStagingPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (input == null || orderedSources == null || orderedSourceIds == null ||
                orderedSources.Count == 0 || orderedSources.Count != orderedSourceIds.Count ||
                orderedSources.Count > NearbyIngredientStagingPlanner.HardMaximumSources ||
                reserves == null || !reserves.IsValid || pullBatchSize <= 0 ||
                pullBatchSize > NearbyIngredientStagingPlanner.HardMaximumPullAmount)
            {
                failure = "nearby-stage.inventory-input-invalid";
                return false;
            }

            StockInventoryState inputBefore;
            Inventory fullInputShadow;
            var sourceBefore = new List<StockInventoryState>(orderedSources.Count);
            var fullSourceShadows = new List<Inventory>(orderedSources.Count);
            var sourceStocks = new List<NearbyIngredientSourceStock>(orderedSources.Count);
            var seenInventories = new HashSet<Inventory>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            List<ReplenishmentRequirement> normalizedRequirements = requirements?
                .Take(ReplenishmentTargetAuthorization.MaximumRequirements + 1)
                .ToList();
            if (normalizedRequirements == null || normalizedRequirements.Count == 0 ||
                normalizedRequirements.Count > ReplenishmentTargetAuthorization.MaximumRequirements ||
                normalizedRequirements.Any(value => value == null))
            {
                failure = "nearby-stage.requirements-invalid";
                return false;
            }

            try
            {
                inputBefore = StockInventoryState.Capture(input);
                fullInputShadow = Clone(input, inputBefore);
                var inputCounts = ExactCounts(input, normalizedRequirements);
                for (int index = 0; index < orderedSources.Count; index++)
                {
                    Inventory source = orderedSources[index];
                    string sourceId = orderedSourceIds[index];
                    if (source == null || ReferenceEquals(source, input) ||
                        !seenInventories.Add(source) || !seenIds.Add(sourceId ?? string.Empty))
                    {
                        failure = "nearby-stage.source-alias-or-duplicate";
                        return false;
                    }
                    StockInventoryState before = StockInventoryState.Capture(source);
                    sourceBefore.Add(before);
                    fullSourceShadows.Add(Clone(source, before));
                    sourceStocks.Add(new NearbyIngredientSourceStock(
                        sourceId, ExactCounts(source, normalizedRequirements)));
                }

                if (!NearbyIngredientStagingPlanner.TryPlan(
                        normalizedRequirements,
                        inputCounts,
                        sourceStocks,
                        reserves,
                        pullBatchSize,
                        out NearbyIngredientStagingPlan aggregate,
                        out failure)) return false;

                foreach (NearbyIngredientStagingContribution contribution in aggregate.Contributions)
                    if (contribution.SourceIndex < 0 ||
                        contribution.SourceIndex >= fullSourceShadows.Count ||
                        !TryMoveExactPreservingMetadata(
                            fullSourceShadows[contribution.SourceIndex],
                            fullInputShadow,
                            contribution.PrefabId,
                            contribution.Amount))
                    {
                        failure = "nearby-stage.complete-batch-does-not-fit-input";
                        return false;
                    }
                // Prove that the simulated complete result is itself a stable exact inventory.
                Clone(fullInputShadow, StockInventoryState.Capture(fullInputShadow));
                foreach (Inventory sourceShadow in fullSourceShadows)
                    Clone(sourceShadow, StockInventoryState.Capture(sourceShadow));

                NearbyIngredientStagingContribution first = aggregate.NextContribution;
                Inventory exactSourceShadow = Clone(
                    orderedSources[first.SourceIndex], sourceBefore[first.SourceIndex]);
                Inventory exactInputShadow = Clone(input, inputBefore);
                int sourceCountBefore = CountExact(exactSourceShadow, first.PrefabId);
                int inputCountBefore = CountExact(exactInputShadow, first.PrefabId);
                if (!TryMoveExactPreservingMetadata(
                        exactSourceShadow,
                        exactInputShadow,
                        first.PrefabId,
                        aggregate.NextTransferAmount) ||
                    sourceCountBefore - CountExact(exactSourceShadow, first.PrefabId) !=
                        aggregate.NextTransferAmount ||
                    CountExact(exactInputShadow, first.PrefabId) - inputCountBefore !=
                        aggregate.NextTransferAmount)
                {
                    failure = "nearby-stage.exact-transfer-could-not-be-modeled";
                    return false;
                }

                StockInventoryState sourceAfter = StockInventoryState.Capture(exactSourceShadow);
                StockInventoryState inputAfter = StockInventoryState.Capture(exactInputShadow);
                Clone(exactSourceShadow, sourceAfter);
                Clone(exactInputShadow, inputAfter);
                plan = new ExactStockStagingPlan(
                    first.SourceIndex,
                    first.SourceId,
                    first.PrefabId,
                    aggregate.NextTransferAmount,
                    new StockInventoryTransition(sourceBefore[first.SourceIndex], sourceAfter),
                    new StockInventoryTransition(inputBefore, inputAfter),
                    aggregate);
                return true;
            }
            catch (Exception exception)
            {
                failure = "nearby-stage.snapshot-or-metadata-failure:" +
                          exception.GetType().Name;
                plan = null;
                return false;
            }
        }

        internal static void ApplySnapshot(Inventory inventory, StockInventoryState state)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Inventory.Load silently skips missing prefabs and can normalize stacks against a
            // changed max-stack definition. Prove that the serialized target round-trips on a
            // callback-free inventory of the same dimensions before touching the live endpoint.
            Clone(inventory, state);
            StockInventoryState rollback = StockInventoryState.Capture(inventory);
            Clone(inventory, rollback);

            Action changed = inventory.m_onChanged;
            Exception applyFailure;
            try
            {
                inventory.m_onChanged = null;
                ProductionContainerCompatibility.Load(inventory, state);
                if (!state.Matches(inventory))
                    throw new InvalidOperationException(
                        "The exact inventory snapshot did not apply.");
                inventory.m_onChanged = changed;
                changed?.Invoke();
                if (!state.Matches(inventory))
                    throw new InvalidOperationException(
                        "The exact inventory snapshot changed during publication.");
                return;
            }
            catch (Exception exception) { applyFailure = exception; }
            finally
            {
                inventory.m_onChanged = changed;
            }

            Exception rollbackFailure = null;
            bool rollbackProven = false;
            try
            {
                inventory.m_onChanged = null;
                ProductionContainerCompatibility.Load(inventory, rollback);
                if (!rollback.Matches(inventory))
                    throw new InvalidOperationException(
                        "The original inventory snapshot did not restore.");
                inventory.m_onChanged = changed;
                changed?.Invoke();
                if (!rollback.Matches(inventory))
                    throw new InvalidOperationException(
                        "The original inventory changed during rollback publication.");
                rollbackProven = true;
            }
            catch (Exception exception) { rollbackFailure = exception; }
            finally { inventory.m_onChanged = changed; }
            throw new StockSnapshotPublicationException(
                rollbackProven
                    ? "The exact inventory publication failed; the original live state was restored."
                    : "The exact inventory publication failed and rollback is indeterminate.",
                rollbackProven,
                rollbackFailure == null
                    ? applyFailure
                    : new AggregateException(applyFailure, rollbackFailure));
        }

        internal static string Fingerprint(Inventory inventory) =>
            StockInventoryState.Capture(inventory).FingerprintValue;

        internal static int CountExact(
            Inventory inventory,
            string prefabId,
            bool matchWorldLevel = true)
        {
            if (inventory == null || !StockDomainValidation.IsExactPrefabId(prefabId)) return 0;
            long total = 0L;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!string.Equals(ValheimAccess.PrefabName(item), prefabId, StringComparison.Ordinal) ||
                    matchWorldLevel && item.m_worldLevel < Game.m_worldLevel) continue;
                total += Math.Max(0, item.m_stack);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)total;
        }

        private static IReadOnlyDictionary<string, int> ExactCounts(
            Inventory inventory,
            IEnumerable<ReplenishmentRequirement> requirements)
        {
            var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (ReplenishmentRequirement requirement in requirements)
                if (!result.ContainsKey(requirement.PrefabId))
                    result.Add(requirement.PrefabId, CountExact(inventory, requirement.PrefabId));
            return result;
        }

        private static bool TryNormalizeRequirements(
            IEnumerable<ReplenishmentRequirement> requirements,
            out List<ReplenishmentRequirement> normalized,
            out string failure)
        {
            normalized = new List<ReplenishmentRequirement>();
            failure = string.Empty;
            var amounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            if (requirements != null)
            {
                int entries = 0;
                foreach (ReplenishmentRequirement requirement in requirements)
                {
                    if (requirement == null || ++entries > ReplenishmentTargetAuthorization.MaximumRequirements)
                    {
                        failure = "stock.requirements-invalid-or-unbounded";
                        return false;
                    }
                    amounts.TryGetValue(requirement.PrefabId, out long previous);
                    long sum = previous + requirement.Amount;
                    if (sum <= 0L || sum > StockDomainValidation.MaximumItemAmount)
                    {
                        failure = "stock.requirement-amount-out-of-range";
                        return false;
                    }
                    amounts[requirement.PrefabId] = sum;
                }
            }
            if (amounts.Count == 0)
            {
                failure = "stock.requirements-empty";
                return false;
            }
            foreach (KeyValuePair<string, long> pair in amounts)
                normalized.Add(new ReplenishmentRequirement(pair.Key, (int)pair.Value));
            return true;
        }

        private static bool RemoveExact(Inventory inventory, string prefabId, int amount) =>
            RemoveExact(inventory, prefabId, amount, out _);

        private static bool RemoveExact(
            Inventory inventory,
            string prefabId,
            int amount,
            out bool consumedCheated)
        {
            consumedCheated = false;
            List<ItemDrop.ItemData> candidates = inventory.GetAllItems()
                .Where(item =>
                    string.Equals(ValheimAccess.PrefabName(item), prefabId, StringComparison.Ordinal) &&
                    item.m_worldLevel >= Game.m_worldLevel)
                .OrderBy(item => item.m_gridPos.y)
                .ThenBy(item => item.m_gridPos.x)
                .ThenBy(item => item.m_quality)
                .ToList();
            int remaining = amount;
            foreach (ItemDrop.ItemData item in candidates)
            {
                int remove = Math.Min(Math.Max(0, item.m_stack), remaining);
                if (remove <= 0) continue;
                consumedCheated |= item.m_cheated;
                if (!ProductionContainerCompatibility.Remove(inventory, item, remove)) return false;
                remaining -= remove;
                if (remaining == 0) return true;
            }
            return false;
        }

        private static bool TryMoveExactPreservingMetadata(
            Inventory source,
            Inventory destination,
            string prefabId,
            int amount)
        {
            if (source == null || destination == null || ReferenceEquals(source, destination) ||
                !StockDomainValidation.IsExactPrefabId(prefabId) || amount <= 0)
                return false;
            List<ItemDrop.ItemData> candidates = source.GetAllItems()
                .Where(item =>
                    string.Equals(ValheimAccess.PrefabName(item), prefabId, StringComparison.Ordinal) &&
                    item.m_worldLevel >= Game.m_worldLevel && item.m_stack > 0)
                .OrderBy(item => item.m_gridPos.y)
                .ThenBy(item => item.m_gridPos.x)
                .ThenBy(item => item.m_quality)
                .ToList();
            int remaining = amount;
            foreach (ItemDrop.ItemData item in candidates)
            {
                int chunk = Math.Min(item.m_stack, remaining);
                if (chunk <= 0) continue;
                ItemDrop.ItemData payload = ProductionContainerCompatibility.TransferTemplate(source, item).Clone();
                payload.m_stack = chunk;
                if (!TryAddExactPreservingMetadata(destination, payload, chunk) ||
                    !ProductionContainerCompatibility.Remove(source, item, chunk)) return false;
                remaining -= chunk;
                if (remaining == 0) return true;
            }
            return false;
        }

        private static bool TryAddExactPreservingMetadata(
            Inventory inventory,
            ItemDrop.ItemData template,
            int amount)
        {
            if (inventory == null || template?.m_shared == null ||
                template.m_dropPrefab == null || amount <= 0) return false;
            if (ProductionContainerCompatibility.IsDrawer(inventory))
                return ProductionContainerCompatibility.TryAdd(inventory, template, amount);
            int maximumStack = template.m_shared.m_maxStackSize;
            if (maximumStack <= 0) return false;
            var mergeTargets = inventory.GetAllItems()
                .Where(candidate =>
                    ExactPersistentMetadataEquals(template, candidate) &&
                    candidate.m_stack >= 0 && candidate.m_stack < maximumStack)
                .OrderBy(candidate => candidate.m_gridPos.y)
                .ThenBy(candidate => candidate.m_gridPos.x)
                .ToList();
            var emptySlots = new List<Vector2i>();
            for (int y = 0; y < inventory.GetHeight(); y++)
            for (int x = 0; x < inventory.GetWidth(); x++)
                if (inventory.GetItemAt(x, y) == null)
                    emptySlots.Add(new Vector2i(x, y));

            long capacity = mergeTargets.Sum(target =>
                (long)maximumStack - target.m_stack);
            capacity += (long)emptySlots.Count * maximumStack;
            if (capacity < amount) return false;
            int remaining = amount;
            foreach (ItemDrop.ItemData target in mergeTargets)
            {
                if (remaining == 0) break;
                int chunk = Math.Min(remaining, maximumStack - target.m_stack);
                if (!AddExactAt(inventory, template, chunk, target.m_gridPos)) return false;
                remaining -= chunk;
            }
            foreach (Vector2i slot in emptySlots)
            {
                if (remaining == 0) break;
                int chunk = Math.Min(remaining, maximumStack);
                if (!AddExactAt(inventory, template, chunk, slot)) return false;
                remaining -= chunk;
            }
            return remaining == 0;
        }

        private static bool TryAddExactOutput(
            Inventory inventory,
            GameObject outputPrefab,
            ItemDrop drop,
            StockOutputDefinition output)
        {
            if (inventory == null || outputPrefab == null || drop?.m_itemData?.m_shared == null)
                return false;
            ItemDrop.ItemData template = drop.m_itemData.Clone();
            template.m_dropPrefab = outputPrefab;
            template.m_stack = 1;
            template.m_quality = output.Quality;
            template.m_variant = output.Variant;
            template.m_crafterID = output.CrafterId;
            template.m_crafterName = output.CrafterName;
            template.m_worldLevel = (byte)Game.m_worldLevel;
            template.m_pickedUp = false;
            template.m_equipped = false;
            template.m_cheated = output.Cheated;
            template.m_durability = template.GetMaxDurability();

            if (ProductionContainerCompatibility.IsDrawer(inventory))
                return ProductionContainerCompatibility.TryAdd(inventory, template, output.Amount);

            int maximumStack = template.m_shared.m_maxStackSize;
            if (maximumStack <= 0) return false;
            var mergeTargets = inventory.GetAllItems()
                .Where(candidate =>
                    ExactPersistentMetadataEquals(template, candidate) &&
                    candidate.m_stack >= 0 && candidate.m_stack < maximumStack)
                .OrderBy(candidate => candidate.m_gridPos.y)
                .ThenBy(candidate => candidate.m_gridPos.x)
                .ToList();
            var emptySlots = new List<Vector2i>();
            for (int y = 0; y < inventory.GetHeight(); y++)
            for (int x = 0; x < inventory.GetWidth(); x++)
                if (inventory.GetItemAt(x, y) == null)
                    emptySlots.Add(new Vector2i(x, y));

            long capacity = 0L;
            foreach (ItemDrop.ItemData target in mergeTargets)
                capacity += maximumStack - target.m_stack;
            capacity += (long)emptySlots.Count * maximumStack;
            if (capacity < output.Amount) return false;

            int remaining = output.Amount;
            foreach (ItemDrop.ItemData target in mergeTargets)
            {
                if (remaining == 0) break;
                int chunk = Math.Min(remaining, maximumStack - target.m_stack);
                if (!AddExactAt(inventory, template, chunk, target.m_gridPos)) return false;
                remaining -= chunk;
            }
            foreach (Vector2i slot in emptySlots)
            {
                if (remaining == 0) break;
                int chunk = Math.Min(remaining, maximumStack);
                if (!AddExactAt(inventory, template, chunk, slot)) return false;
                remaining -= chunk;
            }
            return remaining == 0;
        }

        private static bool AddExactAt(
            Inventory inventory,
            ItemDrop.ItemData template,
            int amount,
            Vector2i slot)
        {
            if (amount <= 0) return false;
            ItemDrop.ItemData payload = template.Clone();
            payload.m_stack = amount;
            bool added = (bool)InventoryAddAtMethod.Invoke(
                inventory,
                new object[] { payload, amount, slot.x, slot.y, false });
            return added && payload.m_stack == 0;
        }

        private static bool ExactPersistentMetadataEquals(
            ItemDrop.ItemData left,
            ItemDrop.ItemData right)
        {
            if (left == null || right == null ||
                !string.Equals(
                    ValheimAccess.PrefabName(left),
                    ValheimAccess.PrefabName(right),
                    StringComparison.Ordinal) ||
                left.m_quality != right.m_quality ||
                left.m_variant != right.m_variant ||
                left.m_worldLevel != right.m_worldLevel ||
                left.m_crafterID != right.m_crafterID ||
                !string.Equals(
                    left.m_crafterName ?? string.Empty,
                    right.m_crafterName ?? string.Empty,
                    StringComparison.Ordinal) ||
                left.m_pickedUp != right.m_pickedUp ||
                left.m_equipped != right.m_equipped ||
                left.m_cheated != right.m_cheated ||
                !left.m_durability.Equals(right.m_durability)) return false;
            return DictionaryEquals(left.m_customData, right.m_customData);
        }

        private static bool DictionaryEquals(
            IDictionary<string, string> left,
            IDictionary<string, string> right)
        {
            int leftCount = left?.Count ?? 0;
            if (leftCount != (right?.Count ?? 0)) return false;
            if (leftCount == 0) return true;
            foreach (KeyValuePair<string, string> pair in left)
                if (!right.TryGetValue(pair.Key, out string value) ||
                    !string.Equals(pair.Value, value, StringComparison.Ordinal)) return false;
            return true;
        }

        private static Inventory Clone(Inventory source, StockInventoryState state)
        {
            var clone = new Inventory(
                source.GetName(),
                null,
                source.GetWidth(),
                source.GetHeight());
            ProductionContainerCompatibility.CopyShape(source, clone);
            ProductionContainerCompatibility.Load(clone, state);
            if (!state.Matches(clone))
                throw new InvalidOperationException(
                    "The serialized inventory snapshot is not an exact round trip under the current item definitions.");
            return clone;
        }
    }
}
