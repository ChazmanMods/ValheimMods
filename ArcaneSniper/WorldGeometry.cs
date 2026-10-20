using System;

namespace ArcaneSniper
{
    internal static class WorldGeometry
    {
        internal static bool InSquare(int x, int z, int centerX, int centerZ, int radius) =>
            Math.Abs(x - centerX) <= radius && Math.Abs(z - centerZ) <= radius;

        internal static bool ValidFocus(float x, float y, float z, float bodyX, float bodyZ, float radius)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return false;
            double dx = x - bodyX, dz = z - bodyZ;
            return y >= -1000 && y <= 10000 && dx * dx + dz * dz <= (radius + 4) * (radius + 4);
        }
    }
}
