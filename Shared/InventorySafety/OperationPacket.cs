using System.IO;

namespace RunicAutomation.Operations
{
    internal static class OperationPacket
    {
        internal static byte[] Bytes(ZPackage packet, int maximum = 65536)
        {
            int length = packet.ReadInt();
            if (length < 0 || length > maximum || length > packet.Size() - packet.GetPos())
                throw new InvalidDataException("Invalid operation payload length");
            return packet.ReadByteArray(length);
        }
        internal static void End(ZPackage packet)
        { if (packet.GetPos() != packet.Size()) throw new InvalidDataException("Trailing operation packet data"); }
    }
}
