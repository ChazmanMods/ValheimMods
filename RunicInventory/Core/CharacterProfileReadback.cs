using System;
using System.Security.Cryptography;
using System.Text;

namespace RunicInventory.Core
{
    internal sealed class CharacterProfileReadbackEvidence
    {
        internal CharacterProfileReadbackEvidence(
            long playerId,
            int outerPackageBytes,
            int playerDataBytes,
            string outerSha512,
            string playerDataSha256)
        {
            PlayerId = playerId;
            OuterPackageBytes = outerPackageBytes;
            PlayerDataBytes = playerDataBytes;
            OuterSha512 = outerSha512 ?? string.Empty;
            PlayerDataSha256 = playerDataSha256 ?? string.Empty;
        }

        internal long PlayerId { get; }
        internal int OuterPackageBytes { get; }
        internal int PlayerDataBytes { get; }
        internal string OuterSha512 { get; }
        internal string PlayerDataSha256 { get; }
    }

    // Pure verifier for the exact character-file format written by Valheim 1.0.7.
    // Reading the current file source and proving a clean storage-provider commit remain the
    // caller's responsibility; this type never treats PlayerProfile.Save() as acknowledgement.
    internal static class CharacterProfileReadback
    {
        internal const int CurrentProfileVersion = 46;
        internal const int CurrentStatCount = 205;
        internal const int CurrentProfileSetCount = 10;
        internal const int CurrentEnemyStatSetCount = 5;
        internal const int OuterHashBytes = 64;
        internal const int MaximumFileBytes = 256 * 1024 * 1024;
        internal const int MaximumPlayerDataBytes = 64 * 1024 * 1024;
        internal const int MaximumWorldEntries = 4096;
        internal const int MaximumDictionaryEntries = 65536;
        internal const int MaximumTotalDictionaryEntries = 262144;
        internal const int MaximumStringBytes = 65536;

