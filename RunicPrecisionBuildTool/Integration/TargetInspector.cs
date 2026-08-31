using System;
using UnityEngine;

namespace QuietBuildRotation.Integration
{
    internal delegate bool PieceTargetQuery(Player player, out Piece piece);

    internal readonly struct TargetInfo
    {
        internal TargetInfo(Piece piece, Vector3 position, Quaternion rotation)
        {
            Piece = piece;
            Position = position;
            Rotation = rotation;
            InstanceId = piece ? piece.GetInstanceID() : 0;
        }

        internal Piece Piece { get; }
        internal Vector3 Position { get; }
        internal Quaternion Rotation { get; }
        internal int InstanceId { get; }
        internal bool IsValid => Piece;
    }

    internal readonly struct SnapMatchInfo
    {
        internal SnapMatchInfo(
            int ghostId,
            uint generation,
            int hostId,
            Vector3 hostWorldPosition,
            Quaternion hostWorldRotation,
            Vector3 hostWorldScale,
            Vector3 sourceLocalPosition,
            Quaternion sourceLocalRotation,
            Vector3 targetWorldPosition,
            Quaternion targetWorldRotation)
        {
            GhostId = ghostId;
            Generation = generation;
            HostId = hostId;
            HostWorldPosition = hostWorldPosition;
            HostWorldRotation = hostWorldRotation;
            HostWorldScale = hostWorldScale;
            SourceLocalPosition = sourceLocalPosition;
            SourceLocalRotation = sourceLocalRotation;
            TargetWorldPosition = targetWorldPosition;
            TargetWorldRotation = targetWorldRotation;
        }

        internal int GhostId { get; }
        internal uint Generation { get; }
        internal int HostId { get; }
        internal Vector3 HostWorldPosition { get; }
        internal Quaternion HostWorldRotation { get; }
        internal Vector3 HostWorldScale { get; }
        internal Vector3 SourceLocalPosition { get; }
        internal Quaternion SourceLocalRotation { get; }
        internal Vector3 TargetWorldPosition { get; }
        internal Quaternion TargetWorldRotation { get; }
        internal bool IsValid => GhostId != 0 && HostId != 0;
    }

    /// <summary>
    /// Maintains the inexpensive, short-lived target cache used by the orientation readout
    /// and Match Rotation. The placement patch supplies the already-resolved ray-test
    /// delegate, so this class performs no reflection on placement frames.
    /// </summary>
    internal sealed class TargetInspector
    {
        internal const float RefreshIntervalSeconds = 0.1f;
        internal const float MatchFeedbackSeconds = 1.25f;

        // A successful vanilla snap finishes with these two transforms coincident. Allow a
        // small amount of float error without treating merely-nearby snap points as active.
        private const float SnapCoincidenceTolerance = 0.025f;
        private const float SnapCoincidenceToleranceSquared =
            SnapCoincidenceTolerance * SnapCoincidenceTolerance;

        private readonly PieceTargetQuery _pieceTargetQuery;
        private bool _rayTestAvailable;

        private bool _captureOpen;
        private GameObject _captureGhost;
        private uint _captureGeneration;
        private Transform _capturedGhostSnap;
        private Transform _capturedTargetSnap;
        private GameObject _validatedSnapGhost;
        private Piece _validatedSnapHost;
        private SnapMatchInfo _validatedSnap;

        private TargetInfo _current;
        private float _nextRefreshAt;
        private int _matchedTargetId;
        private float _matchFeedbackUntil;

        internal TargetInspector(PieceTargetQuery pieceTargetQuery)
        {
            _pieceTargetQuery = pieceTargetQuery;
            _rayTestAvailable = pieceTargetQuery != null;
        }

        internal TargetInfo Current => _current;
        internal float MatchFeedbackUntil => _matchFeedbackUntil;

