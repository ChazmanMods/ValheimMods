using System;
using RunicPortals.Api;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal sealed partial class PortalRuntime
    {
        private PortalHoverPanel _hoverPanel;

        internal TeleportWorld ActiveEditPortalForPanel => _edit?.Portal;

        private void InitializeHoverPanel()
        {
            if (!Application.isBatchMode) _hoverPanel = new PortalHoverPanel(this);
        }

        private void ShutdownHoverPanel()
        {
            _hoverPanel?.Dispose();
            _hoverPanel = null;
        }

        internal void NoteHoveredPortal(TeleportWorld portal) => _hoverPanel?.Observe(portal);

        internal void DrawHoverPanel() => _hoverPanel?.Draw();

        internal void DisableHoverPanel(Exception exception) => _hoverPanel?.Disable(exception);

        private void RefreshHoverPanelConfiguration() => _hoverPanel?.ResetStyles();

        internal bool TryGetHoverPanelState(
            TeleportWorld portal,
            out PortalHoverPanelState state)
        {
            state = default;
            if (!FeatureEnabled || portal == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;

            bool editorOpen = _edit != null && _edit.InstanceId == portal.GetInstanceID();
            bool connected = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None;
            if (PortalZdoCodec.GetMode(zdo) != (int)PortalMode.Network)
            {
                state = new PortalHoverPanelState(
                    false, true, connected, editorOpen, string.Empty, string.Empty,
                    string.Empty, true, true, string.Empty);
                return true;
            }
            if (!PortalZdoCodec.TryRead(zdo, out PortalEndpoint endpoint, out _)) return false;

            Player player = Player.m_localPlayer;
            bool detailsVisible = false;
            string traveler = player == null || player.GetPlayerID() == 0L
                ? string.Empty
                : PortalPermissionAdapter.Identity(player.GetPlayerID());
            if (player != null && player.GetPlayerID() != 0L)
            {
                detailsVisible = string.Equals(
                                     endpoint.OwnerStableId,
                                     traveler,
                                     StringComparison.Ordinal);
                if (!detailsVisible)
                {
                    WardContext ward = ValheimContracts.ResolveWard(
                        zdo.GetPosition(), player.GetPlayerID());
                    detailsVisible = ValheimContracts.WardAllows(ward) &&
                                     _permissions.Allows(
                                         endpoint,
                                         traveler,
                                         PortalAccessAction.ViewDiscover);
                }
            }
            if (!detailsVisible)
            {
                state = new PortalHoverPanelState(
                    true, false, connected, editorOpen, string.Empty, string.Empty,
                    global::Runic.Localization.RunicText.Get("text_a00571bcb257"), false, false, string.Empty);
                return true;
            }

            string selected = SelectedDestinationLabel(endpoint, zdo.m_uid.ToString());
            state = new PortalHoverPanelState(
                true,
                true,
                connected,
                editorOpen,
                endpoint.DisplayName,
                endpoint.NetworkId,
                PolicyLabel(endpoint.NetworkKind),
                endpoint.AcceptsArrival,
                endpoint.PermitsDeparture,
                selected);
            return true;
        }

        private string SelectedDestinationLabel(PortalEndpoint source, string portalId)
        {
            if (source == null || string.IsNullOrEmpty(portalId) || Player.m_localPlayer == null)
                return string.Empty;
            string traveler = PortalPermissionAdapter.Identity(Player.m_localPlayer.GetPlayerID());
            if (!_selections.TryGet(traveler, portalId, out PortalSelection selection))
                return string.Empty;
            if (!_graph.TryGetEndpoint(selection.DestinationPortalId, out PortalEndpoint destination))
                return string.Empty;
            return destination.DisplayName +
                   (selection.IsReturn
                       ? global::Runic.Localization.RunicText.Get("text_84e4a6332923")
                       : HasVisibleDuplicate(source, traveler, destination.DisplayName)
                           ? " [" + destination.PortalId + "]"
                           : string.Empty);
        }

        private static string PolicyLabel(PortalNetworkKind kind)
        {
            switch (kind)
            {
                case PortalNetworkKind.Public: return global::Runic.Localization.RunicText.Get("text_591935b15b1c");
                case PortalNetworkKind.Personal: return global::Runic.Localization.RunicText.Get("text_c63eb6720c6e");
                case PortalNetworkKind.Group: return global::Runic.Localization.RunicText.Get("text_34ca0e766088");
                default: return global::Runic.Localization.RunicText.Get("text_a00571bcb257");
            }
        }
    }
}
