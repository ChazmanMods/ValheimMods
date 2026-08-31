using System;

namespace RunicStorage.Engine
{
    internal static class ContainerQueryCoverage
    {
        private const float RadiusEpsilon = 0.001f;

        internal static bool IsTruncated(
            float requestedRadius,
            float effectiveRadius,
            bool candidateLimitReached)
        {
            if (float.IsNaN(requestedRadius) || float.IsInfinity(requestedRadius) || requestedRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(requestedRadius));
            if (float.IsNaN(effectiveRadius) || float.IsInfinity(effectiveRadius) || effectiveRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(effectiveRadius));

            return candidateLimitReached || effectiveRadius + RadiusEpsilon < requestedRadius;
        }
    }
}
