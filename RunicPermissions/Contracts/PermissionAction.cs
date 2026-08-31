using System;
using System.Collections.Generic;

namespace RunicPermissions.Contracts
{
    /// <summary>Independent least-privilege actions. Values are stable protocol IDs.</summary>
    public enum PermissionAction
    {
        ViewDiscover = 1,
        InteractUse = 2,
        ConsumeLinkedMaterials = 3,
        OpenContainer = 4,
        Deposit = 5,
        Withdraw = 6,
        Configure = 7,
        LinkUnlink = 8,
        Depart = 9,
        Arrive = 10,
        Deconstruct = 11,
        TransferOwnership = 12
    }

    public static class PermissionActionCodec
    {
        private static readonly PermissionAction[] Actions =
        {
            PermissionAction.ViewDiscover,
            PermissionAction.InteractUse,
            PermissionAction.ConsumeLinkedMaterials,
            PermissionAction.OpenContainer,
            PermissionAction.Deposit,
            PermissionAction.Withdraw,
            PermissionAction.Configure,
            PermissionAction.LinkUnlink,
            PermissionAction.Depart,
            PermissionAction.Arrive,
            PermissionAction.Deconstruct,
            PermissionAction.TransferOwnership
        };

        private static readonly string[] Names =
        {
            "view.discover",
            "interact.use",
            "materials.consume-linked",
            "container.open",
            "container.deposit",
            "container.withdraw",
            "object.configure",
            "object.link-unlink",
            "portal.depart",
            "portal.arrive",
            "object.deconstruct",
            "ownership.transfer"
        };

        public static IReadOnlyList<PermissionAction> All => Array.AsReadOnly(Actions);

        public static string ToWireName(PermissionAction action)
        {
            for (int i = 0; i < Actions.Length; i++)
                if (Actions[i] == action) return Names[i];
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown permission action.");
        }

        public static bool TryParse(string wireName, out PermissionAction action)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (!string.Equals(Names[i], wireName, StringComparison.Ordinal)) continue;
                action = Actions[i];
                return true;
            }
            action = default;
            return false;
        }
    }
}
