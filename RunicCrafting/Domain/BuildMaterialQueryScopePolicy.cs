namespace RunicCrafting.Domain
{
    internal enum BuildMaterialQueryScope
    {
        RequiredStationUnavailable = 0,
        StationLocal = 1,
        PlayerLocalStationless = 2
    }

    internal static class BuildMaterialQueryScopePolicy
    {
        internal static BuildMaterialQueryScope Resolve(
            bool pieceRequiresCraftingStation,
            bool requiredStationResolved)
        {
            if (!pieceRequiresCraftingStation)
                return BuildMaterialQueryScope.PlayerLocalStationless;
            return requiredStationResolved
                ? BuildMaterialQueryScope.StationLocal
                : BuildMaterialQueryScope.RequiredStationUnavailable;
        }
    }
}