        internal static bool TryVerifyCurrentV46(
            byte[] fileBytes,
            byte[] expectedPlayerData,
            out CharacterProfileReadbackEvidence evidence,
            out string reasonCode)
        {
            evidence = null;
            if (fileBytes == null)
            {
                reasonCode = "readback.file-null";
                return false;
            }
            if (expectedPlayerData == null)
            {
                reasonCode = "readback.expected-player-data-null";
                return false;
            }
            if (fileBytes.Length < 4 + 4 + OuterHashBytes || fileBytes.Length > MaximumFileBytes)
            {
                reasonCode = "readback.file-size-invalid";
                return false;
            }
            if (expectedPlayerData.Length > MaximumPlayerDataBytes)
            {
                reasonCode = "readback.expected-player-data-too-large";
                return false;
            }

            var file = new ByteCursor(fileBytes, 0, fileBytes.Length);
            if (!file.TryReadInt32(out int outerLength) || outerLength < 0 ||
                outerLength > MaximumFileBytes - 4 - 4 - OuterHashBytes ||
                outerLength > file.Remaining - 4 - OuterHashBytes)
            {
                reasonCode = "readback.outer-length-invalid";
                return false;
            }
            int outerOffset = file.Position;
            int hashLength = -1;
            if (!file.TrySkip(outerLength) || !file.TryReadInt32(out hashLength) ||
                hashLength != OuterHashBytes || file.Remaining != OuterHashBytes)
            {
                reasonCode = hashLength == OuterHashBytes
                    ? "readback.envelope-trailing-or-truncated"
                    : "readback.hash-length-invalid";
                return false;
            }
            int hashOffset = file.Position;

            byte[] computedOuterHash;
            using (SHA512 sha = SHA512.Create())
                computedOuterHash = sha.ComputeHash(fileBytes, outerOffset, outerLength);
            if (!FixedEquals(fileBytes, hashOffset, computedOuterHash, 0, computedOuterHash.Length))
            {
                reasonCode = "readback.outer-hash-invalid";
                return false;
            }

            var outer = new ByteCursor(fileBytes, outerOffset, outerLength);
            if (!outer.TryReadInt32(out int version) || version != CurrentProfileVersion)
            {
                reasonCode = "readback.profile-version-unsupported";
                return false;
            }
            if (!outer.TryReadInt32(out int statCount) || statCount != CurrentStatCount ||
                !outer.TryReadInt32(out int profileSetCount) ||
                profileSetCount != CurrentProfileSetCount)
            {
                reasonCode = "readback.profile-header-invalid";
                return false;
            }

            int totalDictionaryEntries = 0;
            for (int profileSet = 0; profileSet < CurrentProfileSetCount; profileSet++)
            {
                if (!outer.TrySkip(CurrentStatCount * sizeof(float)))
                {
                    reasonCode = "readback.profile-header-invalid";
                    return false;
                }
                for (int dictionary = 0; dictionary < 3; dictionary++)
                {
                    if (!TrySkipDictionary(outer, ref totalDictionaryEntries))
                    {
                        reasonCode = "readback.dictionary-count-invalid";
                        return false;
                    }
                }
                if (!outer.TryReadInt32(out int enemyStatSetCount) ||
                    enemyStatSetCount != CurrentEnemyStatSetCount)
                {
                    reasonCode = "readback.profile-header-invalid";
                    return false;
                }
                for (int dictionary = 0; dictionary < CurrentEnemyStatSetCount + 5; dictionary++)
                {
                    if (!TrySkipDictionary(outer, ref totalDictionaryEntries))
                    {
                        reasonCode = "readback.dictionary-count-invalid";
                        return false;
                    }
                }
            }
            if (!outer.TryReadStrictBoolean(out _))
            {
                reasonCode = "readback.profile-header-invalid";
                return false;
            }
            if (!outer.TryReadInt32(out int worldCount) || worldCount < 0 ||
                worldCount > MaximumWorldEntries)
            {
                reasonCode = "readback.world-count-invalid";
                return false;
            }
            for (int index = 0; index < worldCount; index++)
            {
                if (!outer.TrySkip(sizeof(long)) ||
                    !outer.TryReadStrictBoolean(out _) || !outer.TrySkip(12) ||
                    !outer.TryReadStrictBoolean(out _) || !outer.TrySkip(12) ||
                    !outer.TryReadStrictBoolean(out _) || !outer.TrySkip(12) ||
                    !outer.TrySkip(12) ||
                    !outer.TryReadStrictBoolean(out bool hasMap) ||
                    hasMap && !outer.TrySkipByteArray(MaximumFileBytes))
                {
                    reasonCode = "readback.world-data-invalid";
                    return false;
                }
            }
            if (!outer.TrySkipString(MaximumStringBytes) ||
                !outer.TryReadInt64(out long playerId) ||
                !outer.TrySkipString(MaximumStringBytes) ||
                !outer.TryReadStrictBoolean(out _) ||
                !outer.TrySkip(sizeof(long)))
            {
                reasonCode = "readback.identity-or-date-invalid";
                return false;
            }

            if (!outer.TryReadStrictBoolean(out bool hasPlayerData) || !hasPlayerData)
            {
                reasonCode = "readback.player-data-missing";
                return false;
            }
            if (!outer.TryReadInt32(out int playerDataLength) || playerDataLength < 0 ||
                playerDataLength > MaximumPlayerDataBytes ||
                playerDataLength != outer.Remaining)
            {
                reasonCode = "readback.player-data-length-invalid";
                return false;
            }
            int playerDataOffset = outer.Position;
            if (playerDataLength != expectedPlayerData.Length ||
                !FixedEquals(fileBytes, playerDataOffset, expectedPlayerData, 0, expectedPlayerData.Length))
            {
                reasonCode = "readback.player-data-mismatch";
                return false;
            }

            byte[] playerHash;
            using (SHA256 sha = SHA256.Create())
                playerHash = sha.ComputeHash(fileBytes, playerDataOffset, playerDataLength);
            evidence = new CharacterProfileReadbackEvidence(
                playerId,
                outerLength,
                playerDataLength,
                ToHex(computedOuterHash),
                ToHex(playerHash));
            reasonCode = "ok";
            return true;
        }

