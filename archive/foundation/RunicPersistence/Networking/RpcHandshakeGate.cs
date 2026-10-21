namespace Runic.Foundation.Persistence
{
    /// <summary>
    /// Order-independent one-shot latch for the direct challenge and vanilla client-handshake
    /// signals. Either may arrive first; exactly one offer becomes eligible after both are seen
    /// and local compatibility succeeds.
    /// </summary>
    internal sealed class RpcHandshakeGate
    {
        private string _pendingPeerInfoPassword;

        internal bool ChallengeReceived { get; private set; }
        internal bool ClientHandshakeSeen { get; private set; }
        internal bool CompatibilityAccepted { get; private set; }
        internal bool OfferSent { get; private set; }
        internal bool PeerInfoSent { get; private set; }
        internal bool PeerInfoResumeArmed { get; private set; }
        internal bool Closed { get; private set; }
        internal bool HasPendingPeerInfo => _pendingPeerInfoPassword != null;

        internal void MarkChallenge(bool compatible)
        {
            if (Closed) return;
            ChallengeReceived = true;
            CompatibilityAccepted = compatible;
        }

        internal void MarkClientHandshake()
        {
            if (Closed) return;
            ClientHandshakeSeen = true;
        }

        internal bool TryTakeOffer()
        {
            if (Closed || OfferSent || !ChallengeReceived || !ClientHandshakeSeen ||
                !CompatibilityAccepted)
                return false;
            OfferSent = true;
            return true;
        }

        /// <summary>
        /// Allows the normal vanilla SendPeerInfo call when the offer is ready. If it is not
        /// ready, stores one password in this bounded provisional gate. A resumed reflection
        /// call must consume the one-shot arm produced by <see cref="TryTakeQueuedPeerInfo"/>.
        /// </summary>
        internal bool TryAllowOrQueuePeerInfo(string password)
        {
            if (Closed || PeerInfoSent) return false;
            if (PeerInfoResumeArmed)
            {
                PeerInfoResumeArmed = false;
                PeerInfoSent = true;
                return true;
            }
            if (ChallengeReceived && OfferSent && CompatibilityAccepted)
            {
                PeerInfoSent = true;
                _pendingPeerInfoPassword = null;
                return true;
            }
            if (_pendingPeerInfoPassword == null)
                _pendingPeerInfoPassword = password ?? string.Empty;
            return false;
        }

        /// <summary>
        /// Claims a queued SendPeerInfo resume exactly once. The returned password remains
        /// private to the transport adapter and is cleared before any reflective invocation.
        /// </summary>
        internal bool TryTakeQueuedPeerInfo(out string password)
        {
            password = null;
            if (Closed || PeerInfoSent || PeerInfoResumeArmed ||
                _pendingPeerInfoPassword == null || !ChallengeReceived || !OfferSent ||
                !CompatibilityAccepted)
                return false;
            password = _pendingPeerInfoPassword;
            _pendingPeerInfoPassword = null;
            PeerInfoResumeArmed = true;
            return true;
        }

        internal void Close()
        {
            Closed = true;
            CompatibilityAccepted = false;
            PeerInfoResumeArmed = false;
            _pendingPeerInfoPassword = null;
        }
    }
}
