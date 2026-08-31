using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class CharacterProfileReadbackTests
    {
        private static readonly byte[] ExpectedPlayerData =
            { 0x2b, 0x00, 0x00, 0x00, 0x52, 0x55, 0x4e, 0x49, 0x43, 0x00, 0xff };

        internal static void Register()
        {
            TestRunner.Run("exact v43 character primary validates hash and player package", ExactPrimaryAccepted);
            TestRunner.Run("stale primary is rejected even when new or backup bytes are exact", StalePrimaryRejectsFallbackIllusion);
            TestRunner.Run("outer profile corruption is rejected before player evidence", CorruptionRejected);
            TestRunner.Run("truncated and trailing character envelopes fail closed", EnvelopeBoundsRejectFaults);
            TestRunner.Run("rehashed malformed v43 structures fail closed", RehashedMalformedStructureRejected);
            TestRunner.Run("missing and mismatched player packages fail closed", PlayerDataMismatchRejected);
            TestRunner.Run("profile parser hard-bounds adversarial collections and strings", AdversarialBoundsRejectEarly);
        }

        private static void ExactPrimaryAccepted()
        {
            byte[] primary = Fixture(ExpectedPlayerData, playerId: 9123456789L);
            TestAssert.True(CharacterProfileReadback.TryVerifyCurrentV43(
                primary, ExpectedPlayerData, out CharacterProfileReadbackEvidence evidence, out string code));
            TestAssert.Equal("ok", code);
            TestAssert.Equal(9123456789L, evidence.PlayerId);
            TestAssert.Equal(ExpectedPlayerData.Length, evidence.PlayerDataBytes);
            TestAssert.Equal(128, evidence.OuterSha512.Length);
            TestAssert.Equal(64, evidence.PlayerDataSha256.Length);

            // FileReader returns byte-identical content for both local FileStream and a successful
            // cloud ReadFile. Source durability is a separate storage-provider proof.
            byte[] cloudRead = (byte[])primary.Clone();
            TestAssert.True(CharacterProfileReadback.TryVerifyCurrentV43(
                cloudRead, ExpectedPlayerData, out _, out code));
            TestAssert.Equal("ok", code);
        }

        private static void StalePrimaryRejectsFallbackIllusion()
        {
            byte[] oldPlayer = { 1, 2, 3, 4 };
            byte[] currentPrimary = Fixture(oldPlayer);
            byte[] exactNewFile = Fixture(ExpectedPlayerData);
            byte[] exactBackup = Fixture(ExpectedPlayerData);

            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                currentPrimary, ExpectedPlayerData, out _, out string staleCode));
            TestAssert.Equal("readback.player-data-mismatch", staleCode);
            TestAssert.True(CharacterProfileReadback.TryVerifyCurrentV43(
                exactNewFile, ExpectedPlayerData, out _, out _));
            TestAssert.True(CharacterProfileReadback.TryVerifyCurrentV43(
                exactBackup, ExpectedPlayerData, out _, out _));
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                currentPrimary, ExpectedPlayerData, out _, out _),
                "An exact .new or cloud-backup file must never substitute for stale current-primary evidence.");
        }

        private static void CorruptionRejected()
        {
            byte[] corrupt = Fixture(ExpectedPlayerData);
            corrupt[12] ^= 0x40;
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                corrupt, ExpectedPlayerData, out _, out string code));
            TestAssert.Equal("readback.outer-hash-invalid", code);
        }

        private static void EnvelopeBoundsRejectFaults()
        {
            byte[] exact = Fixture(ExpectedPlayerData);
            byte[] truncated = new byte[exact.Length - 1];
            Buffer.BlockCopy(exact, 0, truncated, 0, truncated.Length);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                truncated, ExpectedPlayerData, out _, out _));

            byte[] trailing = new byte[exact.Length + 1];
            Buffer.BlockCopy(exact, 0, trailing, 0, exact.Length);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                trailing, ExpectedPlayerData, out _, out string trailingCode));
            TestAssert.Equal("readback.envelope-trailing-or-truncated", trailingCode);

            byte[] badHashLength = (byte[])exact.Clone();
            int outerLength = BitConverter.ToInt32(badHashLength, 0);
            WriteInt32(badHashLength, 4 + outerLength, 63);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                badHashLength, ExpectedPlayerData, out _, out string hashCode));
            TestAssert.Equal("readback.hash-length-invalid", hashCode);
        }

        private static void RehashedMalformedStructureRejected()
        {
            byte[] unsupported = Fixture(ExpectedPlayerData, version: 42);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                unsupported, ExpectedPlayerData, out _, out string versionCode));
            TestAssert.Equal("readback.profile-version-unsupported", versionCode);

            byte[] invalidBoolean = Fixture(ExpectedPlayerData, firstSpawnEncoding: 2);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                invalidBoolean, ExpectedPlayerData, out _, out string headerCode));
            TestAssert.Equal("readback.profile-header-invalid", headerCode);
        }

        private static void PlayerDataMismatchRejected()
        {
            byte[] noData = Fixture(ExpectedPlayerData, hasPlayerData: false);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                noData, ExpectedPlayerData, out _, out string missingCode));
            TestAssert.Equal("readback.player-data-missing", missingCode);

            byte[] mismatch = Fixture(new byte[] { 4, 3, 2, 1 });
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                mismatch, ExpectedPlayerData, out _, out string mismatchCode));
            TestAssert.Equal("readback.player-data-mismatch", mismatchCode);
        }

        private static void AdversarialBoundsRejectEarly()
        {
            byte[] tooManyWorlds = Fixture(ExpectedPlayerData, worldCountOverride: int.MaxValue);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                tooManyWorlds, ExpectedPlayerData, out _, out string worldCode));
            TestAssert.Equal("readback.world-count-invalid", worldCode);

            byte[] tooManyDictionaryEntries = Fixture(
                ExpectedPlayerData, firstDictionaryCountOverride: int.MaxValue);
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                tooManyDictionaryEntries, ExpectedPlayerData, out _, out string dictionaryCode));
            TestAssert.Equal("readback.dictionary-count-invalid", dictionaryCode);

            byte[] oversizedExpected = new byte[CharacterProfileReadback.MaximumPlayerDataBytes + 1];
            TestAssert.False(CharacterProfileReadback.TryVerifyCurrentV43(
                Fixture(ExpectedPlayerData), oversizedExpected, out _, out string expectedCode));
            TestAssert.Equal("readback.expected-player-data-too-large", expectedCode);
        }

        private static byte[] Fixture(
            byte[] playerData,
            long playerId = 1234L,
            int version = CharacterProfileReadback.CurrentProfileVersion,
            byte firstSpawnEncoding = 1,
            bool hasPlayerData = true,
            int? worldCountOverride = null,
            int? firstDictionaryCountOverride = null)
        {
            byte[] outer;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(version);
                writer.Write(CharacterProfileReadback.CurrentStatCount);
                for (int index = 0; index < CharacterProfileReadback.CurrentStatCount; index++)
                    writer.Write(index / 10f);
                writer.Write(firstSpawnEncoding);
                writer.Write(worldCountOverride ?? 0);
                writer.Write("Runic Tester");
                writer.Write(playerId);
                writer.Write("start-seed");
                writer.Write(false);
                writer.Write(1700000000L);
                for (int dictionary = 0; dictionary < 6; dictionary++)
                {
                    writer.Write(dictionary == 0 && firstDictionaryCountOverride.HasValue
                        ? firstDictionaryCountOverride.Value
                        : 0);
                }
                writer.Write(hasPlayerData);
                if (hasPlayerData)
                {
                    writer.Write(playerData.Length);
                    writer.Write(playerData);
                }
                writer.Flush();
                outer = stream.ToArray();
            }

            byte[] hash;
            using (SHA512 sha = SHA512.Create()) hash = sha.ComputeHash(outer);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(outer.Length);
                writer.Write(outer);
                writer.Write(hash.Length);
                writer.Write(hash);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }
    }
}
