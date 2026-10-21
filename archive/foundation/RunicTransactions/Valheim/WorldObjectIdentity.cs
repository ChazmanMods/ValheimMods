using System;
using System.Collections.Generic;
using HarmonyLib;
using RunicTransactions.Contracts;
using UnityEngine;

namespace RunicTransactions.Valheim
{
    /// <summary>
    /// Restart-stable identity stored on one persistent Valheim ZDO. Unlike <see cref="ZDOID"/>,
    /// this token is not rewritten when Valheim compacts and renumbers world objects while saving.
    /// </summary>
    public readonly struct WorldObjectToken : IEquatable<WorldObjectToken>, IComparable<WorldObjectToken>
    {
        private WorldObjectToken(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public bool IsValid => TryParse(Value, out _);

        public static bool TryParse(string value, out WorldObjectToken token)
        {
            token = default;
            if (value == null || value.Length != 32 ||
                !Guid.TryParseExact(value, "N", out Guid parsed) ||
                parsed == Guid.Empty ||
                !string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal))
                return false;
            token = new WorldObjectToken(value);
            return true;
        }

        internal static WorldObjectToken NewToken() =>
            new WorldObjectToken(Guid.NewGuid().ToString("N"));

        public bool Equals(WorldObjectToken other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is WorldObjectToken other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public int CompareTo(WorldObjectToken other) =>
            string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(WorldObjectToken left, WorldObjectToken right) =>
            left.Equals(right);

        public static bool operator !=(WorldObjectToken left, WorldObjectToken right) =>
            !left.Equals(right);
    }

    public enum WorldObjectIdentityStatus
    {
        Ready = 0,
        Missing = 1,
        Invalid = 2,
        ServerRequired = 3,
        AuthorityRequired = 4,
        WorldUnavailable = 5,
        NotFound = 6,
        Ambiguous = 7,
        CapacityExceeded = 8,
        InstanceUnavailable = 9,
        ComponentUnavailable = 10
    }

    /// <summary>
    /// Shared persistent identity for Valheim world objects. A small marker makes all tagged ZDOs
    /// discoverable without relying on their restart-unstable numeric ZDOIDs. Resolution always
    /// proves that exactly one current ZDO carries the requested token; a duplicate fails closed.
    /// </summary>
    public static class WorldObjectIdentity
    {
        public const string TokenStorageKey = "runic.transactions.world-object.token";
        public const string PresenceStorageKey = "runic.transactions.world-object.token-present";
        public const int MaximumIndexedObjects = 16384;

        private static readonly int TokenHash = TokenStorageKey.GetStableHashCode();
        private static readonly int PresenceHash = PresenceStorageKey.GetStableHashCode();
        private static readonly object IndexSync = new object();
        private static long _mutationGeneration = 1L;
        private static IdentityIndex _index;

        public static WorldObjectIdentityStatus Read(
            ZDO zdo,
            out WorldObjectToken token)
        {
            token = default;
            if (zdo == null || !zdo.IsValid()) return WorldObjectIdentityStatus.Missing;
            string value = zdo.GetString(TokenStorageKey, string.Empty);
            int presence = zdo.GetInt(PresenceStorageKey, 0);
            if (presence == 0 && string.IsNullOrEmpty(value))
                return WorldObjectIdentityStatus.Missing;
            if (presence != 1 || !WorldObjectToken.TryParse(value, out token))
            {
                token = default;
                return WorldObjectIdentityStatus.Invalid;
            }
            return WorldObjectIdentityStatus.Ready;
        }

        /// <summary>
        /// Reuses or mints one token. Minting is allowed only on the authoritative server while it
        /// owns the exact ZDO. Capacity is proved before either field is changed, so reaching the
        /// bounded global index can deny a new identity without disabling existing identities. The
        /// token is written before its marker, so an interrupted first write can be completed safely
        /// and can never authorize a link by itself.
        /// </summary>
        public static WorldObjectIdentityStatus Ensure(
            ZNetView view,
            out WorldObjectToken token)
        {
            token = default;
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return WorldObjectIdentityStatus.ServerRequired;
            if (view == null || !view.IsValid() || !view.IsOwner())
                return WorldObjectIdentityStatus.AuthorityRequired;
            ZDO zdo = view.GetZDO();
            if (zdo == null || !zdo.IsValid())
                return WorldObjectIdentityStatus.AuthorityRequired;

            // Serialize the capacity proof and publication. Harmony mutation callbacks are
            // synchronous and Monitor locks are re-entrant, so the two token-first writes update
            // this same index without permitting two concurrent Ensures to overbook the cap.
            lock (IndexSync)
            {
                WorldObjectIdentityStatus current = Read(zdo, out token);
                if (current == WorldObjectIdentityStatus.Ready)
                    return ProveUniqueOwner(zdo, token);

                string interruptedValue = zdo.GetString(TokenStorageKey, string.Empty);
                int interruptedPresence = zdo.GetInt(PresenceStorageKey, 0);
                if (current == WorldObjectIdentityStatus.Invalid)
                {
                    // Only the documented token-first crash boundary is repairable. Any other
                    // partial or malformed state is retained as evidence for explicit repair.
                    if (interruptedPresence != 0 ||
                        !WorldObjectToken.TryParse(interruptedValue, out token))
                    {
                        token = default;
                        return WorldObjectIdentityStatus.Invalid;
                    }
                }

                WorldObjectIdentityStatus capacity = CheckNewMarkerCapacity(current);
                if (capacity != WorldObjectIdentityStatus.Ready)
                {
                    token = default;
                    return capacity;
                }

                if (current == WorldObjectIdentityStatus.Missing)
                {
                    token = WorldObjectToken.NewToken();
                    zdo.Set(TokenStorageKey, token.Value);
                    if (!string.Equals(
                            zdo.GetString(TokenStorageKey, string.Empty),
                            token.Value,
                            StringComparison.Ordinal))
                    {
                        token = default;
                        return WorldObjectIdentityStatus.AuthorityRequired;
                    }
                }
                zdo.Set(PresenceStorageKey, 1);

                if (Read(zdo, out WorldObjectToken verified) !=
                        WorldObjectIdentityStatus.Ready || verified != token)
                {
                    token = default;
                    return WorldObjectIdentityStatus.Invalid;
                }
                return ProveUniqueOwner(zdo, token);
            }
        }

        public static WorldObjectIdentityStatus Resolve(
            string tokenValue,
            out ZDO zdo)
        {
            zdo = null;
            return WorldObjectToken.TryParse(tokenValue, out WorldObjectToken token)
                ? Resolve(token, out zdo)
                : WorldObjectIdentityStatus.Invalid;
        }

        public static WorldObjectIdentityStatus Resolve(
            WorldObjectToken token,
            out ZDO zdo)
        {
            zdo = null;
            if (!token.IsValid) return WorldObjectIdentityStatus.Invalid;
            if (ZDOMan.instance == null) return WorldObjectIdentityStatus.WorldUnavailable;

            WorldObjectIdentityStatus indexStatus = GetIndex(
                forceRebuild: false, out IdentityIndex index);
            if (indexStatus != WorldObjectIdentityStatus.Ready) return indexStatus;
            WorldObjectIdentityStatus cached = ResolveFromIndex(index, token, out zdo);
            if (cached != WorldObjectIdentityStatus.Invalid) return cached;

            // A cached positive/duplicate entry stopped matching its exact ZDO. Rebuild even if
            // the marker-ID set did not change; this catches unsupported direct token edits while
            // retaining bounded, fail-closed resolution.
            indexStatus = GetIndex(forceRebuild: true, out index);
            return indexStatus == WorldObjectIdentityStatus.Ready
                ? ResolveFromIndex(index, token, out zdo)
                : indexStatus;
        }

        public static WorldObjectIdentityStatus ResolveContainer(
            string tokenValue,
            out Container container,
            out ZDO zdo)
        {
            container = null;
            WorldObjectIdentityStatus resolved = Resolve(tokenValue, out zdo);
            if (resolved != WorldObjectIdentityStatus.Ready) return resolved;
            GameObject root = ZNetScene.instance?.FindInstance(zdo.m_uid);
            if (root == null) return WorldObjectIdentityStatus.InstanceUnavailable;

            Container[] candidates = root.GetComponentsInChildren<Container>(true);
            if (candidates == null || candidates.Length == 0 || candidates.Length > 64)
                return WorldObjectIdentityStatus.ComponentUnavailable;
            for (int index = 0; index < candidates.Length; index++)
            {
                Container candidate = candidates[index];
                ZNetView view = View(candidate);
                ZDO candidateZdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (candidate == null || candidateZdo == null ||
                    candidateZdo.m_uid != zdo.m_uid)
                    continue;
                if (container != null && !ReferenceEquals(container, candidate))
                {
                    container = null;
                    return WorldObjectIdentityStatus.Ambiguous;
                }
                container = candidate;
            }
            return container == null
                ? WorldObjectIdentityStatus.ComponentUnavailable
                : WorldObjectIdentityStatus.Ready;
        }

        public static string EndpointId(string tokenValue)
        {
            if (!WorldObjectToken.TryParse(tokenValue, out WorldObjectToken token))
                throw new ArgumentException(
                    "A canonical world-object token is required.",
                    nameof(tokenValue));
            return ValheimIdentityIds.WorldObjectEndpoint(token.Value).Value;
        }

        public static ZNetView View(Container container)
        {
            if (container == null) return null;
            return container.m_rootObjectOverride != null
                ? container.m_rootObjectOverride
                : container.GetComponent<ZNetView>();
        }

        private static WorldObjectIdentityStatus ProveUniqueOwner(
            ZDO expected,
            WorldObjectToken token)
        {
            WorldObjectIdentityStatus status = Resolve(token, out ZDO resolved);
            if (status != WorldObjectIdentityStatus.Ready) return status;
            return resolved != null && expected != null && resolved.m_uid == expected.m_uid
                ? WorldObjectIdentityStatus.Ready
                : WorldObjectIdentityStatus.Ambiguous;
        }

        /// <summary>
        /// Pure boundary policy used by focused tests. A Ready identity publishes no new marker and
        /// therefore remains usable at the exact cap; Missing or repairable token-first evidence
        /// requires one free marker slot.
        /// </summary>
        internal static WorldObjectIdentityStatus EvaluateMarkerCapacityForEnsure(
            WorldObjectIdentityStatus current,
            int taggedCount)
        {
            if (current == WorldObjectIdentityStatus.Ready)
                return WorldObjectIdentityStatus.Ready;
            if (taggedCount < 0) return WorldObjectIdentityStatus.WorldUnavailable;
            return taggedCount < MaximumIndexedObjects
                ? WorldObjectIdentityStatus.Ready
                : WorldObjectIdentityStatus.CapacityExceeded;
        }

        private static WorldObjectIdentityStatus CheckNewMarkerCapacity(
            WorldObjectIdentityStatus current)
        {
            WorldObjectIdentityStatus status = GetIndex(
                forceRebuild: false, out IdentityIndex index);
            if (status != WorldObjectIdentityStatus.Ready) return status;
            return EvaluateMarkerCapacityForEnsure(current, index.TaggedCount);
        }

        private static WorldObjectIdentityStatus GetIndex(
            bool forceRebuild,
            out IdentityIndex index)
        {
            lock (IndexSync)
            {
                index = null;
                ZDOMan manager = ZDOMan.instance;
                if (manager == null) return WorldObjectIdentityStatus.WorldUnavailable;
                if (forceRebuild) _index = null;
                if (CanReuseIndexSnapshot(
                        _index != null && ReferenceEquals(_index.Manager, manager),
                        _index?.Generation ?? 0L,
                        _mutationGeneration,
                        _index?.Dirty ?? true))
                {
                    index = _index;
                    return index.TaggedCount > MaximumIndexedObjects
                        ? WorldObjectIdentityStatus.CapacityExceeded
                        : WorldObjectIdentityStatus.Ready;
                }

                List<ZDOID> tagged;
                try
                {
                    tagged = ZDOExtraData.GetAllZDOIDsWithHash(
                        ZDOExtraData.Type.Int,
                        PresenceHash);
                }
                catch
                {
                    _index = null;
                    return WorldObjectIdentityStatus.WorldUnavailable;
                }
                if (tagged == null)
                {
                    _index = null;
                    return WorldObjectIdentityStatus.WorldUnavailable;
                }

                if (tagged.Count > MaximumIndexedObjects)
                {
                    _index = null;
                    return WorldObjectIdentityStatus.CapacityExceeded;
                }
                var taggedSet = new HashSet<ZDOID>(tagged);

                var rebuilt = new IdentityIndex(manager, _mutationGeneration);
                foreach (ZDOID id in taggedSet)
                {
                    if (!TryReadIndexedState(
                            id, markerKnownPresent: true,
                            out bool markerStored, out string tokenValue))
                    {
                        _index = null;
                        return WorldObjectIdentityStatus.WorldUnavailable;
                    }
                    rebuilt.Apply(id, markerStored, tokenValue, incremental: false);
                }
                rebuilt.Generation = _mutationGeneration;
                _index = rebuilt;
                index = rebuilt;
                return WorldObjectIdentityStatus.Ready;
            }
        }

        private static WorldObjectIdentityStatus ResolveFromIndex(
            IdentityIndex index,
            WorldObjectToken token,
            out ZDO zdo)
        {
            lock (IndexSync)
            {
                zdo = null;
                if (index == null || !ReferenceEquals(index, _index) ||
                    !ReferenceEquals(index.Manager, ZDOMan.instance) || index.Dirty ||
                    index.Generation != _mutationGeneration)
                    return WorldObjectIdentityStatus.Invalid;
                int ownerCount = index.Owners.Count(token.Value);
                if (ownerCount == 0) return WorldObjectIdentityStatus.NotFound;
                if (ownerCount > 1) return WorldObjectIdentityStatus.Ambiguous;
                if (!index.Owners.TryGetSingle(token.Value, out ZDOID owner))
                    return WorldObjectIdentityStatus.Invalid;

                ZDO first = index.Manager.GetZDO(owner);
                if (!TokenMatches(first, token))
                    return WorldObjectIdentityStatus.Invalid;
                zdo = first;
                return WorldObjectIdentityStatus.Ready;
            }
        }

        private static bool TokenMatches(ZDO zdo, WorldObjectToken expected) =>
            zdo != null && Read(zdo, out WorldObjectToken actual) ==
                WorldObjectIdentityStatus.Ready && actual == expected;

        /// <summary>
        /// Called by the identity-field mutation patches after Valheim changes its indexed extra
        /// data. One affected ZDO is removed/reinserted in O(1) expected time. A direct duplicate
        /// token edit is therefore visible even though the set of presence-marker IDs did not
        /// change. Unknown/unreadable mutation state discards the snapshot and fails closed into a
        /// single bounded rebuild on the next lookup.
        /// </summary>
        internal static void NotifyIdentityFieldMutation(ZDOID id)
        {
            lock (IndexSync)
            {
                AdvanceMutationGeneration();
                IdentityIndex current = _index;
                ZDOMan manager = ZDOMan.instance;
                if (current == null) return;
                if (manager == null || !ReferenceEquals(current.Manager, manager))
                {
                    _index = null;
                    return;
                }
                if (!TryReadIndexedState(
                        id, markerKnownPresent: false,
                        out bool markerStored, out string tokenValue))
                {
                    _index = null;
                    return;
                }
                current.Apply(id, markerStored, tokenValue, incremental: true);
                current.Generation = _mutationGeneration;
                current.Dirty = false;
            }
        }

        internal static void NotifyIdentityWorldReset()
        {
            lock (IndexSync)
            {
                AdvanceMutationGeneration();
                _index = null;
            }
        }

        internal static bool IsTokenStorageHash(int hash) => hash == TokenHash;

        internal static bool IsPresenceStorageHash(int hash) => hash == PresenceHash;

        private static void AdvanceMutationGeneration()
        {
            unchecked { _mutationGeneration++; }
            if (_mutationGeneration <= 0L) _mutationGeneration = 1L;
        }

        private static bool TryReadIndexedState(
            ZDOID id,
            bool markerKnownPresent,
            out bool markerStored,
            out string tokenValue)
        {
            markerStored = false;
            tokenValue = string.Empty;
            try
            {
                int presence = 0;
                markerStored = markerKnownPresent ||
                               ZDOExtraData.GetInt(id, PresenceHash, out presence);
                if (markerKnownPresent)
                    presence = ZDOExtraData.GetInt(id, PresenceHash, 0);
                string raw = ZDOExtraData.GetString(id, TokenHash, string.Empty);
                if (markerStored && presence == 1 &&
                    WorldObjectToken.TryParse(raw, out WorldObjectToken parsed))
                    tokenValue = parsed.Value;
                return true;
            }
            catch
            {
                markerStored = false;
                tokenValue = string.Empty;
                return false;
            }
        }

        private static bool CanReuseIndexSnapshot(
            bool sameWorld,
            long snapshotGeneration,
            long currentGeneration,
            bool dirty) =>
            sameWorld && !dirty && snapshotGeneration > 0L &&
            snapshotGeneration == currentGeneration;

        internal static bool CanReuseIndexSnapshotForTests(
            bool sameWorld,
            long snapshotGeneration,
            long currentGeneration,
            bool dirty) =>
            CanReuseIndexSnapshot(
                sameWorld, snapshotGeneration, currentGeneration, dirty);

        internal static IdentityIndexWorkProfile ProfileSequentialEnsuresForTests(int count)
        {
            if (count < 0 || count > MaximumIndexedObjects)
                throw new ArgumentOutOfRangeException(nameof(count));
            var owners = new IndexedOwnershipMap<int>();
            for (int index = 0; index < count; index++)
                owners.Replace(index, true, TestToken(index), incremental: false);
            for (int index = 0; index < count; index++)
                owners.Count(TestToken(index));
            return new IdentityIndexWorkProfile(
                count,
                owners.FullScanVisits,
                owners.IncrementalMutationVisits,
                owners.LookupVisits);
        }

        internal static IdentityIndexWorkProfile ProfileSequentialMintingForTests(int count)
        {
            if (count < 0 || count > MaximumIndexedObjects)
                throw new ArgumentOutOfRangeException(nameof(count));
            var owners = new IndexedOwnershipMap<int>();
            for (int index = 0; index < count; index++)
            {
                string token = TestToken(index);
                owners.Replace(index, false, token, incremental: true);
                owners.Replace(index, true, token, incremental: true);
                owners.Count(token);
            }
            return new IdentityIndexWorkProfile(
                count,
                owners.FullScanVisits,
                owners.IncrementalMutationVisits,
                owners.LookupVisits);
        }

        internal static IdentityDuplicateMutationProbe ProbeDuplicateMutationForTests()
        {
            var owners = new IndexedOwnershipMap<int>();
            const string first = "11111111111111111111111111111111";
            const string second = "22222222222222222222222222222222";
            owners.Replace(1, true, first, incremental: false);
            owners.Replace(2, true, second, incremental: false);
            int markerCountBefore = owners.MarkerCount;
            int ownersBefore = owners.Count(first);
            owners.Replace(2, true, first, incremental: true);
            int ownersAfterInjection = owners.Count(first);
            owners.Replace(1, true, second, incremental: true);
            int ownersAfterOriginalMoves = owners.Count(first);
            owners.Replace(2, true, second, incremental: true);
            return new IdentityDuplicateMutationProbe(
                markerCountBefore,
                owners.MarkerCount,
                ownersBefore,
                ownersAfterInjection,
                ownersAfterOriginalMoves,
                owners.Count(first));
        }

        private static string TestToken(int index) =>
            (index + 1).ToString("x32");

        private sealed class IdentityIndex
        {
            internal IdentityIndex(ZDOMan manager, long generation)
            {
                Manager = manager;
                Generation = generation;
                Owners = new IndexedOwnershipMap<ZDOID>();
            }

            internal ZDOMan Manager { get; }
            internal long Generation { get; set; }
            internal bool Dirty { get; set; }
            internal int TaggedCount => Owners.MarkerCount;
            internal IndexedOwnershipMap<ZDOID> Owners { get; }

            internal void Apply(
                ZDOID id,
                bool markerStored,
                string tokenValue,
                bool incremental) =>
                Owners.Replace(id, markerStored, tokenValue, incremental);
        }

        internal readonly struct IdentityIndexWorkProfile
        {
            internal IdentityIndexWorkProfile(
                int objectCount,
                long fullScanVisits,
                long incrementalMutationVisits,
                long lookupVisits)
            {
                ObjectCount = objectCount;
                FullScanVisits = fullScanVisits;
                IncrementalMutationVisits = incrementalMutationVisits;
                LookupVisits = lookupVisits;
            }

            internal int ObjectCount { get; }
            internal long FullScanVisits { get; }
            internal long IncrementalMutationVisits { get; }
            internal long LookupVisits { get; }
            internal long TotalVisits =>
                FullScanVisits + IncrementalMutationVisits + LookupVisits;
        }

        internal readonly struct IdentityDuplicateMutationProbe
        {
            internal IdentityDuplicateMutationProbe(
                int markerCountBefore,
                int markerCountAfter,
                int ownersBefore,
                int ownersAfter,
                int ownersAfterOriginalMoves,
                int ownersAfterAllMove)
            {
                MarkerCountBefore = markerCountBefore;
                MarkerCountAfter = markerCountAfter;
                OwnersBefore = ownersBefore;
                OwnersAfter = ownersAfter;
                OwnersAfterOriginalMoves = ownersAfterOriginalMoves;
                OwnersAfterAllMove = ownersAfterAllMove;
            }

            internal int MarkerCountBefore { get; }
            internal int MarkerCountAfter { get; }
            internal int OwnersBefore { get; }
            internal int OwnersAfter { get; }
            internal int OwnersAfterOriginalMoves { get; }
            internal int OwnersAfterAllMove { get; }
        }

        /// <summary>
        /// Shared O(1)-expected ownership map used by the live ZDO index and deterministic scale
        /// instrumentation. Replace removes an object's previous token before publishing its new
        /// state, so marker-preserving token edits cannot leave stale uniqueness evidence behind.
        /// </summary>
        private sealed class IndexedOwnershipMap<TId>
        {
            private readonly HashSet<TId> _markers = new HashSet<TId>();
            private readonly Dictionary<TId, string> _tokenById =
                new Dictionary<TId, string>();
            private readonly Dictionary<string, OwnerBucket<TId>> _owners =
                new Dictionary<string, OwnerBucket<TId>>(StringComparer.Ordinal);
            internal int MarkerCount => _markers.Count;
            internal long FullScanVisits { get; private set; }
            internal long IncrementalMutationVisits { get; private set; }
            internal long LookupVisits { get; private set; }

            internal void Replace(
                TId id,
                bool markerStored,
                string tokenValue,
                bool incremental)
            {
                if (incremental) IncrementalMutationVisits++;
                else FullScanVisits++;

                if (_tokenById.TryGetValue(id, out string previous))
                {
                    _tokenById.Remove(id);
                    if (_owners.TryGetValue(previous, out OwnerBucket<TId> previousOwners))
                    {
                        previousOwners.Remove(id);
                        if (previousOwners.Count == 0) _owners.Remove(previous);
                        else _owners[previous] = previousOwners;
                    }
                }

                if (!markerStored)
                {
                    _markers.Remove(id);
                    return;
                }

                _markers.Add(id);
                if (string.IsNullOrEmpty(tokenValue)) return;
                _tokenById[id] = tokenValue;
                _owners.TryGetValue(tokenValue, out OwnerBucket<TId> owners);
                owners.Add(id);
                _owners[tokenValue] = owners;
            }

            internal int Count(string tokenValue)
            {
                LookupVisits++;
                return tokenValue != null && _owners.TryGetValue(
                    tokenValue, out OwnerBucket<TId> owners)
                    ? owners.Count
                    : 0;
            }

            internal bool TryGetSingle(string tokenValue, out TId id)
            {
                id = default;
                if (tokenValue == null ||
                    !_owners.TryGetValue(tokenValue, out OwnerBucket<TId> owners))
                    return false;
                return owners.TryGetSingle(out id);
            }
        }

        private struct OwnerBucket<TId>
        {
            private bool _hasFirst;
            private TId _first;
            private HashSet<TId> _additional;

            internal int Count => (_hasFirst ? 1 : 0) + (_additional?.Count ?? 0);

            internal void Add(TId id)
            {
                if (!_hasFirst)
                {
                    _first = id;
                    _hasFirst = true;
                    return;
                }
                if (EqualityComparer<TId>.Default.Equals(_first, id)) return;
                if (_additional == null) _additional = new HashSet<TId>();
                _additional.Add(id);
            }

            internal void Remove(TId id)
            {
                if (!_hasFirst) return;
                if (!EqualityComparer<TId>.Default.Equals(_first, id))
                {
                    if (_additional == null || !_additional.Remove(id)) return;
                    if (_additional.Count == 0) _additional = null;
                    return;
                }
                if (_additional == null || _additional.Count == 0)
                {
                    _hasFirst = false;
                    _first = default;
                    _additional = null;
                    return;
                }
                using (HashSet<TId>.Enumerator enumerator = _additional.GetEnumerator())
                {
                    if (!enumerator.MoveNext())
                    {
                        _hasFirst = false;
                        _first = default;
                        _additional = null;
                        return;
                    }
                    _first = enumerator.Current;
                }
                _additional.Remove(_first);
                if (_additional.Count == 0) _additional = null;
            }

            internal bool TryGetSingle(out TId id)
            {
                id = default;
                if (!_hasFirst || _additional != null && _additional.Count != 0)
                    return false;
                id = _first;
                return true;
            }
        }
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.Set),
        new[] { typeof(ZDOID), typeof(int), typeof(string) })]
    internal static class WorldObjectIdentityStringSetPatch
    {
        private static void Postfix(ZDOID __0, int __1)
        {
            if (WorldObjectIdentity.IsTokenStorageHash(__1))
                WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
        }
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.Set),
        new[] { typeof(ZDOID), typeof(int), typeof(int) })]
    internal static class WorldObjectIdentityIntSetPatch
    {
        private static void Postfix(ZDOID __0, int __1)
        {
            if (WorldObjectIdentity.IsPresenceStorageHash(__1))
                WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
        }
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.Add),
        new[] { typeof(ZDOID), typeof(int), typeof(string) })]
    internal static class WorldObjectIdentityStringAddPatch
    {
        private static void Postfix(ZDOID __0, int __1)
        {
            if (WorldObjectIdentity.IsTokenStorageHash(__1))
                WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
        }
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.Add),
        new[] { typeof(ZDOID), typeof(int), typeof(int) })]
    internal static class WorldObjectIdentityIntAddPatch
    {
        private static void Postfix(ZDOID __0, int __1)
        {
            if (WorldObjectIdentity.IsPresenceStorageHash(__1))
                WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
        }
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.RemoveInt),
        new[] { typeof(ZDOID), typeof(int) })]
    internal static class WorldObjectIdentityIntRemovePatch
    {
        private static void Postfix(ZDOID __0, int __1)
        {
            if (WorldObjectIdentity.IsPresenceStorageHash(__1))
                WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
        }
    }

    [HarmonyPatch(typeof(ZDOExtraData), "ReleaseInts")]
    internal static class WorldObjectIdentityIntReleasePatch
    {
        private static void Postfix(ZDOID __0) =>
            WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
    }

    [HarmonyPatch(typeof(ZDOExtraData), "ReleaseStrings")]
    internal static class WorldObjectIdentityStringReleasePatch
    {
        private static void Postfix(ZDOID __0) =>
            WorldObjectIdentity.NotifyIdentityFieldMutation(__0);
    }

    [HarmonyPatch(
        typeof(ZDOExtraData), nameof(ZDOExtraData.Release),
        new[] { typeof(ZDO), typeof(ZDOID) })]
    internal static class WorldObjectIdentityReleasePatch
    {
        private static void Postfix(ZDOID __1) =>
            WorldObjectIdentity.NotifyIdentityFieldMutation(__1);
    }

    [HarmonyPatch(typeof(ZDOExtraData), nameof(ZDOExtraData.Reset))]
    internal static class WorldObjectIdentityResetPatch
    {
        private static void Postfix() => WorldObjectIdentity.NotifyIdentityWorldReset();
    }
}
