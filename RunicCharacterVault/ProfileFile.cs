using System;
using System.IO;
using System.Globalization;
using BepInEx;
using Splatform;

namespace RunicCharacterVault
{
    internal static class ProfileFile
    {
        internal static byte[] Read(PlayerProfile profile)
        {
            FileReader reader = new FileReader(profile.GetPath(), profile.m_fileSource);
            try
            {
                Stream stream = reader.m_binary.BaseStream;
                stream.Position = 0;
                byte[] data = new byte[stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The character profile ended unexpectedly.");
                    }

                    offset += read;
                }

                return data;
            }
            finally
            {
                reader.Dispose();
            }
        }

        internal static PlayerProfile ReplaceSelected(byte[] data)
        {
            PlayerProfile selected = Game.instance.GetPlayerProfile();
            string path = selected.GetPath();
            BackupSelectedProfile(selected, path);
            string next = path + ".runic-vault-new";
            Write(next, selected.m_fileSource, data);
            SaveApiCompatibility.ReplaceOldFile(path, next, selected.m_fileSource);
            SaveApiCompatibility.InvalidateCharacterCache();
            PlayerProfile loaded = new PlayerProfile(selected.GetFilename(), selected.m_fileSource);
            if (!loaded.Load())
            {
                throw new InvalidDataException("Valheim rejected the authoritative server profile.");
            }

            return loaded;
        }

        internal static void BackupSelectedProfile(PlayerProfile selected, string path)
        {
            try
            {
                byte[] current = Read(selected);
                string directory = Path.Combine(
                    Paths.ConfigPath, "RunicCharacterVault", "client-backups");
                Directory.CreateDirectory(directory);
                string stamp = DateTime.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
                string name = VaultStorage.SafeSegment(selected.GetFilename());
                string backup = Path.Combine(directory, $"{name}_{stamp}.fch");
                using (FileStream stream = new FileStream(backup, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                {
                    stream.Write(current, 0, current.Length);
                    stream.Flush(true);
                }
                if (VaultStorage.Hash(File.ReadAllBytes(backup)) != VaultStorage.Hash(current))
                    throw new IOException("Local character backup verification failed.");
                BackupRetention.Apply(directory, name);
            }
            catch (Exception exception)
            {
                CharacterVaultPlugin.Log?.LogWarning(
                    "Could not create the local pre-download character backup: " + exception.Message);
                throw new IOException("Character Vault stopped because it could not preserve your local character backup.", exception);
            }
        }

        private static void Write(string path, FileHelpers.FileSource source, byte[] data)
        {
            FileWriter writer = SaveApiCompatibility.CreateWriter(path, source);
            writer.m_binary.Write(data);
            writer.Finish();
            if (writer.Status != FileWriter.WriterStatus.CloseSucceeded)
            {
                throw new IOException("The authoritative character profile could not be written.");
            }
        }
    }
}
