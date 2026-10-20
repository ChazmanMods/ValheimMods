using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicStorage.Runtime;

internal static class SmartSortTransaction
{
    internal sealed class Endpoint
    {
        internal Inventory Live, Planned;
        internal StorageInventorySnapshot Before;
        internal Func<bool> Authorized;
        internal Func<bool> Persisted;
    }

    private static readonly MethodInfo Changed = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

    internal static void Apply(IReadOnlyList<Endpoint> endpoints)
    {
        if (RunicAutomation.MutationGate.Current == null) throw new InvalidOperationException("SmartSort requires an active inventory lease.");
        if (Changed == null) throw new MissingMethodException("Inventory.Changed");
        if (endpoints.Count == 0 || endpoints.Select(e => e.Live).Distinct().Count() != endpoints.Count)
            throw new InvalidOperationException("Invalid SmartSort endpoints.");
        foreach (var e in endpoints)
        {
            if (!e.Authorized() || RunicAutomation.MutationGate.IsBlocked(e.Live))
                throw new InvalidOperationException("Chest access changed; preview again.");
            e.Before.Verify(e.Live);
            RunicAutomation.MutationGate.Current?.Track(e.Live);
        }
        SmartSortPlanner.VerifyConservation(endpoints.Select(e => e.Live), endpoints.Select(e => e.Planned));
        var expected = endpoints.Select(e => ValheimContainerService.Fingerprint(e.Planned)).ToArray();
        var originals = endpoints.Select(e => ValheimContainerService.Fingerprint(e.Live)).ToArray();
        // All clone allocation happens before changing any live list.
        var replacements = endpoints.Select(e => e.Planned.GetAllItems().Select(i => i.Clone()).ToList()).ToArray();
        var callbacks = endpoints.Select(e => e.Live.m_onChanged).ToArray();
        try
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                var live = endpoints[i].Live;
                live.m_onChanged = null;
                live.GetAllItems().Clear(); live.GetAllItems().AddRange(replacements[i]);
                Changed.Invoke(live, new object[] { false, false });
            }
            // Every inventory is already in its final state before any observer can see it.
            for (int i = 0; i < endpoints.Count; i++) endpoints[i].Live.m_onChanged = callbacks[i];
            for (int i = 0; i < endpoints.Count; i++) callbacks[i]?.Invoke();
            for (int i = 0; i < endpoints.Count; i++)
                if (!endpoints[i].Authorized() || ValheimContainerService.Fingerprint(endpoints[i].Live) != expected[i] ||
                    !(endpoints[i].Persisted?.Invoke() ?? true))
                    throw new InvalidOperationException("A chest changed during SmartSort publication.");
            RunicAutomation.MutationGate.Current?.Complete(RunicAutomation.MutationOutcome.Committed);
        }
        catch (Exception failure)
        {
            for (int i = 0; i < endpoints.Count; i++) endpoints[i].Live.m_onChanged = callbacks[i];
            try
            {
                // Never restore only half a cross-chest operation or overwrite an unrelated write.
                for (int i = 0; i < endpoints.Count; i++)
                {
                    string now = ValheimContainerService.Fingerprint(endpoints[i].Live);
                    if (!endpoints[i].Authorized() || (now != expected[i] && now != originals[i]))
                        throw new InvalidOperationException("Recovery cannot prove every chest's state.");
                }
                foreach (var e in endpoints) e.Live.m_onChanged = null;
                foreach (var e in endpoints) { e.Before.Restore(e.Live); Changed.Invoke(e.Live, new object[] { false, false }); }
                for (int i = 0; i < endpoints.Count; i++) endpoints[i].Live.m_onChanged = callbacks[i];
                foreach (var callback in callbacks) callback?.Invoke();
                foreach (var e in endpoints)
                {
                    e.Before.Verify(e.Live);
                    if (!e.Authorized() || !(e.Persisted?.Invoke() ?? true))
                        throw new InvalidOperationException("Restored chest was not persisted exactly.");
                }
                RunicAutomation.MutationGate.Current?.Complete(RunicAutomation.MutationOutcome.RolledBack);
            }
            catch (Exception recovery)
            {
                foreach (var e in endpoints) RunicAutomation.MutationGate.Block(e.Live);
                RunicAutomation.MutationGate.Current?.Complete(RunicAutomation.MutationOutcome.Indeterminate);
                throw new RunicAutomation.MutationIndeterminateException("SmartSort recovery failed", new AggregateException(failure, recovery));
            }
            throw new InvalidOperationException("SmartSort stopped and restored the original inventories.", failure);
        }
        finally
        {
            for (int i = 0; i < endpoints.Count; i++) endpoints[i].Live.m_onChanged = callbacks[i];
        }
    }
}
