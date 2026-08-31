using UnityEngine;

namespace QuietBuildRotation
{
    internal readonly struct PlacementTransform
    {
        private readonly bool _rotationWasFinite;

        internal PlacementTransform(Vector3 position, Quaternion rotation)
        {
            Position = position;
            _rotationWasFinite = QuaternionMath.TryNormalize(
                rotation,
                out Quaternion normalized);
            Rotation = _rotationWasFinite ? normalized : Quaternion.identity;
        }

        internal Vector3 Position { get; }

        internal Quaternion Rotation { get; }

        internal bool IsFinite => _rotationWasFinite && VectorMath.IsFinite(Position);
    }

    /// <summary>
    /// Fixed-size history for repeating the most recent committed relative transform. It retains
    /// two value snapshots only and never holds placed GameObjects or grows with world history.
    /// </summary>
    internal sealed class RelativeTransformHistory
    {
        private bool _hasLast;
        private bool _hasPattern;
        private PlacementTransform _last;
        private Vector3 _relativeOffset;
        private Quaternion _relativeRotation = Quaternion.identity;

        internal bool HasPattern => _hasPattern;

        internal PlacementTransform Last => _last;

        internal void Clear()
        {
            _hasLast = false;
            _hasPattern = false;
            _last = default;
            _relativeOffset = Vector3.zero;
            _relativeRotation = Quaternion.identity;
        }

        internal bool ObserveCommit(PlacementTransform committed)
        {
            if (!committed.IsFinite)
                return false;

            if (_hasLast)
            {
                Quaternion inverse = QuaternionMath.InverseSafe(_last.Rotation);
                _relativeOffset = inverse * (committed.Position - _last.Position);
                _relativeRotation = QuaternionMath.NormalizeSafe(inverse * committed.Rotation);
                _hasPattern = VectorMath.IsFinite(_relativeOffset) &&
                              QuaternionMath.TryNormalize(_relativeRotation, out _);
            }

            _last = committed;
            _hasLast = true;
            return true;
        }

        internal bool TryPredictNext(out PlacementTransform predicted)
        {
            if (!_hasLast || !_hasPattern)
            {
                predicted = default;
                return false;
            }

            Vector3 position = _last.Position + _last.Rotation * _relativeOffset;
            Quaternion rotation = QuaternionMath.NormalizeSafe(_last.Rotation * _relativeRotation);
            predicted = new PlacementTransform(position, rotation);
            return predicted.IsFinite;
        }
    }

    internal static class SnapAlignment
    {
        private const float MinimumDirectionSquared = 0.00000001f;

        /// <summary>
        /// Aligns the selected source snap normal opposite the target normal while preserving the
        /// target tangent as the source up direction. Only root position/rotation are returned;
        /// scale, ownership, and every Piece field remain outside this value operation.
        /// </summary>
        internal static bool TryAlignOpposed(
            Vector3 sourceLocalPosition,
            Quaternion sourceLocalRotation,
            Vector3 targetWorldPosition,
            Quaternion targetWorldRotation,
            out PlacementTransform root)
        {
            root = default;
            if (!VectorMath.IsFinite(sourceLocalPosition) ||
                !VectorMath.IsFinite(targetWorldPosition) ||
                !QuaternionMath.TryNormalize(sourceLocalRotation, out Quaternion sourceLocal) ||
                !QuaternionMath.TryNormalize(targetWorldRotation, out Quaternion targetWorld))
            {
                return false;
            }

            Vector3 targetNormal = targetWorld * Vector3.forward;
            Vector3 targetTangent = targetWorld * Vector3.up;
            if (!VectorMath.IsFinite(targetNormal) || !VectorMath.IsFinite(targetTangent) ||
                targetNormal.sqrMagnitude < MinimumDirectionSquared ||
                targetTangent.sqrMagnitude < MinimumDirectionSquared)
            {
                return false;
            }

            if (!QuaternionMath.TryLookRotation(
                    -targetNormal,
                    targetTangent,
                    out Quaternion desiredSourceWorld))
                return false;

            Quaternion rootRotation = QuaternionMath.NormalizeSafe(
                desiredSourceWorld * QuaternionMath.InverseSafe(sourceLocal));
            Vector3 rootPosition = targetWorldPosition - rootRotation * sourceLocalPosition;
            root = new PlacementTransform(rootPosition, rootRotation);
            return root.IsFinite;
        }
    }

    internal static class VectorMath
    {
        internal static bool IsFinite(Vector3 value) =>
            QuaternionMath.IsFinite(value.x) &&
            QuaternionMath.IsFinite(value.y) &&
            QuaternionMath.IsFinite(value.z);

        internal static bool Approximately(Vector3 left, Vector3 right, float tolerance)
        {
            if (!IsFinite(left) || !IsFinite(right) || !QuaternionMath.IsFinite(tolerance) || tolerance < 0f)
                return false;
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }
    }
}
