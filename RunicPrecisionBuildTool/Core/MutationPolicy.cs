namespace QuietBuildRotation
{
    internal enum MutationDenial : byte
    {
        None = 0,
        RuntimeUnavailable,
        NotLocalPlayer,
        ObjectAuthorityUnavailable,
        WrongCreator,
        WardDenied,
        OutOfRange,
        NoBuildZone,
        Changed,
        Damaged,
        AccessedOrInteractive,
        StructurallyDependedUpon,
        NativePolicyDenied,
        ToolUnavailable,
        CapacityExceeded
    }

    internal readonly struct MutationEvidence
    {
        internal MutationEvidence(
            bool runtimeAvailable,
            bool localPlayer,
            bool objectOwner,
            bool creator,
            bool wardAccess,
            bool inRange,
            bool outsideNoBuildZone,
            bool unchanged,
            bool fullHealth,
            bool inert,
            bool structurallyIndependent,
            bool nativePolicy,
            bool toolAvailable,
            bool withinCapacity)
        {
            RuntimeAvailable = runtimeAvailable;
            LocalPlayer = localPlayer;
            ObjectOwner = objectOwner;
            Creator = creator;
            WardAccess = wardAccess;
            InRange = inRange;
            OutsideNoBuildZone = outsideNoBuildZone;
            Unchanged = unchanged;
            FullHealth = fullHealth;
            Inert = inert;
            StructurallyIndependent = structurallyIndependent;
            NativePolicy = nativePolicy;
            ToolAvailable = toolAvailable;
            WithinCapacity = withinCapacity;
        }

        internal bool RuntimeAvailable { get; }
        internal bool LocalPlayer { get; }
        internal bool ObjectOwner { get; }
        internal bool Creator { get; }
        internal bool WardAccess { get; }
        internal bool InRange { get; }
        internal bool OutsideNoBuildZone { get; }
        internal bool Unchanged { get; }
        internal bool FullHealth { get; }
        internal bool Inert { get; }
        internal bool StructurallyIndependent { get; }
        internal bool NativePolicy { get; }
        internal bool ToolAvailable { get; }
        internal bool WithinCapacity { get; }
    }

    internal static class MutationPolicy
    {
        internal static MutationDenial EvaluateUndo(in MutationEvidence evidence)
        {
            MutationDenial common = EvaluateCommon(in evidence);
            if (common != MutationDenial.None) return common;
            if (!evidence.Unchanged) return MutationDenial.Changed;
            if (!evidence.FullHealth) return MutationDenial.Damaged;
            if (!evidence.Inert) return MutationDenial.AccessedOrInteractive;
            if (!evidence.StructurallyIndependent)
                return MutationDenial.StructurallyDependedUpon;
            return evidence.NativePolicy ? MutationDenial.None : MutationDenial.NativePolicyDenied;
        }

        internal static MutationDenial EvaluateRepair(in MutationEvidence evidence)
        {
            MutationDenial common = EvaluateCommon(in evidence);
            if (common != MutationDenial.None) return common;
            if (!evidence.NativePolicy) return MutationDenial.NativePolicyDenied;
            if (!evidence.ToolAvailable) return MutationDenial.ToolUnavailable;
            return evidence.WithinCapacity ? MutationDenial.None : MutationDenial.CapacityExceeded;
        }

        private static MutationDenial EvaluateCommon(in MutationEvidence evidence)
        {
            if (!evidence.RuntimeAvailable) return MutationDenial.RuntimeUnavailable;
            if (!evidence.LocalPlayer) return MutationDenial.NotLocalPlayer;
            if (!evidence.ObjectOwner) return MutationDenial.ObjectAuthorityUnavailable;
            if (!evidence.Creator) return MutationDenial.WrongCreator;
            if (!evidence.WardAccess) return MutationDenial.WardDenied;
            if (!evidence.InRange) return MutationDenial.OutOfRange;
            return evidence.OutsideNoBuildZone ? MutationDenial.None : MutationDenial.NoBuildZone;
        }
    }
}
