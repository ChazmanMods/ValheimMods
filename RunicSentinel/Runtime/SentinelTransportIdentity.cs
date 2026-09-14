using System;
using System.Globalization;
using System.Reflection;
using Steamworks;

namespace RunicSentinel.Runtime
{
    internal static class SentinelTransportIdentity
    {
        private static readonly FieldInfo SteamConnectionField =
            typeof(ZSteamSocket).GetField("m_con", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool TryResolvePeer(ZNetPeer peer, out string authority, out string subject)
            => TryResolvePeer(peer, out authority, out subject, out _);

        internal static bool TryResolvePeer(ZNetPeer peer, out string authority, out string subject, out string reason)
        {
            authority = string.Empty;
            subject = string.Empty;
            reason = "peer-not-ready";
            if (peer == null || !peer.IsReady()) return false;
            return TryResolveConnection(peer, out authority, out subject, out reason);
        }

        internal static bool TryResolveConnection(
            ZNetPeer peer,
            out string authority,
            out string subject)
            => TryResolveConnection(peer, out authority, out subject, out _);

        private static bool TryResolveConnection(ZNetPeer peer, out string authority, out string subject, out string reason)
        {
            authority = string.Empty;
            subject = string.Empty;
            if (!SentinelSocketTransport.TryResolve(peer, out ISocket transport, out reason)) return false;
            if (transport is ZSteamSocket steam)
            {
                if (!TrySteam(steam, out subject, out reason)) return false;
                authority = "steam";
                return true;
            }
            reason = "playfab-identity-unavailable";
            if (transport is ZPlayFabSocket playFab && TryPlayFab(playFab, out subject))
            { authority = "playfab.entity"; reason = string.Empty; return true; }
            return false;
        }

        internal static bool TryResolveLocal(out string authority, out string subject)
        {
            authority = string.Empty;
            subject = string.Empty;
            try
            {
                ulong id = SteamUser.GetSteamID().m_SteamID;
                if (id == 0UL) return false;
                authority = "steam";
                subject = id.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        internal static bool TryGetHandle(ZSteamSocket steam, out HSteamNetConnection handle)
        {
            handle = HSteamNetConnection.Invalid;
            if (steam == null || SteamConnectionField?.FieldType != typeof(HSteamNetConnection)) return false;
            if (!(SteamConnectionField.GetValue(steam) is HSteamNetConnection value) || value == HSteamNetConnection.Invalid)
                return false;
            handle = value;
            return true;
        }

        private static bool TrySteam(ZSteamSocket steam, out string subject, out string reason)
        {
            subject = string.Empty;
            reason = "steam-connection-field-unavailable";
            try
            {
                if (!TryGetHandle(steam, out HSteamNetConnection handle)) return false;
                string host = steam.GetHostName();
                string peer = steam.GetPeerID().ToString();
                reason = "steam-connection-info-unavailable";
                if (!TryConnectionInfo(handle, out SteamNetConnectionInfo_t info)) return false;
                reason = "steam-connection-not-connected";
                if (info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected) return false;
                string remote = info.m_identityRemote.GetSteamID().ToString();
                const int unauthenticatedFlag = 1;
                reason = "steam-identity-mismatch";
                if (!Canonical(host, out ulong a) || !Canonical(peer, out ulong b) ||
                    !Canonical(remote, out ulong c) || a != b || b != c) return false;
                reason = "steam-session-rejected";
                if (SentinelSteamSessions.IsRejected(b)) return false;
                // A missing transport certificate is not a rejected Steam session ticket.
                // Require BOTH native acceptance and Steam's asynchronous successful verdict,
                // bound to this exact socket/handle. All three account identities still must agree.
                if ((info.m_nFlags & unauthenticatedFlag) != 0 &&
                    !SentinelSteamSessions.TryValidate(steam, handle, b, out reason)) return false;
                subject = b.ToString(CultureInfo.InvariantCulture);
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) { reason = "steam-identity-error:" + exception.GetType().Name; return false; }
        }

        private static bool TryConnectionInfo(HSteamNetConnection handle, out SteamNetConnectionInfo_t info)
        {
            info = default;
            try
            {
                return ZNet.instance != null && ZNet.instance.IsDedicated()
                    ? SteamGameServerNetworkingSockets.GetConnectionInfo(handle, out info)
                    : SteamNetworkingSockets.GetConnectionInfo(handle, out info);
            }
            catch { info = default; return false; }
        }

        private static bool TryPlayFab(ZPlayFabSocket socket, out string subject)
        {
            subject = string.Empty;
            try
            {
                FieldInfo field = typeof(ZPlayFabSocket).GetField(
                    "m_remotePlayerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string value = field?.GetValue(socket) as string;
                if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Trim() != value)
                    return false;
                subject = value;
                return true;
            }
            catch { return false; }
        }

        private static bool Canonical(string value, out ulong parsed) =>
            ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
            parsed != 0UL && parsed.ToString(CultureInfo.InvariantCulture) == value;
    }
}
