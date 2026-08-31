using System;
using RunicPermissions.Groups;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal enum PortalEditKind
    {
        Invalid = 0,
        StandardPair = 1,
        PublicNetwork = 2
    }

    internal sealed class PortalEditCommand
    {
        private PortalEditCommand(
            PortalEditKind kind,
            string networkId,
            string displayName,
            PortalNetworkKind networkKind,
            string groupId,
            bool acceptsArrival,
            bool permitsDeparture,
            string error)
        {
            Kind = kind;
            NetworkId = networkId;
            DisplayName = displayName;
            NetworkKind = networkKind;
            GroupId = groupId;
            AcceptsArrival = acceptsArrival;
            PermitsDeparture = permitsDeparture;
            Error = error;
        }

        internal PortalEditKind Kind { get; }
        internal string NetworkId { get; }
        internal string DisplayName { get; }
        internal PortalNetworkKind NetworkKind { get; }
        internal string GroupId { get; }
        internal bool RequiresActiveGroup =>
            NetworkKind == PortalNetworkKind.Group && GroupId.Length == 0;
        internal bool AcceptsArrival { get; }
        internal bool PermitsDeparture { get; }
        internal string Error { get; }

        internal PortalEditCommand BindGroup(string groupId)
        {
            if (!RequiresActiveGroup)
                return this;
            if (!GroupIdentity.IsCanonicalId(groupId))
                return Invalid("Select a Runic Group in chat before configuring a Group portal.");
            return new PortalEditCommand(
                Kind,
                NetworkId,
                DisplayName,
                NetworkKind,
                groupId,
                AcceptsArrival,
                PermitsDeparture,
                string.Empty);
        }

        internal static PortalEditCommand Parse(string text)
        {
            string raw = text ?? string.Empty;
            if (raw.Length > 256)
                return Invalid(Usage());
            string normalized = raw.Trim();
            if (string.Equals(normalized, "standard", StringComparison.OrdinalIgnoreCase))
                return new PortalEditCommand(PortalEditKind.StandardPair, string.Empty, string.Empty,
                    PortalNetworkKind.Custom, string.Empty, true, true, string.Empty);
            if (normalized.Length == 0 || normalized.Length > 256)
                return Invalid(Usage());
            for (int index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index])) return Invalid("Control characters are not allowed.");
            string[] parts = normalized.Split('|');
            if (parts.Length < 4 || !string.Equals(parts[0].Trim(), "network",
                    StringComparison.OrdinalIgnoreCase))
                return Invalid(Usage());
            try
            {
                PortalNetworkKind kind = PortalNetworkKind.Public;
                string groupId = string.Empty;
                int networkIndex = 1;
                int nameIndex = 2;
                int directionIndex = 3;
                if (parts.Length == 5)
                {
                    string policy = parts[1].Trim();
                    if (string.Equals(policy, "public", StringComparison.OrdinalIgnoreCase))
                        kind = PortalNetworkKind.Public;
                    else if (string.Equals(policy, "private", StringComparison.OrdinalIgnoreCase))
                        kind = PortalNetworkKind.Personal;
                    else if (string.Equals(policy, "group", StringComparison.OrdinalIgnoreCase))
                        kind = PortalNetworkKind.Group;
                    else return Invalid(Usage());
                    networkIndex = 2;
                    nameIndex = 3;
                    directionIndex = 4;
                }
                else if (parts.Length == 6 &&
                         string.Equals(parts[1].Trim(), "group", StringComparison.OrdinalIgnoreCase))
                {
                    kind = PortalNetworkKind.Group;
                    groupId = parts[2].Trim();
                    if (!GroupIdentity.IsCanonicalId(groupId))
                        return Invalid("Group portals require the exact lowercase 32-character Group UUID.");
                    networkIndex = 3;
                    nameIndex = 4;
                    directionIndex = 5;
                }
                else if (parts.Length != 4)
                {
                    return Invalid(Usage());
                }
                string network = PortalText.Require(parts[networkIndex], PortalContractLimits.MaximumNetworkIdLength,
                    nameof(text));
                string name = PortalText.Require(parts[nameIndex], PortalContractLimits.MaximumNameLength, nameof(text));
                string direction = parts[directionIndex].Trim();
                bool arrive;
                bool depart;
                if (string.Equals(direction, "both", StringComparison.OrdinalIgnoreCase))
                {
                    arrive = true;
                    depart = true;
                }
                else if (string.Equals(direction, "arrive", StringComparison.OrdinalIgnoreCase))
                {
                    arrive = true;
                    depart = false;
                }
                else if (string.Equals(direction, "depart", StringComparison.OrdinalIgnoreCase))
                {
                    arrive = false;
                    depart = true;
                }
                else return Invalid("Direction must be both, arrive, or depart.");
                return new PortalEditCommand(PortalEditKind.PublicNetwork, network, name,
                    kind, groupId, arrive, depart, string.Empty);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception.Message);
            }
        }

        private static PortalEditCommand Invalid(string error) =>
            new PortalEditCommand(PortalEditKind.Invalid, string.Empty, string.Empty,
                PortalNetworkKind.Custom, string.Empty, false, false,
                error ?? "Invalid portal command.");

        private static string Usage() =>
            "Use network|NETWORK|NAME|DIRECTION, network|public|NETWORK|NAME|DIRECTION, " +
            "network|private|NETWORK|NAME|DIRECTION, " +
            "network|group|NETWORK|NAME|DIRECTION, or standard. Select the Group in chat first.";
    }
}
