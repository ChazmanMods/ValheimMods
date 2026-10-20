using System;

namespace RunicCrafting.Integration
{
    internal static class CraftingInventoryPayloadComparison
    {
        internal static bool MatchesLoaded(byte[] persisted, byte[] loaded)
        {
            if (persisted == null || loaded == null) return false;
            try
            {
                byte[] left = ForComparison(persisted), right = ForComparison(loaded);
                if (left.Length != right.Length) return false;
                for (int index = 0; index < left.Length; index++)
                    if (left[index] != right[index]) return false;
                return true;
            }
            catch { return false; }
        }

        internal static byte[] ForComparison(byte[] bytes) =>
            RunicAutomation.Operations.InventoryMetadataComparison.ForPayload(bytes);
    }
}
