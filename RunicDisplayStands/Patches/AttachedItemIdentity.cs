using System;

namespace RunicDisplayStands
{
    /// <summary>
    /// Resolves Valheim 1.0's integer prefab identity while retaining a one-time read path
    /// for stands saved by releases that stored the prefab name as a string.
    /// </summary>
    internal static class AttachedItemIdentity
    {
        internal static int Resolve(
            int currentHash,
            bool isOwner,
            Func<string> readLegacyName,
            Func<string, int> getStableHash,
            Action<int> migrateOwnedValue)
        {
            if (currentHash != 0) return currentHash;

            string legacyName = readLegacyName();
            if (string.IsNullOrEmpty(legacyName)) return 0;

            int migratedHash = getStableHash(legacyName);
            if (isOwner && migratedHash != 0)
                migrateOwnedValue?.Invoke(migratedHash);

            return migratedHash;
        }
    }
}
