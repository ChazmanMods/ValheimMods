using System;
using System.Collections.Generic;

namespace RunicCrafting.Domain
{
    internal static class AreaRepairPolicy
    {
        internal static float Radius(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 50f : Math.Max(1f, Math.Min(100f, value));

        internal static bool Eligible(bool hammerPiece, bool networkReady, bool inRange,
            bool wardAccess, bool containerAccess, bool stationAccess) =>
            hammerPiece && networkReady && inRange && wardAccess && containerAccess && stationAccess;
    }

    // Bounded work queue: each pulse examines at most 32 pieces and submits at most eight repairs.
    internal sealed class AreaRepairBatch<T>
    {
        internal const int MaximumPieces = 32768;
        private readonly Queue<T> _pieces = new Queue<T>();
        internal int Count => _pieces.Count;
        internal bool Add(T piece)
        {
            if (_pieces.Count >= MaximumPieces) return false;
            _pieces.Enqueue(piece); return true;
        }
        internal void Clear() => _pieces.Clear();
        internal int Step(Func<T, bool> repair)
        {
            int submitted = 0;
            for (int checkedCount = 0; checkedCount < 32 && submitted < 8 && _pieces.Count > 0; checkedCount++)
                if (repair(_pieces.Dequeue())) submitted++;
            return submitted;
        }
    }
}
