using System;
using System.Reflection;
using RunicBuildCamera.Core;
using UnityEngine;

namespace RunicBuildCamera.Tests
{
    internal static class CoreBehaviorTests
    {
        internal static void Register()
        {
            TestRunner.Run("camera input axes are clamped", CameraInputAxesAreClamped);
            TestRunner.Run("camera angles normalize deterministically", CameraAnglesNormalize);
            TestRunner.Run("camera anchor clamp preserves inside positions", AnchorClampPreservesInside);
            TestRunner.Run("camera anchor clamp bounds outside positions", AnchorClampBoundsOutside);
            TestRunner.Run("camera anchor clamp rejects non-finite state", AnchorClampRejectsInvalid);
            TestRunner.Run("camera step caps frame delta", CameraStepCapsFrameDelta);
            TestRunner.Run("camera step normalizes diagonal movement", CameraStepNormalizesDiagonal);
            TestRunner.Run("camera step applies bounded fast movement", CameraStepAppliesFastMovement);
            TestRunner.Run("camera-relative movement is derived from view rotation", CameraRelativeMovementUsesView);
            TestRunner.Run("camera step sanitizes non-finite look input", CameraStepSanitizesLook);
            TestRunner.Run("camera session rejects invalid starts", SessionRejectsInvalidStarts);
            TestRunner.Run("camera session ownership is player-specific", SessionOwnershipIsPlayerSpecific);
            TestRunner.Run("camera session reanchors without relative drift", SessionReanchorsWithoutDrift);
            TestRunner.Run("camera session pose and range remain bounded", SessionPoseAndRangeRemainBounded);
            TestRunner.Run("camera session end clears all state", SessionEndClearsState);
            TestRunner.Run("pickup policy exposes hard work bounds", PickupPolicyHasHardBounds);
            TestRunner.Run("pickup policy accepts only fully valid facts", PickupPolicyAcceptsValidFacts);
            TestRunner.Run("pickup policy rejection order is stable", PickupPolicyRejectionOrder);
            TestRunner.Run("pickup range validates finite boundary", PickupRangeValidation);
            TestRunner.Run("pickup scan schedule clamps and saturates", PickupScheduleClampsAndSaturates);
            TestRunner.Run("pickup retry schedules enforce floors", PickupRetrySchedulesEnforceFloors);
        }

        private static void CameraInputAxesAreClamped()
        {
            var input = new CameraMotionInput(2f, -3f, 9f, 4f, 5f, false);
            TestAssert.Equal(1f, input.Right);
            TestAssert.Equal(-1f, input.Up);
            TestAssert.Equal(1f, input.Forward);
            TestAssert.Equal(4f, input.YawDelta);
            TestAssert.Equal(5f, input.PitchDelta);
        }

        private static void CameraAnglesNormalize()
        {
            TestAssert.Near(0f, CameraMotion.NormalizeAngle(720f), 0.0001f);
            TestAssert.Near(-170f, CameraMotion.NormalizeAngle(190f), 0.0001f);
            TestAssert.Near(180f, CameraMotion.NormalizeAngle(-180f), 0.0001f);
            TestAssert.Near(0f, CameraMotion.NormalizeAngle(float.NaN), 0.0001f);
            TestAssert.Near(0f, CameraMotion.NormalizeAngle(float.PositiveInfinity), 0.0001f);
        }

        private static void AnchorClampPreservesInside()
        {
            Vector3 anchor = new Vector3(10f, 20f, 30f);
            Vector3 inside = new Vector3(12f, 20f, 30f);
            TestAssert.VectorNear(inside, CameraMotion.ClampToAnchor(inside, anchor, 3f), 0.0001f);
        }

        private static void AnchorClampBoundsOutside()
        {
            Vector3 result = CameraMotion.ClampToAnchor(
                new Vector3(10f, 0f, 0f), Vector3.zero, 4f);
            TestAssert.VectorNear(new Vector3(4f, 0f, 0f), result, 0.0001f);
        }

        private static void AnchorClampRejectsInvalid()
        {
            Vector3 anchor = new Vector3(1f, 2f, 3f);
            TestAssert.VectorNear(
                anchor,
                CameraMotion.ClampToAnchor(new Vector3(float.NaN, 0f, 0f), anchor, 10f),
                0f);
            TestAssert.VectorNear(anchor, CameraMotion.ClampToAnchor(Vector3.zero, anchor, 0f), 0f);
        }

        private static void CameraStepCapsFrameDelta()
        {
            var input = new CameraMotionInput(0f, 0f, 1f, 0f, 0f, false);
            CameraPose pose = CameraMotion.Step(
                Vector3.zero,
                0f,
                0f,
                Vector3.zero,
                100f,
                in input,
                8f,
                10f,
                3f,
                worldRelativeMovement: true);
            TestAssert.VectorNear(new Vector3(0f, 0f, 1f), pose.Position, 0.0001f);
        }

