using System;

namespace RunicAwareness.Core
{
    internal static class ContextDisclosurePolicy
    {
        internal const float HardMaximumDetailReachMeters = 10f;

        internal static bool AllowsDetailedDisclosure(
            float squaredAvatarDistance,
            float vanillaInteractDistance)
        {
            if (float.IsNaN(squaredAvatarDistance) || float.IsInfinity(squaredAvatarDistance) ||
                float.IsNaN(vanillaInteractDistance) || float.IsInfinity(vanillaInteractDistance) ||
                squaredAvatarDistance < 0f || vanillaInteractDistance <= 0f)
                return false;
            float limit = Math.Min(
                vanillaInteractDistance,
                HardMaximumDetailReachMeters);
            return squaredAvatarDistance <= limit * limit;
        }
    }
}
