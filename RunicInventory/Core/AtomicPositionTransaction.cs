using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RunicInventory.Core
{
    internal sealed class PositionChange<TItem, TPosition> where TItem : class
    {
        internal PositionChange(TItem item, TPosition original, TPosition destination)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Original = original;
            Destination = destination;
        }

        internal TItem Item { get; }
        internal TPosition Original { get; }
        internal TPosition Destination { get; }
    }

    internal static class AtomicPositionTransaction
    {
        internal const int MaximumChanges = 128;

        internal static bool TryCommit<TItem, TPosition>(
            IReadOnlyList<PositionChange<TItem, TPosition>> changes,
            Action<TItem, TPosition> assign,
            Func<bool> verify,
            Action publish,
            out Exception failure,
            out Exception rollbackFailure)
            where TItem : class
        {
            failure = null;
            rollbackFailure = null;
            if (!Validate(changes, assign, verify, publish, out failure)) return false;

            try
            {
                for (int index = 0; index < changes.Count; index++)
                    assign(changes[index].Item, changes[index].Destination);
                if (!verify()) throw new InvalidOperationException("Position transaction verification failed.");
                publish();
                return true;
            }
            catch (Exception exception)
            {
                failure = exception;
                var rollbackFaults = new List<Exception>();
                for (int index = 0; index < changes.Count; index++)
                {
                    PositionChange<TItem, TPosition> change = changes[index];
                    try { assign(change.Item, change.Original); }
                    catch (Exception restore) { rollbackFaults.Add(restore); }
                }
                try { publish(); }
                catch (Exception publication) { rollbackFaults.Add(publication); }
                if (rollbackFaults.Count == 1) rollbackFailure = rollbackFaults[0];
                else if (rollbackFaults.Count > 1) rollbackFailure = new AggregateException(rollbackFaults);
                return false;
            }
        }

        private static bool Validate<TItem, TPosition>(
            IReadOnlyList<PositionChange<TItem, TPosition>> changes,
            Action<TItem, TPosition> assign,
            Func<bool> verify,
            Action publish,
            out Exception failure)
            where TItem : class
        {
            failure = null;
            if (changes == null || assign == null || verify == null || publish == null)
            {
                failure = new ArgumentNullException("A position transaction input was null.");
                return false;
            }
            if (changes.Count == 0 || changes.Count > MaximumChanges)
            {
                failure = new ArgumentOutOfRangeException(nameof(changes));
                return false;
            }
            var unique = new HashSet<TItem>(ReferenceComparer<TItem>.Instance);
            for (int index = 0; index < changes.Count; index++)
            {
                PositionChange<TItem, TPosition> change = changes[index];
                if (change == null || change.Item == null || !unique.Add(change.Item))
                {
                    failure = new ArgumentException("Position changes must contain unique non-null item references.", nameof(changes));
                    return false;
                }
            }
            return true;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T left, T right) => ReferenceEquals(left, right);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
