using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Core
{
    public sealed class BatchCoordinator
    {
        public const int DefaultCap = 100;
        public const int MaximumConfigurableCap = 1000;
        private readonly ActionIntent template;
        private ActionCoordinator? active;
        private bool stopped;
        public BatchCoordinator(ActionIntent template, int crafts, int outputPerCraft, int cap = DefaultCap)
        {
            if (template.Kind != ActionKind.Craft) throw new ArgumentException("A batch contains individual crafts.", nameof(template));
            if (cap < 1 || cap > MaximumConfigurableCap || crafts < 1 || crafts > cap || outputPerCraft < 1)
                throw new ArgumentOutOfRangeException(nameof(crafts));
            this.template = template; RequestedCrafts = crafts; OutputPerCraft = outputPerCraft;
            RequestedOutput = checked(crafts * outputPerCraft);
        }
        public int RequestedCrafts { get; }
        public int OutputPerCraft { get; }
        public int RequestedOutput { get; }
        public int CompletedCrafts { get; private set; }
        public int CompletedOutput => checked(CompletedCrafts * OutputPerCraft);
        public bool Stopped => stopped;
        public bool Finished => (stopped || CompletedCrafts == RequestedCrafts) && active == null;

        // Every invocation needs a new native-condition observation. No plan, custody, or chest hold spans crafts.
        public ActionCoordinator? Next(ActionObservation current, long nowMilliseconds)
        {
            if (stopped || active != null || CompletedCrafts == RequestedCrafts) return null;
            if (!template.Matches(current)) { stopped = true; return null; }
            var intent = new ActionIntent(template.World, template.SessionNonce, template.Id + ":" + (CompletedCrafts + 1),
                ActionKind.Craft, template.Selection, template.Tool, template.Target);
            active = new ActionCoordinator(intent, nowMilliseconds); return active;
        }
        public bool Observe(ActionCoordinator action)
        {
            if (!ReferenceEquals(active, action)) return false;
            if (action.State == ActionState.Uncertain || action.State == ActionState.Returning) stopped = true;
            if (!action.Terminal) return false;
            if (action.State == ActionState.Completed) CompletedCrafts++;
            else stopped = true;
            active = null; return true;
        }
        public void Stop() { stopped = true; active?.Cancel(); }
    }

    public sealed class BatchEstimate
    {
        internal BatchEstimate(int crafts, int output, bool complete) { Crafts = crafts; Output = output; Complete = complete; }
        public int Crafts { get; }
        public int Output { get; }
        public bool Complete { get; }
    }
    public static class BatchPreview
    {
        // This estimates resource and adapter-supplied native capacity constraints. It never authorizes a batch debit.
        public static BatchEstimate Maximum(MaterialPlanner planner, IReadOnlyList<MaterialCost> costs,
            IReadOnlyList<MaterialSource> sources, bool searchComplete, int outputPerCraft, int cap,
            Func<int, bool> nativeCanProduceOutput)
        {
            if (cap < 1 || cap > BatchCoordinator.MaximumConfigurableCap || outputPerCraft < 1) throw new ArgumentOutOfRangeException(nameof(cap));
            int low = 0, high = Math.Min(cap, int.MaxValue / outputPerCraft); bool complete = searchComplete;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                MaterialPlan plan;
                try { plan = planner.Plan(costs.Select(x => x.Multiply(mid)), sources, searchComplete); }
                catch (OverflowException) { high = mid - 1; continue; }
                if (plan.Status == PlanStatus.SearchIncomplete) complete = false;
                if (plan.Status == PlanStatus.Ready && nativeCanProduceOutput(checked(mid * outputPerCraft))) low = mid;
                else high = mid - 1;
            }
            return new BatchEstimate(low, checked(low * outputPerCraft), complete);
        }
    }
}
