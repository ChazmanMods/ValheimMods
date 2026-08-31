namespace QuietBuildRotation.Tests
{
    internal static class TargetSelectionPolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run("Aimed piece wins over an ambiguous snap host", AimedPieceWins);
            TestRunner.Run("Snap host is used when the crosshair has no piece", SnapHostFallback);
            TestRunner.Run("Aimed piece works without an active snap", AimedPieceWithoutSnap);
            TestRunner.Run("No target signal produces no target", NoTarget);
        }

        private static void AimedPieceWins() =>
            TestAssert.Equal(
                TargetCandidateSource.AimedPiece,
                TargetSelectionPolicy.Choose(true, true));

        private static void SnapHostFallback() =>
            TestAssert.Equal(
                TargetCandidateSource.SnapHost,
                TargetSelectionPolicy.Choose(false, true));

        private static void AimedPieceWithoutSnap() =>
            TestAssert.Equal(
                TargetCandidateSource.AimedPiece,
                TargetSelectionPolicy.Choose(true, false));

        private static void NoTarget() =>
            TestAssert.Equal(
                TargetCandidateSource.None,
                TargetSelectionPolicy.Choose(false, false));
    }
}
