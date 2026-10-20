using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    public enum Invalidation { Movement, SourceLifecycle, Inventory, Access, SpendOrReturn, Station, Configuration, Session }

    // Game adapters publish immutable snapshots on lifecycle/change events, not on every UI frame.
    public sealed class SourceIndex
    {
        private const double CellSize = 20;
        private sealed class Entry
        {
            public Entry(MaterialSource source, double x, double y, double z) { Source = source; X = x; Y = y; Z = z; }
            public readonly MaterialSource Source;
            public readonly double X, Y, Z;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<(int X, int Z), HashSet<string>> cells = new Dictionary<(int, int), HashSet<string>>();
        public long Generation { get; private set; }
        public Invalidation LastInvalidation { get; private set; }
        private static (int, int) Cell(double x, double z) => (checked((int)Math.Floor(x / CellSize)), checked((int)Math.Floor(z / CellSize)));
        private static void ValidatePosition(double x, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z))
                throw new ArgumentOutOfRangeException(nameof(x));
        }
        public void Publish(MaterialSource source, double x, double y, double z, Invalidation reason = Invalidation.Inventory)
        {
            ValidatePosition(x, z); ValidatePosition(y, 0); var cell = Cell(x, z);
            RemoveInternal(source.Id);
            entries.Add(source.Id, new Entry(source, x, y, z));
            if (!cells.TryGetValue(cell, out var ids)) cells.Add(cell, ids = new HashSet<string>(StringComparer.Ordinal));
            ids.Add(source.Id); Invalidate(reason);
        }
        private bool RemoveInternal(string sourceId)
        {
            if (!entries.TryGetValue(sourceId, out var old)) return false;
            var cell = Cell(old.X, old.Z); cells[cell].Remove(sourceId);
            if (cells[cell].Count == 0) cells.Remove(cell);
            return entries.Remove(sourceId);
        }
        public void Remove(string sourceId) { if (RemoveInternal(sourceId)) Invalidate(Invalidation.SourceLifecycle); }
        public void Invalidate(Invalidation reason) { Generation = checked(Generation + 1); LastInvalidation = reason; }
        public void Clear() { entries.Clear(); cells.Clear(); Invalidate(Invalidation.Session); }

        public SourceSearch BeginSearch(double x, double y, double z, double range = 20)
        {
            ValidatePosition(x, z); ValidatePosition(y, 0);
            if (double.IsNaN(range) || double.IsInfinity(range) || range <= 0 || range > 100)
                throw new ArgumentOutOfRangeException(nameof(range));
            var min = Cell(x - range, z - range); var max = Cell(x + range, z + range);
            var candidates = new List<MaterialSource>();
            for (int cx = min.Item1; cx <= max.Item1; cx++)
            for (int cz = min.Item2; cz <= max.Item2; cz++)
            {
                if (!cells.TryGetValue((cx, cz), out var ids)) continue;
                foreach (string id in ids)
                {
                    var entry = entries[id]; double dx = entry.X - x, dy = entry.Y - y, dz = entry.Z - z, distance = dx * dx + dy * dy + dz * dz;
                    if (distance > range * range) continue;
                    var s = entry.Source;
                    candidates.Add(new MaterialSource(s.Id, s.Revision, s.Kind, s.Availability, s.Items, distance, s.Preference));
                }
            }
            return new SourceSearch(this, Generation, candidates.OrderBy(s => s.DistanceSquared)
                .ThenBy(s => s.Id, StringComparer.Ordinal).ToArray());
        }
    }

    public sealed class SourceSearch
    {
        private readonly SourceIndex index;
        private readonly long generation;
        private readonly MaterialSource[] candidates;
        private readonly List<MaterialSource> discovered = new List<MaterialSource>();
        private readonly IReadOnlyList<MaterialSource> view;
        internal SourceSearch(SourceIndex index, long generation, MaterialSource[] candidates)
        { this.index = index; this.generation = generation; this.candidates = candidates; view = discovered.AsReadOnly(); }
        public bool IsValid => generation == index.Generation;
        public bool IsComplete => IsValid && discovered.Count == candidates.Length;
        public IReadOnlyList<MaterialSource> Sources => IsValid ? view : Array.Empty<MaterialSource>();
        public bool Expand(int sourceBudget = 128)
        {
            if (sourceBudget < 1) throw new ArgumentOutOfRangeException(nameof(sourceBudget));
            if (!IsValid) return false;
            int count = Math.Min(sourceBudget, candidates.Length - discovered.Count);
            for (int i = 0; i < count; i++) discovered.Add(candidates[discovered.Count]);
            return true;
        }
    }
}
