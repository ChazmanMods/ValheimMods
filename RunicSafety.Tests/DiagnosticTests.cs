using System;
using System.Linq;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class DiagnosticTests
    {
        internal static void Register()
        {
            TestRunner.Run("diagnostics default capacity is 256", DefaultCapacity);
            TestRunner.Run("diagnostics rejects unsafe capacities", RejectsUnsafeCapacity);
            TestRunner.Run("diagnostics ring remains bounded", RingBounded);
            TestRunner.Run("diagnostics snapshot preserves chronological order", SnapshotOrder);
            TestRunner.Run("diagnostics correlation IDs are monotonic", CorrelationsMonotonic);
            TestRunner.Run("diagnostics tokens are sanitized and bounded", TokensSanitized);
            TestRunner.Run("diagnostics snapshot is read-only", SnapshotReadOnly);
            TestRunner.Run("diagnostics severity is retained", SeverityRetained);
        }

        private static void DefaultCapacity() =>
            TestAssert.Equal(256, new CorrelatedDiagnosticBuffer().Capacity);

        private static void RejectsUnsafeCapacity()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new CorrelatedDiagnosticBuffer(7));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new CorrelatedDiagnosticBuffer(4097));
        }

        private static void RingBounded()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            for (int index = 0; index < 50; index++) buffer.Record("c", "cat", "code-" + index);
            TestAssert.Equal(8, buffer.Count);
            TestAssert.Equal(8, buffer.Snapshot().Count);
        }

        private static void SnapshotOrder()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            for (int index = 0; index < 12; index++) buffer.Record("c", "cat", "c" + index);
            long[] sequence = buffer.Snapshot().Select(entry => entry.Sequence).ToArray();
            TestAssert.True(sequence.SequenceEqual(sequence.OrderBy(value => value)));
            TestAssert.Equal(8, sequence.Length);
        }

        private static void CorrelationsMonotonic()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            string first = buffer.NewCorrelationId("backup");
            string second = buffer.NewCorrelationId("backup");
            TestAssert.NotEqual(first, second);
            TestAssert.True(string.CompareOrdinal(first, second) < 0);
        }

        private static void TokensSanitized()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            buffer.Record(new string('x', 100), "private contents!", "C:\\secret\\world.db");
            SafetyDiagnosticEvent entry = buffer.Snapshot()[0];
            TestAssert.True(entry.CorrelationId.Length <= 64);
            TestAssert.False(entry.Category.Contains(" ", StringComparison.Ordinal));
            TestAssert.False(entry.Code.Contains("\\", StringComparison.Ordinal));
        }

        private static void SnapshotReadOnly()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            buffer.Record("c", "cat", "code");
            TestAssert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<SafetyDiagnosticEvent>)buffer.Snapshot()).Clear());
        }

        private static void SeverityRetained()
        {
            var buffer = new CorrelatedDiagnosticBuffer(8);
            buffer.Record("c", "cat", "fail", SafetyDiagnosticSeverity.Error);
            TestAssert.Equal(SafetyDiagnosticSeverity.Error, buffer.Snapshot()[0].Severity);
        }
    }
}
