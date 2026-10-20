using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    public sealed class MaterialPlanner
    {
        private sealed class Edge
        {
            public Edge(int to, int reverse, long capacity, long cost)
            { To = to; Reverse = reverse; Capacity = capacity; Cost = cost; Initial = capacity; }
            public readonly int To, Reverse;
            public readonly long Cost, Initial;
            public long Capacity;
        }
        private sealed class Budget
        {
            private int remaining;
            public Budget(int operations) { remaining = operations; }
            public void Tick() { if (--remaining < 0) throw new BudgetExpired(); }
        }
        private sealed class BudgetExpired : Exception { }
        private static Edge Add(List<Edge>[] graph, int from, int to, long capacity, long cost)
        {
            var edge = new Edge(to, graph[to].Count, capacity, cost);
            graph[from].Add(edge); graph[to].Add(new Edge(from, graph[from].Count - 1, 0, -cost));
            return edge;
        }

        // Run on refresh / preparation, never every rendered frame. Limits return incomplete, never "missing".
        public MaterialPlan Plan(IEnumerable<MaterialCost> costs, IEnumerable<MaterialSource> sources,
            bool searchComplete, int workBudget = 2_000_000)
        {
            if (workBudget < 1) throw new ArgumentOutOfRangeException(nameof(workBudget));
            var requirements = costs.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
            var ordered = sources.OrderBy(x => x.Kind).ThenBy(x => x.Preference).ThenBy(x => x.DistanceSquared)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            if (requirements.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != requirements.Length ||
                ordered.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
                throw new ArgumentException("Duplicate cost or source identity.");
            var budget = new Budget(workBudget);
            try { return PlanCore(requirements, ordered, searchComplete, budget); }
            catch (BudgetExpired)
            {
                return new MaterialPlan(PlanStatus.SearchIncomplete, Array.Empty<MaterialSlice>(), Array.Empty<MaterialCount>(), requirements);
            }
        }

        private static MaterialPlan PlanCore(MaterialCost[] costs, MaterialSource[] sources, bool complete, Budget budget)
        {
            var available = new List<(MaterialSource Source, ItemStack Item)>();
            var carried = new long[costs.Length]; var nearby = new long[costs.Length]; var busy = new long[costs.Length];
            foreach (var source in sources)
            foreach (var item in source.Items.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            {
                budget.Tick();
                if (source.Availability == SourceAvailability.Usable) available.Add((source, item));
                for (int c = 0; c < costs.Length; c++)
                {
                    budget.Tick();
                    if (!costs[c].Matches(source, item)) continue;
                    if (source.Availability == SourceAvailability.Busy) busy[c] = checked(busy[c] + item.Quantity);
                    else if (source.Availability == SourceAvailability.Usable)
                    {
                        if (source.Kind == SourceKind.Carried) carried[c] = checked(carried[c] + item.Quantity);
                        else nearby[c] = checked(nearby[c] + item.Quantity);
                    }
                }
            }
            long demand = 0; foreach (var c in costs) demand = checked(demand + c.Quantity);
            int sink = checked(1 + available.Count + costs.Length);
            var graph = new List<Edge>[checked(sink + 1)];
            for (int n = 0; n < graph.Length; n++) graph[n] = new List<Edge>();
            var matches = new List<(int Cost, int Item, Edge Edge)>();
            var demandEdges = new Edge[costs.Length];
            // One extra carried unit outranks every possible preference saving in a full plan.
            long nearbyPenalty = checked(demand * (available.Count + 1L) + 1);
            var matchCounts = new int[costs.Length];
            var matching = new List<int>[available.Count];
            for (int i = 0; i < available.Count; i++)
            {
                matching[i] = new List<int>();
                for (int c = 0; c < costs.Length; c++)
                {
                    budget.Tick();
                    if (!costs[c].Matches(available[i].Source, available[i].Item)) continue;
                    matching[i].Add(c); matchCounts[c]++;
                }
            }
            var restrictiveOrder = Enumerable.Range(0, costs.Length).OrderBy(c => matchCounts[c])
                .ThenBy(c => costs[c].Selection == null ? 1 : 0).ThenBy(c => costs[c].Id, StringComparer.Ordinal).ToArray();
            var rank = new int[costs.Length]; for (int c = 0; c < rank.Length; c++) rank[restrictiveOrder[c]] = c;
            for (int i = 0; i < available.Count; i++)
            {
                Add(graph, 0, i + 1, available[i].Item.Quantity,
                    checked(i + (available[i].Source.Kind == SourceKind.Carried ? 0 : nearbyPenalty)));
                foreach (var c in matching[i].OrderBy(c => rank[c]))
                    matches.Add((c, i, Add(graph, i + 1, available.Count + 1 + rank[c], available[i].Item.Quantity, 0)));
            }
            foreach (int c in restrictiveOrder)
                demandEdges[c] = Add(graph, available.Count + 1 + rank[c], sink, costs[c].Quantity, 0);

            // Residual reverse edges can reassign earlier choices; greedy alternatives alone give false deficits.
            long flow = 0;
            var distance = new long[graph.Length]; var previousNode = new int[graph.Length];
            var previousEdge = new int[graph.Length]; var queued = new bool[graph.Length];
            while (flow < demand)
            {
                for (int n = 0; n < graph.Length; n++) { distance[n] = long.MaxValue; queued[n] = false; }
                distance[0] = 0;
                var queue = new Queue<int>(); queue.Enqueue(0); queued[0] = true;
                while (queue.Count != 0)
                {
                    int from = queue.Dequeue(); queued[from] = false;
                    for (int e = 0; e < graph[from].Count; e++)
                    {
                        budget.Tick(); var edge = graph[from][e];
                        if (edge.Capacity == 0) continue;
                        long candidate = checked(distance[from] + edge.Cost);
                        if (candidate >= distance[edge.To]) continue;
                        distance[edge.To] = candidate; previousNode[edge.To] = from; previousEdge[edge.To] = e;
                        if (!queued[edge.To]) { queue.Enqueue(edge.To); queued[edge.To] = true; }
                    }
                }
                if (distance[sink] == long.MaxValue) break;
                long add = demand - flow;
                for (int to = sink; to != 0; to = previousNode[to])
                    add = Math.Min(add, graph[previousNode[to]][previousEdge[to]].Capacity);
                for (int to = sink; to != 0; to = previousNode[to])
                {
                    var edge = graph[previousNode[to]][previousEdge[to]];
                    edge.Capacity -= add; graph[to][edge.Reverse].Capacity += add;
                }
                flow = checked(flow + add);
            }
            PlanStatus status = flow == demand ? PlanStatus.Ready : !complete ? PlanStatus.SearchIncomplete
                : busy.Any(x => x > 0) ? PlanStatus.WaitingForSources : PlanStatus.Missing;
            var counts = costs.Select((cost, c) => new MaterialCount(cost.Id, carried[c], nearby[c], busy[c], checked((int)demandEdges[c].Capacity)));
            var slices = status == PlanStatus.Ready
                ? matches.Where(x => x.Edge.Initial > x.Edge.Capacity).Select(x => new MaterialSlice(costs[x.Cost].Id,
                    available[x.Item].Source, available[x.Item].Item, checked((int)(x.Edge.Initial - x.Edge.Capacity))))
                : Enumerable.Empty<MaterialSlice>();
            return new MaterialPlan(status, slices, counts, costs);
        }

        // Still not access authorization: caller must hold proven exclusion and use freshly loaded authority snapshots.
        public bool IsFresh(MaterialPlan plan, IEnumerable<MaterialCost> currentCosts, IEnumerable<MaterialSource> freshSources)
        {
            if (plan.Status != PlanStatus.Ready) return false;
            if (!plan.Costs.Select(x => x.Fingerprint).SequenceEqual(currentCosts.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Fingerprint))) return false;
            var sources = freshSources.ToArray();
            if (sources.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != sources.Length) return false;
            var byId = sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var group in plan.Slices.GroupBy(x => (x.SourceId, x.InstanceId)))
            {
                var first = group.First();
                if (!byId.TryGetValue(first.SourceId, out var source) || source.Availability != SourceAvailability.Usable || source.Revision != first.SourceRevision) return false;
                var item = source.Items.FirstOrDefault(x => x.InstanceId == first.InstanceId);
                if (item == null || item.Identity.Fingerprint != first.Identity.Fingerprint || item.Quantity != first.ExpectedStack ||
                    group.Sum(x => (long)x.Quantity) > item.Quantity) return false;
                foreach (var slice in group)
                    if (!plan.Costs.First(x => x.Id == slice.CostId).Matches(source, item)) return false;
            }
            return true;
        }
    }
}
