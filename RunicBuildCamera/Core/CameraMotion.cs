using UnityEngine;

namespace RunicBuildCamera.Core
{
    internal readonly struct CameraMotionInput
    {
        internal CameraMotionInput(
            float right,
            float up,
            float forward,
            float yawDelta,
            float pitchDelta,
            bool fast)
        {
            Right = Mathf.Clamp(right, -1f, 1f);
            Up = Mathf.Clamp(up, -1f, 1f);
            Forward = Mathf.Clamp(forward, -1f, 1f);
            YawDelta = yawDelta;
            PitchDelta = pitchDelta;
            Fast = fast;
        }

        internal float Right { get; }

        internal float Up { get; }

        internal float Forward { get; }

        /// <summary>Signed degrees to add during this frame.</summary>
        internal float YawDelta { get; }

        /// <summary>Signed degrees to add during this frame.</summary>
        internal float PitchDelta { get; }

        internal bool Fast { get; }
    }

    internal readonly struct CameraPose
    {
        internal CameraPose(Vector3 position, float yaw, float pitch)
        {
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
        }

        internal Vector3 Position { get; }

        internal float Yaw { get; }

        internal float Pitch { get; }

        internal Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    }

    /// <summary>Pure detached-camera movement and player-anchor bounding.</summary>
    internal static class CameraMotion
    {
        internal static CameraPose Step(
            Vector3 position,
            float yaw,
            float pitch,
            Vector3 anchor,
            float range,
            in CameraMotionInput input,
            float deltaTime,
            float moveSpeed,
            float fastMoveMultiplier,
            bool worldRelativeMovement)
        {
            float frameTime = SanitizeDeltaTime(deltaTime);
            float nextYaw = NormalizeAngle(yaw + Sanitize(input.YawDelta));
            float nextPitch = Mathf.Clamp(pitch + Sanitize(input.PitchDelta), -89f, 89f);

            Vector3 local = new Vector3(input.Right, input.Up, input.Forward);
            if (local.sqrMagnitude > 1f)
                local.Normalize();

            Vector3 movement;
            if (worldRelativeMovement)
            {
                movement = local;
            }
            else
            {
                // Forward/right follow the detached view while the explicit rise/descend input
                // stays aligned to world-up.
                Quaternion view = Quaternion.Euler(nextPitch, nextYaw, 0f);
                movement = view * new Vector3(local.x, 0f, local.z) + Vector3.up * local.y;
                if (movement.sqrMagnitude > 1f)
                    movement.Normalize();
            }

            float speed = Mathf.Max(0f, Sanitize(moveSpeed));
            if (input.Fast)
                speed *= Mathf.Max(1f, Sanitize(fastMoveMultiplier));

            Vector3 nextPosition = position + movement * (speed * frameTime);
            float boundedRange = IsFinite(range) && range > 0f ? range : 0.01f;
            nextPosition = ClampToAnchor(nextPosition, anchor, boundedRange);
            return new CameraPose(nextPosition, nextYaw, nextPitch);
        }

        internal static Vector3 ClampToAnchor(Vector3 position, Vector3 anchor, float range)
        {
            if (!IsFinite(position) || !IsFinite(anchor)) return anchor;
            if (!IsFinite(range) || range <= 0f) return anchor;

            Vector3 offset = position - anchor;
            float squareRange = range * range;
            if (offset.sqrMagnitude <= squareRange) return position;
            return anchor + offset.normalized * range;
        }

        internal static float NormalizeAngle(float degrees)
        {
            if (!IsFinite(degrees)) return 0f;
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        private static float SanitizeDeltaTime(float value) =>
            IsFinite(value) ? Mathf.Clamp(value, 0f, 0.1f) : 0f;

        private static float Sanitize(float value) => IsFinite(value) ? value : 0f;

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
