using RunicProduction.Contracts;

namespace RunicProduction.Core
{
    internal enum RepeatedControlAction
    {
        Select = 0,
        CancelSelection = 1,
        UnlinkExisting = 2
    }

    internal static class ProductionControlGuidance
    {
        internal static RepeatedControlAction DecideRepeatedControl(
            bool pendingMatches,
            bool hasLink) =>
            !pendingMatches
                ? RepeatedControlAction.Select
                : RepeatedControlAction.CancelSelection;

        internal static string Format(
            ProductionLinkRole role,
            bool hasLink,
            int fuelReserve)
        {
            string gesture = role == ProductionLinkRole.Output
                ? "Alt+Right Mouse"
                : role == ProductionLinkRole.Replenishment
                    ? "Alt+Middle Mouse"
                    : "Alt+Left Mouse";
            string displayRole = role == ProductionLinkRole.Fuel
                ? "Fuel Input"
                : role.ToString();
            string action = gesture + ": select " + displayRole +
                            "; repeat it on the chest" +
                            (hasLink ? " (add Shift at both steps to unlink)" : string.Empty);
            if (role == ProductionLinkRole.Fuel)
                action += $" (reserve {fuelReserve})";
            return action;
        }
    }
}
