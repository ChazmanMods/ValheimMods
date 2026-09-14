using System;

namespace RunicProduction.Integration
{
    internal static class ProductionInventoryPayloadComparison
    {
        internal static bool MatchesLoaded(byte[] persisted, byte[] loaded)
        {
            if (persisted == null || loaded == null || persisted.Length != loaded.Length) return false;
            if (Equal(persisted, loaded)) return true;
            try
            {
                var package = new ZPackage(persisted);
                int version = package.ReadInt();
                // Version 109 is the installed Valheim 1.0 codec. Unknown formats fail closed.
                if (version != 109) return false;
                int count = package.ReadUShort();
                var expected = (byte[])persisted.Clone();
                for (int index = 0; index < count; index++)
                {
                    int offset = package.GetPos();
                    ItemDrop.ItemData.Load(package, (Version.Item)version);
                    int encoded = BitConverter.ToInt32(persisted, offset);
                    // Exactly ONE native load/save, not an arbitrary durability tolerance.
                    float durability = (float)encoded * 0.01f;
                    byte[] normalized = BitConverter.GetBytes((int)(durability * 100f));
                    Buffer.BlockCopy(normalized, 0, expected, offset, 4);
                }
                return package.GetPos() == persisted.Length && Equal(expected, loaded);
            }
            catch { return false; }
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }
    }
}
