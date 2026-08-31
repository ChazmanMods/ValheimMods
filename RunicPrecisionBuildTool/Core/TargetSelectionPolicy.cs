namespace QuietBuildRotation
{
    internal enum TargetCandidateSource
    {
        None = 0,
        AimedPiece,
        SnapHost
    }

    /// <summary>
    /// Resolves the two target signals without depending on Unity objects. The crosshair is the
    /// player's explicit choice; a completed vanilla snap is only a fallback for cases where the
    /// ray does not land on a rotatable Piece.
    /// </summary>
    internal static class TargetSelectionPolicy
    {
        internal static TargetCandidateSource Choose(bool hasAimedPiece, bool hasSnapHost)
        {
            if (hasAimedPiece)
                return TargetCandidateSource.AimedPiece;
            if (hasSnapHost)
                return TargetCandidateSource.SnapHost;
            return TargetCandidateSource.None;
        }
    }
}
