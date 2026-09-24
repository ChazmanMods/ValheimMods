using System;
using System.IO;
using System.Text;
using System.Threading;

namespace RunicSentinel.Core
{
    // A single command is approved for one exact connection. No global cheat permission is granted.
    internal static class SentinelCommandRequest
    {
        internal const int MaximumBytes = 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static bool TryNormalize(string input, out string command)
        {
            command = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string start=input.TrimStart();
            bool storesCommand=start.StartsWith("alias ",StringComparison.OrdinalIgnoreCase)||start.StartsWith("bind ",StringComparison.OrdinalIgnoreCase);
            foreach (char value in input)
                if (char.IsControl(value) || (value == ';'&&!storesCommand) || value == '\u2028' || value == '\u2029') return false;
            string normalized = input.Trim();
            try { if (Utf8.GetByteCount(normalized) > MaximumBytes) return false; }
            catch (EncoderFallbackException) { return false; }
            command = normalized;
            return true;
        }

        internal static byte[] Encode(string input)
        {
            if (!TryNormalize(input, out string command))
                throw new InvalidDataException("Enter one command, at most 1024 UTF-8 bytes, without line breaks. Chains must be expanded before authorization; alias/bind may store them.");
            return Utf8.GetBytes(command);
        }

        internal static bool TryDecode(byte[] bytes, out string command)
        {
            command = string.Empty;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) return false;
            try { return TryNormalize(Utf8.GetString(bytes), out command); }
            catch (DecoderFallbackException) { return false; }
        }

        internal static void Dispatch(string input, Action<string, Action<bool, string>> authorize,
            Func<bool> sameConnection, Action<string> execute, Action<bool, string> completed)
        {
            if (!TryNormalize(input, out string command))
            { completed(false, "Enter one bounded command without line breaks. Chains must be expanded before authorization; alias/bind may store them."); return; }
            int delivered = 0;
            try
            {
                authorize(command, (accepted, reason) =>
                {
                    if (Interlocked.Exchange(ref delivered, 1) != 0) return;
                    if (!accepted) { completed(false, reason); return; }
                    try
                    {
                        if (!sameConnection())
                        { completed(false, "The game connection changed; the command was not executed."); return; }
                        execute(command);
                        // Terminal handlers return void and remote actions may finish later.
                        completed(true, "Sentinel processed the request. Check console output for the result.");
                    }
                    catch (Exception error) { completed(false, error.Message); }
                });
            }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref delivered, 1) == 0) completed(false, error.Message);
            }
        }
    }
}
