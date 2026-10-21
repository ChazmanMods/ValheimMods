using System.IO;

namespace RunicAutomation.Operations
{
    internal static class CharacterOperationCheckpoint
    {
        private const string ReceiptKey = "runic.operations.character.v2";

        // Character data and operation metadata must land in the same character save. A separate
        // receipt file cannot prove that the inventory from an interrupted save was delivered.
        // Receipts ride in m_customData inside Valheim's ordinary character saves; inventory
        // actions never force a profile write of their own.
        internal static string Receipt(Player player) => player.m_customData.TryGetValue(ReceiptKey, out string value) ? value : "";
        internal static void SetReceipt(Player player, string value)
        {
            if (value == null || value.Length > 2 * 1024 * 1024) throw new InvalidDataException("Invalid character operation receipt");
            if (value.Length == 0) player.m_customData.Remove(ReceiptKey); else player.m_customData[ReceiptKey] = value;
        }
    }
}
