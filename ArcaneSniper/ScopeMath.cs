using System;

namespace ArcaneSniper
{
    internal static class ScopeMath
    {
        // Optical magnification is a ratio of tan(FOV/2), not a linear FOV division.
        internal static float FieldOfView(float baseFov, float magnification)
        {
            double fov = Math.Max(1, Math.Min(179, baseFov));
            double zoom = Math.Max(1, Math.Min(40, magnification));
            return (float)(2 * Math.Atan(Math.Tan(fov * Math.PI / 360) / zoom) * 180 / Math.PI);
        }
    }
}
