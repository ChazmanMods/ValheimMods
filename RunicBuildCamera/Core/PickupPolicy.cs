using System;

namespace RunicBuildCamera.Core
{
    /// <summary>
    /// Stable, engine-independent reasons for rejecting a loose-item pickup candidate.
    /// Keeping this decision separate from scene discovery makes the security boundary easy to
    /// exercise without constructing Unity objects.
    /// </summary>
    internal enum PickupRejectionReason
    {
        None = 0,
        InvalidNetworkIdentity = 1,
        OutsideRange = 2,
        WardDenied = 3,
        AutoPickupDisabled = 4,
        Piece = 5,
        InTar = 6,
        UniqueOrQuestItem = 7,
        InventoryFull = 8,
        TooHeavy = 9,
        CoolingDown = 10
    }

    internal readonly struct PickupCandidateFacts
    {
        internal PickupCandidateFacts(
            bool hasValidNetworkIdentity,
            bool isWithinRange,
            bool wardAllows,
            bool autoPickupEnabled,
            bool isPiece,
            bool isInTar,
            bool isUniqueOrQuestItem,
            bool inventoryCanAdd,
            bool wouldExceedCarryWeight,
            bool isCoolingDown)
        {
            HasValidNetworkIdentity = hasValidNetworkIdentity;
            IsWithinRange = isWithinRange;
            WardAllows = wardAllows;
            AutoPickupEnabled = autoPickupEnabled;
            IsPiece = isPiece;
            IsInTar = isInTar;
            IsUniqueOrQuestItem = isUniqueOrQuestItem;
            InventoryCanAdd = inventoryCanAdd;
            WouldExceedCarryWeight = wouldExceedCarryWeight;
            IsCoolingDown = isCoolingDown;
        }

        internal bool HasValidNetworkIdentity { get; }
        internal bool IsWithinRange { get; }
        internal bool WardAllows { get; }
        internal bool AutoPickupEnabled { get; }
        internal bool IsPiece { get; }
        internal bool IsInTar { get; }
        internal bool IsUniqueOrQuestItem { get; }
        internal bool InventoryCanAdd { get; }
        internal bool WouldExceedCarryWeight { get; }
        internal bool IsCoolingDown { get; }
    }

    internal static class PickupPolicy
    {
        // Scene discovery and mutation are hard-bounded independently of config.
        // A saturated overlap buffer simply defers excess objects to a later scan.
        internal const int ColliderCapacity = 128;
        internal const int MaximumAttemptsPerScan = 16;
        internal const int MaximumCooldownEntries = 256;

        internal const float MinimumOwnershipRetrySeconds = 0.20f;
        internal const float FailedPickupRetrySeconds = 0.50f;
        internal const float CompletedPickupDedupeSeconds = 5.00f;

        internal static bool IsScanDue(float now, float nextScanAt) =>
            IsFinite(now) && IsFinite(nextScanAt) && now >= nextScanAt;

        internal static float NextScanAt(float now, float configuredIntervalSeconds)
        {
            float interval = IsFinite(configuredIntervalSeconds)
                ? Math.Max(0.02f, configuredIntervalSeconds)
                : 0.20f;
            return SaturatingAdd(now, interval);
        }

        internal static bool IsWithinRange(float squaredDistance, float rangeMeters)
        {
            if (!IsFinite(squaredDistance) || squaredDistance < 0f ||
                !IsFinite(rangeMeters) || rangeMeters <= 0f)
                return false;

            double range = rangeMeters;
            return squaredDistance <= range * range;
        }

        internal static PickupRejectionReason Evaluate(in PickupCandidateFacts facts)
        {
            if (!facts.HasValidNetworkIdentity)
                return PickupRejectionReason.InvalidNetworkIdentity;
            if (!facts.IsWithinRange)
                return PickupRejectionReason.OutsideRange;
            if (!facts.WardAllows)
                return PickupRejectionReason.WardDenied;
            if (!facts.AutoPickupEnabled)
                return PickupRejectionReason.AutoPickupDisabled;
            if (facts.IsPiece)
                return PickupRejectionReason.Piece;
            if (facts.IsInTar)
                return PickupRejectionReason.InTar;
            if (facts.IsUniqueOrQuestItem)
                return PickupRejectionReason.UniqueOrQuestItem;
            if (!facts.InventoryCanAdd)
                return PickupRejectionReason.InventoryFull;
            if (facts.WouldExceedCarryWeight)
                return PickupRejectionReason.TooHeavy;
            if (facts.IsCoolingDown)
                return PickupRejectionReason.CoolingDown;
            return PickupRejectionReason.None;
        }

        internal static float OwnershipRetryAt(float now, float configuredIntervalSeconds) =>
            SaturatingAdd(
                now,
                Math.Max(
                    MinimumOwnershipRetrySeconds,
                    IsFinite(configuredIntervalSeconds) ? configuredIntervalSeconds : 0f));

        internal static float FailedPickupRetryAt(float now) =>
            SaturatingAdd(now, FailedPickupRetrySeconds);

        internal static float CompletedPickupRetryAt(float now) =>
            SaturatingAdd(now, CompletedPickupDedupeSeconds);

        private static float SaturatingAdd(float left, float right)
        {
            if (!IsFinite(left) || !IsFinite(right)) return float.MaxValue;
            double result = (double)left + right;
            return result >= float.MaxValue ? float.MaxValue : (float)result;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
