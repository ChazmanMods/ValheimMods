using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    /// <summary>Splits an exact quantity into chunks accepted by Valheim's one-stack AddItem API.</summary>
    public static class StackChunkPlanner
    {
        public const int MaximumChunks = 4096;

        public static IReadOnlyList<int> Plan(int amount, int maximumStackSize)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (maximumStackSize <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStackSize));
            long chunkCount = (amount + (long)maximumStackSize - 1L) / maximumStackSize;
            if (chunkCount > MaximumChunks)
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "The exact insertion exceeds the bounded output-chunk limit.");
            var chunks = new List<int>((int)chunkCount);
            int remaining = amount;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, maximumStackSize);
                chunks.Add(chunk);
                remaining -= chunk;
            }
            return chunks.AsReadOnly();
        }
    }
}
