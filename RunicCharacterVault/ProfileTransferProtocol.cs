using System;
using System.IO;
using System.Linq;

namespace RunicCharacterVault
{
    internal static class ProfileTransferProtocol
    {
        internal const int ChunkSize = 65536;

        internal static ZPackage Begin(string transferId, int length, string hash)
        {
            ZPackage package = new ZPackage();
            package.Write(transferId);
            package.Write(length);
            package.Write(hash);
            return package;
        }

        internal static ZPackage Chunk(string transferId, byte[] data, int offset)
        {
            int length = Math.Min(ChunkSize, data.Length - offset);
            byte[] chunk = new byte[length];
            Buffer.BlockCopy(data, offset, chunk, 0, length);
            ZPackage package = new ZPackage();
            package.Write(transferId);
            package.Write(offset);
            package.Write(chunk);
            return package;
        }
    }

    internal sealed class IncomingTransfer
    {
        private readonly byte[] _data;
        private readonly bool[] _blocks;
        private readonly string _hash;
        private readonly string _transferId;

        private IncomingTransfer(string transferId, int length, string hash)
        {
            _transferId = transferId;
            _hash = hash;
            _data = new byte[length];
            _blocks = new bool[(length + ProfileTransferProtocol.ChunkSize - 1) /
                ProfileTransferProtocol.ChunkSize];
        }

        internal string RequestId { get; set; }

        internal static IncomingTransfer Create(ZPackage package, int maximumLength)
        {
            string id = package.ReadString();
            int length = package.ReadInt();
            string hash = package.ReadString();
            if (string.IsNullOrWhiteSpace(id) || length <= 0 || length > maximumLength || hash.Length != 64)
            {
                throw new InvalidDataException("The profile transfer header is invalid.");
            }

            return new IncomingTransfer(id, length, hash);
        }

        internal void Add(ZPackage package)
        {
            string id = package.ReadString();
            int offset = package.ReadInt();
            byte[] chunk = package.ReadByteArray();
            if (id != _transferId || offset < 0 || offset % ProfileTransferProtocol.ChunkSize != 0 ||
                chunk.Length == 0 || chunk.Length > ProfileTransferProtocol.ChunkSize ||
                offset + chunk.Length > _data.Length)
            {
                throw new InvalidDataException("The profile transfer chunk is invalid.");
            }

            int block = offset / ProfileTransferProtocol.ChunkSize;
            if (_blocks[block])
            {
                throw new InvalidDataException("The profile transfer contains a duplicate chunk.");
            }

            Buffer.BlockCopy(chunk, 0, _data, offset, chunk.Length);
            _blocks[block] = true;
        }

        internal byte[] Complete(string transferId)
        {
            if (transferId != _transferId || _blocks.Any(block => !block) ||
                !string.Equals(VaultStorage.Hash(_data), _hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The profile transfer is incomplete or corrupted.");
            }

            return _data;
        }
    }
}
