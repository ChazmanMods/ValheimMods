namespace ArcaneWaterWorks.Pools
{
    internal static class GardenHealth
    {
        // The old removal bug left exactly zero health. Negative values mean invulnerable.
        internal static bool IsPreviouslyRemoved(float health) => health == 0f;
    }
}
