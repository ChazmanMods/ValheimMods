namespace QuietBuildRotation.Tests
{
    internal static class MutationPolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run("Undo accepts only complete fresh evidence", UndoRequiresEveryProof);
            TestRunner.Run("Undo denial order is deterministic and fail closed", UndoDenialOrderIsExact);
            TestRunner.Run("Repair requires owner, creator, policy, tool, and capacity", RepairRequiresEveryProof);
            TestRunner.Run("Dedicated clients may act only through current native ownership", DedicatedClientUsesNativeOwnership);
        }

        private static void UndoRequiresEveryProof()
        {
            MutationEvidence evidence = Evidence();
            TestAssert.Equal(MutationDenial.None, MutationPolicy.EvaluateUndo(in evidence));
            TestAssert.Equal(MutationDenial.Changed,
                MutationPolicy.EvaluateUndo(Evidence(unchanged: false)));
            TestAssert.Equal(MutationDenial.Damaged,
                MutationPolicy.EvaluateUndo(Evidence(fullHealth: false)));
            TestAssert.Equal(MutationDenial.AccessedOrInteractive,
                MutationPolicy.EvaluateUndo(Evidence(inert: false)));
            TestAssert.Equal(MutationDenial.StructurallyDependedUpon,
                MutationPolicy.EvaluateUndo(Evidence(independent: false)));
            TestAssert.Equal(MutationDenial.NativePolicyDenied,
                MutationPolicy.EvaluateUndo(Evidence(nativePolicy: false)));
        }

        private static void UndoDenialOrderIsExact()
        {
            MutationEvidence evidence = Evidence(
                runtime: false,
                local: false,
                owner: false,
                creator: false,
                ward: false,
                range: false,
                outsideNoBuild: false,
                unchanged: false,
                fullHealth: false,
                inert: false,
                independent: false,
                nativePolicy: false);
            TestAssert.Equal(MutationDenial.RuntimeUnavailable,
                MutationPolicy.EvaluateUndo(in evidence));
            TestAssert.Equal(MutationDenial.NotLocalPlayer,
                MutationPolicy.EvaluateUndo(Evidence(local: false)));
            TestAssert.Equal(MutationDenial.ObjectAuthorityUnavailable,
                MutationPolicy.EvaluateUndo(Evidence(owner: false, creator: false)));
        }

        private static void RepairRequiresEveryProof()
        {
            TestAssert.Equal(MutationDenial.None,
                MutationPolicy.EvaluateRepair(Evidence()));
            TestAssert.Equal(MutationDenial.NativePolicyDenied,
                MutationPolicy.EvaluateRepair(Evidence(nativePolicy: false)));
            TestAssert.Equal(MutationDenial.ToolUnavailable,
                MutationPolicy.EvaluateRepair(Evidence(tool: false)));
            TestAssert.Equal(MutationDenial.CapacityExceeded,
                MutationPolicy.EvaluateRepair(Evidence(capacity: false)));
        }

        private static void DedicatedClientUsesNativeOwnership()
        {
            TestAssert.Equal(MutationDenial.None,
                MutationPolicy.EvaluateUndo(Evidence(owner: true)));
            TestAssert.Equal(MutationDenial.ObjectAuthorityUnavailable,
                MutationPolicy.EvaluateUndo(Evidence(owner: false)));
            TestAssert.Equal(MutationDenial.None,
                MutationPolicy.EvaluateRepair(Evidence(owner: true)));
            TestAssert.Equal(MutationDenial.ObjectAuthorityUnavailable,
                MutationPolicy.EvaluateRepair(Evidence(owner: false)));
        }

        private static MutationEvidence Evidence(
            bool runtime = true,
            bool local = true,
            bool owner = true,
            bool creator = true,
            bool ward = true,
            bool range = true,
            bool outsideNoBuild = true,
            bool unchanged = true,
            bool fullHealth = true,
            bool inert = true,
            bool independent = true,
            bool nativePolicy = true,
            bool tool = true,
            bool capacity = true) =>
            new MutationEvidence(
                runtime, local, owner, creator, ward, range, outsideNoBuild,
                unchanged, fullHealth, inert, independent, nativePolicy, tool, capacity);
    }
}
