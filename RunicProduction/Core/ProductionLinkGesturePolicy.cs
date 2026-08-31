using RunicProduction.Contracts;

namespace RunicProduction.Core
{
    internal enum ProductionLinkMouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>
    /// The mouse button owns the requested high-level chest role. Runtime may refine Input to
    /// Fuel Input only when the player explicitly aims at a station's native fuel control.
    /// </summary>
    internal static class ProductionLinkGesturePolicy
    {
        internal static ProductionLinkRole RequestedRole(
            ProductionLinkMouseButton button)
        {
            switch (button)
            {
                case ProductionLinkMouseButton.Left:
                    return ProductionLinkRole.Input;
                case ProductionLinkMouseButton.Right:
                    return ProductionLinkRole.Output;
                case ProductionLinkMouseButton.Middle:
                    return ProductionLinkRole.Replenishment;
                default:
                    return ProductionLinkRole.Input;
            }
        }
    }
}
