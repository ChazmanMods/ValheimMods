using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class PoseControllerTests
    {
        internal static void Register()
        {
            TestRunner.Run("Compound commands compose in world yaw and local pitch and roll", ComposeOrder);
            TestRunner.Run("Pitch follows a yawed piece's local X axis", LocalPitchAfterYaw);
            TestRunner.Run("Roll follows the piece-local Z axis after yaw and pitch", LocalRollAfterCompound);
            TestRunner.Run("Yaw stays world-up after pitch and roll", WorldYawAfterCompound);
            TestRunner.Run("World yaw and local pitch and roll persist after exact match", LocalAxesAfterMatch);
            TestRunner.Run("Piece-local compound order produces the expected physical basis", LocalCompoundBasis);
            TestRunner.Run("Local-axis rotation leaves the placement pivot unchanged", RotationPreservesPivot);
            TestRunner.Run("Rotation after translation cannot orbit the placement position", RotationAfterTranslationPreservesPosition);
            TestRunner.Run("Untouched and translation-only sessions follow exact vanilla rotation", VanillaFirstUntilRunicRotation);
            TestRunner.Run("Explicit Runic rotation locks the world pose against candidate drift", ExplicitRotationLocksWorldPose);
            TestRunner.Run("Flat rotation preserves tilted beam elevation and pivot", FlatRotationAfterTilt);
            TestRunner.Run("Both rotation frames use their selected axes and toggle without moving", RotationFrames);
            TestRunner.Run("Matched beam bends in its existing plane in local mode", ArchBend);
            TestRunner.Run("Pose remains normalized after accumulated increments", NormalizesAccumulation);
            TestRunner.Run("Quaternion angle metric resolves hundredth-degree differences", QuaternionAnglePrecision);
            TestRunner.Run("Sway, heave, and surge use independent fixed-world axes", FixedWorldTranslation);
            TestRunner.Run("Every compound operation order preserves translation independence", CompoundSixDegreeIndependence);
            TestRunner.Run("Match copies the complete quaternion and preserves translation", ExactMatch);
            TestRunner.Run("Pitch match replaces only the Y-X-Z pitch component", PartialPitchMatch);
            TestRunner.Run("Roll match replaces only the Y-X-Z roll component", PartialRollMatch);
            TestRunner.Run("Yaw match replaces only the Y-X-Z yaw component", PartialYawMatch);
            TestRunner.Run("Sequential pitch and yaw matches compose independently", SequentialPartialMatches);
            TestRunner.Run("Partial matching preserves sub-display components", PartialMatchPreservesTinyComponents);
            TestRunner.Run("Partial matching is deterministic for normalized q and -q", PartialMatchQuaternionSignStability);
            TestRunner.Run("Partial matching preserves world position and offset", PartialMatchPreservesPosition);
            TestRunner.Run("Partial matching is finite and canonical at Y-X-Z poles", PartialMatchPoleSafety);
            TestRunner.Run("Invalid partial match fails without changing pose", InvalidPartialMatchIsAtomic);
            TestRunner.Run("Vanilla yaw continues from an absolute match", VanillaYawAfterMatch);
            TestRunner.Run("Vanilla yaw-index wrap produces one world-yaw step", VanillaYawWrap);
            TestRunner.Run("Reconciled vanilla yaw is not applied twice by the next full base pose", VanillaYawDoesNotDoubleApply);
            TestRunner.Run("Vanilla yaw preserves an absolute Runic pose across surface drift", VanillaYawPreservesAbsolutePose);
            TestRunner.Run("Multiple vanilla yaw steps preserve the absolute Runic pose", MultipleVanillaYawPreservesFixedFrame);
            TestRunner.Run("Base pose follows vanilla until an explicit rotation or match", BaseReconciliation);
            TestRunner.Run("Reset clears all pose state and match feedback", ResetClearsPose);
        }

        private static void ComposeOrder()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 45f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 30f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));

            Quaternion expected =
                QuaternionMath.AngleAxis(45f, 0f, 1f, 0f) *
                QuaternionMath.AngleAxis(30f, 1f, 0f, 0f) *
                QuaternionMath.AngleAxis(15f, 0f, 0f, 1f);
            TestAssert.QuaternionNear(expected, controller.Session.DesiredRotation, 0.001f);
            TestAssert.True(controller.Session.HasRunicRotation);
        }

        private static void LocalPitchAfterYaw()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 73f));
            Quaternion before = controller.Session.DesiredRotation;

            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 31f));

            Quaternion localDelta = QuaternionMath.NormalizeSafe(
                QuaternionMath.InverseSafe(before) * controller.Session.DesiredRotation);
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(31f, 1f, 0f, 0f),
                localDelta,
                0.001f);
        }

        private static void LocalRollAfterCompound()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 73f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 31f));
            Quaternion before = controller.Session.DesiredRotation;

            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, -19f));

            Quaternion localDelta = QuaternionMath.NormalizeSafe(
                QuaternionMath.InverseSafe(before) * controller.Session.DesiredRotation);
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(-19f, 0f, 0f, 1f),
                localDelta,
                0.001f);
        }

        private static void WorldYawAfterCompound()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 31f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, -19f));
            Quaternion before = controller.Session.DesiredRotation;

            controller.RotationReferenceFrame = PlacementReferenceFrame.World;
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 47f));

            Quaternion worldDelta = QuaternionMath.NormalizeSafe(
                controller.Session.DesiredRotation * QuaternionMath.InverseSafe(before));
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(47f, 0f, 1f, 0f),
                worldDelta,
                0.001f);
        }

        private static void FlatRotationAfterTilt()
        {
            foreach (float pitch in new[] { 1f, 22.5f, 89f, 90f, 135f, -22.5f })
            foreach (float yawStep in new[] { 1f, 22.5f, -22.5f })
            {
                PoseController controller = NewController(Quaternion.identity);
                controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, pitch));
                controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
                Vector3 pivot = controller.ComposePosition(new Vector3(4f, 5f, 6f));
                Quaternion before = controller.Session.DesiredRotation;
                for (int step = 0; step < 16; step++)
                {
                    controller.RotationReferenceFrame = PlacementReferenceFrame.World;
                    controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, yawStep));
                    Quaternion after = controller.Session.DesiredRotation;
                    // A horizontal turn preserves the height of every basis vector, including
                    // beam endpoints, even at vertical pitch and after repeated wheel input.
                    foreach (Vector3 basis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                        TestAssert.Near((before * basis).y, (after * basis).y, 0.00001f);
                    TestAssert.VectorNear(pivot, controller.ComposePosition(new Vector3(4f, 5f, 6f)), 0f);
                }
            }
        }

        private static void RotationFrames()
        {
            foreach (PlacementReferenceFrame frame in new[] { PlacementReferenceFrame.Local, PlacementReferenceFrame.World })
            foreach (RotationAxis axis in new[] { RotationAxis.Pitch, RotationAxis.Yaw, RotationAxis.Roll })
            {
                Quaternion matched = ComposeYxz(67f, 31f, -23f);
                PoseController controller = NewController(Quaternion.identity);
                controller.MatchOrientation(matched, 0f);
                controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, 0.5f));
                Vector3 pivot = controller.ComposePosition(new Vector3(2f, 4f, 6f));
                controller.RotationReferenceFrame = frame;
                TestAssert.QuaternionNear(matched, controller.Session.DesiredRotation, 0.001f);
                Vector3 basis = axis == RotationAxis.Pitch ? Vector3.right : axis == RotationAxis.Yaw ? Vector3.up : Vector3.forward;
                Vector3 fixedAxis = frame == PlacementReferenceFrame.Local ? matched * basis : basis;
                controller.Apply(SemanticCommand.Rotation(axis, 22.5f));
                Quaternion relative = controller.Session.DesiredRotation * QuaternionMath.InverseSafe(matched);
                TestAssert.VectorNear(fixedAxis, relative * fixedAxis, 0.00001f);
                TestAssert.QuaternionNear(QuaternionMath.AngleAxis(22.5f, fixedAxis.x, fixedAxis.y, fixedAxis.z), relative, 0.001f);
                TestAssert.VectorNear(pivot, controller.ComposePosition(new Vector3(2f, 4f, 6f)), 0f);
                controller.Apply(SemanticCommand.Reset);
                TestAssert.Equal(frame, controller.RotationReferenceFrame);
                controller.ObserveSelection(678, Quaternion.identity, 0);
                TestAssert.Equal(frame, controller.RotationReferenceFrame);
            }
        }

        private static void ArchBend()
        {
            PoseController controller = NewController(Quaternion.identity);
            Quaternion beam = ComposeYxz(67f, 31f, -23f);
            controller.MatchOrientation(beam, 0f);
            controller.RotationReferenceFrame = PlacementReferenceFrame.Local;
            Vector3 planeNormal = beam * Vector3.forward;
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 5f));
            Quaternion bent = controller.Session.DesiredRotation;
            TestAssert.VectorNear(planeNormal, bent * Vector3.forward, 0.00001f);
            TestAssert.QuaternionNear(beam * QuaternionMath.AngleAxis(5f, 0f, 0f, 1f), bent, 0.001f);
        }

        private static void LocalAxesAfterMatch()
        {
            PoseController controller = NewController(Quaternion.identity);
            Quaternion matched = QuaternionMath.AngleAxis(117f, 0.3f, -0.8f, 0.5f);
            TestAssert.True(controller.MatchOrientation(matched, 0f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
            Vector3 candidate = new Vector3(4f, 5f, 6f);
            Vector3 fixedPosition = candidate + new Vector3(0.25f, 0f, 0f);

            AssertNextLocalRotation(controller, RotationAxis.Pitch, 13f, 1f, 0f, 0f);
            TestAssert.VectorNear(fixedPosition, controller.ComposePosition(candidate), 0f);
            controller.RotationReferenceFrame = PlacementReferenceFrame.World;
            Quaternion beforeYaw = controller.Session.DesiredRotation;
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, -29f));
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(-29f, 0f, 1f, 0f) * beforeYaw,
                controller.Session.DesiredRotation, 0.001f);
            TestAssert.VectorNear(fixedPosition, controller.ComposePosition(candidate), 0f);
            controller.RotationReferenceFrame = PlacementReferenceFrame.Local;
            AssertNextLocalRotation(controller, RotationAxis.Roll, 47f, 0f, 0f, 1f);
            TestAssert.VectorNear(fixedPosition, controller.ComposePosition(candidate), 0f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
        }

        private static void AssertNextLocalRotation(
            PoseController controller,
            RotationAxis axis,
            float degrees,
            float axisX,
            float axisY,
            float axisZ)
        {
            Quaternion before = controller.Session.DesiredRotation;
            controller.Apply(SemanticCommand.Rotation(axis, degrees));
            Quaternion relativeLocalRotation = QuaternionMath.NormalizeSafe(
                QuaternionMath.InverseSafe(before) * controller.Session.DesiredRotation);
            TestAssert.QuaternionNear(
                QuaternionMath.AngleAxis(degrees, axisX, axisY, axisZ),
                relativeLocalRotation,
                0.001f);
        }

        private static void LocalCompoundBasis()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 90f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 90f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 90f));

            Quaternion rotation = controller.Session.DesiredRotation;
            TestAssert.VectorNear(Vector3.down, rotation * Vector3.forward, 0.00001f);
            TestAssert.VectorNear(Vector3.right, rotation * Vector3.right, 0.00001f);
            TestAssert.VectorNear(Vector3.forward, rotation * Vector3.up, 0.00001f);
        }

        private static void RotationPreservesPivot()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 73f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 31f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, -19f));
            Vector3 pivot = new Vector3(12.5f, -4f, 81.25f);

            TestAssert.VectorNear(
                pivot,
                controller.ComposePosition(pivot, controller.Session.DesiredRotation),
                0f);
        }

        private static void RotationAfterTranslationPreservesPosition()
        {
            PoseController controller = NewController(
                QuaternionMath.AngleAxis(33f, 0f, 1f, 0f));
            Vector3 candidate = new Vector3(12.5f, -4f, 81.25f);
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.5f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, -0.25f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 1.25f));
            Vector3 translatedPosition = candidate + new Vector3(0.5f, -0.25f, 1.25f);

            TestAssert.VectorNear(translatedPosition, controller.ComposePosition(candidate), 0f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 73f));
            TestAssert.VectorNear(translatedPosition, controller.ComposePosition(candidate), 0f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 31f));
            TestAssert.VectorNear(translatedPosition, controller.ComposePosition(candidate), 0f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, -19f));
            TestAssert.VectorNear(translatedPosition, controller.ComposePosition(candidate), 0f);
            TestAssert.VectorNear(
                translatedPosition,
                controller.ComposePosition(candidate, controller.Session.DesiredRotation),
                0f);
        }

        private static void VanillaFirstUntilRunicRotation()
        {
            PoseController controller = NewController(Quaternion.identity);
            Quaternion firstVanilla =
                QuaternionMath.AngleAxis(40f, 0f, 1f, 0f) *
                QuaternionMath.AngleAxis(12f, 1f, 0f, 0f);
            TestAssert.QuaternionNear(
                firstVanilla,
                controller.ComposeEffectiveRotation(firstVanilla),
                0.0001f);
            TestAssert.False(controller.Session.HasRunicRotation);

            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 0.25f));
            Quaternion secondVanilla =
                QuaternionMath.AngleAxis(-65f, 0f, 1f, 0f) *
                QuaternionMath.AngleAxis(-8f, 1f, 0f, 0f);
            Quaternion effective = controller.ComposeEffectiveRotation(secondVanilla);
            TestAssert.QuaternionNear(secondVanilla, effective, 0.0001f);
            TestAssert.VectorNear(
                new Vector3(0f, 0f, 0.25f),
                controller.ComposePosition(Vector3.zero, effective),
                0.00001f);
            TestAssert.False(controller.Session.HasRunicRotation);

            controller.ReconcileVanillaYaw(1, 22.5f, QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f));
            TestAssert.False(controller.Session.HasRunicRotation);
        }

        private static void ExplicitRotationLocksWorldPose()
        {
            Quaternion initialSurface = QuaternionMath.AngleAxis(18f, 1f, 0f, 0f);
            PoseController controller = NewController(Quaternion.identity);
            controller.ComposeEffectiveRotation(initialSurface);

            Quaternion localRoll = QuaternionMath.AngleAxis(15f, 0f, 0f, 1f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            Quaternion locked = initialSurface * localRoll;
            TestAssert.True(controller.Session.HasAbsoluteRotation);

            Quaternion unrelatedSurface =
                QuaternionMath.AngleAxis(37f, 1f, 0f, 0f) *
                QuaternionMath.AngleAxis(-26f, 0f, 1f, 0f);
            TestAssert.QuaternionNear(
                locked,
                controller.ComposeEffectiveRotation(unrelatedSurface),
                0.001f);
            TestAssert.QuaternionNear(locked, controller.Session.DesiredRotation, 0.001f);
        }

        private static void NormalizesAccumulation()
        {
            PoseController controller = NewController(Quaternion.identity);
            for (int index = 0; index < 10000; index++)
            {
                controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 0.1f));
                controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, -0.1f));
                controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 0.1f));
            }

            Quaternion q = controller.Session.DesiredRotation;
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            TestAssert.Near(1f, norm, 0.00001f);
        }

        private static void QuaternionAnglePrecision()
        {
            Quaternion difference = QuaternionMath.AngleAxis(0.01f, 0f, 1f, 0f);
            TestAssert.Near(0.01f, QuaternionMath.AngleDegrees(Quaternion.identity, difference), 0.0001f);
            TestAssert.False(QuaternionMath.AreEquivalent(Quaternion.identity, difference));
        }

        private static void FixedWorldTranslation()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 45f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 30f));
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, -0.5f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 1f));

            Vector3 expected = new Vector3(0.25f, -0.5f, 1f);
            Vector3 actual = controller.ComposePosition(Vector3.zero);
            TestAssert.VectorNear(expected, actual, 0.00001f);
            TestAssert.VectorNear(expected, controller.Session.WorldOffset, 0f);
        }

        private static void CompoundSixDegreeIndependence()
        {
            PoseController controller = NewController(
                QuaternionMath.AngleAxis(-27f, 0f, 1f, 0f));
            Vector3 candidate = new Vector3(-8f, 12f, 3.5f);
            Vector3 expectedOffset = Vector3.zero;

            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 17f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, 0.125f));
            expectedOffset += new Vector3(0f, 0.125f, 0f);
            TestAssert.VectorNear(candidate + expectedOffset, controller.ComposePosition(candidate), 0.000001f);

            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, -41f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, -0.25f));
            expectedOffset += new Vector3(-0.25f, 0f, 0f);
            TestAssert.VectorNear(candidate + expectedOffset, controller.ComposePosition(candidate), 0.000001f);

            controller.Apply(SemanticCommand.Rotation(RotationAxis.Yaw, 83f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 0.75f));
            expectedOffset += new Vector3(0f, 0f, 0.75f);
            TestAssert.VectorNear(candidate + expectedOffset, controller.ComposePosition(candidate), 0.000001f);

            Quaternion target = QuaternionMath.AngleAxis(119f, 0.4f, -0.2f, 0.7f);
            controller.MatchOrientation(target, 0f);
            TestAssert.VectorNear(candidate + expectedOffset, controller.ComposePosition(candidate), 0.000001f);
            TestAssert.VectorNear(expectedOffset, controller.Session.WorldOffset, 0.000001f);
        }

        private static void ExactMatch()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 0.25f));
            Quaternion target = QuaternionMath.AngleAxis(72.25f, 0.3f, 0.8f, -0.2f);
            Quaternion scaled = new Quaternion(
                target.x * 3f,
                target.y * 3f,
                target.z * 3f,
                target.w * 3f);

            TestAssert.True(controller.MatchOrientation(scaled, 10f));
            TestAssert.QuaternionNear(target, controller.Session.DesiredRotation, 0.001f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
            TestAssert.VectorNear(new Vector3(0f, 0f, 0.25f), controller.Session.WorldOffset, 0f);
            TestAssert.VectorNear(
                new Vector3(0f, 0f, 0.25f),
                controller.ComposePosition(Vector3.zero, target),
                0f);
            TestAssert.Near(11.25f, controller.Session.MatchFeedbackUntil, 0.00001f);
        }

        private static void PartialPitchMatch()
        {
            Quaternion current = ComposeYxz(48f, -17f, 26f);
            Quaternion target = ComposeYxz(-91f, 37.5f, -42f);
            PoseController controller = NewController(current);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Pitch,
                3f));

            TestAssert.QuaternionNear(
                ComposeYxz(48f, 37.5f, 26f),
                controller.Session.DesiredRotation,
                0.002f);
            TestAssert.True(controller.Session.HasRunicRotation);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
            TestAssert.Near(4.25f, controller.Session.MatchFeedbackUntil, 0.00001f);
        }

        private static void PartialRollMatch()
        {
            Quaternion current = ComposeYxz(-63f, 24f, 11f);
            Quaternion target = ComposeYxz(81f, -39f, -32.25f);
            PoseController controller = NewController(current);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Roll,
                0f));

            TestAssert.QuaternionNear(
                ComposeYxz(-63f, 24f, -32.25f),
                controller.Session.DesiredRotation,
                0.002f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
        }

        private static void PartialYawMatch()
        {
            Quaternion current = ComposeYxz(15f, -47f, 28f);
            Quaternion target = ComposeYxz(133.75f, 19f, -8f);
            PoseController controller = NewController(current);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Yaw,
                0f));

            TestAssert.QuaternionNear(
                ComposeYxz(133.75f, -47f, 28f),
                controller.Session.DesiredRotation,
                0.002f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
        }

        private static void SequentialPartialMatches()
        {
            PoseController controller = NewController(ComposeYxz(12f, -21f, 34f));
            Quaternion target = ComposeYxz(-107f, 43f, -61f);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Pitch,
                1f));
            TestAssert.QuaternionNear(
                ComposeYxz(12f, 43f, 34f),
                controller.Session.DesiredRotation,
                0.002f);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Yaw,
                2f));
            TestAssert.QuaternionNear(
                ComposeYxz(-107f, 43f, 34f),
                controller.Session.DesiredRotation,
                0.002f);
            TestAssert.Near(3.25f, controller.Session.MatchFeedbackUntil, 0.00001f);
        }

        private static void PartialMatchPreservesTinyComponents()
        {
            const float tinyYaw = 0.021f;
            const float tinyRoll = -0.041f;
            PoseController controller = NewController(ComposeYxz(tinyYaw, -12f, tinyRoll));
            Quaternion target = ComposeYxz(70f, 35f, 80f);

            TestAssert.True(controller.MatchOrientationComponent(
                target,
                RotationAxis.Pitch,
                0f));
            TestAssert.QuaternionNear(
                ComposeYxz(tinyYaw, 35f, tinyRoll),
                controller.Session.DesiredRotation,
                0.001f);
        }

        private static void PartialMatchQuaternionSignStability()
        {
            Quaternion current = ComposeYxz(22f, 89.999f, -17f);
            Quaternion target = ComposeYxz(-123f, 31f, 44f);
            Quaternion negatedScaledTarget = new Quaternion(
                -target.x * 4f,
                -target.y * 4f,
                -target.z * 4f,
                -target.w * 4f);
            PoseController positive = NewController(current);
            PoseController negative = NewController(current);

            TestAssert.True(positive.MatchOrientationComponent(
                target,
                RotationAxis.Yaw,
                0f));
            TestAssert.True(negative.MatchOrientationComponent(
                negatedScaledTarget,
                RotationAxis.Yaw,
                0f));
            TestAssert.QuaternionNear(
                positive.Session.DesiredRotation,
                negative.Session.DesiredRotation,
                0.0001f);

            Quaternion actual = positive.Session.DesiredRotation;
            float normSquared = actual.x * actual.x + actual.y * actual.y +
                                actual.z * actual.z + actual.w * actual.w;
            TestAssert.Near(1f, normSquared, 0.00001f);
        }

        private static void PartialMatchPreservesPosition()
        {
            PoseController controller = NewController(ComposeYxz(14f, -23f, 31f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.5f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, -0.25f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSurge, 1.25f));
            Vector3 candidate = new Vector3(8f, -4f, 12f);
            Vector3 offset = new Vector3(0.5f, -0.25f, 1.25f);
            Vector3 expectedPosition = candidate + offset;
            Quaternion target = ComposeYxz(-80f, 42f, -16f);

            TestAssert.True(controller.MatchOrientationComponent(target, RotationAxis.Pitch, 0f));
            TestAssert.VectorNear(offset, controller.Session.WorldOffset, 0f);
            TestAssert.VectorNear(expectedPosition, controller.ComposePosition(candidate), 0f);
            TestAssert.True(controller.MatchOrientationComponent(target, RotationAxis.Roll, 0f));
            TestAssert.VectorNear(offset, controller.Session.WorldOffset, 0f);
            TestAssert.VectorNear(expectedPosition, controller.ComposePosition(candidate), 0f);
            TestAssert.True(controller.MatchOrientationComponent(target, RotationAxis.Yaw, 0f));
            TestAssert.VectorNear(offset, controller.Session.WorldOffset, 0f);
            TestAssert.VectorNear(expectedPosition, controller.ComposePosition(candidate), 0f);
        }

        private static void PartialMatchPoleSafety()
        {
            Quaternion currentAtPositivePole = ComposeYxz(37f, 90f, 19f);
            Quaternion targetAtNegativePole = ComposeYxz(-52f, -90f, -11f);
            PoseController first = NewController(currentAtPositivePole);
            PoseController second = NewController(currentAtPositivePole);
            Quaternion negatedTarget = new Quaternion(
                -targetAtNegativePole.x,
                -targetAtNegativePole.y,
                -targetAtNegativePole.z,
                -targetAtNegativePole.w);

            TestAssert.True(first.MatchOrientationComponent(
                targetAtNegativePole,
                RotationAxis.Yaw,
                0f));
            TestAssert.True(second.MatchOrientationComponent(
                negatedTarget,
                RotationAxis.Yaw,
                0f));
            TestAssert.QuaternionNear(
                first.Session.DesiredRotation,
                second.Session.DesiredRotation,
                0.0001f);

            Quaternion actual = first.Session.DesiredRotation;
            TestAssert.True(
                QuaternionMath.IsFinite(actual.x) && QuaternionMath.IsFinite(actual.y) &&
                QuaternionMath.IsFinite(actual.z) && QuaternionMath.IsFinite(actual.w));
            float normSquared = actual.x * actual.x + actual.y * actual.y +
                                actual.z * actual.z + actual.w * actual.w;
            TestAssert.Near(1f, normSquared, 0.00001f);

            PoseController pitch = NewController(ComposeYxz(28f, 12f, -7f));
            TestAssert.True(pitch.MatchOrientationComponent(
                targetAtNegativePole,
                RotationAxis.Pitch,
                0f));
            TestAssert.QuaternionNear(
                ComposeYxz(28f, -90f, -7f),
                pitch.Session.DesiredRotation,
                0.002f);
        }

        private static void InvalidPartialMatchIsAtomic()
        {
            PoseController controller = NewController(ComposeYxz(10f, 20f, 30f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveSway, 0.25f));
            Quaternion before = controller.Session.DesiredRotation;
            Vector3 offset = controller.Session.WorldOffset;

            TestAssert.False(controller.MatchOrientationComponent(
                new Quaternion(float.NaN, 0f, 0f, 1f),
                RotationAxis.Pitch,
                10f));
            TestAssert.QuaternionNear(before, controller.Session.DesiredRotation, 0f);
            TestAssert.VectorNear(offset, controller.Session.WorldOffset, 0f);
            TestAssert.False(controller.Session.HasAbsoluteRotation);
            TestAssert.Near(0f, controller.Session.MatchFeedbackUntil, 0f);
        }

        private static void VanillaYawAfterMatch()
        {
            PoseController controller = NewController(Quaternion.identity, 4);
            Quaternion matched =
                QuaternionMath.AngleAxis(31f, 0f, 1f, 0f) *
                QuaternionMath.AngleAxis(27f, 1f, 0f, 0f);
            controller.MatchOrientation(matched, 0f);

            Quaternion newBase = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            TestAssert.True(controller.ReconcileVanillaYaw(5, 22.5f, newBase));
            Quaternion expected = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f) * matched;
            TestAssert.QuaternionNear(expected, controller.Session.DesiredRotation, 0.001f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
        }

        private static void VanillaYawWrap()
        {
            PoseController controller = NewController(Quaternion.identity, 15);
            controller.MatchOrientation(Quaternion.identity, 0f);
            Quaternion newBase = QuaternionMath.AngleAxis(0f, 0f, 1f, 0f);

            controller.ReconcileVanillaYaw(0, 22.5f, newBase);
            Quaternion expected = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            TestAssert.QuaternionNear(expected, controller.Session.DesiredRotation, 0.001f);
        }

        private static void VanillaYawDoesNotDoubleApply()
        {
            Quaternion surface = QuaternionMath.AngleAxis(18f, 1f, 0f, 0f);
            PoseController controller = NewController(Quaternion.identity, 0);
            Quaternion oldEffective = controller.ComposeEffectiveRotation(surface);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            oldEffective = controller.Session.DesiredRotation;

            Quaternion yaw = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            controller.ReconcileVanillaYaw(1, 22.5f, yaw);
            Quaternion expected = yaw * oldEffective;
            Quaternion fullNextBase = yaw * surface;
            Quaternion actual = controller.ComposeEffectiveRotation(fullNextBase);

            TestAssert.QuaternionNear(expected, actual, 0.001f);
            TestAssert.QuaternionNear(fullNextBase, controller.Session.BaseRotation, 0.001f);
        }

        private static void VanillaYawPreservesAbsolutePose()
        {
            Quaternion oldSurface = QuaternionMath.AngleAxis(18f, 1f, 0f, 0f);
            PoseController controller = NewController(Quaternion.identity, 0);
            controller.ComposeEffectiveRotation(oldSurface);
            Quaternion localRoll = QuaternionMath.AngleAxis(15f, 0f, 0f, 1f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));

            Quaternion yaw = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            controller.ReconcileVanillaYaw(1, 22.5f, yaw);
            Quaternion newSurface = QuaternionMath.AngleAxis(27f, 1f, 0f, 0f);
            Quaternion fullNextBase = yaw * newSurface;
            Quaternion actual = controller.ComposeEffectiveRotation(fullNextBase);

            TestAssert.QuaternionNear(yaw * oldSurface * localRoll, actual, 0.001f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);
        }

        private static void MultipleVanillaYawPreservesFixedFrame()
        {
            Quaternion oldSurface = QuaternionMath.AngleAxis(18f, 1f, 0f, 0f);
            PoseController controller = NewController(Quaternion.identity, 0);
            controller.ComposeEffectiveRotation(oldSurface);
            Quaternion localRoll = QuaternionMath.AngleAxis(15f, 0f, 0f, 1f);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));

            Quaternion yawStep = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            Quaternion yawTwoSteps = QuaternionMath.AngleAxis(45f, 0f, 1f, 0f);
            controller.ReconcileVanillaYawDelta(22.5f, 1, yawStep * oldSurface);
            controller.ReconcileVanillaYawDelta(22.5f, 2, yawTwoSteps * oldSurface);

            Quaternion newSurface = QuaternionMath.AngleAxis(27f, 1f, 0f, 0f);
            Quaternion actual = controller.ComposeEffectiveRotation(yawTwoSteps * newSurface);
            TestAssert.QuaternionNear(
                yawTwoSteps * oldSurface * localRoll,
                actual,
                0.001f);
        }

        private static void BaseReconciliation()
        {
            PoseController controller = NewController(Quaternion.identity);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Pitch, 30f));
            Quaternion newBase = QuaternionMath.AngleAxis(45f, 0f, 1f, 0f);
            Quaternion effective = controller.ComposeEffectiveRotation(newBase);
            Quaternion expected = QuaternionMath.AngleAxis(30f, 1f, 0f, 0f);
            TestAssert.QuaternionNear(expected, effective, 0.001f);
            TestAssert.True(controller.Session.HasAbsoluteRotation);

            Quaternion matched = QuaternionMath.AngleAxis(-37f, 0.2f, 1f, 0.1f);
            controller.MatchOrientation(matched, 0f);
            Quaternion unrelatedBase = QuaternionMath.AngleAxis(120f, 0f, 1f, 0f);
            effective = controller.ComposeEffectiveRotation(unrelatedBase);
            TestAssert.QuaternionNear(matched, effective, 0.001f);
        }

        private static void ResetClearsPose()
        {
            Quaternion baseRotation = QuaternionMath.AngleAxis(12f, 0f, 1f, 0f);
            PoseController controller = NewController(baseRotation, 3);
            controller.Apply(SemanticCommand.Rotation(RotationAxis.Roll, 15f));
            controller.Apply(SemanticCommand.Translation(SemanticCommandKind.MoveHeave, 0.25f));
            controller.MatchOrientation(QuaternionMath.AngleAxis(80f, 1f, 0f, 0f), 4f);
            controller.UpdateWheelPreview(RotationAxis.Roll, 1f, true);

            controller.Apply(SemanticCommand.Reset);
            TestAssert.QuaternionNear(baseRotation, controller.Session.DesiredRotation, 0.001f);
            TestAssert.VectorNear(Vector3.zero, controller.Session.WorldOffset, 0f);
            TestAssert.False(controller.Session.HasAbsoluteRotation);
            TestAssert.False(controller.Session.HasRunicRotation);
            TestAssert.Equal(0, controller.Session.LastVanillaYawIndex);
            TestAssert.Equal(RotationAxis.None, controller.Session.ActiveAxis);
            TestAssert.Near(0f, controller.Session.MatchFeedbackUntil, 0f);
        }

        private static Quaternion ComposeYxz(float yaw, float pitch, float roll) =>
            QuaternionMath.AngleAxis(yaw, 0f, 1f, 0f) *
            QuaternionMath.AngleAxis(pitch, 1f, 0f, 0f) *
            QuaternionMath.AngleAxis(roll, 0f, 0f, 1f);

        private static PoseController NewController(Quaternion baseRotation, int yawIndex = 0)
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            controller.ObserveSelection(12345, baseRotation, yawIndex);
            return controller;
        }
    }
}
