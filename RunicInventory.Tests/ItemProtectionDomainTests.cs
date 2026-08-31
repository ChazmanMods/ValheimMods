using System;
using System.Collections.Generic;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class ItemProtectionDomainTests
    {
        internal static void Register()
        {
            TestRunner.Run("item protection false is limited to exact out-of-domain evidence", OutsideIsNotApplicable);
            TestRunner.Run("one exact governed reference is eligible for lock proof", OneReferenceIsExact);
            TestRunner.Run("duplicate governed references remain in-domain Unknown", DuplicateIsUnknown);
            TestRunner.Run("oversized domain evidence fails before item enumeration", OversizedIsConstant);
            TestRunner.Run("bounded exact domain proof allocates zero managed bytes", ExactProofAllocatesZero);
            TestRunner.Run("healthy exact members classify ordinary unlocked and protected locked", ExactMemberClassificationIsTyped);
        }

        private static void OutsideIsNotApplicable()
        {
            object governed = new object();
            TestAssert.Equal(ItemProtectionDomainEvidence.NotApplicable,
                ItemProtectionDomain.Evaluate(new[] { governed }, new object(), 128));
            TestAssert.Equal(ItemProtectionDomainEvidence.NotApplicable,
                ItemProtectionDomain.Evaluate<object>(new[] { governed }, null, 128));
            TestAssert.Equal(ItemProtectionDomainEvidence.InDomainUnknown,
                ItemProtectionDomain.Evaluate<object>(null, governed, 128));
        }

        private static void OneReferenceIsExact()
        {
            object candidate = new object();
            TestAssert.Equal(ItemProtectionDomainEvidence.ExactCurrentMember,
                ItemProtectionDomain.Evaluate(
                    new[] { new object(), candidate, new object() }, candidate, 128));
        }

        private static void DuplicateIsUnknown()
        {
            object candidate = new object();
            TestAssert.Equal(ItemProtectionDomainEvidence.InDomainUnknown,
                ItemProtectionDomain.Evaluate(new[] { candidate, candidate }, candidate, 128));
        }

        private static void OversizedIsConstant()
        {
            object candidate = new object();
            var evidence = new InstrumentedList(10_000, candidate);
            TestAssert.Equal(ItemProtectionDomainEvidence.InDomainUnknown,
                ItemProtectionDomain.Evaluate(evidence, candidate, 128));
            TestAssert.Equal(0, evidence.IndexReads);
        }

        private static void ExactProofAllocatesZero()
        {
            object candidate = new object();
            object[] evidence = { new object(), candidate, new object() };
            ItemProtectionDomain.Evaluate(evidence, candidate, 128);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10_000; index++)
                ItemProtectionDomain.Evaluate(evidence, candidate, 128);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestAssert.Equal(0L, allocated);
        }

        private static void ExactMemberClassificationIsTyped()
        {
            TestAssert.Equal(
                ItemProtectionState.Unlocked,
                ItemProtectionDomain.ClassifyExactMember(
                    occupiesSpecialRow: false,
                    explicitlyLocked: false));
            TestAssert.Equal(
                ItemProtectionState.Locked,
                ItemProtectionDomain.ClassifyExactMember(
                    occupiesSpecialRow: false,
                    explicitlyLocked: true));
            TestAssert.Equal(
                ItemProtectionState.Locked,
                ItemProtectionDomain.ClassifyExactMember(
                    occupiesSpecialRow: true,
                    explicitlyLocked: false));
        }

        private sealed class InstrumentedList : IReadOnlyList<object>
        {
            private readonly object _candidate;

            internal InstrumentedList(int count, object candidate)
            {
                Count = count;
                _candidate = candidate;
            }

            public int Count { get; }
            internal int IndexReads { get; private set; }
            public object this[int index]
            {
                get
                {
                    IndexReads++;
                    return _candidate;
                }
            }

            public IEnumerator<object> GetEnumerator() => throw new NotSupportedException();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
