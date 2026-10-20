using System;
using System.Collections.Generic;
using RunicCrafting.Domain;
namespace RunicCrafting.Tests
{
    internal static class StorageReadinessTests
    {
        internal static void MissingAndUnavailableAreDifferent()
        {
            var resources = new HashSet<string>(StringComparer.Ordinal) { "Wood" };
            if (MaterialFailureReason.Classify("missing:Wood", resources) != "storage-not-ready:Wood") throw new Exception("Unavailable wood reported missing");
            if (MaterialFailureReason.Classify("missing:Stone", resources) != "missing:Stone") throw new Exception("Unrelated chest obscured shortage");
            if (MaterialFailureReason.Classify("station-use:denied", resources) != "station-use:denied") throw new Exception("Permission denial obscured");
            resources.Clear();
            if (MaterialFailureReason.Classify("missing:Wood", resources) != "missing:Wood") throw new Exception("Real shortage obscured");
        }
    }
}