        internal bool TryGetValidatedSnapHost(
            GameObject ghost,
            uint generation,
            out int hostId)
        {
            hostId = 0;
            SnapMatchInfo snap = _validatedSnap;
            if (!ghost || !_validatedSnapGhost || ghost != _validatedSnapGhost ||
                generation != snap.Generation || ghost.GetInstanceID() != snap.GhostId ||
                !IsEligibleSnapHost(_validatedSnapHost, ghost) ||
                _validatedSnapHost.GetInstanceID() != snap.HostId ||
                !HostTransformUnchanged(_validatedSnapHost, in snap))
            {
                return false;
            }

            hostId = snap.HostId;
            return true;
        }

        /// <summary>
        /// Opens a capture window around Player.UpdatePlacementGhost. This prevents a call to
        /// FindClosestSnapPoints made for some other purpose from becoming the preferred host.
        /// </summary>
        internal void BeginPlacementUpdate(
            GameObject ghost,
            uint generation)
        {
            _captureOpen = ghost;
            _captureGhost = ghost;
            _captureGeneration = generation;
            _capturedGhostSnap = null;
            _capturedTargetSnap = null;
            _validatedSnapGhost = null;
            _validatedSnapHost = null;
            _validatedSnap = default;
        }

        /// <summary>
        /// Observes the pair returned by Valheim without changing the search root, result, or
        /// either returned transform. The enclosing placement-update window excludes unrelated
        /// snap searches from target selection.
        /// </summary>
        internal void ObserveSnapSearch(
            Transform searchRoot,
            bool result,
            Transform ghostSnap,
            Transform targetSnap)
        {
            if (!_captureOpen || !_captureGhost || !searchRoot ||
                searchRoot != _captureGhost.transform)
                return;

            // Treat the last search made for this ghost as authoritative. A later failed search
            // must not leave an earlier successful pair looking active.
            _capturedGhostSnap = result && ghostSnap ? ghostSnap : null;
            _capturedTargetSnap = result && targetSnap ? targetSnap : null;
        }

        internal void CompletePlacementUpdate(
            GameObject ghost,
            uint generation)
        {
            _captureOpen = false;

            if (!ghost || ghost != _captureGhost || generation != _captureGeneration ||
                !_capturedGhostSnap || !_capturedTargetSnap)
            {
                ClearCapture();
                return;
            }

            Transform ghostTransform = ghost.transform;
            if (!IsSelfOrChild(_capturedGhostSnap, ghostTransform))
            {
                ClearCapture();
                return;
            }

            if (!VectorMath.IsFinite(_capturedGhostSnap.position) ||
                !VectorMath.IsFinite(_capturedTargetSnap.position) ||
                (_capturedGhostSnap.position - _capturedTargetSnap.position).sqrMagnitude >
                SnapCoincidenceToleranceSquared)
            {
                // FindClosestSnapPoints proposed a pair, but Valheim did not finish the update
                // with that pair coincident. Do not treat a rejected proposal as the target.
                ClearCapture();
                return;
            }

            Piece host = _capturedTargetSnap.GetComponentInParent<Piece>();
            if (IsEligibleSnapHost(host, ghost) && IsEligible(host, ghost))
            {
                if (!QuaternionMath.TryNormalize(
                        ghostTransform.rotation,
                        out Quaternion ghostRotation) ||
                    !QuaternionMath.TryNormalize(
                        _capturedTargetSnap.rotation,
                        out Quaternion targetRotation) ||
                    !QuaternionMath.TryNormalize(
                        host.transform.rotation,
                        out Quaternion hostRotation) ||
                    !VectorMath.IsFinite(host.transform.position) ||
                    !VectorMath.IsFinite(host.transform.lossyScale))
                {
                    ClearCapture();
                    return;
                }

                Quaternion inverseGhostRotation = QuaternionMath.InverseSafe(ghostRotation);
                Vector3 sourceLocalPosition = inverseGhostRotation *
                                              (_capturedGhostSnap.position - ghostTransform.position);
                if (!QuaternionMath.TryNormalize(
                        inverseGhostRotation * _capturedGhostSnap.rotation,
                        out Quaternion sourceLocalRotation))
                {
                    ClearCapture();
                    return;
                }
                SnapMatchInfo snapshot = new SnapMatchInfo(
                    ghost.GetInstanceID(),
                    generation,
                    host.GetInstanceID(),
                    host.transform.position,
                    hostRotation,
                    host.transform.lossyScale,
                    sourceLocalPosition,
                    sourceLocalRotation,
                    _capturedTargetSnap.position,
                    targetRotation);
                if (!VectorMath.IsFinite(sourceLocalPosition) ||
                    !VectorMath.IsFinite(snapshot.TargetWorldPosition))
                {
                    ClearCapture();
                    return;
                }
                _validatedSnapGhost = ghost;
                _validatedSnapHost = host;
                _validatedSnap = snapshot;
            }

            ClearCapture();
        }