        private static void CameraStepNormalizesDiagonal()
        {
            var input = new CameraMotionInput(1f, 1f, 1f, 0f, 0f, false);
            CameraPose pose = CameraMotion.Step(
                Vector3.zero, 0f, 0f, Vector3.zero, 100f, in input,
                0.1f, 10f, 3f, worldRelativeMovement: true);
            TestAssert.Near(1f, pose.Position.magnitude, 0.0001f);
            TestAssert.True(pose.Position.x > 0f && pose.Position.y > 0f && pose.Position.z > 0f);
        }

        private static void CameraStepAppliesFastMovement()
        {
            var normal = new CameraMotionInput(1f, 0f, 0f, 0f, 0f, false);
            var fast = new CameraMotionInput(1f, 0f, 0f, 0f, 0f, true);
            CameraPose normalPose = CameraMotion.Step(
                Vector3.zero, 0f, 0f, Vector3.zero, 100f, in normal,
                0.05f, 10f, 3f, true);
            CameraPose fastPose = CameraMotion.Step(
                Vector3.zero, 0f, 0f, Vector3.zero, 100f, in fast,
                0.05f, 10f, 3f, true);
            TestAssert.Near(normalPose.Position.x * 3f, fastPose.Position.x, 0.0001f);
        }

        private static void CameraRelativeMovementUsesView()
        {
            MethodInfo step = typeof(CameraMotion).GetMethod(
                "Step", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(step, typeof(Quaternion), nameof(Quaternion.Euler)));
            TestAssert.True(IlReader.Calls(step, typeof(Quaternion), "op_Multiply"));
        }

        private static void CameraStepSanitizesLook()
        {
            var input = new CameraMotionInput(
                0f, 0f, 0f, float.NaN, float.PositiveInfinity, false);
            CameraPose pose = CameraMotion.Step(
                Vector3.zero, 179f, 88f, Vector3.zero, 10f, in input,
                float.NaN, float.NaN, float.NaN, true);
            TestAssert.Near(179f, pose.Yaw, 0.0001f);
            TestAssert.Near(88f, pose.Pitch, 0.0001f);
            TestAssert.VectorNear(Vector3.zero, pose.Position, 0f);
        }

        private static void SessionRejectsInvalidStarts()
        {
            var session = new BuildCameraSession();
            TestAssert.False(session.Begin(
                0, Vector3.zero, Vector3.zero, Quaternion.identity, 10f));
            TestAssert.False(session.Begin(
                1, Vector3.zero, Vector3.zero, Quaternion.identity, 0f));
            TestAssert.False(session.Begin(
                1,
                new Vector3(float.NaN, 0f, 0f),
                Vector3.zero,
                Quaternion.identity,
                10f));
            TestAssert.False(session.IsActive);
        }

        private static void SessionOwnershipIsPlayerSpecific()
        {
            BuildCameraSession session = SeedActiveSession(
                44, Vector3.zero, Vector3.zero, 10f);
            TestAssert.True(session.BelongsTo(44));
            TestAssert.False(session.BelongsTo(0));
            TestAssert.False(session.BelongsTo(45));
        }

        private static void SessionReanchorsWithoutDrift()
        {
            BuildCameraSession session = SeedActiveSession(
                44,
                new Vector3(1f, 2f, 3f),
                new Vector3(5f, 2f, 3f),
                10f);
            session.Reanchor(new Vector3(11f, 4f, -2f));
            TestAssert.VectorNear(new Vector3(11f, 4f, -2f), session.Anchor, 0f);
            TestAssert.VectorNear(new Vector3(15f, 4f, -2f), session.Position, 0.0001f);
        }

        private static void SessionPoseAndRangeRemainBounded()
        {
            BuildCameraSession session = SeedActiveSession(
                44, Vector3.zero, Vector3.zero, 10f);
            var pose = new CameraPose(new Vector3(20f, 0f, 0f), 540f, 120f);
            session.SetPose(in pose);
            TestAssert.VectorNear(new Vector3(10f, 0f, 0f), session.Position, 0.0001f);
            TestAssert.Near(180f, session.Yaw, 0.0001f);
            TestAssert.Near(89f, session.Pitch, 0.0001f);

            session.UpdateRange(2f);
            TestAssert.Equal(2f, session.Range);
            TestAssert.VectorNear(new Vector3(2f, 0f, 0f), session.Position, 0.0001f);
            session.UpdateRange(float.NaN);
            TestAssert.Equal(2f, session.Range);
        }

        private static void SessionEndClearsState()
        {
            BuildCameraSession session = SeedActiveSession(
                44, new Vector3(1f, 2f, 3f), new Vector3(2f, 3f, 4f), 10f);
            session.End();
            TestAssert.False(session.IsActive);
            TestAssert.Equal(0, session.PlayerInstanceId);
            TestAssert.VectorNear(Vector3.zero, session.Anchor, 0f);
            TestAssert.VectorNear(Vector3.zero, session.Position, 0f);
            TestAssert.Equal(0f, session.Range);
        }

        private static void PickupPolicyHasHardBounds()
        {
            TestAssert.Equal(128, PickupPolicy.ColliderCapacity);
            TestAssert.Equal(16, PickupPolicy.MaximumAttemptsPerScan);
            TestAssert.Equal(256, PickupPolicy.MaximumCooldownEntries);
            TestAssert.True(PickupPolicy.MaximumAttemptsPerScan < PickupPolicy.ColliderCapacity);
        }

