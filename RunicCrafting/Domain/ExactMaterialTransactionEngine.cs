using System;
using System.Collections.Generic;
using System.Threading;

namespace RunicCrafting.Domain
{
    /// <summary>
    /// Serializes exact local material allocation until the caller commits or rolls back.
    /// </summary>
    public sealed class ExactMaterialTransactionEngine
    {
        private readonly object _gate = new object();
        private readonly ExactMaterialPlanner _planner;

        public ExactMaterialTransactionEngine(ExactMaterialPlanner planner = null) =>
            _planner = planner ?? new ExactMaterialPlanner();

        public bool TryBegin(
            IEnumerable<MaterialRequirement> requirements,
            IEnumerable<IMutableMaterialSource> sources,
            out MaterialConsumptionLease lease,
            out string denialReason)
        {
            lease = null;
            denialReason = string.Empty;
            var restoreTokens = new List<IMaterialRestoreToken>();
            Monitor.Enter(_gate);
            try
            {
                var sourceList = new List<IMutableMaterialSource>();
                foreach (IMutableMaterialSource source in sources)
                {
                    if (source == null || sourceList.Count >= ExactMaterialPlanner.MaximumSources)
                    {
                        denialReason = "source-limit";
                        Monitor.Exit(_gate);
                        return false;
                    }
                    sourceList.Add(source);
                }

                var snapshots = new List<MaterialSourceSnapshot>(sourceList.Count);
                var byId = new Dictionary<string, IMutableMaterialSource>(StringComparer.Ordinal);
                foreach (IMutableMaterialSource source in sourceList)
                {
                    MaterialSourceSnapshot snapshot = source.Snapshot();
                    if (!string.Equals(snapshot.SourceId, source.SourceId, StringComparison.Ordinal) ||
                        byId.ContainsKey(source.SourceId))
                    {
                        denialReason = "invalid-sources";
                        Monitor.Exit(_gate);
                        return false;
                    }
                    snapshots.Add(snapshot);
                    byId.Add(source.SourceId, source);
                }

                if (!_planner.TryPlan(requirements, snapshots, out MaterialPlan plan, out denialReason))
                {
                    Monitor.Exit(_gate);
                    return false;
                }

                foreach (MaterialPlanLine line in plan.Lines)
                {
                    if (!byId[line.SourceId].TryTake(
                            line.ResourceId,
                            line.Quantity,
                            out IMaterialRestoreToken token) || token == null)
                    {
                        RestoreReverse(restoreTokens);
                        denialReason = "source-changed:" + line.SourceId;
                        Monitor.Exit(_gate);
                        return false;
                    }
                    restoreTokens.Add(token);
                }

                lease = new MaterialConsumptionLease(_gate, plan, snapshots, restoreTokens);
                return true;
            }
            catch
            {
                RestoreReverse(restoreTokens);
                Monitor.Exit(_gate);
                throw;
            }
        }

        internal static bool RestoreReverse(IReadOnlyList<IMaterialRestoreToken> tokens)
        {
            bool restored = true;
            for (int index = tokens.Count - 1; index >= 0; index--)
            {
                try { restored &= tokens[index].Restore(); }
                catch (Exception) { restored = false; }
            }
            return restored;
        }
    }

    public sealed class MaterialConsumptionLease : IDisposable
    {
        private readonly object _gate;
        private readonly IReadOnlyList<IMaterialRestoreToken> _restoreTokens;
        private int _active = 1;

        internal MaterialConsumptionLease(
            object gate,
            MaterialPlan plan,
            IReadOnlyList<MaterialSourceSnapshot> sourceSnapshots,
            IReadOnlyList<IMaterialRestoreToken> restoreTokens)
        {
            _gate = gate;
            Plan = plan;
            SourceSnapshots = sourceSnapshots;
            _restoreTokens = restoreTokens;
        }

        public MaterialPlan Plan { get; }
        public IReadOnlyList<MaterialSourceSnapshot> SourceSnapshots { get; }
        public bool IsActive => Volatile.Read(ref _active) == 1;

        public void Commit()
        {
            if (Interlocked.Exchange(ref _active, 0) != 1) return;
            Monitor.Exit(_gate);
        }

        public bool Rollback()
        {
            if (Interlocked.Exchange(ref _active, 0) != 1) return true;
            bool restored = ExactMaterialTransactionEngine.RestoreReverse(_restoreTokens);
            Monitor.Exit(_gate);
            return restored;
        }

        public void Dispose() => Rollback();
    }
}
