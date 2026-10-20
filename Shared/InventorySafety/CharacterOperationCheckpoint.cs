using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace RunicAutomation.Operations
{
    internal static class CharacterOperationCheckpoint
    {
        private static readonly FieldInfo PlayerData = AccessTools.Field(typeof(PlayerProfile), "m_playerData");
        private const string ReceiptKey = "runic.operations.character.v2";

        // Character data and operation metadata must land in the same character save. A separate
        // receipt file cannot prove that the inventory from an interrupted save was delivered.
        internal static string Receipt(Player player) => player.m_customData.TryGetValue(ReceiptKey, out string value) ? value : "";
        internal static void SetReceipt(Player player, string value)
        {
            if (value == null || value.Length > 2 * 1024 * 1024) throw new InvalidDataException("Invalid character operation receipt");
            if (value.Length == 0) player.m_customData.Remove(ReceiptKey); else player.m_customData[ReceiptKey] = value;
        }

        internal static bool SaveAndVerify(Player player, out string error)
        {
            error = "";
            try
            {
                if (player == null || player != Player.m_localPlayer || Game.instance == null ||
                    SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveCharacter))
                { error = "character-save-unavailable"; return false; }
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null || string.IsNullOrEmpty(profile.GetFilename()))
                { error = "character-profile-unavailable"; return false; }
                profile.SavePlayerData(player);
                byte[] expected = (byte[])PlayerData.GetValue(profile);
                bool saved;
                using (CharacterCheckpointPresentation.Enter()) saved = expected != null && profile.Save();
                if (!saved) { error = "character-save-failed"; return false; }
                // Native Save() returns true even for its cloud-failure fallback path. Re-open the
                // actual active profile and compare its persisted character block before sending.
                var readback = new PlayerProfile(profile.GetFilename(), profile.m_fileSource);
                if (!readback.Load() || !OwnerOperationQueue.Same(expected, (byte[])PlayerData.GetValue(readback)))
                { error = "character-save-readback-mismatch"; return false; }
                CharacterCheckpointPresentation.Verified(profile, expected);
                return true;
            }
            catch (Exception exception) { error = "character-save-error:" + exception.GetType().Name; return false; }
        }
    }
}
