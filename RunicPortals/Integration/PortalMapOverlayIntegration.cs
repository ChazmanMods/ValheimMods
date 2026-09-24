using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RunicPortals.Api;
using RunicPortals.Core;
using UnityEngine;
using OverlayCandidate = RunicPortals.Core.PortalMapCandidate;

namespace RunicPortals.Integration
{
    internal sealed partial class PortalRuntime
    {
        private static readonly OverlayCandidate[] EmptyMapCandidates =
            Array.Empty<OverlayCandidate>();
        private PortalMapOverlayRuntime _mapOverlay;
        private string _mapOverlayContext = string.Empty;
        private float _nextMapOverlayRefresh;

        private void InitializeMapOverlay()
        {
            _mapOverlay?.Shutdown();
            _mapOverlay = new PortalMapOverlayRuntime();
            _mapOverlayContext = string.Empty;
            _nextMapOverlayRefresh = 0f;
        }

        private void ShutdownMapOverlay()
        {
            _mapOverlay?.Shutdown();
            _mapOverlay = null;
            _mapOverlayContext = string.Empty;
            _nextMapOverlayRefresh = 0f;
        }

        private void TickMapOverlay(float realtime, float interval)
        {
            PortalMapOverlayRuntime overlay = _mapOverlay;
            if (overlay == null) return;
            if (!TryGetMapOverlayContext(out string context))
            {
                if (_mapOverlayContext.Length != 0)
                {
                    _mapOverlayContext = string.Empty;
                    overlay.OnIdentityOrWorldChanged(string.Empty);
                }
                overlay.Tick(Minimap.instance, MapPickerActive);
                return;
            }
            if (!string.Equals(_mapOverlayContext, context, StringComparison.Ordinal))
            {
                _mapOverlayContext = context;
                overlay.OnIdentityOrWorldChanged(context);
                _nextMapOverlayRefresh = 0f;
            }
            overlay.Tick(Minimap.instance, MapPickerActive);
            if (!MapPickerActive && overlay.IsMapOpen)
                TickMapDirectorySync(context, realtime);
            if (MapPickerActive || !overlay.IsMapOpen || realtime < _nextMapOverlayRefresh)
                return;
            _nextMapOverlayRefresh = realtime + Math.Max(0.5f, interval);
            overlay.SetRefreshState(true, false);
            bool accepted = TryBuildLocalMapSnapshot(out List<OverlayCandidate> candidates);
            if (!TryGetMapOverlayContext(out string finalContext) ||
                !string.Equals(context, finalContext, StringComparison.Ordinal))
            {
                overlay.OnIdentityOrWorldChanged(finalContext ?? string.Empty);
                return;
            }
            overlay.ReplaceAuthorizedSnapshot(
                context,
                accepted ? candidates : EmptyMapCandidates);
            overlay.SetRefreshState(false, false, !accepted);
        }

