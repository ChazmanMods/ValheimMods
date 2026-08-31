using System;
using System.Linq;
using System.Text;

namespace RunicPermissions.Groups
{
    internal static class GroupQueryProtocol
    {
        internal const int MaximumWireBytes = 64 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static bool TryDecodeRequest(
            byte[] payload,
            out byte kind,
            out Guid groupId,
            out string failureCode)
        {
            kind = 0;
            groupId = Guid.Empty;
            if (payload == null || payload.Length != 1 && payload.Length != 17)
            {
                failureCode = "group-query-shape-invalid";
                return false;
            }
            kind = payload[0];
            if ((kind == 1 || kind == 3) && payload.Length != 1 ||
                kind == 2 && payload.Length != 17 || kind < 1 || kind > 3)
            {
                failureCode = "group-query-kind-invalid";
                return false;
            }
            if (kind == 2)
                groupId = new Guid(payload.Skip(1).Take(16).ToArray());
            failureCode = "ok";
            return true;
        }

        internal static byte[] EncodeResponse(string text)
        {
            byte[] bytes = StrictUtf8.GetBytes(text ?? string.Empty);
            if (bytes.Length > MaximumWireBytes)
                throw new InvalidOperationException("The Group response exceeds its wire bound.");
            return bytes;
        }
    }
}
