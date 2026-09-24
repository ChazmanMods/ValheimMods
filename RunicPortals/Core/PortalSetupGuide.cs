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
                : global::Runic.Localization.RunicText.Get("text_3cbd94169b76") +
                  (state.VanillaConnected ? "connected" : "unlinked");
            string warning = string.Empty;
            if (state.EditorOpen)
                warning = global::Runic.Localization.RunicText.Get("text_7d16f1b8ad1c");
            else if (!state.IsNetwork && state.VanillaConnected)
                warning = global::Runic.Localization.RunicText.Get("text_3b03c1feaa44");

            string controls;
            if (!state.IsNetwork)
                controls = use + global::Runic.Localization.RunicText.Get("text_4557ff28f4bd") +
                           alternateUse + global::Runic.Localization.RunicText.Get("text_2308fea27b6c");
            else if (!state.DetailsVisible)
                controls = global::Runic.Localization.RunicText.Get("text_88ec4dc0ef31");
            else if (!state.PermitsDeparture)
                controls = use + global::Runic.Localization.RunicText.Get("text_dc6fd4e622e8") +
                           global::Runic.Localization.RunicText.Get("text_4c78182c42ad");
            else
                controls = use + global::Runic.Localization.RunicText.Get("text_dc6fd4e622e8") +
                           global::Runic.Localization.RunicText.Get("text_c8d4795e025f") +
                           global::Runic.Localization.RunicText.Get("text_a7916026edbf") +
                           (state.AcceptsArrival
                               ? string.Empty
                               : global::Runic.Localization.RunicText.Get("text_68b643541d98"));

            string instructions =
                global::Runic.Localization.RunicText.Get("text_31dc2b011b30") +
                global::Runic.Localization.RunicText.Get("text_40697685d448") +
                global::Runic.Localization.RunicText.Get("text_45d60c7557c9") +
                global::Runic.Localization.RunicText.Get("text_22f27007de2a") +
                global::Runic.Localization.RunicText.Get("text_4154e72a3b21") +
                global::Runic.Localization.RunicText.Get("text_c14684466edb");
            return new PortalSetupGuideContent(status, warning, controls, instructions);
        }

        private static string NetworkStatus(PortalHoverPanelState state)
        {
            if (!state.DetailsVisible)
                return global::Runic.Localization.RunicText.Get("text_5245186207f8");
            string direction = state.AcceptsArrival && state.PermitsDeparture
                ? "both"
                : state.AcceptsArrival ? global::Runic.Localization.RunicText.Get("text_b16a6dd9b127") : global::Runic.Localization.RunicText.Get("text_e8440ffba352");
            string selected = state.SelectedDestination.Length == 0
                ? "none"
                : state.SelectedDestination;
            return global::Runic.Localization.RunicText.Get("text_bd4d698e09d9") + (state.Policy.Length == 0 ? global::Runic.Localization.RunicText.Get("text_1744b96470b5") : state.Policy + global::Runic.Localization.RunicText.Get("text_d77462b4f025")) +
                   global::Runic.Localization.RunicText.Get("text_c436e786c0fd") + state.NetworkId + global::Runic.Localization.RunicText.Get("text_6a7d1835592c") + state.DisplayName +
                   global::Runic.Localization.RunicText.Get("text_88d68eeae5d9") + direction + global::Runic.Localization.RunicText.Get("text_1fbf04039d5a") + selected;
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
