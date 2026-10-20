using System;
using System.IO;

namespace RunicSentinel.Core
{
    // Immutable session settings. No save schema changes or live support-strength transitions.
    internal sealed class BuildingLimits
    {
        internal static readonly BuildingLimits Vanilla = new BuildingLimits(1f, 8f, 8f);
        internal readonly float SupportMultiplier;
        internal readonly float RaiseHeight;
        internal readonly float DigDepth;
        internal bool IsVanilla => SupportMultiplier == 1f && RaiseHeight == 8f && DigDepth == 8f;

        internal BuildingLimits(float supportMultiplier, float raiseHeight, float digDepth)
        {
            if (!Valid(supportMultiplier, 1f, 10f) || !Valid(raiseHeight, 1f, 64f) || !Valid(digDepth, 1f, 64f))
                throw new ArgumentOutOfRangeException(nameof(supportMultiplier), "Building limits must be finite and within their documented ranges.");
            SupportMultiplier = supportMultiplier;
            RaiseHeight = raiseHeight;
            DigDepth = digDepth;
        }

        private static bool Valid(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;
        internal float VerticalLoss(float vanilla) => vanilla / SupportMultiplier;
        internal byte[] Encode()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(SupportMultiplier); writer.Write(RaiseHeight); writer.Write(DigDepth);
                return stream.ToArray();
            }
        }
        internal static BuildingLimits Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 16) throw new InvalidDataException("Invalid building settings length.");
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported building settings schema.");
                return new BuildingLimits(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
        }
    }
}
