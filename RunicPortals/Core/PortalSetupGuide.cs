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
        internal const string PublicCommand = "network|public|NETWORK|NAME|both";
        internal const string PrivateCommand = "network|private|NETWORK|NAME|both";
        internal const string GroupCommand = "network|group|NETWORK|NAME|both";

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
                warning = "Runic editor open: enter one exact command from this guide and confirm it.";
            else if (!state.IsNetwork && state.VanillaConnected)
                warning = "Connected vanilla pair: first use " + use +
                          " to give this portal a unique vanilla tag. Runic Portals refuses conversion while the vanilla pair is connected.";

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
                "PUBLIC - players may discover and use it, subject to wards and current route/world rules\n" +
                PublicCommand + "\n\n" +
                "PRIVATE - only the portal owner's persisted identity may discover and use it, subject to wards and route/world rules\n" +
                PrivateCommand + "\n\n" +
                "GROUP - current members of one Runic group may discover and use it, subject to wards and route/world rules\n" +
                GroupCommand + "\n\n" +
                "NETWORK is the map and route filter. Only portals with the exact same NETWORK spelling appear together. Public, your private, and your current Group endpoints may share a NETWORK; access is checked separately for every portal. Give every portal a distinct NAME.\n\n" +
                "Direction replaces 'both': both = arrive and depart; arrive = destination only; depart = source only.\n\n" +
                "Groups use Valheim chat, not F5: /group create <name>, /group invite <player>, /group accept <name>, and /group use <group name>. The active Group is used automatically by the Group portal command; no UUID is entered.\n\n" +
                "Setup: open the Runic editor on each unlinked Standard Pair portal and enter its command. Walk into a configured depart/both portal to open the destination map, then click an authorized arrive/both portal. On the normal large map, press P to show or hide the world-wide authorized directory: vanilla Standard Pair portals, public Runic portals, your private Runic portals, and Runic Group portals for groups you currently belong to. This directory includes arrive-only, depart-only, and bidirectional Runic portals and does not require standing near a portal. To restore vanilla mode, edit a configured portal with " +
                use + " and enter 'standard'. Changing an existing Network portal or restoring 'standard' requires repeating the identical command once to confirm.";
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
