using System;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal readonly struct PortalHoverPanelState
    {
        internal PortalHoverPanelState(
            bool isNetwork,
            bool detailsVisible,
            bool vanillaConnected,
            bool editorOpen,
            string displayName,
            string networkId,
            string policy,
            bool acceptsArrival,
            bool permitsDeparture,
            string selectedDestination)
        {
            IsNetwork = isNetwork;
            DetailsVisible = detailsVisible;
            VanillaConnected = vanillaConnected;
            EditorOpen = editorOpen;
            DisplayName = Bounded(displayName, 64);
            NetworkId = Bounded(networkId, 64);
            Policy = Bounded(policy, 16);
            AcceptsArrival = acceptsArrival;
            PermitsDeparture = permitsDeparture;
            SelectedDestination = Bounded(selectedDestination, 96);
        }

        internal bool IsNetwork { get; }
        internal bool DetailsVisible { get; }
        internal bool VanillaConnected { get; }
        internal bool EditorOpen { get; }
        internal string DisplayName { get; }
        internal string NetworkId { get; }
        internal string Policy { get; }
        internal bool AcceptsArrival { get; }
        internal bool PermitsDeparture { get; }
        internal string SelectedDestination { get; }

        private static string Bounded(string value, int maximum)
        {
            string text = value ?? string.Empty;
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }
    }

    internal readonly struct PortalSetupGuideContent
    {
        internal PortalSetupGuideContent(
            string status,
            string warning,
            string controls,
            string instructions)
        {
            Status = status ?? string.Empty;
            Warning = warning ?? string.Empty;
            Controls = controls ?? string.Empty;
            Instructions = instructions ?? string.Empty;
        }

        internal string Status { get; }
        internal string Warning { get; }
        internal string Controls { get; }
        internal string Instructions { get; }
    }

    internal static class PortalSetupGuide
    {
        internal static PortalSetupGuideContent Compose(
            PortalHoverPanelState state,
            string useBinding,
            string alternateUseBinding)
        {
            string use = Binding(useBinding, "Use");
            string alternateUse = Binding(alternateUseBinding, "Alternate Place + Use");
            string status = state.IsNetwork
                ? NetworkStatus(state)
                : "Current mode: Standard Pair (vanilla) | Link: " +
                  (state.VanillaConnected ? "connected" : "unlinked");
            string warning = string.Empty;
            if (state.EditorOpen)
                warning = "Portal setup is open. Choose Standard Pair or Runic Network, fill in the visible fields, then Save.";
            else if (!state.IsNetwork && state.VanillaConnected)
                warning = "Connected vanilla pair: give it a unique Standard Pair tag before converting it to a Runic network.";

            string controls;
            if (!state.IsNetwork)
                controls = use + " - edit the vanilla tag\n" +
                           alternateUse + " - open the Runic portal editor";
            else if (!state.DetailsVisible)
                controls = "This portal's details and route controls are hidden from your current identity.";
            else if (!state.PermitsDeparture)
                controls = use + " - edit this Runic portal\n" +
                           "Arrival-only endpoint: it appears in a portal picker, but walking into it does not start a trip.";
            else
                controls = use + " - edit this Runic portal\n" +
                           "Walk into the portal - open its safe destination map\n" +
                           "Click an authorized arrival portal on the map to travel" +
                           (state.AcceptsArrival
                               ? string.Empty
                               : "\nDeparture-only endpoint: a destination click confirms that the trip may be one-way.");

            string instructions =
                "STANDARD PAIR - Valheim's normal one-to-one portal. Check Standard Pair and enter a tag of up to 10 characters.\n\n" +
                "RUNIC NETWORK - enter a Network Name shared by the destinations that belong together, then give this endpoint a distinct Portal Name.\n\n" +
                "ACCESS - Public is available to everyone, Private is owner-only, and Group is available to current members of the selected Runic Group.\n\n" +
                "DIRECTION - Both supports arrival and departure; Arrivals Only is a destination; Departures Only starts trips and may be one-way.\n\n" +
                "Groups are managed in Valheim chat with /group create, /group invite, /group accept, and /group list. Your groups appear automatically in the editor.\n\n" +
                "Walk into a departure-capable Runic portal and click an authorized destination on the map. On the normal large map, press P to show or hide the world-wide authorized portal directory.";
            return new PortalSetupGuideContent(status, warning, controls, instructions);
        }

        private static string NetworkStatus(PortalHoverPanelState state)
        {
            if (!state.DetailsVisible)
                return "Current mode: Restricted Network Portal | Details hidden by current permissions";
            string direction = state.AcceptsArrival && state.PermitsDeparture
                ? "both"
                : state.AcceptsArrival ? "arrive only" : "depart only";
            string selected = state.SelectedDestination.Length == 0
                ? "none"
                : state.SelectedDestination;
            return "Current mode: " + (state.Policy.Length == 0 ? "Network" : state.Policy + " Network") +
                   " | Network: " + state.NetworkId + " | Name: " + state.DisplayName +
                   " | Direction: " + direction + "\nSelected destination: " + selected;
        }

        private static string Binding(string value, string fallback)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return fallback;
            if (text.Length > 64) text = text.Substring(0, 64);
            for (int index = 0; index < text.Length; index++)
                if (char.IsControl(text[index])) return fallback;
            return text;
        }
    }

}
