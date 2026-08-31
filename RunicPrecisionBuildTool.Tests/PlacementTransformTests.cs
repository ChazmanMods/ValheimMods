using System;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class PlacementTransformTests
    {
        internal static void Register()
        {
            TestRunner.Run("Position-axis matches and translation resets remain independent", PositionAxesResetIndependently);
            TestRunner.Run("Local reference movement rotates only the new axis contribution", LocalMovementUsesPendingOrientation);
            TestRunner.Run("Full position match is released for a replacement ghost", AbsolutePositionDoesNotStrandNextGhost);
            TestRunner.Run("Relative transform repeats exact local offset and rotation", RelativePatternIsExact);
            TestRunner.Run("Invalid relative history input is atomic", InvalidRelativeCommitIsAtomic);
            TestRunner.Run("Snap-side alignment opposes normals and preserves tangent", SnapAlignmentIsExact);
            TestRunner.Run("Invalid snap-side evidence fails closed", InvalidSnapFailsClosed);
            TestRunner.Run("Snap-side alignment survives 10,000 deterministic compound poses", SnapAlignmentScaleProbe);
            TestRunner.Run("Relative-transform prediction allocates zero managed bytes", RelativePredictionDoesNotAllocate);
        }

        private static void PositionAxesResetIndependently()
        {
            PoseController pose = NewController(Quaternion.identity);
            pose.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 1f));
            pose.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, 2f));
            pose.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 3f));
            TestAssert.True(pose.MatchPositionComponent(
                new Vector3(20f, 30f, 40f), SemanticCommandKind.MatchPositionX, 0f));

            Vector3 candidate = new Vector3(4f, 5f, 6f);
            TestAssert.VectorNear(new Vector3(20f, 7f, 9f), pose.ComposePosition(candidate), 0f);

            TestAssert.True(pose.Apply(SemanticCommand.ResetSway));
            TestAssert.VectorNear(new Vector3(4f, 7f, 9f), pose.ComposePosition(candidate), 0f);
            TestAssert.True(pose.Apply(SemanticCommand.ResetHeave));
            TestAssert.VectorNear(new Vector3(4f, 5f, 9f), pose.ComposePosition(candidate), 0f);
            TestAssert.True(pose.Apply(SemanticCommand.ResetSurge));
            TestAssert.VectorNear(candidate, pose.ComposePosition(candidate), 0f);
        }

        private static void LocalMovementUsesPendingOrientation()
        {
            Quaternion yaw = QuaternionMath.AngleAxis(90f, 0f, 1f, 0f);
            PoseController pose = NewController(yaw);
            TestAssert.True(pose.Apply(
                SemanticCommand.Translation(SemanticCommandKind.MoveSway, 1.25f),
                PlacementReferenceFrame.Local));
            TestAssert.True(pose.Apply(
                SemanticCommand.Translation(SemanticCommandKind.MoveSurge, -0.5f),
                PlacementReferenceFrame.Local));

            Vector3 expected = yaw * new Vector3(1.25f, 0f, -0.5f);
            TestAssert.VectorNear(expected, pose.Session.WorldOffset, 0.00001f);
            pose.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 35f));
            TestAssert.VectorNear(expected, pose.Session.WorldOffset, 0.00001f,
                "A later rotation must not orbit prior movement.");
        }

        private static void AbsolutePositionDoesNotStrandNextGhost()
        {
            PlacementSession session = new PlacementSession();
            PoseController pose = new PoseController(session);
            pose.ObserveSelection(7, Quaternion.identity, 0);
            TestAssert.True(pose.MatchTransform(
                new PlacementTransform(new Vector3(8f, 9f, 10f),
                    QuaternionMath.AngleAxis(30f, 0f, 1f, 0f)), 0f));
            TestAssert.True(session.HasAbsolutePosition);
            Quaternion matched = session.DesiredRotation;

            pose.BeginGhostGeneration(7, Quaternion.identity, 0);
            TestAssert.False(session.HasAbsolutePosition,
                "The placed object's exact coordinate must not be inherited by the replacement ghost.");
            TestAssert.QuaternionNear(matched, session.DesiredRotation, 0.0001f,
                "Same-piece pattern building should retain deliberate orientation.");
        }

        private static void RelativePatternIsExact()
        {
            RelativeTransformHistory history = new RelativeTransformHistory();
            PlacementTransform first = new PlacementTransform(Vector3.zero, Quaternion.identity);
            PlacementTransform second = new PlacementTransform(
                Vector3.right,
                QuaternionMath.AngleAxis(90f, 0f, 1f, 0f));
            TestAssert.True(history.ObserveCommit(first));
            TestAssert.True(history.ObserveCommit(second));
            TestAssert.True(history.TryPredictNext(out PlacementTransform next));
            TestAssert.VectorNear(
                second.Position + second.Rotation * Vector3.right,
                next.Position,
                0.00001f);
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(180f, 0f, 1f, 0f),
                next.Rotation,
                0.0001f);
        }

        private static void InvalidRelativeCommitIsAtomic()
        {
            RelativeTransformHistory history = new RelativeTransformHistory();
            history.ObserveCommit(new PlacementTransform(Vector3.zero, Quaternion.identity));
            history.ObserveCommit(new PlacementTransform(Vector3.right, Quaternion.identity));
            TestAssert.True(history.TryPredictNext(out PlacementTransform before));
            TestAssert.False(history.ObserveCommit(new PlacementTransform(
                new Vector3(float.NaN, 0f, 0f), Quaternion.identity)));
            TestAssert.False(history.ObserveCommit(new PlacementTransform(
                Vector3.zero, new Quaternion(float.NaN, 0f, 0f, 0f))));
            TestAssert.True(history.TryPredictNext(out PlacementTransform after));
            TestAssert.VectorNear(before.Position, after.Position, 0f);
            TestAssert.QuaternionNear(before.Rotation, after.Rotation, 0f);
        }

        private static void SnapAlignmentIsExact()
        {
            Vector3 sourceLocal = new Vector3(0.25f, -0.5f, 1f);
            Quaternion sourceRotation = QuaternionMath.AngleAxis(37f, 1f, 0f, 0f);
            Vector3 targetPosition = new Vector3(8f, 3f, -2f);
            Quaternion targetRotation = QuaternionMath.AngleAxis(61f, 0f, 1f, 0f);
            TestAssert.True(SnapAlignment.TryAlignOpposed(
                sourceLocal, sourceRotation, targetPosition, targetRotation,
                out PlacementTransform root));

            TestAssert.VectorNear(targetPosition, root.Position + root.Rotation * sourceLocal, 0.0001f);
            Quaternion sourceWorld = root.Rotation * sourceRotation;
            TestAssert.VectorNear(
                -(targetRotation * Vector3.forward),
                sourceWorld * Vector3.forward,
                0.0001f);
            TestAssert.VectorNear(
                targetRotation * Vector3.up,
                sourceWorld * Vector3.up,
                0.0001f);
        }

        private static void InvalidSnapFailsClosed()
        {
            TestAssert.False(SnapAlignment.TryAlignOpposed(
                new Vector3(float.PositiveInfinity, 0f, 0f),
                Quaternion.identity,
                Vector3.zero,
                Quaternion.identity,
                out _));
            TestAssert.False(SnapAlignment.TryAlignOpposed(
                Vector3.zero,
                new Quaternion(float.NaN, 0f, 0f, 0f),
                Vector3.zero,
                Quaternion.identity,
                out _));
        }

        private static void SnapAlignmentScaleProbe()
        {
            for (int index = 0; index < 10000; index++)
            {
                float sourcePitch = (index * 17 % 358) - 179f;
                float sourceRoll = (index * 31 % 358) - 179f;
                float sourceYaw = (index * 47 % 358) - 179f;
                float targetPitch = (index * 13 % 178) - 89f;
                float targetRoll = (index * 29 % 358) - 179f;
                float targetYaw = (index * 43 % 358) - 179f;
                TestAssert.True(OrientationAngles.TryRecompose(
                    sourcePitch, sourceRoll, sourceYaw, out Quaternion source));
                TestAssert.True(OrientationAngles.TryRecompose(
                    targetPitch, targetRoll, targetYaw, out Quaternion target));
                Vector3 sourcePosition = new Vector3(
                    (index % 17) * 0.03f,
                    (index % 11) * -0.02f,
                    (index % 23) * 0.01f);
                Vector3 targetPosition = new Vector3(
                    (index % 97) - 48f,
                    (index % 53) * 0.1f,
                    (index % 89) - 44f);
                TestAssert.True(SnapAlignment.TryAlignOpposed(
                    sourcePosition, source, targetPosition, target,
                    out PlacementTransform root));
                Quaternion worldSource = root.Rotation * source;
                TestAssert.VectorNear(
                    -(target * Vector3.forward),
                    worldSource * Vector3.forward,
                    0.001f);
                TestAssert.VectorNear(
                    target * Vector3.up,
                    worldSource * Vector3.up,
                    0.001f);
                TestAssert.VectorNear(
                    targetPosition,
                    root.Position + root.Rotation * sourcePosition,
                    0.001f);
            }
        }

        private static void RelativePredictionDoesNotAllocate()
        {
            RelativeTransformHistory history = new RelativeTransformHistory();
            history.ObserveCommit(new PlacementTransform(Vector3.zero, Quaternion.identity));
            history.ObserveCommit(new PlacementTransform(Vector3.right, Quaternion.identity));
            float checksum = 0f;
            for (int index = 0; index < 1000; index++)
            {
                history.TryPredictNext(out PlacementTransform warmup);
                checksum += warmup.Position.x;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100000; index++)
            {
                history.TryPredictNext(out PlacementTransform predicted);
                checksum += predicted.Position.x;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(checksum);
            TestAssert.Equal(0L, allocated, "Relative prediction allocated " + allocated + " bytes.");
        }

        private static PoseController NewController(Quaternion baseRotation)
        {
            PlacementSession session = new PlacementSession();
            PoseController pose = new PoseController(session);
            pose.ObserveSelection(1, baseRotation, 0);
            return pose;
        }
    }
}
