using System;
using System.Globalization;

namespace RunicSentinel.Core
{
    internal static class SentinelAdministratorRules
    {
        internal static bool Allows(bool signedAdministrator, bool serverAdministrator, bool banned) =>
            !banned && (signedAdministrator || serverAdministrator);

        // The subject must come from the authenticated Steam socket, never request data.
        internal static bool MatchesSteamList(string authority, string subject, Func<string, bool> contains)
        {
            if (authority != "steam" || contains == null ||
                !ulong.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ||
                id == 0 || id.ToString(CultureInfo.InvariantCulture) != subject) return false;
            return contains(subject) || contains("Steam_" + subject) || contains("V_" + subject);
        }

        internal static bool CanInitialize(bool serverAdministrator, bool hasTrustMaterial,
            bool snapshotReady, bool banned) =>
            serverAdministrator && !hasTrustMaterial && snapshotReady && !banned;
    }
}
