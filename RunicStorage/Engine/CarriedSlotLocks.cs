using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RunicStorage.Engine;

// Read RunicInventory's existing v1 player slot map once per explicit sort.
// Includes empty locked cells; item-only queries cannot protect those destinations.
internal static class CarriedSlotLocks
{
    internal static bool TryRead(IDictionary<string, string> data, int liveWidth, int liveHeight,
        out HashSet<string> locked)
    {
        locked = new HashSet<string>(StringComparer.Ordinal);
        if (data == null || !data.TryGetValue("runic.inventory.topology.v1", out string payload)) return true;
        if (string.IsNullOrEmpty(payload) || payload.Length > 256) return false;
        string[] fields = payload.Split('|');
        if (fields.Length != 5 || fields[0] != "1" ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
            width != liveWidth || width <= 0 || height <= 0 || height > liveHeight) return false;
        long cells = (long)width * height;
        if (cells > 1024 || fields[3].Length != ((cells + 7) / 8) * 2) return false;
        string body = string.Join("|", fields, 0, 4);
        using (SHA256 sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
            string expected = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(fields[4], expected, StringComparison.Ordinal)) return false;
        }
        var bits = new byte[(cells + 7) / 8];
        for (int n = 0; n < bits.Length; n++)
            if (!byte.TryParse(fields[3].Substring(n * 2, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out bits[n])) return false;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            if ((bits[index >> 3] & (1 << (index & 7))) != 0) locked.Add(x + "," + y);
        }
        return true;
    }
}
