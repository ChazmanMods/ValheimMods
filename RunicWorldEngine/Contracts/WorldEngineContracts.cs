using System;

namespace RunicWorldEngine.Contracts
{
    internal sealed class ZdoObservatorySnapshot
    {
        internal ZdoObservatorySnapshot(
            long sequence,
            long capturedUnixMilliseconds,
            int totalObjects,
            int connectedPeers,
            int createdSincePreviousSample,
            int destroyedSincePreviousSample,
            int sentLastSecond,
            int receivedLastSecond,
            double lastSaveMilliseconds,
            double lastLoadMilliseconds)
        {
            Sequence = Math.Max(0L, sequence);
            CapturedUnixMilliseconds = Math.Max(0L, capturedUnixMilliseconds);
            TotalObjects = Math.Max(0, totalObjects);
            ConnectedPeers = Math.Max(0, connectedPeers);
            CreatedSincePreviousSample = Math.Max(0, createdSincePreviousSample);
            DestroyedSincePreviousSample = Math.Max(0, destroyedSincePreviousSample);
            SentLastSecond = Math.Max(0, sentLastSecond);
            ReceivedLastSecond = Math.Max(0, receivedLastSecond);
            LastSaveMilliseconds = Math.Max(0d, lastSaveMilliseconds);
            LastLoadMilliseconds = Math.Max(0d, lastLoadMilliseconds);
        }

        internal long Sequence { get; }
        internal long CapturedUnixMilliseconds { get; }
        internal int TotalObjects { get; }
        internal int ConnectedPeers { get; }
        internal int CreatedSincePreviousSample { get; }
        internal int DestroyedSincePreviousSample { get; }
        internal int SentLastSecond { get; }
        internal int ReceivedLastSecond { get; }
        internal double LastSaveMilliseconds { get; }
        internal double LastLoadMilliseconds { get; }

        internal static ZdoObservatorySnapshot Empty { get; } =
            new ZdoObservatorySnapshot(0L, 0L, 0, 0, 0, 0, 0, 0, 0d, 0d);
    }
}