        /// <summary>
        /// Closes an interrupted vanilla placement-update capture without accepting a partial
        /// snap observation. Harmony finalizers use this when Valheim or another patch throws.
        /// The last fully validated target remains available for the next healthy frame.
        /// </summary>
        internal void AbortPlacementUpdate()
        {
            _captureOpen = false;
            ClearCapture();
        }

        /// <summary>
        /// Refreshes at no more than 10 Hz during ordinary display updates. Passing force is
        /// reserved for the Match Rotation key so its target is sampled on that same press.
        /// </summary>
        internal bool Refresh(Player player, GameObject ghost, float now, bool force)
        {
            if (!force && now < _nextRefreshAt)
                return IsEligible(_current.Piece, ghost);

            _nextRefreshAt = now + RefreshIntervalSeconds;

            // The crosshair is the user's explicit target. This must win at a crowded joint:
            // several pieces can expose perfectly co-located snap points, and vanilla is free to
            // return any one of those hosts even while the player is visibly aiming at another.
            // Retain the completed vanilla snap host as a useful fallback when the ray lands in
            // empty space or on a non-Piece surface.
            Piece aimedPiece = RaycastPiece(player, ghost);
            Piece snapHost = _validatedSnapGhost == ghost &&
                             IsEligible(_validatedSnapHost, ghost)
                ? _validatedSnapHost
                : null;

            Piece target;
            switch (TargetSelectionPolicy.Choose(aimedPiece, snapHost))
            {
                case TargetCandidateSource.AimedPiece:
                    target = aimedPiece;
                    break;
                case TargetCandidateSource.SnapHost:
                    target = snapHost;
                    break;
                default:
                    target = null;
                    break;
            }

            _current = target
                ? new TargetInfo(target, target.transform.position, target.transform.rotation)
                : default;
            return _current.IsValid;
        }

        internal bool TryGetSnapAlignmentForMatch(
            GameObject ghost,
            uint generation,
            out PlacementTransform transform,
            out Piece host)
        {
            transform = default;
            host = null;
            SnapMatchInfo snap = _validatedSnap;
            if (!ghost || !_validatedSnapGhost || ghost != _validatedSnapGhost ||
                generation != snap.Generation || ghost.GetInstanceID() != snap.GhostId ||
                !IsEligibleSnapHost(_validatedSnapHost, ghost) ||
                _validatedSnapHost.GetInstanceID() != snap.HostId ||
                !HostTransformUnchanged(_validatedSnapHost, in snap))
            {
                return false;
            }

            if (!SnapAlignment.TryAlignOpposed(
                    snap.SourceLocalPosition,
                    snap.SourceLocalRotation,
                    snap.TargetWorldPosition,
                    snap.TargetWorldRotation,
                    out transform))
            {
                return false;
            }

            host = _validatedSnapHost;
            return true;
        }

