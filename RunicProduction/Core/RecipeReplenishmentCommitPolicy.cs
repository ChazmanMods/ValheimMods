using System;
using RunicProduction.Contracts;

namespace RunicProduction.Core
{
    internal enum RecipeReplenishmentCommitAction
    {
        CreateLink = 1,
        RefreshSameReplenishmentLink = 2,
        RejectExistingLink = 3
    }

    /// <summary>
    /// Keeps Refresh Targets distinct from relinking: committing the same Replenishment chest
    /// refreshes in place; no existing link is ever overwritten with a different endpoint.
    /// </summary>
    internal static class RecipeReplenishmentCommitPolicy
    {
        /// <summary>
        /// The exact native owner applies its allow list when a Commit would create or refresh
        /// persistent production state.
        /// </summary>
        internal static bool RequiresAuthoritativeAllowlist(bool committing) => committing;

        internal static RecipeReplenishmentCommitAction Decide(
            ProductionLinkRole role,
            bool hasExistingLink,
            bool selectedTargetMatchesExisting)
        {
            if (role != ProductionLinkRole.Input && role != ProductionLinkRole.Replenishment)
                throw new ArgumentOutOfRangeException(nameof(role));
            if (!hasExistingLink) return RecipeReplenishmentCommitAction.CreateLink;
            return role == ProductionLinkRole.Replenishment && selectedTargetMatchesExisting
                ? RecipeReplenishmentCommitAction.RefreshSameReplenishmentLink
                : RecipeReplenishmentCommitAction.RejectExistingLink;
        }
    }
}
