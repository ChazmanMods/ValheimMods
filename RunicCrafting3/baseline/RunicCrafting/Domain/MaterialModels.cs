using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicCrafting.Domain
{
    public enum MaterialSourceKind
    {
        PlayerInventory = 0,
        NearbyContainer = 1
    }

    public sealed class MaterialRequirement
    {
        /// <summary>
        /// Resource IDs use stable Valheim prefab names when crossing a module boundary.
        /// </summary>
        public MaterialRequirement(string resourceId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new ArgumentException("A resource identifier is required.", nameof(resourceId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            ResourceId = resourceId.Trim();
            Quantity = quantity;
        }

        public string ResourceId { get; }
        public int Quantity { get; }
    }

    public sealed class MaterialSourceSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _quantities;

        public MaterialSourceSnapshot(
            string sourceId,
            MaterialSourceKind kind,
            float distanceSquared,
            IDictionary<string, int> quantities)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("A source identifier is required.", nameof(sourceId));
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared < 0f)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            if (quantities == null) throw new ArgumentNullException(nameof(quantities));

            SourceId = sourceId.Trim();
            Kind = kind;
            DistanceSquared = distanceSquared;
            var copy = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in quantities)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
                    throw new ArgumentException("Source quantities must have valid IDs and non-negative amounts.", nameof(quantities));
                copy[pair.Key.Trim()] = pair.Value;
            }
            _quantities = new ReadOnlyDictionary<string, int>(copy);
        }

        public string SourceId { get; }
        public MaterialSourceKind Kind { get; }
        public float DistanceSquared { get; }
        public IReadOnlyDictionary<string, int> Quantities => _quantities;

        public int Available(string resourceId) =>
            resourceId != null && _quantities.TryGetValue(resourceId, out int quantity) ? quantity : 0;
    }

    public sealed class MaterialPlanLine
    {
        public MaterialPlanLine(string sourceId, string resourceId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Source ID is required.", nameof(sourceId));
            if (string.IsNullOrWhiteSpace(resourceId)) throw new ArgumentException("Resource ID is required.", nameof(resourceId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            SourceId = sourceId;
            ResourceId = resourceId;
            Quantity = quantity;
        }

        public string SourceId { get; }
        public string ResourceId { get; }
        public int Quantity { get; }
    }

    public sealed class MaterialPlan
    {
        private readonly ReadOnlyCollection<MaterialPlanLine> _lines;
        private readonly ReadOnlyCollection<MaterialRequirement> _requirements;

        internal MaterialPlan(IEnumerable<MaterialRequirement> requirements, IEnumerable<MaterialPlanLine> lines)
        {
            _requirements = new List<MaterialRequirement>(requirements).AsReadOnly();
            _lines = new List<MaterialPlanLine>(lines).AsReadOnly();
        }

        public IReadOnlyList<MaterialRequirement> Requirements => _requirements;
        public IReadOnlyList<MaterialPlanLine> Lines => _lines;
    }

    public interface IMaterialRestoreToken
    {
        bool Restore();
    }

    public interface IMutableMaterialSource
    {
        string SourceId { get; }
        MaterialSourceSnapshot Snapshot();
        bool TryTake(string resourceId, int quantity, out IMaterialRestoreToken restoreToken);
    }
}
