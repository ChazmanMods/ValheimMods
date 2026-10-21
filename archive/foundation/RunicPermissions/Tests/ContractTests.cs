using System;
using System.Collections.Generic;
using RunicPermissions.Contracts;

namespace RunicPermissions.Tests
{
    internal static class ContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("identity authority is canonicalized", IdentityAuthorityIsCanonicalized);
            TestRunner.Run("identity subject remains case sensitive", IdentitySubjectIsCaseSensitive);
            TestRunner.Run("identity canonical key escapes separators", IdentityCanonicalKeyEscapesSeparators);
            TestRunner.Run("invalid identity is rejected", InvalidIdentityIsRejected);
            TestRunner.Run("display snapshot is not identity", DisplaySnapshotIsNotIdentity);
            TestRunner.Run("action wire names round trip", ActionWireNamesRoundTrip);
            TestRunner.Run("unknown action wire name fails", UnknownActionWireNameFails);
            TestRunner.Run("policy wire names round trip", PolicyWireNamesRoundTrip);
            TestRunner.Run("ACL copies source collections", AccessListCopiesSources);
            TestRunner.Run("ACL resolves allow and deny independently", AccessListResolvesConflict);
            TestRunner.Run("permission profile scopes are independent", ProfileScopesAreIndependent);
            TestRunner.Run("group policy requires canonical UUID identity", GroupPolicyRequiresCanonicalUuid);
        }

        private static void IdentityAuthorityIsCanonicalized()
        {
            var left = new StableIdentity(" STEAM ", "76561198000000000");
            var right = new StableIdentity("steam", "76561198000000000");
            TestAssert.Equal(left, right);
            TestAssert.Equal("steam", left.Authority);
        }

        private static void IdentitySubjectIsCaseSensitive()
        {
            var left = new StableIdentity("crossplay", "Player-A");
            var right = new StableIdentity("crossplay", "player-a");
            TestAssert.False(left.Equals(right));
        }

        private static void IdentityCanonicalKeyEscapesSeparators()
        {
            var identity = new StableIdentity("custom", "realm:account/1");
            TestAssert.Equal("custom:realm%3Aaccount%2F1", identity.CanonicalKey);
        }

        private static void InvalidIdentityIsRejected()
        {
            TestAssert.Throws<ArgumentException>(() => new StableIdentity("Display Name", "123"));
            TestAssert.False(StableIdentity.TryCreate("steam", "\r\n", out _));
        }

        private static void DisplaySnapshotIsNotIdentity()
        {
            StableIdentity identity = new StableIdentity("steam", "123");
            IdentityClaim oldName = IdentityClaim.Verified(identity, "Old Name");
            IdentityClaim newName = IdentityClaim.Verified(identity, "New Name");
            TestAssert.Equal(oldName.Identity, newName.Identity);
            TestAssert.False(oldName.DisplayNameSnapshot == newName.DisplayNameSnapshot);
        }

        private static void ActionWireNamesRoundTrip()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PermissionAction action in PermissionActionCodec.All)
            {
                string wireName = PermissionActionCodec.ToWireName(action);
                TestAssert.True(names.Add(wireName), "Wire names must be unique.");
                TestAssert.True(PermissionActionCodec.TryParse(wireName, out PermissionAction parsed));
                TestAssert.Equal(action, parsed);
            }
            TestAssert.Equal(12, names.Count);
        }

        private static void UnknownActionWireNameFails()
        {
            TestAssert.False(PermissionActionCodec.TryParse("container.open-ish", out _));
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => PermissionActionCodec.ToWireName((PermissionAction)999));
        }

        private static void PolicyWireNamesRoundTrip()
        {
            foreach (PermissionPolicyKind kind in (PermissionPolicyKind[])Enum.GetValues(typeof(PermissionPolicyKind)))
            {
                string wireName = PermissionPolicyCodec.ToWireName(kind);
                TestAssert.True(PermissionPolicyCodec.TryParse(wireName, out PermissionPolicyKind parsed));
                TestAssert.Equal(kind, parsed);
            }
            TestAssert.False(PermissionPolicyCodec.TryParse("friends", out _));
        }

        private static void AccessListCopiesSources()
        {
            StableIdentity identity = new StableIdentity("steam", "123");
            var source = new List<StableIdentity> { identity };
            var acl = new AccessControlList(source);
            source.Clear();
            TestAssert.Equal(1, acl.Allowed.Count);
            TestAssert.True(acl.Resolve(identity).Allow);
        }

        private static void AccessListResolvesConflict()
        {
            StableIdentity identity = new StableIdentity("steam", "123");
            var acl = new AccessControlList(
                new[] { identity },
                new[] { new StableIdentity("STEAM", "123") });
            AccessOverride result = acl.Resolve(identity);
            TestAssert.True(result.Allow);
            TestAssert.True(result.Deny);
        }

        private static void ProfileScopesAreIndependent()
        {
            var profile = new PermissionProfile(
                "profile-1",
                1,
                0,
                new[]
                {
                    Pair(PermissionAction.OpenContainer, PermissionPolicyKind.Owner),
                    Pair(PermissionAction.ConsumeLinkedMaterials, PermissionPolicyKind.Approved)
                });
            TestAssert.True(profile.TryGetPolicy(PermissionAction.OpenContainer, out PermissionPolicy open));
            TestAssert.True(profile.TryGetPolicy(PermissionAction.ConsumeLinkedMaterials, out PermissionPolicy consume));
            TestAssert.Equal(PermissionPolicyKind.Owner, open.Kind);
            TestAssert.Equal(PermissionPolicyKind.Approved, consume.Kind);
            TestAssert.False(profile.TryGetPolicy(PermissionAction.Withdraw, out _));
        }

        private static void GroupPolicyRequiresCanonicalUuid()
        {
            TestAssert.False(new PermissionPolicy(PermissionPolicyKind.Group, groupId: "builders").IsWellFormed);
            TestAssert.False(new PermissionPolicy(
                PermissionPolicyKind.Group,
                groupId: "11111111-1111-1111-1111-111111111111").IsWellFormed);
            TestAssert.True(new PermissionPolicy(
                PermissionPolicyKind.Group,
                groupId: "11111111111111111111111111111111").IsWellFormed);
        }

        private static KeyValuePair<PermissionAction, PermissionPolicy> Pair(
            PermissionAction action,
            PermissionPolicyKind kind) =>
            new KeyValuePair<PermissionAction, PermissionPolicy>(action, new PermissionPolicy(kind));
    }
}
