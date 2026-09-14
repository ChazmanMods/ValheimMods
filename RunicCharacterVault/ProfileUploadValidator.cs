using System;
using System.IO;

namespace RunicCharacterVault
{
    internal static class ProfileUploadValidator
    {
        internal static void Validate(VaultSession session, byte[] data)
        {
            string filename = "character_vault_validation_" + Guid.NewGuid().ToString("N");
            FileHelpers.FileSource source = SaveApiCompatibility.LocalSource;
            string path = SaveApiCompatibility.GetCharacterPath(source, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            try
            {
                PlayerProfile profile = new PlayerProfile(filename, source);
                if (!profile.Load() || profile.GetPlayerID() != session.CharacterId ||
                    !string.Equals(profile.GetName(), session.Name, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The uploaded profile identity is invalid.");
                }
            }
            finally
            {
                File.Delete(path);
                SaveApiCompatibility.InvalidateCharacterCache();
            }
        }
    }
}