        private static void PickupPolicyAcceptsValidFacts()
        {
            PickupCandidateFacts facts = Facts();
            TestAssert.Equal(PickupRejectionReason.None, PickupPolicy.Evaluate(in facts));
        }

        private static void PickupPolicyRejectionOrder()
        {
            var allBad = Facts(
                network: false,
                range: false,
                ward: false,
                autoPickup: false,
                piece: true,
                tar: true,
                unique: true,
                inventory: false,
                heavy: true,
                cooldown: true);
            TestAssert.Equal(
                PickupRejectionReason.InvalidNetworkIdentity,
                PickupPolicy.Evaluate(in allBad));

            AssertRejected(PickupRejectionReason.OutsideRange, Facts(range: false));
            AssertRejected(PickupRejectionReason.WardDenied, Facts(ward: false));
            AssertRejected(PickupRejectionReason.AutoPickupDisabled, Facts(autoPickup: false));
            AssertRejected(PickupRejectionReason.Piece, Facts(piece: true));
            AssertRejected(PickupRejectionReason.InTar, Facts(tar: true));
            AssertRejected(PickupRejectionReason.UniqueOrQuestItem, Facts(unique: true));
            AssertRejected(PickupRejectionReason.InventoryFull, Facts(inventory: false));
            AssertRejected(PickupRejectionReason.TooHeavy, Facts(heavy: true));
            AssertRejected(PickupRejectionReason.CoolingDown, Facts(cooldown: true));
        }

        private static void PickupRangeValidation()
        {
            TestAssert.True(PickupPolicy.IsWithinRange(25f, 5f));
            TestAssert.False(PickupPolicy.IsWithinRange(25.01f, 5f));
            TestAssert.False(PickupPolicy.IsWithinRange(-1f, 5f));
            TestAssert.False(PickupPolicy.IsWithinRange(float.NaN, 5f));
            TestAssert.False(PickupPolicy.IsWithinRange(1f, float.PositiveInfinity));
        }

        private static void PickupScheduleClampsAndSaturates()
        {
            TestAssert.True(PickupPolicy.IsScanDue(4f, 4f));
            TestAssert.False(PickupPolicy.IsScanDue(3.99f, 4f));
            TestAssert.False(PickupPolicy.IsScanDue(float.NaN, 4f));
            TestAssert.Near(10.02f, PickupPolicy.NextScanAt(10f, -10f), 0.0001f);
            TestAssert.Near(10.2f, PickupPolicy.NextScanAt(10f, float.NaN), 0.0001f);
            TestAssert.Equal(float.MaxValue, PickupPolicy.NextScanAt(float.MaxValue, 2f));
        }

        private static void PickupRetrySchedulesEnforceFloors()
        {
            TestAssert.Near(
                3f + PickupPolicy.MinimumOwnershipRetrySeconds,
                PickupPolicy.OwnershipRetryAt(3f, 0.01f),
                0.0001f);
            TestAssert.Near(
                3f + PickupPolicy.FailedPickupRetrySeconds,
                PickupPolicy.FailedPickupRetryAt(3f),
                0.0001f);
            TestAssert.Near(
                3f + PickupPolicy.CompletedPickupDedupeSeconds,
                PickupPolicy.CompletedPickupRetryAt(3f),
                0.0001f);
        }

        private static void AssertRejected(
            PickupRejectionReason expected,
            PickupCandidateFacts facts) =>
            TestAssert.Equal(expected, PickupPolicy.Evaluate(in facts));

        private static PickupCandidateFacts Facts(
            bool network = true,
            bool range = true,
            bool ward = true,
            bool autoPickup = true,
            bool piece = false,
            bool tar = false,
            bool unique = false,
            bool inventory = true,
            bool heavy = false,
            bool cooldown = false) =>
            new PickupCandidateFacts(
                network,
                range,
                ward,
                autoPickup,
                piece,
                tar,
                unique,
                inventory,
                heavy,
                cooldown);

        private static BuildCameraSession SeedActiveSession(
            int playerId,
            Vector3 anchor,
            Vector3 position,
            float range)
        {
            var session = new BuildCameraSession();
            SetAutoProperty(session, nameof(BuildCameraSession.IsActive), true);
            SetAutoProperty(session, nameof(BuildCameraSession.PlayerInstanceId), playerId);
            SetAutoProperty(session, nameof(BuildCameraSession.Anchor), anchor);
            SetAutoProperty(session, nameof(BuildCameraSession.Position), position);
            SetAutoProperty(session, nameof(BuildCameraSession.Yaw), 0f);
            SetAutoProperty(session, nameof(BuildCameraSession.Pitch), 0f);
            SetAutoProperty(session, nameof(BuildCameraSession.Range), range);
            return session;
        }

        private static void SetAutoProperty<T>(object instance, string propertyName, T value)
        {
            FieldInfo field = instance.GetType().GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.NotNull(field, "Missing backing field for " + propertyName + ".")
                .SetValue(instance, value);
        }
    }
}
