using System;
using System.Collections.Generic;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime
{
    internal sealed class ContainerIndex
    {
        private readonly object _gate = new object();
        private readonly Dictionary<CellKey, List<Entry>> _cells = new Dictionary<CellKey, List<Entry>>();
        private readonly Dictionary<int, Membership> _membership = new Dictionary<int, Membership>();
        private readonly Dictionary<string, HashSet<int>> _endpointMembers =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        internal void Add(Container container)
        {
            if (container == null) return;
            ChestRuleStore.Attach(container);
            int instanceId;
            Vector3 position;
            try
            {
                instanceId = container.GetInstanceID();
                position = container.transform.position;
            }
            catch { return; }

            lock (_gate)
            {
                if (!Finite(position))
                {
                    RemoveLocked(instanceId, container);
                    return;
                }
                CellKey cell = CellKey.From(position);
                bool exists = _membership.TryGetValue(instanceId, out Membership old);
                bool cellUnchanged = exists && old.Cell.Equals(cell);
                bool exactPresent = cellUnchanged && _cells.TryGetValue(old.Cell, out List<Entry> entries) &&
                                    ContainsExact(entries, instanceId, container);
                if (ContainerSpatialPolicy.CanRetain(exists, cellUnchanged,
                        !string.IsNullOrEmpty(old.EndpointId), exactPresent))
                    return;

                string endpoint;
                try { endpoint = ValheimContainerIdentity.EndpointId(container); }
                catch { return; }
                RemoveLocked(instanceId, container);
                if (!ContainerSpatialPolicy.CanIndexEndpoint(endpoint)) return;
                if (!_cells.TryGetValue(cell, out entries))
                {
                    entries = new List<Entry>();
                    _cells.Add(cell, entries);
                }
                entries.Add(new Entry(instanceId, container));
                _membership.Add(instanceId, new Membership(cell, endpoint, container));
                AddEndpoint(endpoint, instanceId);
            }
        }

        /// <summary>
        /// Reconciles the index with containers that are already loaded in the current Unity
        /// scene. BepInEx plugin Awake can run before Valheim finishes instantiating or
        /// synchronizing world objects, so relying only on the startup scan and Container.Awake
        /// hook leaves a legitimate first-use window where the index is empty. Storage actions
        /// are explicit and infrequent, making this bounded loaded-object reconciliation the
        /// correct place to close that window.
        /// </summary>
        internal int RefreshLoadedContainers()
        {
            return RefreshLoadedContainers(Vector3.zero, 0f, out _);
        }

        internal int RefreshLoadedContainers(Vector3 origin, float radius, out int loadedInRange)
        {
            loadedInRange = 0;
            Container[] loaded;
            try
            {
                loaded = UnityEngine.Object.FindObjectsByType<Container>(
                    FindObjectsSortMode.None);
            }
            catch
            {
                return 0;
            }
            bool measureRange = Finite(origin) && !float.IsNaN(radius) &&
                                !float.IsInfinity(radius) && radius > 0f;
            float radiusSquared = measureRange ? radius * radius : 0f;
            for (int index = 0; index < loaded.Length; index++)
            {
                Container container = loaded[index];
                Add(container);
                if (!measureRange || container == null) continue;
                try
                {
                    if (!container.isActiveAndEnabled) continue;
                    float distance = (container.transform.position - origin).sqrMagnitude;
                    if (!float.IsNaN(distance) && !float.IsInfinity(distance) &&
                        distance <= radiusSquared)
                        loadedInRange++;
                }
                catch { }
            }
            return loaded.Length;
        }

        internal void Remove(Container container)
        {
            if (ReferenceEquals(container, null)) return;
            int instanceId;
            try { instanceId = container.GetInstanceID(); }
            catch { return; }
            lock (_gate) RemoveLocked(instanceId, container);
        }

        internal IReadOnlyList<Container> Nearest(Vector3 origin, float radius, int maximum, out bool truncated)
        {
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (!Finite(origin) || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
            {
                truncated = false;
                return Array.Empty<Container>();
            }
            radius = Math.Min(ContainerSpatialPolicy.HardMaximumRadiusMeters, radius);
            maximum = Math.Min(ContainerSpatialPolicy.HardMaximumCandidates, maximum);
            float radiusSquared = radius * radius;
            CellKey minimum = CellKey.From(origin - new Vector3(radius, 0f, radius));
            CellKey maximumCell = CellKey.From(origin + new Vector3(radius, 0f, radius));
            var selected = new SortedSet<Candidate>(CandidateComparer.Instance);
            int candidateCount = 0;

            lock (_gate)
            {
                for (int x = minimum.X; x <= maximumCell.X; x++)
                for (int z = minimum.Z; z <= maximumCell.Z; z++)
                {
                    var cell = new CellKey(x, z);
                    if (!_cells.TryGetValue(cell, out List<Entry> entries)) continue;
                    for (int index = entries.Count - 1; index >= 0; index--)
                    {
                        Entry entry = entries[index];
                        Container container = entry.Container;
                        if (container == null)
                        {
                            entries.RemoveAt(index);
                            RemoveIndexesLocked(entry.InstanceId);
                            continue;
                        }
                        try
                        {
                            if (!container.isActiveAndEnabled) continue;
                            float distance = (container.transform.position - origin).sqrMagnitude;
                            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > radiusSquared)
                                continue;
                            if (!_membership.TryGetValue(entry.InstanceId, out Membership membership) ||
                                !ContainerSpatialPolicy.CanIndexEndpoint(membership.EndpointId) ||
                                !_endpointMembers.TryGetValue(
                                    membership.EndpointId, out HashSet<int> endpointMembers) ||
                                !ContainerSpatialPolicy.IsUniqueEndpointMemberCount(endpointMembers.Count))
                                continue;
                            if (candidateCount < int.MaxValue) candidateCount++;
                            string endpoint = membership.EndpointId;
                            selected.Add(new Candidate(
                                new SpatialCandidateKey(distance, endpoint, entry.InstanceId), container));
                            if (selected.Count > maximum) selected.Remove(selected.Max);
                        }
                        catch { }
                    }
                    if (entries.Count == 0) _cells.Remove(cell);
                }
            }

            var result = new List<Container>(selected.Count);
            foreach (Candidate candidate in selected) result.Add(candidate.Container);
            truncated = candidateCount > maximum;
            return result.AsReadOnly();
        }

        internal bool TryGet(string endpointId, out Container container)
        {
            container = null;
            if (string.IsNullOrEmpty(endpointId)) return false;
            lock (_gate)
            {
                if (!_endpointMembers.TryGetValue(endpointId, out HashSet<int> members) ||
                    !ContainerSpatialPolicy.IsUniqueEndpointMemberCount(members.Count))
                    return false;
                foreach (int instanceId in members)
                {
                    if (!_membership.TryGetValue(instanceId, out Membership membership) ||
                        !string.Equals(membership.EndpointId, endpointId, StringComparison.Ordinal) ||
                        membership.Container == null)
                    {
                        RemoveIndexesLocked(instanceId);
                        return false;
                    }
                    container = membership.Container;
                    return true;
                }
            }
            return false;
        }

        private void RemoveLocked(int instanceId, Container container)
        {
            if (!_membership.TryGetValue(instanceId, out Membership membership)) return;
            if (_cells.TryGetValue(membership.Cell, out List<Entry> entries))
            {
                for (int index = entries.Count - 1; index >= 0; index--)
                    if (entries[index].InstanceId == instanceId ||
                        ReferenceEquals(entries[index].Container, container))
                        entries.RemoveAt(index);
                if (entries.Count == 0) _cells.Remove(membership.Cell);
            }
            RemoveIndexesLocked(instanceId);
        }

        private void RemoveIndexesLocked(int instanceId)
        {
            if (!_membership.TryGetValue(instanceId, out Membership membership)) return;
            _membership.Remove(instanceId);
            if (string.IsNullOrEmpty(membership.EndpointId) ||
                !_endpointMembers.TryGetValue(membership.EndpointId, out HashSet<int> members)) return;
            members.Remove(instanceId);
            if (members.Count == 0) _endpointMembers.Remove(membership.EndpointId);
        }

        private void AddEndpoint(string endpointId, int instanceId)
        {
            if (string.IsNullOrEmpty(endpointId)) return;
            if (!_endpointMembers.TryGetValue(endpointId, out HashSet<int> members))
            {
                members = new HashSet<int>();
                _endpointMembers.Add(endpointId, members);
            }
            members.Add(instanceId);
        }

        private static bool ContainsExact(List<Entry> entries, int instanceId, Container container)
        {
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId == instanceId &&
                    ReferenceEquals(entries[index].Container, container)) return true;
            return false;
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private readonly struct CellKey : IEquatable<CellKey>
        {
            internal CellKey(int x, int z) { X = x; Z = z; }
            internal int X { get; }
            internal int Z { get; }
            internal static CellKey From(Vector3 value) => new CellKey(
                ContainerSpatialPolicy.CellCoordinate(value.x),
                ContainerSpatialPolicy.CellCoordinate(value.z));
            public bool Equals(CellKey other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => unchecked((X * 397) ^ Z);
        }

        private readonly struct Entry
        {
            internal Entry(int instanceId, Container container)
            { InstanceId = instanceId; Container = container; }
            internal int InstanceId { get; }
            internal Container Container { get; }
        }

        private readonly struct Membership
        {
            internal Membership(CellKey cell, string endpointId, Container container)
            { Cell = cell; EndpointId = endpointId ?? string.Empty; Container = container; }
            internal CellKey Cell { get; }
            internal string EndpointId { get; }
            internal Container Container { get; }
        }

        private readonly struct Candidate
        {
            internal Candidate(SpatialCandidateKey key, Container container)
            { Key = key; Container = container; }
            internal SpatialCandidateKey Key { get; }
            internal Container Container { get; }
        }

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            internal static CandidateComparer Instance { get; } = new CandidateComparer();
            public int Compare(Candidate left, Candidate right) => left.Key.CompareTo(right.Key);
        }
    }

    internal static class ValheimContainerIdentity
    {
        internal static string EndpointId(Container container)
        {
            if (container == null) return string.Empty;
            ZNetView view = NetworkView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo == null ? string.Empty : "valheim.zdo:" + zdo.m_uid;
        }

        internal static ZNetView NetworkView(Container container)
        {
            if (container == null) return null;
            return container.m_rootObjectOverride != null
                ? container.m_rootObjectOverride
                : container.GetComponent<ZNetView>();
        }

        internal static string TypeId(Container container)
        {
            string value = container == null ? "container" : container.gameObject.name;
            const string clone = "(Clone)";
            if (value.EndsWith(clone, StringComparison.Ordinal)) value = value.Substring(0, value.Length - clone.Length);
            return string.IsNullOrWhiteSpace(value) ? "container" : value.Trim();
        }

        internal static string ResourceId(ItemDrop.ItemData item)
        {
            if (item == null) return string.Empty;
            if (item.m_dropPrefab != null && !string.IsNullOrWhiteSpace(item.m_dropPrefab.name))
            {
                string prefab = item.m_dropPrefab.name;
                const string clone = "(Clone)";
                if (prefab.EndsWith(clone, StringComparison.Ordinal)) prefab = prefab.Substring(0, prefab.Length - clone.Length);
                return prefab.Trim();
            }
            return item.m_shared == null || string.IsNullOrWhiteSpace(item.m_shared.m_name)
                ? string.Empty : item.m_shared.m_name.Trim();
        }
    }
}
