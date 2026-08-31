using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal sealed class PersistedTopologyState
    {
        private readonly byte[] _lockBits;

        internal PersistedTopologyState(int width, int height, byte[] lockBits)
        {
            Width = width;
            Height = height;
            _lockBits = (byte[])(lockBits ?? throw new ArgumentNullException(nameof(lockBits))).Clone();
        }

        internal int Width { get; }
        internal int Height { get; }

        internal bool IsLocked(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            int index = y * Width + x;
            return (_lockBits[index >> 3] & (1 << (index & 7))) != 0;
        }

        internal IReadOnlyList<InventorySlotCoordinate> LockedSlots()
        {
            var result = new List<InventorySlotCoordinate>();
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (IsLocked(x, y)) result.Add(new InventorySlotCoordinate(x, y));
            return result.AsReadOnly();
        }

        internal byte[] CopyBits() => (byte[])_lockBits.Clone();
    }

    internal static class TopologyPersistenceCodec
    {
        internal const string MetadataKey = "runic.inventory.topology.v1";
        internal const int MaximumPayloadCharacters = 256;
        private const string Version = "1";

        internal static bool TryEncode(
            TopologyLayout layout,
            IEnumerable<InventorySlotCoordinate> locks,
            out string payload,
            out string reasonCode)
        {
            payload = string.Empty;
            if (layout == null)
            {
                reasonCode = "persistence.layout-null";
                return false;
            }
            byte[] bits = new byte[(layout.TotalSlots + 7) / 8];
            var unique = new HashSet<InventorySlotCoordinate>();
            if (locks != null)
            {
                foreach (InventorySlotCoordinate coordinate in locks)
                {
                    if (!layout.InBounds(coordinate))
                    {
                        reasonCode = "persistence.lock-out-of-bounds";
                        return false;
                    }
                    if (!unique.Add(coordinate))
                    {
                        reasonCode = "persistence.lock-duplicate";
                        return false;
                    }
                    int index = coordinate.Y * layout.Width + coordinate.X;
                    bits[index >> 3] |= (byte)(1 << (index & 7));
                }
            }
            string body = Version + "|" + layout.Width.ToString(CultureInfo.InvariantCulture) + "|" +
                          layout.Height.ToString(CultureInfo.InvariantCulture) + "|" + ToHex(bits);
            string digest = Digest(body);
            payload = body + "|" + digest;
            if (payload.Length > MaximumPayloadCharacters)
            {
                payload = string.Empty;
                reasonCode = "persistence.payload-too-large";
                return false;
            }
            reasonCode = "ok";
            return true;
        }

        internal static bool TryDecode(string payload, out PersistedTopologyState state, out string reasonCode)
        {
            state = null;
            if (string.IsNullOrEmpty(payload))
            {
                reasonCode = "persistence.missing";
                return false;
            }
            if (payload.Length > MaximumPayloadCharacters)
            {
                reasonCode = "persistence.payload-too-large";
                return false;
            }
            string[] parts = payload.Split('|');
            if (parts.Length != 5 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
            {
                reasonCode = "persistence.schema-invalid";
                return false;
            }
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
                !TopologyLayout.TryCreate(width, height, out TopologyLayout layout, out _))
            {
                reasonCode = "persistence.dimensions-invalid";
                return false;
            }
            int byteCount = (layout.TotalSlots + 7) / 8;
            if (!TryHex(parts[3], byteCount, out byte[] bits))
            {
                reasonCode = "persistence.lock-bits-invalid";
                return false;
            }
            string body = parts[0] + "|" + parts[1] + "|" + parts[2] + "|" + parts[3];
            string expected = Digest(body);
            if (parts[4].Length != expected.Length || !FixedEquals(parts[4], expected))
            {
                reasonCode = "persistence.digest-invalid";
                return false;
            }
            state = new PersistedTopologyState(width, height, bits);
            reasonCode = "ok";
            return true;
        }

        private static string Digest(string value)
        {
            using (SHA256 sha = SHA256.Create()) return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++) result.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static bool TryHex(string text, int byteCount, out byte[] bytes)
        {
            bytes = null;
            if (text == null || text.Length != byteCount * 2) return false;
            var result = new byte[byteCount];
            for (int index = 0; index < byteCount; index++)
            {
                int high = Hex(text[index * 2]);
                int low = Hex(text[index * 2 + 1]);
                if (high < 0 || low < 0) return false;
                result[index] = (byte)((high << 4) | low);
            }
            bytes = result;
            return true;
        }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            return -1;
        }

        private static bool FixedEquals(string left, string right)
        {
            int difference = left.Length ^ right.Length;
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
