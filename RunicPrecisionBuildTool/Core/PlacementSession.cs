using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Mutable state for one local-player placement session. The object is created once and
    /// reused; selecting the same prefab after a successful placement preserves
    /// its pose, while a prefab change or placement exit advances the generation and resets it.
    /// </summary>
    internal sealed class PlacementSession
    {
        internal bool IsActive { get; private set; }

        internal int SelectedPrefabId { get; private set; }

        internal Quaternion BaseRotation { get; private set; } = Quaternion.identity;

        internal Quaternion DesiredRotation { get; private set; } = Quaternion.identity;

        internal bool HasAbsoluteRotation { get; private set; }

        internal bool HasRunicRotation { get; private set; }

        // Each semantic movement axis owns its accumulated world-space contribution. In local
        // reference mode a contribution can contain multiple world components, but it can still
        // be reset independently without reconstructing or rotating any earlier movement.
        internal Vector3 SwayOffset { get; private set; } = Vector3.zero;
        internal Vector3 HeaveOffset { get; private set; } = Vector3.zero;
        internal Vector3 SurgeOffset { get; private set; } = Vector3.zero;

        internal Vector3 WorldOffset => SwayOffset + HeaveOffset + SurgeOffset;

        internal Vector3 DesiredPosition { get; private set; } = Vector3.zero;
        internal bool LocksPositionX { get; private set; }
        internal bool LocksPositionY { get; private set; }
        internal bool LocksPositionZ { get; private set; }
        internal bool HasAbsolutePosition => LocksPositionX || LocksPositionY || LocksPositionZ;

        internal int LastVanillaYawIndex { get; private set; }

        internal uint Generation { get; private set; }

        internal RotationAxis ActiveAxis { get; private set; }

        internal float ActiveStep { get; private set; }

        internal bool ActiveStepIsFine { get; private set; }

        internal float MatchFeedbackUntil { get; private set; }

        /// <summary>
        /// Starts a new generation when necessary. Returns true only when state was reset.
        /// Calling this with the currently selected prefab preserves state for repeated builds.
        /// </summary>
        internal bool ObserveSelection(int selectedPrefabId, Quaternion baseRotation, int vanillaYawIndex)
        {
            if (IsActive && SelectedPrefabId == selectedPrefabId)
                return false;

            AdvanceGeneration();
            IsActive = true;
            SelectedPrefabId = selectedPrefabId;
            ResetPose(baseRotation, vanillaYawIndex);
            return true;
        }

        /// <summary>
        /// Ends the active placement generation. Calling Exit repeatedly is a no-op.
        /// </summary>
        internal bool Exit()
        {
            if (!IsActive)
                return false;

            AdvanceGeneration();
            IsActive = false;
            SelectedPrefabId = 0;
            BaseRotation = Quaternion.identity;
            DesiredRotation = Quaternion.identity;
            HasAbsoluteRotation = false;
            HasRunicRotation = false;
            ClearTranslation();
            ClearPositionLocks();
            LastVanillaYawIndex = 0;
            ActiveAxis = RotationAxis.None;
            ActiveStep = 0f;
            ActiveStepIsFine = false;
            MatchFeedbackUntil = 0f;
            return true;
        }

        internal void ResetPose(Quaternion baseRotation, int vanillaYawIndex)
        {
            BaseRotation = QuaternionMath.NormalizeSafe(baseRotation);
            DesiredRotation = BaseRotation;
            HasAbsoluteRotation = false;
            HasRunicRotation = false;
            ClearTranslation();
            ClearPositionLocks();
            LastVanillaYawIndex = vanillaYawIndex;
            ActiveAxis = RotationAxis.None;
            ActiveStep = 0f;
            ActiveStepIsFine = false;
            MatchFeedbackUntil = 0f;
        }

        internal void SetBaseRotation(Quaternion rotation) =>
            BaseRotation = QuaternionMath.NormalizeSafe(rotation);

        internal void SetDesiredRotation(Quaternion rotation, bool absolute)
        {
            DesiredRotation = QuaternionMath.NormalizeSafe(rotation);
            HasAbsoluteRotation = absolute;
        }

        internal void SetRunicRotation(bool isDirty) =>
            HasRunicRotation = isDirty;

        internal void SetLastVanillaYawIndex(int yawIndex) =>
            LastVanillaYawIndex = yawIndex;

        internal void AddAxisOffset(SemanticCommandKind axis, Vector3 worldDelta)
        {
            switch (axis)
            {
                case SemanticCommandKind.MoveSway:
                    SwayOffset += worldDelta;
                    break;
                case SemanticCommandKind.MoveHeave:
                    HeaveOffset += worldDelta;
                    break;
                case SemanticCommandKind.MoveSurge:
                    SurgeOffset += worldDelta;
                    break;
            }
        }

        internal void ResetTranslationAxis(SemanticCommandKind axis)
        {
            switch (axis)
            {
                case SemanticCommandKind.MoveSway:
                    SwayOffset = Vector3.zero;
                    LocksPositionX = false;
                    break;
                case SemanticCommandKind.MoveHeave:
                    HeaveOffset = Vector3.zero;
                    LocksPositionY = false;
                    break;
                case SemanticCommandKind.MoveSurge:
                    SurgeOffset = Vector3.zero;
                    LocksPositionZ = false;
                    break;
            }
        }

        internal void ClearTranslation()
        {
            SwayOffset = Vector3.zero;
            HeaveOffset = Vector3.zero;
            SurgeOffset = Vector3.zero;
        }

        internal void LockPositionAxis(SemanticCommandKind axis, float value)
        {
            if (!QuaternionMath.IsFinite(value)) return;
            Vector3 desired = DesiredPosition;
            switch (axis)
            {
                case SemanticCommandKind.MatchPositionX:
                    desired.x = value;
                    LocksPositionX = true;
                    break;
                case SemanticCommandKind.MatchPositionY:
                    desired.y = value;
                    LocksPositionY = true;
                    break;
                case SemanticCommandKind.MatchPositionZ:
                    desired.z = value;
                    LocksPositionZ = true;
                    break;
                default:
                    return;
            }
            DesiredPosition = desired;
        }

        internal bool LockFullPosition(Vector3 position)
        {
            if (!VectorMath.IsFinite(position)) return false;
            DesiredPosition = position;
            LocksPositionX = true;
            LocksPositionY = true;
            LocksPositionZ = true;
            return true;
        }

        internal void ClearPositionLocks()
        {
            DesiredPosition = Vector3.zero;
            LocksPositionX = false;
            LocksPositionY = false;
            LocksPositionZ = false;
        }

        internal void SetWheelPreview(RotationAxis axis, float step, bool isFine)
        {
            ActiveAxis = axis;
            ActiveStep = step;
            ActiveStepIsFine = isFine;
        }

        internal void ClearWheelPreview() =>
            SetWheelPreview(RotationAxis.None, 0f, false);

        internal void MarkMatched(Quaternion rotation, float feedbackUntil)
        {
            SetDesiredRotation(rotation, true);
            HasRunicRotation = true;
            MatchFeedbackUntil = feedbackUntil;
        }

        internal bool MarkMatchedTransform(PlacementTransform transform, float feedbackUntil)
        {
            if (!transform.IsFinite || !LockFullPosition(transform.Position)) return false;
            MarkMatched(transform.Rotation, feedbackUntil);
            return true;
        }

        internal bool IsMatchFeedbackActive(float currentTime) =>
            MatchFeedbackUntil > currentTime;

        internal void ClearMatchFeedback() => MatchFeedbackUntil = 0f;

        /// <summary>
        /// Marks a newly created placement ghost. Same-prefab generations preserve deliberate
        /// Runic pose state, but a translation-only/untouched session adopts the new vanilla
        /// rotation exactly. Returns true when a prefab/lifecycle reset occurred.
        /// </summary>
        internal bool BeginGhostGeneration(
            int selectedPrefabId,
            Quaternion vanillaBaseRotation,
            int vanillaYawIndex)
        {
            if (!IsActive || SelectedPrefabId != selectedPrefabId)
                return ObserveSelection(selectedPrefabId, vanillaBaseRotation, vanillaYawIndex);

            AdvanceGeneration();
            Quaternion normalizedBase = QuaternionMath.NormalizeSafe(vanillaBaseRotation);
            BaseRotation = normalizedBase;
            LastVanillaYawIndex = vanillaYawIndex;
            if (!HasRunicRotation)
            {
                DesiredRotation = normalizedBase;
                HasAbsoluteRotation = false;
            }


            // An exact world position belongs to the placed object, not the replacement ghost.
            // Preserve orientation and ordinary relative movement for same-prefab repetition, but
            // never silently stack a newly created ghost on the prior absolute coordinate.
            ClearPositionLocks();

            ClearWheelPreview();
            return false;
        }

        private void AdvanceGeneration()
        {
            unchecked
            {
                Generation++;
            }
        }
    }
}
