using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RunicProduction.Integration
{
    /// <summary>Bounded canonical binary writer for versioned producer signatures.</summary>
    internal sealed class StockSignatureWriter : IDisposable
    {
        private const int MaximumPayloadBytes = 65536;
        private const int MaximumStringBytes = 4096;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly MemoryStream _stream = new MemoryStream();
        private readonly BinaryWriter _writer;
        private bool _disposed;

        internal StockSignatureWriter()
        {
            _writer = new BinaryWriter(_stream, Utf8, leaveOpen: true);
        }

        internal void WriteString(string value)
        {
            EnsureUsable();
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > MaximumStringBytes)
                throw new InvalidOperationException("A producer-signature string exceeds its bound.");
            _writer.Write(bytes.Length);
            _writer.Write(bytes);
            EnsureBounded();
        }

        internal void WriteInt32(int value)
        {
            EnsureUsable();
            _writer.Write(value);
            EnsureBounded();
        }

        internal void WriteInt64(long value)
        {
            EnsureUsable();
            _writer.Write(value);
            EnsureBounded();
        }

        internal void WriteBoolean(bool value)
        {
            EnsureUsable();
            _writer.Write(value);
            EnsureBounded();
        }

        internal void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        internal byte[] ToArray()
        {
            EnsureUsable();
            _writer.Flush();
            EnsureBounded();
            return _stream.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
            _stream.Dispose();
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StockSignatureWriter));
        }

        private void EnsureBounded()
        {
            if (_stream.Length > MaximumPayloadBytes)
                throw new InvalidOperationException("The producer-signature payload exceeds its bound.");
        }
    }

    internal static class StockProducerSignature
    {
        internal const int SignatureBytes = 32;

        internal static byte[] Create(Action<StockSignatureWriter> write)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            byte[] payload;
            using (var writer = new StockSignatureWriter())
            {
                write(writer);
                payload = writer.ToArray();
            }
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(payload);
        }

        internal static bool Matches(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null ||
                expected.Length != SignatureBytes || actual.Length != SignatureBytes) return false;
            int difference = 0;
            for (int index = 0; index < SignatureBytes; index++)
                difference |= expected[index] ^ actual[index];
            return difference == 0;
        }
    }
}
