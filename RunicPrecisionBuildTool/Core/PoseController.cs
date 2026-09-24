using System;
using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Owns all quaternion/vector manipulation through the unchanged placement pivot.
    /// Rotation uses either fixed world axes or the piece's current local axes.
    /// </summary>
    internal sealed class PoseController
    {
        internal const float MatchFeedbackSeconds = 1.25f;

        private const float CommandEpsilon = 0.000001f;
        private readonly PlacementSession _session;
        private bool _hasPendingVanillaBase;
        private Quaternion _baseBeforePendingYaw = Quaternion.identity;
        private Quaternion _pendingWorldYaw = Quaternion.identity;

        internal PoseController(PlacementSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal PlacementSession Session => _session;

        internal PlacementReferenceFrame RotationReferenceFrame { get; set; } = PlacementReferenceFrame.Local;

        internal bool ObserveSelection(int selectedPrefabId, Quaternion vanillaBaseRotation, int vanillaYawIndex)
        {
            bool reset = _session.ObserveSelection(selectedPrefabId, vanillaBaseRotation, vanillaYawIndex);
            if (reset)
                ClearPendingVanillaBase();
            return reset;
        }

        internal bool BeginGhostGeneration(
            int selectedPrefabId,
            Quaternion vanillaBaseRotation,
            int vanillaYawIndex)
        {
            ClearPendingVanillaBase();
            return _session.BeginGhostGeneration(
                selectedPrefabId,
                vanillaBaseRotation,
                vanillaYawIndex);
        }

        internal bool ExitPlacement()
        {
            bool exited = _session.Exit();
            if (exited)
                ClearPendingVanillaBase();
            return exited;
        }

        /// <summary>
        /// Applies one already-arbitrated command. MatchOrientation requires a target quaternion
        /// and is therefore handled by the dedicated overload below.
        /// </summary>
        internal bool Apply(SemanticCommand command) =>
            Apply(command, PlacementReferenceFrame.World);

        internal bool Apply(SemanticCommand command, PlacementReferenceFrame referenceFrame)
        {
            if (!_session.IsActive || command.IsNone)
                return false;

            float delta = command.Delta;
            switch (command.Kind)
            {
                case SemanticCommandKind.RotateYaw:
                    return ApplyRotation(delta, RotationAxis.Yaw);
                case SemanticCommandKind.RotatePitch:
                    return ApplyRotation(delta, RotationAxis.Pitch);
                case SemanticCommandKind.RotateRoll:
                    return ApplyRotation(delta, RotationAxis.Roll);
                case SemanticCommandKind.MoveSway:
                    return ApplyTranslation(delta, command.Kind, referenceFrame);
                case SemanticCommandKind.MoveHeave:
                    return ApplyTranslation(delta, command.Kind, referenceFrame);
                case SemanticCommandKind.MoveSurge:
                    return ApplyTranslation(delta, command.Kind, referenceFrame);
                case SemanticCommandKind.ResetPitch:
                    return ResetRotationComponent(RotationAxis.Pitch);
                case SemanticCommandKind.ResetRoll:
                    return ResetRotationComponent(RotationAxis.Roll);
                case SemanticCommandKind.ResetYaw:
                    return ResetRotationComponent(RotationAxis.Yaw);
                case SemanticCommandKind.ResetSway:
                case SemanticCommandKind.ResetHeave:
                case SemanticCommandKind.ResetSurge:
                    return ResetTranslationComponent(command.Kind);
                case SemanticCommandKind.Reset:
                    Reset();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Copies a target world quaternion exactly after normalization. Position and local
        /// translation are untouched.
        /// </summary>
        internal bool MatchOrientation(Quaternion targetRotation, float currentTime)
        {
            if (!_session.IsActive || !QuaternionMath.TryNormalize(targetRotation, out Quaternion normalized))
                return false;

            CommitMatchedRotation(normalized, currentTime);
            return true;
        }

        /// <summary>
        /// Replaces exactly one component of the current placement's canonical Y-X-Z
        /// orientation with the same displayed component from the target. The other two
        /// components, the placement pivot, and the accumulated world translation are retained.
        /// A successful component match becomes an absolute world pose just like an exact match,
        /// so later component matches compose from the result instead of from Valheim's base.
        /// </summary>
        internal bool MatchOrientationComponent(
            Quaternion targetRotation,
            RotationAxis component,
            float currentTime)
        {
            if (!_session.IsActive || component == RotationAxis.None ||
                !OrientationAngles.TryDecomposeForMatch(
                    _session.DesiredRotation,
                    out OrientationAngles current) ||
                !OrientationAngles.TryDecomposeForMatch(
                    targetRotation,
                    out OrientationAngles target))
            {
                return false;
            }

            float pitch = current.Pitch;
            float roll = current.Roll;
            float yaw = current.Yaw;
            switch (component)
            {
                case RotationAxis.Pitch:
                    pitch = target.Pitch;
                    break;
                case RotationAxis.Roll:
                    roll = target.Roll;
                    break;
                case RotationAxis.Yaw:
                    yaw = target.Yaw;
                    break;
                default:
                    return false;
            }

            if (!OrientationAngles.TryRecompose(pitch, roll, yaw, out Quaternion matched))
                return false;

            CommitMatchedRotation(matched, currentTime);
            return true;
        }

        internal bool MatchPositionComponent(
            Vector3 targetPosition,
            SemanticCommandKind component,
            float currentTime)
        {
            if (!_session.IsActive || !VectorMath.IsFinite(targetPosition))
                return false;

            float value;
            switch (component)
            {
                case SemanticCommandKind.MatchPositionX:
                    value = targetPosition.x;
                    break;
                case SemanticCommandKind.MatchPositionY:
                    value = targetPosition.y;
                    break;
                case SemanticCommandKind.MatchPositionZ:
                    value = targetPosition.z;
                    break;
                default:
                    return false;
            }

            _session.LockPositionAxis(component, value);
            _session.ClearMatchFeedback();
            return true;
        }

        internal bool MatchPosition(Vector3 targetPosition, float currentTime)
        {
            if (!_session.IsActive || !_session.LockFullPosition(targetPosition))
                return false;
            _session.ClearMatchFeedback();
            return true;
        }

        internal bool MatchTransform(PlacementTransform target, float currentTime)
        {
            if (!_session.IsActive || !target.IsFinite)
                return false;
            ClearPendingVanillaBase();
            return _session.MarkMatchedTransform(
                target, currentTime + MatchFeedbackSeconds);
        }

        private void CommitMatchedRotation(Quaternion rotation, float currentTime)
        {
            ClearPendingVanillaBase();
            _session.MarkMatched(rotation, currentTime + MatchFeedbackSeconds);
        }

        internal void Reset()
        {
            if (!_session.IsActive)
                return;

            // The adapter resets Valheim's yaw index to zero in the same transaction. On the
            // following compose call, any resulting vanilla-base change is reconciled normally.
            _session.ResetPose(_session.BaseRotation, 0);
            ClearPendingVanillaBase();
        }

        internal void ResetWithoutChangingVanillaYaw()
        {
            if (!_session.IsActive) return;
            _session.ResetPose(_session.BaseRotation, _session.LastVanillaYawIndex);
            ClearPendingVanillaBase();
        }

        /// <summary>
        /// Reconciles an ordinary vanilla yaw-index change. The yaw delta is world-premultiplied,
        /// including after an absolute match, so vanilla wheel yaw continues from the sampled pose.
        /// </summary>
        internal bool ReconcileVanillaYaw(
            int newYawIndex,
            float vanillaYawStepDegrees,
            Quaternion newVanillaBaseRotation)
        {
            if (!_session.IsActive || newYawIndex == _session.LastVanillaYawIndex)
                return false;

            if (!QuaternionMath.IsFinite(vanillaYawStepDegrees) ||
                Math.Abs(vanillaYawStepDegrees) <= CommandEpsilon)
            {
                return false;
            }

            int deltaIndex = newYawIndex - _session.LastVanillaYawIndex;
            int indexCount = (int)Math.Round(360d / Math.Abs(vanillaYawStepDegrees));
            if (indexCount > 1)
            {
                deltaIndex %= indexCount;
                int half = indexCount / 2;
                if (deltaIndex > half)
                    deltaIndex -= indexCount;
                else if (deltaIndex < -half)
                    deltaIndex += indexCount;
            }

            return ReconcileVanillaYawDelta(
                deltaIndex * vanillaYawStepDegrees,
                newYawIndex,
                newVanillaBaseRotation);
        }

        /// <summary>
        /// Direct form used when the adapter already knows Valheim's signed yaw delta.
        /// </summary>
        internal bool ReconcileVanillaYawDelta(
            float deltaDegrees,
            int newYawIndex,
            Quaternion newVanillaBaseRotation)
        {
            if (!_session.IsActive || !QuaternionMath.IsFinite(deltaDegrees))
                return false;

            Quaternion normalizedBase = QuaternionMath.NormalizeSafe(newVanillaBaseRotation);
            if (!_session.HasRunicRotation)
            {
                ClearPendingVanillaBase();
                _session.SetBaseRotation(normalizedBase);
                _session.SetDesiredRotation(normalizedBase, false);
                _session.SetLastVanillaYawIndex(newYawIndex);
                return Math.Abs(deltaDegrees) > CommandEpsilon;
            }

            Quaternion yaw = Quaternion.identity;
            if (Math.Abs(deltaDegrees) > CommandEpsilon)
            {
                yaw = QuaternionMath.AngleAxis(deltaDegrees, 0f, 1f, 0f);
                Quaternion desired = QuaternionMath.NormalizeSafe(yaw * _session.DesiredRotation);
                _session.SetDesiredRotation(desired, _session.HasAbsoluteRotation);
            }

            if (_hasPendingVanillaBase)
            {
                _pendingWorldYaw = QuaternionMath.NormalizeSafe(yaw * _pendingWorldYaw);
            }
            else
            {
                _hasPendingVanillaBase = true;
                _baseBeforePendingYaw = _session.BaseRotation;
                _pendingWorldYaw = yaw;
            }

            _session.SetBaseRotation(normalizedBase);
            _session.SetLastVanillaYawIndex(newYawIndex);
            return Math.Abs(deltaDegrees) > CommandEpsilon;
        }

        /// <summary>
        /// Produces the world quaternion consumed by the placement pipeline. Untouched and
        /// translation-only sessions continue to follow Valheim's base pose. Once an explicit
        /// Runic rotation or match has selected a world quaternion, later candidate/surface base
        /// changes cannot rebase it; only another explicit rotation or vanilla yaw may change it.
        /// </summary>
        internal Quaternion ComposeEffectiveRotation(Quaternion vanillaBaseRotation)
        {
            Quaternion normalizedBase = QuaternionMath.NormalizeSafe(vanillaBaseRotation);
            if (!_session.IsActive)
                return normalizedBase;

            if (!_session.HasRunicRotation)
            {
                ClearPendingVanillaBase();
                _session.SetBaseRotation(normalizedBase);
                _session.SetDesiredRotation(normalizedBase, false);
                return normalizedBase;
            }

            if (_hasPendingVanillaBase)
            {
                if (!_session.HasAbsoluteRotation)
                {
                    // DesiredRotation already contains the later vanilla world-yaw command.
                    // Remove that yaw temporarily, recover the prior Runic rotation delta, then
                    // rebuild around Valheim's new base before restoring the yaw. This preserves
                    // temporal input order while Valheim changes the candidate surface pose.
                    Quaternion inversePendingYaw = QuaternionMath.InverseSafe(_pendingWorldYaw);
                    Quaternion newBaseBeforePendingYaw = QuaternionMath.NormalizeSafe(
                        inversePendingYaw * normalizedBase);
                    Quaternion desired = QuaternionMath.NormalizeSafe(
                        _session.DesiredRotation *
                        QuaternionMath.InverseSafe(_baseBeforePendingYaw) *
                        newBaseBeforePendingYaw);
                    _session.SetDesiredRotation(desired, false);
                }

                ClearPendingVanillaBase();
            }
            else if (!_session.HasAbsoluteRotation &&
                !QuaternionMath.AreEquivalent(_session.BaseRotation, normalizedBase))
            {
                // Preserve the accumulated Runic delta on top of Valheim's authoritative base
                // when a non-absolute candidate/surface pose changes.
                Quaternion runicWorldDelta = QuaternionMath.NormalizeSafe(
                    _session.DesiredRotation * QuaternionMath.InverseSafe(_session.BaseRotation));
                Quaternion desired = QuaternionMath.NormalizeSafe(runicWorldDelta * normalizedBase);
                _session.SetDesiredRotation(desired, false);
            }

            _session.SetBaseRotation(normalizedBase);
            return _session.DesiredRotation;
        }

        /// <summary>
        /// Applies the accumulated fixed-world displacement to Valheim's current candidate.
        /// Position composition is independent of the supplied orientation.
        /// </summary>
        internal Vector3 ComposePosition(Vector3 candidatePosition, Quaternion effectiveRotation) =>
            ComposePosition(candidatePosition);

        internal Vector3 ComposePosition(Vector3 candidatePosition)
        {
            Vector3 result = candidatePosition + _session.WorldOffset;
            Vector3 locked = _session.DesiredPosition;
            if (_session.LocksPositionX) result.x = locked.x;
            if (_session.LocksPositionY) result.y = locked.y;
            if (_session.LocksPositionZ) result.z = locked.z;
            return result;
        }

        internal void UpdateWheelPreview(RotationAxis axis, float step, bool isFine) =>
            _session.SetWheelPreview(axis, step, isFine);

        private void ClearPendingVanillaBase()
        {
            _hasPendingVanillaBase = false;
            _baseBeforePendingYaw = Quaternion.identity;
            _pendingWorldYaw = Quaternion.identity;
        }

        private bool ApplyRotation(float degrees, RotationAxis axis)
        {
            if (!QuaternionMath.IsFinite(degrees) || Math.Abs(degrees) <= CommandEpsilon)
                return false;

            Quaternion increment;
            switch (axis)
            {
                case RotationAxis.Yaw:
                    increment = QuaternionMath.AngleAxis(degrees, 0f, 1f, 0f);
                    break;
                case RotationAxis.Pitch:
                    increment = QuaternionMath.AngleAxis(degrees, 1f, 0f, 0f);
                    break;
                case RotationAxis.Roll:
                    increment = QuaternionMath.AngleAxis(degrees, 0f, 0f, 1f);
                    break;
                default:
                    return false;
            }

            // Pre-multiply for fixed world axes; post-multiply for the piece's own axes.
            // Selecting a frame only affects future increments, never the current pose.
            Quaternion desired = RotationReferenceFrame == PlacementReferenceFrame.World
                ? increment * _session.DesiredRotation
                : _session.DesiredRotation * increment;
            // The resulting orientation is still held as a concrete world pose so the preview
            // cannot drift when Valheim samples a different candidate surface on a later frame.
            ClearPendingVanillaBase();
            _session.SetDesiredRotation(
                QuaternionMath.NormalizeSafe(desired),
                true);
            _session.SetRunicRotation(true);
            return true;
        }

        private bool ApplyTranslation(
            float metres,
            SemanticCommandKind component,
            PlacementReferenceFrame referenceFrame)
        {
            if (!QuaternionMath.IsFinite(metres) || Math.Abs(metres) <= CommandEpsilon)
                return false;

            Vector3 delta;
            switch (component)
            {
                case SemanticCommandKind.MoveSway:
                    delta = new Vector3(metres, 0f, 0f);
                    break;
                case SemanticCommandKind.MoveHeave:
                    delta = new Vector3(0f, metres, 0f);
                    break;
                case SemanticCommandKind.MoveSurge:
                    delta = new Vector3(0f, 0f, metres);
                    break;
                default:
                    return false;
            }

            if (referenceFrame == PlacementReferenceFrame.Local)
                delta = _session.DesiredRotation * delta;
            if (!VectorMath.IsFinite(delta)) return false;
            _session.AddAxisOffset(component, delta);
            return true;
        }

        private bool ResetRotationComponent(RotationAxis component)
        {
            if (!_session.IsActive || component == RotationAxis.None ||
                !OrientationAngles.TryDecomposeForMatch(
                    _session.DesiredRotation, out OrientationAngles current) ||
                !OrientationAngles.TryDecomposeForMatch(
                    _session.BaseRotation, out OrientationAngles baseline))
            {
                return false;
            }

            float pitch = current.Pitch;
            float roll = current.Roll;
            float yaw = current.Yaw;
            switch (component)
            {
                case RotationAxis.Pitch:
                    pitch = baseline.Pitch;
                    break;
                case RotationAxis.Roll:
                    roll = baseline.Roll;
                    break;
                case RotationAxis.Yaw:
                    yaw = baseline.Yaw;
                    break;
                default:
                    return false;
            }

            if (!OrientationAngles.TryRecompose(pitch, roll, yaw, out Quaternion reset))
                return false;
            ClearPendingVanillaBase();
            _session.SetDesiredRotation(reset, _session.HasAbsoluteRotation);
            _session.SetRunicRotation(!QuaternionMath.AreEquivalent(reset, _session.BaseRotation));
            return true;
        }

        private bool ResetTranslationComponent(SemanticCommandKind resetKind)
        {
            if (!_session.IsActive) return false;
            SemanticCommandKind movement;
            switch (resetKind)
            {
                case SemanticCommandKind.ResetSway:
                    movement = SemanticCommandKind.MoveSway;
                    break;
                case SemanticCommandKind.ResetHeave:
                    movement = SemanticCommandKind.MoveHeave;
                    break;
                case SemanticCommandKind.ResetSurge:
                    movement = SemanticCommandKind.MoveSurge;
                    break;
                default:
                    return false;
            }
            _session.ResetTranslationAxis(movement);
            return true;
        }
    }

    /// <summary>
    /// Small allocation-free quaternion helpers shared by the state and presentation math.
    /// </summary>
    internal static class QuaternionMath
    {
        private const float MinimumNormSquared = 0.000000000001f;
        private const double EquivalentDotThreshold = 0.9999999999996192d; // 0.0001 degrees

        internal static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool TryNormalize(Quaternion value, out Quaternion normalized)
        {
            float normSquared = value.x * value.x + value.y * value.y +
                                value.z * value.z + value.w * value.w;
            if (!IsFinite(normSquared) || normSquared < MinimumNormSquared)
            {
                normalized = Quaternion.identity;
                return false;
            }

            float inverseNorm = (float)(1d / Math.Sqrt(normSquared));
            normalized = new Quaternion(
                value.x * inverseNorm,
                value.y * inverseNorm,
                value.z * inverseNorm,
                value.w * inverseNorm);
            return true;
        }

        internal static bool TryLookRotation(
            Vector3 forward,
            Vector3 up,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!TryNormalizeVector(forward, out Vector3 z) ||
                !TryNormalizeVector(Cross(up, z), out Vector3 x))
                return false;
            Vector3 y = Cross(z, x);
            if (!VectorMath.IsFinite(y)) return false;

            double m00 = x.x;
            double m01 = y.x;
            double m02 = z.x;
            double m10 = x.y;
            double m11 = y.y;
            double m12 = z.y;
            double m20 = x.z;
            double m21 = y.z;
            double m22 = z.z;
            double trace = m00 + m11 + m22;
            Quaternion candidate;
            if (trace > 0d)
            {
                double scale = Math.Sqrt(trace + 1d) * 2d;
                candidate = new Quaternion(
                    (float)((m21 - m12) / scale),
                    (float)((m02 - m20) / scale),
                    (float)((m10 - m01) / scale),
                    (float)(0.25d * scale));
            }
            else if (m00 > m11 && m00 > m22)
            {
                double scale = Math.Sqrt(1d + m00 - m11 - m22) * 2d;
                candidate = new Quaternion(
                    (float)(0.25d * scale),
                    (float)((m01 + m10) / scale),
                    (float)((m02 + m20) / scale),
                    (float)((m21 - m12) / scale));
            }
            else if (m11 > m22)
            {
                double scale = Math.Sqrt(1d + m11 - m00 - m22) * 2d;
                candidate = new Quaternion(
                    (float)((m01 + m10) / scale),
                    (float)(0.25d * scale),
                    (float)((m12 + m21) / scale),
                    (float)((m02 - m20) / scale));
            }
            else
            {
                double scale = Math.Sqrt(1d + m22 - m00 - m11) * 2d;
                candidate = new Quaternion(
                    (float)((m02 + m20) / scale),
                    (float)((m12 + m21) / scale),
                    (float)(0.25d * scale),
                    (float)((m10 - m01) / scale));
            }

            return TryNormalize(candidate, out rotation);
        }

        private static bool TryNormalizeVector(Vector3 value, out Vector3 normalized)
        {
            double lengthSquared = (double)value.x * value.x +
                                   (double)value.y * value.y +
                                   (double)value.z * value.z;
            if (double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) ||
                lengthSquared < 0.000000000001d)
            {
                normalized = Vector3.zero;
                return false;
            }
            float inverse = (float)(1d / Math.Sqrt(lengthSquared));
            normalized = new Vector3(value.x * inverse, value.y * inverse, value.z * inverse);
            return VectorMath.IsFinite(normalized);
        }

        private static Vector3 Cross(Vector3 left, Vector3 right) =>
            new Vector3(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);

        internal static Quaternion NormalizeSafe(Quaternion value) =>
            TryNormalize(value, out Quaternion normalized) ? normalized : Quaternion.identity;

        internal static Quaternion InverseSafe(Quaternion value)
        {
            float normSquared = value.x * value.x + value.y * value.y +
                                value.z * value.z + value.w * value.w;
            if (!IsFinite(normSquared) || normSquared < MinimumNormSquared)
                return Quaternion.identity;

            float inverseNormSquared = 1f / normSquared;
            return new Quaternion(
                -value.x * inverseNormSquared,
                -value.y * inverseNormSquared,
                -value.z * inverseNormSquared,
                value.w * inverseNormSquared);
        }

        internal static Quaternion AngleAxis(float degrees, float axisX, float axisY, float axisZ)
        {
            float axisNormSquared = axisX * axisX + axisY * axisY + axisZ * axisZ;
            if (!IsFinite(degrees) || !IsFinite(axisNormSquared) || axisNormSquared < MinimumNormSquared)
                return Quaternion.identity;

            double halfRadians = degrees * (Math.PI / 360d);
            float scale = (float)(Math.Sin(halfRadians) / Math.Sqrt(axisNormSquared));
            return NormalizeSafe(new Quaternion(
                axisX * scale,
                axisY * scale,
                axisZ * scale,
                (float)Math.Cos(halfRadians)));
        }

        internal static bool AreEquivalent(Quaternion left, Quaternion right)
        {
            return NormalizedAbsoluteDot(left, right) >= EquivalentDotThreshold;
        }

        internal static float AngleDegrees(Quaternion left, Quaternion right)
        {
            double dot = NormalizedAbsoluteDot(left, right);
            if (dot > 1d)
                dot = 1d;
            return (float)(2d * Math.Acos(dot) * (180d / Math.PI));
        }

        private static double NormalizedAbsoluteDot(Quaternion left, Quaternion right)
        {
            double leftX = left.x;
            double leftY = left.y;
            double leftZ = left.z;
            double leftW = left.w;
            double leftNormSquared = leftX * leftX + leftY * leftY +
                                     leftZ * leftZ + leftW * leftW;
            if (double.IsNaN(leftNormSquared) || double.IsInfinity(leftNormSquared) ||
                leftNormSquared < MinimumNormSquared)
            {
                leftX = leftY = leftZ = 0d;
                leftW = 1d;
                leftNormSquared = 1d;
            }

            double rightX = right.x;
            double rightY = right.y;
            double rightZ = right.z;
            double rightW = right.w;
            double rightNormSquared = rightX * rightX + rightY * rightY +
                                      rightZ * rightZ + rightW * rightW;
            if (double.IsNaN(rightNormSquared) || double.IsInfinity(rightNormSquared) ||
                rightNormSquared < MinimumNormSquared)
            {
                rightX = rightY = rightZ = 0d;
                rightW = 1d;
                rightNormSquared = 1d;
            }

            double dot = leftX * rightX + leftY * rightY +
                         leftZ * rightZ + leftW * rightW;
            double normalized = Math.Abs(dot) /
                                Math.Sqrt(leftNormSquared * rightNormSquared);
            return normalized > 1d ? 1d : normalized;
        }
    }
}
