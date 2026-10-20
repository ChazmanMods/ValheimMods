using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicCrafting.Core
{
    internal static class Contract
    {
        public static string Id(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Identity is required.", name);
            return value;
        }
        public static string Hash(Action<BinaryWriter> write)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) write(writer);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "");
        }
        public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
            (values ?? throw new ArgumentNullException(nameof(values))).ToList().AsReadOnly();
    }

    // Recipe matching deliberately ignores transport-only metadata. Returning a slice must not.
    public sealed class ItemIdentity
    {
        public ItemIdentity(string prefab, int quality = 1, int variant = 0, int worldLevel = 0,
            float durability = 0, long crafterId = 0, string crafterName = "",
            IReadOnlyDictionary<string, string>? customData = null,
            IReadOnlyDictionary<string, string>? extensions = null)
        {
            Prefab = Contract.Id(prefab, nameof(prefab));
            if (quality < 1 || variant < 0 || worldLevel < 0 || float.IsNaN(durability) ||
                float.IsInfinity(durability) || durability < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            Quality = quality; Variant = variant; WorldLevel = worldLevel; Durability = durability;
            CrafterId = crafterId; CrafterName = crafterName ?? throw new ArgumentNullException(nameof(crafterName));
            CustomData = CopyMap(customData); Extensions = CopyMap(extensions);
            Fingerprint = Contract.Hash(w =>
            {
                w.Write(Prefab); w.Write(Quality); w.Write(Variant); w.Write(WorldLevel); w.Write(Durability);
                w.Write(CrafterId); w.Write(CrafterName); WriteMap(w, CustomData); WriteMap(w, Extensions);
            });
        }
        private static IReadOnlyDictionary<string, string> CopyMap(IReadOnlyDictionary<string, string>? map)
        {
            var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (map != null) foreach (var pair in map)
                copy.Add(Contract.Id(pair.Key, nameof(map)), pair.Value ?? throw new ArgumentException("Null metadata."));
            return new ReadOnlyDictionary<string, string>(copy);
        }
        private static void WriteMap(BinaryWriter writer, IReadOnlyDictionary<string, string> map)
        {
            writer.Write(map.Count);
            foreach (var pair in map) { writer.Write(pair.Key); writer.Write(pair.Value); }
        }
        public string Prefab { get; }
        public int Quality { get; }
        public int Variant { get; }
        public int WorldLevel { get; }
        public float Durability { get; }
        public long CrafterId { get; }
        public string CrafterName { get; }
        public IReadOnlyDictionary<string, string> CustomData { get; }
        public IReadOnlyDictionary<string, string> Extensions { get; }
        public string Fingerprint { get; }
    }

    [Flags]
    public enum Protection { None = 0, Equipped = 1, Favorite = 2, Locked = 4, Nonfungible = 8, Unknown = 16 }
    public enum SourceKind { Carried, Nearby }
    public enum SourceAvailability { Usable, Busy, Denied, Unsupported }

    public sealed class ItemStack
    {
        public ItemStack(string instanceId, ItemIdentity identity, int quantity, Protection protection = Protection.None)
        {
            InstanceId = Contract.Id(instanceId, nameof(instanceId));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity; Protection = protection;
        }
        public string InstanceId { get; }
        public ItemIdentity Identity { get; }
        public int Quantity { get; }
        public Protection Protection { get; }
        public bool Protected => Protection != Protection.None || Identity.CustomData.Count != 0 || Identity.Extensions.Count != 0;
    }

    public sealed class MaterialSource
    {
        public MaterialSource(string id, string revision, SourceKind kind, SourceAvailability availability,
            IEnumerable<ItemStack> items, double distanceSquared = 0, int preference = 0)
        {
            Id = Contract.Id(id, nameof(id)); Revision = Contract.Id(revision, nameof(revision));
            if (double.IsNaN(distanceSquared) || double.IsInfinity(distanceSquared) || distanceSquared < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            Kind = kind; Availability = availability; DistanceSquared = distanceSquared; Preference = preference;
            // Do not retain private inventory contents, even if a caller mistakenly supplies them.
            Items = Contract.Copy(availability == SourceAvailability.Denied || availability == SourceAvailability.Unsupported
                ? Array.Empty<ItemStack>() : items);
            if (Items.Any(x => x == null) || Items.Select(x => x.InstanceId).Distinct(StringComparer.Ordinal).Count() != Items.Count)
                throw new ArgumentException("Duplicate or null item identity.", nameof(items));
        }
        public string Id { get; }
        public string Revision { get; }
        public SourceKind Kind { get; }
        public SourceAvailability Availability { get; }
        public IReadOnlyList<ItemStack> Items { get; }
        public double DistanceSquared { get; }
        public int Preference { get; }
    }

    public sealed class ItemMatch
    {
        public ItemMatch(string prefab, int? quality = null, int? variant = null, int? worldLevel = null)
        {
            Prefab = Contract.Id(prefab, nameof(prefab));
            if (quality < 1 || variant < 0 || worldLevel < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            Quality = quality; Variant = variant; WorldLevel = worldLevel;
        }
        public string Prefab { get; }
        public int? Quality { get; }
        public int? Variant { get; }
        public int? WorldLevel { get; }
        public bool Matches(ItemIdentity item) => item.Prefab == Prefab && (!Quality.HasValue || Quality == item.Quality)
            && (!Variant.HasValue || Variant == item.Variant) && (!WorldLevel.HasValue || WorldLevel == item.WorldLevel);
        internal void Write(BinaryWriter w)
        {
            w.Write(Prefab); w.Write(Quality ?? -1); w.Write(Variant ?? -1); w.Write(WorldLevel ?? -1);
        }
    }

    // Created only by a verified action-specific selector in a game adapter, never by ordinary matching.
    public sealed class ExactSelection
    {
        public ExactSelection(string sourceId, string instanceId, string fingerprint, Protection permittedProtection,
            bool permitCustomData = false)
        {
            SourceId = Contract.Id(sourceId, nameof(sourceId)); InstanceId = Contract.Id(instanceId, nameof(instanceId));
            Fingerprint = Contract.Id(fingerprint, nameof(fingerprint)); PermittedProtection = permittedProtection;
            PermitCustomData = permitCustomData;
        }
        public string SourceId { get; }
        public string InstanceId { get; }
        public string Fingerprint { get; }
        public Protection PermittedProtection { get; }
        public bool PermitCustomData { get; }
        internal bool Accepts(MaterialSource source, ItemStack stack) => SourceId == source.Id && InstanceId == stack.InstanceId
            && Fingerprint == stack.Identity.Fingerprint && (stack.Protection & Protection.Unknown) == 0 && (stack.Protection & ~PermittedProtection) == 0
            && (PermitCustomData || (stack.Identity.CustomData.Count == 0 && stack.Identity.Extensions.Count == 0));
    }

    public sealed class MaterialCost
    {
        public MaterialCost(string id, int quantity, IEnumerable<ItemMatch> alternatives, ExactSelection? selection = null)
        {
            Id = Contract.Id(id, nameof(id));
            if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity; Alternatives = Contract.Copy(alternatives); Selection = selection;
            if (Alternatives.Count == 0 || Alternatives.Any(x => x == null)) throw new ArgumentException("Missing item match.");
            Fingerprint = Contract.Hash(w =>
            {
                w.Write(Id); w.Write(Quantity); w.Write(Alternatives.Count);
                foreach (var match in Alternatives) match.Write(w);
                w.Write(selection != null);
                if (selection != null)
                {
                    w.Write(selection.SourceId); w.Write(selection.InstanceId); w.Write(selection.Fingerprint);
                    w.Write((int)selection.PermittedProtection); w.Write(selection.PermitCustomData);
                }
            });
        }
        public string Id { get; }
        public int Quantity { get; }
        public IReadOnlyList<ItemMatch> Alternatives { get; }
        public ExactSelection? Selection { get; }
        public string Fingerprint { get; }
        public bool Matches(MaterialSource source, ItemStack stack) =>
            (Selection != null ? Selection.Accepts(source, stack) : !stack.Protected) && Alternatives.Any(x => x.Matches(stack.Identity));
        public MaterialCost Multiply(int crafts)
        {
            if (crafts < 1) throw new ArgumentOutOfRangeException(nameof(crafts));
            return new MaterialCost(Id, checked(Quantity * crafts), Alternatives, Selection);
        }
    }

    public sealed class MaterialSlice
    {
        internal MaterialSlice(string costId, MaterialSource source, ItemStack item, int quantity)
        {
            CostId = costId; SourceId = source.Id; SourceRevision = source.Revision;
            InstanceId = item.InstanceId; Identity = item.Identity; ExpectedStack = item.Quantity; Quantity = quantity;
        }
        public string CostId { get; }
        public string SourceId { get; }
        public string SourceRevision { get; }
        public string InstanceId { get; }
        public ItemIdentity Identity { get; }
        public int ExpectedStack { get; }
        public int Quantity { get; }
    }

    public enum PlanStatus { Ready, Missing, WaitingForSources, SearchIncomplete }
    public sealed class MaterialCount
    {
        internal MaterialCount(string costId, long carried, long nearby, long busy, int missing)
        { CostId = costId; Carried = carried; Nearby = nearby; Busy = busy; Missing = missing; }
        public string CostId { get; }
        public long Carried { get; }
        public long Nearby { get; }
        public long Total => checked(Carried + Nearby);
        public long Busy { get; }
        // Missing is unallocated demand, not demand minus each independently counted overlapping alternative.
        public int Missing { get; }
    }
    public sealed class MaterialPlan
    {
        internal MaterialPlan(PlanStatus status, IEnumerable<MaterialSlice> slices, IEnumerable<MaterialCount> counts,
            IEnumerable<MaterialCost> costs)
        {
            Status = status; Slices = Contract.Copy(slices); Counts = Contract.Copy(counts);
            Costs = Contract.Copy(costs);
            Fingerprint = Contract.Hash(w =>
            {
                w.Write(Costs.Count); foreach (var cost in Costs) w.Write(cost.Fingerprint);
                w.Write(Slices.Count);
                foreach (var s in Slices)
                {
                    w.Write(s.CostId); w.Write(s.SourceId); w.Write(s.SourceRevision); w.Write(s.InstanceId);
                    w.Write(s.Identity.Fingerprint); w.Write(s.ExpectedStack); w.Write(s.Quantity);
                }
            });
        }
        public PlanStatus Status { get; }
        public IReadOnlyList<MaterialSlice> Slices { get; }
        public IReadOnlyList<MaterialCount> Counts { get; }
        public IReadOnlyList<MaterialCost> Costs { get; }
        public string Fingerprint { get; }
        public bool DefinitiveDeficit => Status == PlanStatus.Missing;
    }
}
