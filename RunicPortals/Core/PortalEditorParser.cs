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
        internal const int MaximumVanillaTagLength = 10;

        private PortalEditCommand(
            PortalEditKind kind,
            string networkId,
            string displayName,
            PortalNetworkKind networkKind,
            string groupId,
            bool acceptsArrival,
            bool permitsDeparture,
            bool hasVanillaTag,
            string vanillaTag,
            string error)
        {
            Kind = kind;
            NetworkId = networkId;
            DisplayName = displayName;
            NetworkKind = networkKind;
            GroupId = groupId;
            AcceptsArrival = acceptsArrival;
            PermitsDeparture = permitsDeparture;
            HasVanillaTag = hasVanillaTag;
            VanillaTag = vanillaTag ?? string.Empty;
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
        internal bool HasVanillaTag { get; }
        internal string VanillaTag { get; }
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
                HasVanillaTag,
                VanillaTag,
                string.Empty);
        }

        internal static PortalEditCommand CreateStandard(string vanillaTag)
        {
            try
            {
                return new PortalEditCommand(
                    PortalEditKind.StandardPair,
                    string.Empty,
                    string.Empty,
                    PortalNetworkKind.Custom,
                    string.Empty,
                    true,
                    true,
                    true,
                    NormalizeVanillaTag(vanillaTag),
                    string.Empty);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception.Message);
            }
        }

        internal static PortalEditCommand CreateNetwork(
            string networkId,
            string displayName,
            PortalNetworkKind networkKind,
            string groupId,
            bool acceptsArrival,
            bool permitsDeparture) => CreateNetwork(
                networkId,
                displayName,
                networkKind,
                groupId,
                acceptsArrival,
                permitsDeparture,
                false);

        private static PortalEditCommand CreateNetwork(
            string networkId,
            string displayName,
            PortalNetworkKind networkKind,
            string groupId,
            bool acceptsArrival,
            bool permitsDeparture,
            bool allowActiveGroupFallback)
        {
            try
            {
                if (networkKind != PortalNetworkKind.Public &&
                    networkKind != PortalNetworkKind.Personal &&
                    networkKind != PortalNetworkKind.Group)
                    return Invalid("Access must be Public, Private, or Group.");
                if (!acceptsArrival && !permitsDeparture)
                    return Invalid("Choose Both, Arrivals only, or Departures only.");
                ValidateRawEditorText(networkId, "Network names");
                ValidateRawEditorText(displayName, "Portal names");
                ValidateRawEditorText(groupId, "Group selections");
                string network = PortalText.Require(
                    networkId,
                    PortalContractLimits.MaximumNetworkIdLength,
                    nameof(networkId));
                string name = PortalText.Require(
                    displayName,
                    PortalContractLimits.MaximumNameLength,
                    nameof(displayName));
                if (network.IndexOf('|') >= 0 || name.IndexOf('|') >= 0)
                    return Invalid("Network and portal names cannot contain the | character.");
                string group = (groupId ?? string.Empty).Trim();
                if (networkKind == PortalNetworkKind.Group)
                {
                    if (group.Length == 0 && !allowActiveGroupFallback ||
                        group.Length != 0 && !GroupIdentity.IsCanonicalId(group))
                        return Invalid("Choose one of your current Runic Groups.");
                }
                else if (group.Length != 0)
                {
                    return Invalid("Only Group access may include a Group.");
                }
                return new PortalEditCommand(
                    PortalEditKind.PublicNetwork,
                    network,
                    name,
                    networkKind,
                    group,
                    acceptsArrival,
                    permitsDeparture,
                    false,
                    string.Empty,
                    string.Empty);
            }
            catch (ArgumentException exception)
            {
                return Invalid(EditorError(exception));
            }
        }

        internal static PortalEditCommand Parse(string text)
        {
            string raw = text ?? string.Empty;
            if (raw.Length > 256)
                return Invalid(Usage());
            string normalized = raw.Trim();
            if (string.Equals(normalized, "standard", StringComparison.OrdinalIgnoreCase))
                return new PortalEditCommand(PortalEditKind.StandardPair, string.Empty, string.Empty,
                    PortalNetworkKind.Custom, string.Empty, true, true, false, string.Empty,
                    string.Empty);
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
                return CreateNetwork(
                    parts[networkIndex],
                    parts[nameIndex],
                    kind,
                    groupId,
                    arrive,
                    depart,
                    kind == PortalNetworkKind.Group && groupId.Length == 0);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception.Message);
            }
        }

        private static void ValidateRawEditorText(string value, string label)
        {
            string raw = value ?? string.Empty;
            for (int index = 0; index < raw.Length; index++)
            {
                char character = raw[index];
                if (char.IsControl(character))
                    throw new ArgumentException(label + " cannot contain control characters.");
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || index + 1 >= raw.Length ||
                    !char.IsLowSurrogate(raw[index + 1]))
                    throw new ArgumentException(label + " contain malformed text.");
                index++;
            }
        }

        private static PortalEditCommand Invalid(string error) =>
            new PortalEditCommand(PortalEditKind.Invalid, string.Empty, string.Empty,
                PortalNetworkKind.Custom, string.Empty, false, false, false, string.Empty,
                error ?? "Invalid portal command.");

        private static string NormalizeVanillaTag(string value)
        {
            string tag = value ?? string.Empty;
            if (tag.Length > MaximumVanillaTagLength)
                throw new ArgumentException(
                    "A Standard Pair tag can contain at most 10 characters.");
            for (int index = 0; index < tag.Length; index++)
            {
                char character = tag[index];
                if (char.IsControl(character))
                    throw new ArgumentException(
                        "A Standard Pair tag cannot contain control characters.");
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || index + 1 >= tag.Length ||
                    !char.IsLowSurrogate(tag[index + 1]))
                    throw new ArgumentException(
                        "A Standard Pair tag contains malformed text.");
                index++;
            }
            return tag;
        }

        private static string EditorError(ArgumentException exception)
        {
            if (exception is ArgumentOutOfRangeException)
                return "Network and portal names can contain at most 64 characters.";
            return exception.Message.StartsWith("A value is required", StringComparison.Ordinal)
                ? "Enter both a network name and a portal name."
                : exception.Message;
        }

        private static string Usage() =>
            "Use network|NETWORK|NAME|DIRECTION, network|public|NETWORK|NAME|DIRECTION, " +
            "network|private|NETWORK|NAME|DIRECTION, " +
            "network|group|NETWORK|NAME|DIRECTION, or standard. Select the Group in chat first.";
    }
}