        private bool TryGetMapOverlayContext(out string context)
        {
            context = string.Empty;
            Player player = Player.m_localPlayer;
            ZNet network = ZNet.instance;
            if (player == null || network == null || player.GetPlayerID() == 0L) return false;
            long world = network.GetWorldUID();
            if (world == 0L) return false;
            context = unchecked((ulong)world).ToString("x16", CultureInfo.InvariantCulture) + ":" +
                      player.GetPlayerID().ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private void NoteMapNetworkUsed(string networkName)
        {
            if (_mapOverlay == null || !TryGetMapOverlayContext(out string context)) return;
            _mapOverlay.NoteNetworkUsed(context, networkName);
        }

        private bool TryBuildLocalMapSnapshot(out List<OverlayCandidate> candidates)
        {
            candidates = new List<OverlayCandidate>();
            Player player = Player.m_localPlayer;
            List<ZDO> portals = ValheimContracts.PortalObjects();
            if (player == null || player.GetPlayerID() == 0L || portals == null ||
                portals.Count > ValheimContracts.MaximumPortalObjectsScanned) return false;
            string traveler = PortalPermissionAdapter.Identity(player.GetPlayerID());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < portals.Count; index++)
            {
                ZDO zdo = portals[index];
                if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) continue;
                string portalId = zdo.m_uid.ToString();
                if (!ids.Add(portalId)) return false;
                int mode = PortalZdoCodec.GetMode(zdo);
                if (mode == (int)PortalMode.StandardPair)
                {
                    Vector3 position = zdo.GetPosition();
                    candidates.Add(new OverlayCandidate(
                        portalId,
                        "Standard Pair",
                        MapDisplayName("Vanilla", VanillaPortalTag(zdo), string.Empty),
                        position.x,
                        position.y,
                        position.z,
                        false,
                        true,
                        true,
                        true,
                        true));
                    continue;
                }
                if (mode != (int)PortalMode.Network ||
                    !PortalZdoCodec.TryRead(zdo, out PortalEndpoint endpoint, out _) ||
                    endpoint.OnlineState != PortalOnlineState.Online) continue;
                WardContext ward = ValheimContracts.ResolveWard(
                    zdo.GetPosition(), player.GetPlayerID());
                bool allowed = ValheimContracts.DestinationWardAllows(ward) &&
                               _permissions.Allows(
                                   endpoint,
                                   traveler,
                                   PortalAccessAction.ViewDiscover);
                if (!allowed) continue;
                Vector3 point = zdo.GetPosition();
                candidates.Add(new OverlayCandidate(
                    portalId,
                    endpoint.NetworkId,
                    MapDisplayName(MapCategory(endpoint), endpoint.DisplayName, endpoint.NetworkId),
                    point.x,
                    point.y,
                    point.z,
                    true,
                    endpoint.AcceptsArrival,
                    endpoint.PermitsDeparture,
                    true,
                    endpoint.PermitsDeparture));
                if (candidates.Count > PortalContractLimits.MaximumGraphEndpoints) return false;
            }
            return true;
        }

        private static string VanillaPortalTag(ZDO zdo)
        {
            string tag = zdo?.GetString(ZDOVars.s_tag, string.Empty) ?? string.Empty;
            return string.IsNullOrWhiteSpace(tag) ? global::Runic.Localization.RunicText.Get("text_b65ffe0cec61") : tag.Trim();
        }

        private static string MapCategory(PortalEndpoint endpoint)
        {
            switch (endpoint?.NetworkKind)
            {
                case PortalNetworkKind.Public: return global::Runic.Localization.RunicText.Get("text_591935b15b1c");
                case PortalNetworkKind.Personal: return global::Runic.Localization.RunicText.Get("text_c63eb6720c6e");
                case PortalNetworkKind.Group: return global::Runic.Localization.RunicText.Get("text_34ca0e766088");
                default: return global::Runic.Localization.RunicText.Get("text_00674f364dc3");
            }
        }

        private static string MapDisplayName(string category, string name, string network)
        {
            string value = "[" + SanitizeMapText(category, global::Runic.Localization.RunicText.Get("text_b65ffe0cec61")) + "] " +
                           SanitizeMapText(name, global::Runic.Localization.RunicText.Get("text_b65ffe0cec61"));
            string safeNetwork = SanitizeMapText(network, string.Empty);
            if (safeNetwork.Length != 0) value += " · " + safeNetwork;
            if (value.Length <= PortalContractLimits.MaximumNameLength) return value;
            int length = PortalContractLimits.MaximumNameLength;
            if (char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, length);
        }

        private static string SanitizeMapText(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var builder = new StringBuilder(Math.Min(
                value.Length, PortalContractLimits.MaximumNameLength));
            for (int index = 0; index < value.Length &&
                                builder.Length < PortalContractLimits.MaximumNameLength; index++)
            {
                char current = value[index];
                if (char.IsControl(current)) builder.Append(' ');
                else if (!char.IsSurrogate(current)) builder.Append(current);
                else if (char.IsHighSurrogate(current) && index + 1 < value.Length &&
                         char.IsLowSurrogate(value[index + 1]) &&
                         builder.Length + 2 <= PortalContractLimits.MaximumNameLength)
                {
                    builder.Append(current);
                    builder.Append(value[++index]);
                }
            }
            string result = builder.ToString().Trim();
            return result.Length == 0 ? fallback : result;
        }
    }
}
