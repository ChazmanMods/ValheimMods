using System;
using System.Collections.Generic;
using RunicInventory.Api;

namespace RunicInventory.Tests
{
    internal static class ApiContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("topology API copies and sorts every input collection", CopiesInputs);
            TestRunner.Run("topology API exposes exactly five equipment and three quick roles", ExactRoleSet);
            TestRunner.Run("topology API rejects duplicate roles and coordinates", DuplicateFactsRejected);
            TestRunner.Run("topology API enforces every canonical bottom-row coordinate", CanonicalCoordinatesRequired);
            TestRunner.Run("topology API rejects more than 128 native slots", SlotBoundIsExact);
            TestRunner.Run("topology API rejects live-looking or unsafe fingerprints", FingerprintsAreExactHex);
            TestRunner.Run("empty roles cannot carry hidden item facts", EmptyRolesCannotDisclose);
            TestRunner.Run("topology API cross-checks role occupancy and lock facts", CrossFactsMustAgree);
            TestRunner.Run("feature status reason codes are bounded identifiers", StatusCodesAreSafe);
            TestRunner.Run("topology authority mode reports dedicated compatibility explicitly", RemoteModeIsExplicit);
        }

        private static void CopiesInputs()
        {
            var roles = Roles(3);
            roles[7] = new InventoryRoleSnapshot(
                InventoryRoleKind.Quick3, new InventorySlotCoordinate(7, 3),
                false, false, true, 0, string.Empty, string.Empty);
            var locks = new List<InventorySlotCoordinate>
            {
                new InventorySlotCoordinate(7, 3),
                new InventorySlotCoordinate(0, 0)
            };
            var snapshot = Snapshot(roles, locks, InventoryAuthorityMode.AuthoritativeLocal, 4);
            roles.Clear();
            locks.Clear();
            TestAssert.Equal(8, snapshot.Roles.Count);
            TestAssert.Equal(2, snapshot.LockedSlots.Count);
            TestAssert.Equal(new InventorySlotCoordinate(0, 0), snapshot.LockedSlots[0]);
            TestAssert.Throws<NotSupportedException>(() => ((IList<InventoryRoleSnapshot>)snapshot.Roles).Add(snapshot.Roles[0]));
        }

        private static void ExactRoleSet()
        {
            InventoryTopologySnapshot snapshot = Snapshot(Roles(3), new List<InventorySlotCoordinate>(),
                InventoryAuthorityMode.AuthoritativeLocal, 4);
            for (int index = 0; index < snapshot.Roles.Count; index++)
                TestAssert.Equal((InventoryRoleKind)(index + 1), snapshot.Roles[index].Role);
        }

        private static void DuplicateFactsRejected()
        {
            List<InventoryRoleSnapshot> roles = Roles(3);
            roles[7] = new InventoryRoleSnapshot(
                InventoryRoleKind.Quick3,
                new InventorySlotCoordinate(6, 3),
                false, false, false, 0, string.Empty, string.Empty);
            TestAssert.Throws<ArgumentException>(() => Snapshot(roles, new List<InventorySlotCoordinate>(),
                InventoryAuthorityMode.AuthoritativeLocal, 4));
            TestAssert.Throws<ArgumentException>(() => Snapshot(Roles(3), new[]
            {
                new InventorySlotCoordinate(1, 1), new InventorySlotCoordinate(1, 1)
            }, InventoryAuthorityMode.AuthoritativeLocal, 4));
        }

        private static void SlotBoundIsExact()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(() => Snapshot(Roles(16),
                new List<InventorySlotCoordinate>(), InventoryAuthorityMode.AuthoritativeLocal, 17));
        }

        private static void CanonicalCoordinatesRequired()
        {
            List<InventoryRoleSnapshot> roles = Roles(3);
            roles[0] = new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(1, 2),
                false, false, false, 0, string.Empty, string.Empty);
            TestAssert.Throws<ArgumentException>(() => Snapshot(
                roles, Array.Empty<InventorySlotCoordinate>(), InventoryAuthorityMode.AuthoritativeLocal, 4));
        }

        private static void FingerprintsAreExactHex()
        {
            TestAssert.Throws<ArgumentException>(() => new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(0, 3), true, true, false, 1,
                "Helmet", new string('Z', 64)));
            TestAssert.Throws<ArgumentException>(() => new InventoryTopologySnapshot(
                "runic.inventory", "1.0", 1, InventoryAuthorityMode.AuthoritativeLocal,
                8, 4, 0, true, Roles(3), Array.Empty<InventorySlotCoordinate>(), new string('Z', 64)));
            TestAssert.Throws<ArgumentException>(() => new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(0, 3), true, true, false, 1,
                "<color=red>Helmet", new string('0', 64)));
        }

        private static void EmptyRolesCannotDisclose()
        {
            TestAssert.Throws<ArgumentException>(() => new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(0, 3), false, false, false, 1,
                "Helmet", new string('0', 64)));
        }

        private static void CrossFactsMustAgree()
        {
            List<InventoryRoleSnapshot> lockedRole = Roles(3);
            lockedRole[0] = new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(0, 3),
                false, false, true, 0, string.Empty, string.Empty);
            TestAssert.Throws<ArgumentException>(() => Snapshot(
                lockedRole, Array.Empty<InventorySlotCoordinate>(), InventoryAuthorityMode.AuthoritativeLocal, 4));

            List<InventoryRoleSnapshot> occupiedRole = Roles(3);
            occupiedRole[0] = new InventoryRoleSnapshot(
                InventoryRoleKind.Head, new InventorySlotCoordinate(0, 3),
                true, true, false, 1, "Helmet", new string('0', 64));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new InventoryTopologySnapshot(
                "runic.inventory", "1.0", 1, InventoryAuthorityMode.AuthoritativeLocal,
                8, 4, 0, true, occupiedRole, Array.Empty<InventorySlotCoordinate>(), new string('0', 64)));
        }

        private static void StatusCodesAreSafe()
        {
            TestAssert.Throws<ArgumentException>(() => new InventoryFeatureStatus(
                InventoryAuthorityMode.Unavailable, false, "<color=red>bad"));
            TestAssert.Throws<ArgumentException>(() => new InventoryFeatureStatus(
                InventoryAuthorityMode.Unavailable, false, new string('x', 97)));
        }

        private static void RemoteModeIsExplicit()
        {
            InventoryTopologySnapshot snapshot = Snapshot(Roles(3), Array.Empty<InventorySlotCoordinate>(),
                InventoryAuthorityMode.RemoteDedicatedCompatibility, 4);
            TestAssert.Equal(InventoryAuthorityMode.RemoteDedicatedCompatibility, snapshot.AuthorityMode);
        }

        private static InventoryTopologySnapshot Snapshot(
            IEnumerable<InventoryRoleSnapshot> roles,
            IEnumerable<InventorySlotCoordinate> locks,
            InventoryAuthorityMode mode,
            int height) =>
            new InventoryTopologySnapshot(
                "runic.inventory", "1.0", 1, mode, 8, height, 0, true,
                roles, locks, new string('0', 64));

        private static List<InventoryRoleSnapshot> Roles(int row)
        {
            var result = new List<InventoryRoleSnapshot>();
            for (int index = 0; index < 8; index++)
                result.Add(new InventoryRoleSnapshot(
                    (InventoryRoleKind)(index + 1), new InventorySlotCoordinate(index, row),
                    false, false, false, 0, string.Empty, string.Empty));
            return result;
        }
    }
}
