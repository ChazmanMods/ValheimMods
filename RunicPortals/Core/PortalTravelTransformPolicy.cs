using UnityEngine;

namespace RunicPortals.Core
{
    internal static class PortalTravelTransformPolicy
    {
        internal const float MaximumExitOffsetMeters = 64f;
        private const float MinimumQuaternionMagnitudeSquared = 0.99f;
        private const float MaximumQuaternionMagnitudeSquared = 1.01f;

        internal static bool TryResolve(
            Vector3 destination,
            Quaternion destinationRotation,
            float exitOffset,
            out Vector3 arrival,
            out Quaternion normalizedRotation)
        {
            arrival = default;
            normalizedRotation = default;
            if (!Finite(destination.x) || !Finite(destination.y) || !Finite(destination.z) ||
                !Finite(destinationRotation.x) || !Finite(destinationRotation.y) ||
                !Finite(destinationRotation.z) || !Finite(destinationRotation.w) ||
                !Finite(exitOffset) || exitOffset < 0f || exitOffset > MaximumExitOffsetMeters)
                return false;

            float magnitudeSquared = destinationRotation.x * destinationRotation.x +
                                     destinationRotation.y * destinationRotation.y +
                                     destinationRotation.z * destinationRotation.z +
                                     destinationRotation.w * destinationRotation.w;
            if (!Finite(magnitudeSquared) ||
                magnitudeSquared < MinimumQuaternionMagnitudeSquared ||
                magnitudeSquared > MaximumQuaternionMagnitudeSquared)
                return false;

            float inverseMagnitude = 1f / Mathf.Sqrt(magnitudeSquared);
            if (!Finite(inverseMagnitude) || inverseMagnitude <= 0f) return false;
            normalizedRotation = new Quaternion(
                destinationRotation.x * inverseMagnitude,
                destinationRotation.y * inverseMagnitude,
                destinationRotation.z * inverseMagnitude,
                destinationRotation.w * inverseMagnitude);
            Vector3 forward = normalizedRotation * Vector3.forward;
            if (!Finite(forward.x) || !Finite(forward.y) || !Finite(forward.z)) return false;

            arrival = destination + forward * exitOffset + Vector3.up;
            return Finite(arrival.x) && Finite(arrival.y) && Finite(arrival.z);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
