using System;

namespace RunicAgriculture.Core
{
    /// <summary>
    /// Pure limits and one-time migration rules for the ordinary rectangular planting grid.
    /// Rows and columns describe the requested footprint. The hard renderer cap exists only as a
    /// safety boundary; seed availability never changes the configured geometry.
    /// </summary>
    public static class PlantingGridPolicy
    {
        public const int LegacyPreviewLimit = 256;
        public const int DefaultPreviewLimit = PatternRequest.AbsoluteMaximumPoints;

        public static PlantPattern DefaultPattern => PlantPattern.Grid;

        public static PlantPattern MigrateToDefaultGrid(
            bool migrationAlreadyApplied,
            PlantPattern configuredPattern) =>
            migrationAlreadyApplied ? configuredPattern : PlantPattern.Grid;

        public static int ConfiguredPreviewLimit(int configured)
        {
            if (configured < 1) throw new ArgumentOutOfRangeException(nameof(configured));
            return Math.Min(configured, PatternRequest.AbsoluteMaximumPoints);
        }

        public static int SelectableSeedCount(
            int availableSeedActions,
            int groundValidCells,
            bool unlimitedSeeds)
        {
            if (groundValidCells < 0)
                throw new ArgumentOutOfRangeException(nameof(groundValidCells));
            if (unlimitedSeeds) return groundValidCells;
            if (availableSeedActions < 0)
                throw new ArgumentOutOfRangeException(nameof(availableSeedActions));
            return Math.Min(availableSeedActions, groundValidCells);
        }

        public static bool ConsumesSeedResources(
            bool worldFreeBuild,
            bool noCostCheat) => !worldFreeBuild && !noCostCheat;
    }
}