        private static bool TrySkipDictionary(ByteCursor cursor, ref int totalEntries)
        {
            if (!cursor.TryReadInt32(out int count) || count < 0 ||
                count > MaximumDictionaryEntries ||
                totalEntries > MaximumTotalDictionaryEntries - count)
                return false;
            totalEntries += count;
            for (int entry = 0; entry < count; entry++)
                if (!cursor.TrySkipString(MaximumStringBytes) || !cursor.TrySkip(sizeof(float)))
                    return false;
            return true;
        }

        private static bool FixedEquals(
            byte[] left,
            int leftOffset,
            byte[] right,
            int rightOffset,
            int count)
        {
            if (left == null || right == null || leftOffset < 0 || rightOffset < 0 || count < 0 ||
                leftOffset > left.Length - count || rightOffset > right.Length - count)
                return false;
            int difference = 0;
            for (int index = 0; index < count; index++)
                difference |= left[leftOffset + index] ^ right[rightOffset + index];
            return difference == 0;
        }

        private static string ToHex(byte[] value)
        {
            var result = new StringBuilder(value.Length * 2);
            for (int index = 0; index < value.Length; index++)
                result.Append(value[index].ToString("x2"));
            return result.ToString();
        }

        private sealed class ByteCursor
        {
            private readonly byte[] _data;
            private readonly int _end;

            internal ByteCursor(byte[] data, int offset, int count)
            {
                _data = data;
                Position = offset;
                _end = offset + count;
            }

            internal int Position { get; private set; }
            internal int Remaining => _end - Position;

            internal bool TrySkip(int count)
            {
                if (count < 0 || count > Remaining) return false;
                Position += count;
                return true;
            }

            internal bool TryReadInt32(out int value)
            {
                value = 0;
                if (Remaining < 4) return false;
                value = _data[Position] |
                        _data[Position + 1] << 8 |
                        _data[Position + 2] << 16 |
                        _data[Position + 3] << 24;
                Position += 4;
                return true;
            }

            internal bool TryReadInt64(out long value)
            {
                value = 0;
                if (Remaining < 8) return false;
                ulong unsigned = (ulong)_data[Position] |
                                (ulong)_data[Position + 1] << 8 |
                                (ulong)_data[Position + 2] << 16 |
                                (ulong)_data[Position + 3] << 24 |
                                (ulong)_data[Position + 4] << 32 |
                                (ulong)_data[Position + 5] << 40 |
                                (ulong)_data[Position + 6] << 48 |
                                (ulong)_data[Position + 7] << 56;
                Position += 8;
                value = unchecked((long)unsigned);
                return true;
            }

            internal bool TryReadStrictBoolean(out bool value)
            {
                value = false;
                if (Remaining < 1) return false;
                byte encoded = _data[Position++];
                if (encoded > 1) return false;
                value = encoded != 0;
                return true;
            }

            internal bool TrySkipByteArray(int maximumBytes)
            {
                return TryReadInt32(out int count) && count >= 0 && count <= maximumBytes && TrySkip(count);
            }

            internal bool TrySkipString(int maximumBytes)
            {
                if (!TryRead7BitEncodedInt(out int count) || count < 0 || count > maximumBytes)
                    return false;
                return TrySkip(count);
            }

            private bool TryRead7BitEncodedInt(out int value)
            {
                value = 0;
                for (int index = 0; index < 5; index++)
                {
                    if (Remaining < 1) return false;
                    byte current = _data[Position++];
                    if (index == 4 && (current & 0xf0) != 0) return false;
                    value |= (current & 0x7f) << (index * 7);
                    if ((current & 0x80) == 0) return value >= 0;
                }
                return false;
            }
        }
    }
}