        internal bool TryGetRotationForMatch(
            Player player,
            GameObject ghost,
            float now,
            out Quaternion rotation,
            out TargetInfo target)
        {
            if (Refresh(player, ghost, now, true))
            {
                target = _current;
                rotation = target.Rotation;
                return true;
            }

            target = default;
            rotation = Quaternion.identity;
            return false;
        }

        internal void MarkMatched(float now)
        {
            if (!_current.IsValid) return;
            _matchedTargetId = _current.InstanceId;
            _matchFeedbackUntil = now + MatchFeedbackSeconds;
        }

        internal bool IsMatchFeedbackActive(float now) =>
            now < _matchFeedbackUntil &&
            _current.IsValid &&
            _current.InstanceId == _matchedTargetId;

        internal void Clear()
        {
            _captureOpen = false;
            _captureGhost = null;
            _captureGeneration = 0u;
            _capturedGhostSnap = null;
            _capturedTargetSnap = null;
            _validatedSnapGhost = null;
            _validatedSnapHost = null;
            _validatedSnap = default;
            _current = default;
            _nextRefreshAt = 0f;
            _matchedTargetId = 0;
            _matchFeedbackUntil = 0f;
        }

        private Piece RaycastPiece(Player player, GameObject ghost)
        {
            if (!_rayTestAvailable || !player || !ghost) return null;

            try
            {
                Piece piece;
                bool hit = _pieceTargetQuery(player, out piece);
                return hit && IsEligible(piece, ghost) ? piece : null;
            }
            catch (Exception exception)
            {
                // A verified adapter should not throw. Treat a later private-method failure as
                // an adapter failure too: every subsequent Harmony path becomes vanilla/no-op.
                _rayTestAvailable = false;
                Diagnostics.DisableAdapter(
                    $"PieceRayTest target query failed: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                return null;
            }
        }

        private void ClearCapture()
        {
            _captureOpen = false;
            _captureGhost = null;
            _captureGeneration = 0u;
            _capturedGhostSnap = null;
            _capturedTargetSnap = null;
        }

        private static bool IsEligible(Piece piece, GameObject ghost)
        {
            if (!piece || !piece.m_canRotate || !piece.gameObject.activeInHierarchy || !ghost)
                return false;

            Transform pieceTransform = piece.transform;
            Transform ghostTransform = ghost.transform;
            return pieceTransform != ghostTransform &&
                   !pieceTransform.IsChildOf(ghostTransform) &&
                   !ghostTransform.IsChildOf(pieceTransform) &&
                   VectorMath.IsFinite(pieceTransform.position) &&
                   QuaternionMath.TryNormalize(pieceTransform.rotation, out _);
        }

        private static bool IsEligibleSnapHost(Piece piece, GameObject ghost)
        {
            if (!piece || !piece.gameObject.activeInHierarchy || !ghost)
                return false;

            Transform pieceTransform = piece.transform;
            Transform ghostTransform = ghost.transform;
            return pieceTransform != ghostTransform &&
                   !pieceTransform.IsChildOf(ghostTransform) &&
                   !ghostTransform.IsChildOf(pieceTransform) &&
                   VectorMath.IsFinite(pieceTransform.position) &&
                   QuaternionMath.TryNormalize(pieceTransform.rotation, out _);
        }

        private static bool IsSelfOrChild(Transform candidate, Transform root) =>
            candidate == root || candidate.IsChildOf(root);

        private static bool HostTransformUnchanged(Piece host, in SnapMatchInfo snap) =>
            host &&
            VectorMath.Approximately(
                host.transform.position,
                snap.HostWorldPosition,
                SnapCoincidenceTolerance) &&
            QuaternionMath.AngleDegrees(
                host.transform.rotation,
                snap.HostWorldRotation) <= 0.05f &&
            VectorMath.Approximately(
                host.transform.lossyScale,
                snap.HostWorldScale,
                0.0005f);

    }
}
