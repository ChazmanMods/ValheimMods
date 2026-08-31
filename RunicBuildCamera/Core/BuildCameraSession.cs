using UnityEngine;

namespace RunicBuildCamera.Core
{
    /// <summary>
    /// Owns the state for one detached-camera session. The session stores only a
    /// local player instance id; live Valheim objects remain the integration layer's concern.
    /// </summary>
    internal sealed class BuildCameraSession
    {
        internal bool IsActive { get; private set; }

        internal int PlayerInstanceId { get; private set; }

        internal Vector3 Anchor { get; private set; }

        internal Vector3 Position { get; private set; }

        internal float Yaw { get; private set; }

        internal float Pitch { get; private set; }

        internal float Range { get; private set; }

        internal Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);

        internal bool Begin(
            int playerInstanceId,
            Vector3 anchor,
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float range)
        {
            if (playerInstanceId == 0 || !IsFinite(anchor) || !IsFinite(cameraPosition) ||
                !IsFinite(cameraRotation) || !IsPositiveFinite(range))
            {
                return false;
            }

            Vector3 euler = cameraRotation.eulerAngles;
            IsActive = true;
            PlayerInstanceId = playerInstanceId;
            Anchor = anchor;
            Range = range;
            Yaw = CameraMotion.NormalizeAngle(euler.y);
            Pitch = Mathf.Clamp(CameraMotion.NormalizeAngle(euler.x), -89f, 89f);
            Position = CameraMotion.ClampToAnchor(cameraPosition, anchor, range);
            return true;
        }

        internal bool BelongsTo(int playerInstanceId) =>
            IsActive && playerInstanceId != 0 && PlayerInstanceId == playerInstanceId;

        /// <summary>
        /// The avatar can still be displaced by platforms, impacts, or networking even though
        /// locomotion input is frozen. Translate the detached camera by the same delta so its
        /// bounded volume remains stable relative to that avatar.
        /// </summary>
        internal void Reanchor(Vector3 anchor)
        {
            if (!IsActive || !IsFinite(anchor)) return;

            Vector3 delta = anchor - Anchor;
            Anchor = anchor;
            Position = CameraMotion.ClampToAnchor(Position + delta, Anchor, Range);
        }

        internal void SetPose(in CameraPose pose)
        {
            if (!IsActive || !IsFinite(pose.Position) || !IsFinite(pose.Yaw) ||
                !IsFinite(pose.Pitch))
            {
                return;
            }

            Position = CameraMotion.ClampToAnchor(pose.Position, Anchor, Range);
            Yaw = CameraMotion.NormalizeAngle(pose.Yaw);
            Pitch = Mathf.Clamp(pose.Pitch, -89f, 89f);
        }

        internal void UpdateRange(float range)
        {
            if (!IsActive || !IsPositiveFinite(range)) return;
            Range = range;
            Position = CameraMotion.ClampToAnchor(Position, Anchor, Range);
        }

        internal void End()
        {
            IsActive = false;
            PlayerInstanceId = 0;
            Anchor = Vector3.zero;
            Position = Vector3.zero;
            Yaw = 0f;
            Pitch = 0f;
            Range = 0f;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsPositiveFinite(float value) => IsFinite(value) && value > 0f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
