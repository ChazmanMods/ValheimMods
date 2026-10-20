using System.Collections.Generic;
using System;
namespace RunicCrafting.Domain
{
    internal static class MaterialFailureReason
    {
        internal static string Classify(string reason, ISet<string> unavailable) =>
            reason != null && reason.StartsWith("missing:", StringComparison.Ordinal) && unavailable.Contains(reason.Substring(8))
                ? "storage-not-ready:" + reason.Substring(8) : reason;
    }
}
