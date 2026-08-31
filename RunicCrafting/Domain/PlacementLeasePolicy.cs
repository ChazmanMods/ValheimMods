namespace RunicCrafting.Domain
{
    public enum PlacementLeaseDisposition
    {
        Rollback = 0,
        Commit = 1
    }

    /// <summary>
    /// Resolves the reversible material lease at the end of Valheim's placement update. Once
    /// either the piece exists or Valheim has reached its normal consume point, restoring the
    /// reserved materials could duplicate value; every earlier exit must restore them.
    /// </summary>
    public static class PlacementLeasePolicy
    {
        public static PlacementLeaseDisposition Resolve(
            bool outputCreated,
            bool vanillaConsumeReached) =>
            outputCreated || vanillaConsumeReached
                ? PlacementLeaseDisposition.Commit
                : PlacementLeaseDisposition.Rollback;
    }
}
