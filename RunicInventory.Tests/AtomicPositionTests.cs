using System;
using System.Collections.Generic;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class AtomicPositionTests
    {
        private sealed class Item
        {
            internal Item(int position) { Position = position; }
            internal int Position { get; set; }
        }

        internal static void Register()
        {
            TestRunner.Run("atomic position commit publishes one verified permutation", Success);
            TestRunner.Run("verification failure restores every original position", VerificationFailureRollsBack);
            TestRunner.Run("commit publication failure restores and republishes originals", PublicationFailureRollsBack);
            TestRunner.Run("mid-commit exception restores already and not-yet assigned items", AssignmentFailureRollsBack);
            TestRunner.Run("rollback publication failure is reported without hiding restored positions", RollbackPublicationIsReported);
            TestRunner.Run("owning-client publication fault restores the complete topology", OwningClientPublicationFaultRollsBack);
            TestRunner.Run("duplicate and oversized position plans fail before mutation", InvalidPlansFailBeforeMutation);
        }

        private static void Success()
        {
            Item first = new Item(0);
            Item second = new Item(1);
            int publications = 0;
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((first, 0, 1), (second, 1, 0)),
                (item, value) => item.Position = value,
                () => first.Position == 1 && second.Position == 0,
                () => publications++,
                out Exception failure,
                out Exception rollback);
            TestAssert.True(committed);
            TestAssert.True(failure == null && rollback == null);
            TestAssert.Equal(1, publications);
            TestAssert.Equal(1, first.Position);
            TestAssert.Equal(0, second.Position);
        }

        private static void VerificationFailureRollsBack()
        {
            Item first = new Item(4);
            Item second = new Item(5);
            int publications = 0;
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((first, 4, 5), (second, 5, 4)),
                (item, value) => item.Position = value,
                () => false,
                () => publications++,
                out Exception failure,
                out Exception rollback);
            TestAssert.False(committed);
            TestAssert.NotNull(failure);
            TestAssert.True(rollback == null);
            TestAssert.Equal(1, publications);
            TestAssert.Equal(4, first.Position);
            TestAssert.Equal(5, second.Position);
        }

        private static void PublicationFailureRollsBack()
        {
            Item item = new Item(2);
            int publications = 0;
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((item, 2, 7)),
                (target, value) => target.Position = value,
                () => item.Position == 7,
                () => { if (++publications == 1) throw new InvalidOperationException("commit publish"); },
                out Exception failure,
                out Exception rollback);
            TestAssert.False(committed);
            TestAssert.NotNull(failure);
            TestAssert.True(rollback == null);
            TestAssert.Equal(2, publications);
            TestAssert.Equal(2, item.Position);
        }

        private static void AssignmentFailureRollsBack()
        {
            Item first = new Item(10);
            Item second = new Item(11);
            int forwardAssignments = 0;
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((first, 10, 11), (second, 11, 10)),
                (item, value) =>
                {
                    if (value != 10 && value != 11) throw new InvalidOperationException("unexpected");
                    if (forwardAssignments++ == 1 && ReferenceEquals(item, second) && value == 10)
                        throw new InvalidOperationException("injected assignment fault");
                    item.Position = value;
                },
                () => true,
                () => { },
                out Exception failure,
                out _);
            TestAssert.False(committed);
            TestAssert.NotNull(failure);
            TestAssert.Equal(10, first.Position);
            TestAssert.Equal(11, second.Position);
        }

        private static void RollbackPublicationIsReported()
        {
            Item item = new Item(1);
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((item, 1, 9)),
                (target, value) => target.Position = value,
                () => false,
                () => throw new InvalidOperationException("rollback publish"),
                out _,
                out Exception rollback);
            TestAssert.False(committed);
            TestAssert.NotNull(rollback);
            TestAssert.Equal(1, item.Position);
        }

        private static void OwningClientPublicationFaultRollsBack()
        {
            Item first = new Item(20);
            Item second = new Item(21);
            Item third = new Item(22);
            int publications = 0;
            bool committed = AtomicPositionTransaction.TryCommit(
                Plan((first, 20, 22), (second, 21, 20), (third, 22, 21)),
                (item, value) => item.Position = value,
                () => first.Position == 22 && second.Position == 20 && third.Position == 21,
                () =>
                {
                    publications++;
                    if (publications == 1) throw new InvalidOperationException("injected owner-client publication fault");
                },
                out Exception failure,
                out Exception rollback);

            TestAssert.False(committed);
            TestAssert.NotNull(failure);
            TestAssert.True(rollback == null);
            TestAssert.Equal(2, publications);
            TestAssert.Equal(20, first.Position);
            TestAssert.Equal(21, second.Position);
            TestAssert.Equal(22, third.Position);
        }

        private static void InvalidPlansFailBeforeMutation()
        {
            Item duplicate = new Item(0);
            int assignments = 0;
            TestAssert.False(AtomicPositionTransaction.TryCommit(
                Plan((duplicate, 0, 1), (duplicate, 0, 2)),
                (item, value) => { assignments++; item.Position = value; },
                () => true,
                () => { },
                out _, out _));
            TestAssert.Equal(0, assignments);

            var oversized = new List<PositionChange<Item, int>>();
            for (int index = 0; index <= AtomicPositionTransaction.MaximumChanges; index++)
            {
                Item item = new Item(index);
                oversized.Add(new PositionChange<Item, int>(item, index, index + 1));
            }
            TestAssert.False(AtomicPositionTransaction.TryCommit(
                oversized,
                (item, value) => { assignments++; item.Position = value; },
                () => true,
                () => { },
                out _, out _));
            TestAssert.Equal(0, assignments);
        }

        private static IReadOnlyList<PositionChange<Item, int>> Plan(params (Item item, int before, int after)[] values)
        {
            var result = new List<PositionChange<Item, int>>(values.Length);
            foreach ((Item item, int before, int after) in values)
                result.Add(new PositionChange<Item, int>(item, before, after));
            return result;
        }
    }
}
