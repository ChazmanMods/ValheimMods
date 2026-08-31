using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class PlacementSessionTests
    {
        internal static void Register()
        {
            TestRunner.Run("Same-prefab observation preserves pose", SamePrefabPersists);
            TestRunner.Run("Prefab switch resets pose and advances generation", PrefabSwitchResets);
            TestRunner.Run("Same-prefab ghost generation advances while preserving deliberate pose", SamePrefabGhostGeneration);
            TestRunner.Run("Untouched same-prefab ghost generation adopts new vanilla rotation", UntouchedGhostAdoptsVanilla);
            TestRunner.Run("Placement exit resets state exactly once", ExitResets);
            TestRunner.Run("Exit and re-enter with the same prefab starts clean", ReenterSamePrefabResets);
            TestRunner.Run("Match feedback expires at 1.25 seconds", MatchFeedbackExpires);
        }

        private static void SamePrefabPersists()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
            Quaternion desired = session.DesiredRotation;
            uint generation = session.Generation;

            TestAssert.False(controller.ObserveSelection(
                101,
                QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f),
                1));
            TestAssert.Equal(generation, session.Generation);
            TestAssert.QuaternionNear(desired, session.DesiredRotation, 0.001f);
            TestAssert.VectorNear(new Vector3(0.25f, 0f, 0f), session.WorldOffset, 0f);
        }

        private static void PrefabSwitchResets()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 0.25f));
            uint generation = session.Generation;
            Quaternion nextBase = QuaternionMath.AngleAxis(45f, 0f, 1f, 0f);

            TestAssert.True(controller.ObserveSelection(202, nextBase, 2));
            TestAssert.Equal(generation + 1u, session.Generation);
            TestAssert.Equal(202, session.SelectedPrefabId);
            TestAssert.QuaternionNear(nextBase, session.DesiredRotation, 0.001f);
            TestAssert.VectorNear(Vector3.zero, session.WorldOffset, 0f);
            TestAssert.False(session.HasAbsoluteRotation);
            TestAssert.Equal(2, session.LastVanillaYawIndex);
        }

        private static void ExitResets()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            uint activeGeneration = session.Generation;

            TestAssert.True(controller.ExitPlacement());
            TestAssert.False(session.IsActive);
            TestAssert.Equal(activeGeneration + 1u, session.Generation);
            TestAssert.False(controller.ExitPlacement());
            TestAssert.Equal(activeGeneration + 1u, session.Generation);
            TestAssert.QuaternionNear(Quaternion.identity, session.DesiredRotation, 0f);
            TestAssert.VectorNear(Vector3.zero, session.WorldOffset, 0f);
        }

        private static void SamePrefabGhostGeneration()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
            Quaternion desired = session.DesiredRotation;
            uint generation = session.Generation;

            Quaternion newVanilla = QuaternionMath.AngleAxis(90f, 0f, 1f, 0f);
            TestAssert.False(controller.BeginGhostGeneration(101, newVanilla, 4));
            TestAssert.Equal(generation + 1u, session.Generation);
            TestAssert.QuaternionNear(desired, session.DesiredRotation, 0.0001f);
            TestAssert.VectorNear(new Vector3(0.25f, 0f, 0f), session.WorldOffset, 0f);
            TestAssert.True(session.HasRunicRotation);
            TestAssert.Equal(4, session.LastVanillaYawIndex);
        }

        private static void UntouchedGhostAdoptsVanilla()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 0.25f));
            Quaternion newVanilla = QuaternionMath.AngleAxis(135f, 0f, 1f, 0f);

            controller.BeginGhostGeneration(101, newVanilla, 6);
            TestAssert.QuaternionNear(newVanilla, session.DesiredRotation, 0.0001f);
            TestAssert.False(session.HasRunicRotation);
            TestAssert.VectorNear(new Vector3(0f, 0f, 0.25f), session.WorldOffset, 0f);
        }

        private static void ReenterSamePrefabResets()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, 0.25f));
            controller.ExitPlacement();

            Quaternion newBase = QuaternionMath.AngleAxis(45f, 0f, 1f, 0f);
            controller.ObserveSelection(101, newBase, 2);
            TestAssert.QuaternionNear(newBase, session.DesiredRotation, 0.0001f);
            TestAssert.VectorNear(Vector3.zero, session.WorldOffset, 0f);
            TestAssert.False(session.HasRunicRotation);
        }

        private static void MatchFeedbackExpires()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(101, Quaternion.identity, 0);
            controller.MatchOrientation(Quaternion.identity, 5f);

            TestAssert.True(session.IsMatchFeedbackActive(6.249f));
            TestAssert.False(session.IsMatchFeedbackActive(6.25f));
        }
    }
}
