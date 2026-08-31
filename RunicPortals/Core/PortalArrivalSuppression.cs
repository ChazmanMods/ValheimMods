using System;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    /// <summary>
    /// Bounded local guard against the destination trigger treating a completed arrival as a new
    /// departure. It is exact to one player and one destination and never survives a runtime reset.
    /// </summary>
    internal sealed class PortalArrivalSuppression
    {
        private long _playerId;
        private string _destinationPortalId = string.Empty;
        private long _expiresUtcTicks;

        internal void Arm(long playerId, string destinationPortalId, long nowUtcTicks, long durationTicks)
        {
            if (playerId == 0L) throw new ArgumentOutOfRangeException(nameof(playerId));
            _destinationPortalId = PortalText.Require(
                destinationPortalId,
                PortalContractLimits.MaximumPortalIdLength,
                nameof(destinationPortalId));
            if (durationTicks <= 0L) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            _playerId = playerId;
            _expiresUtcTicks = checked(nowUtcTicks + durationTicks);
        }

        internal bool Blocks(long playerId, string portalId, long nowUtcTicks)
        {
            if (_playerId == 0L || nowUtcTicks > _expiresUtcTicks)
            {
                Clear();
                return false;
            }
            return playerId == _playerId && string.Equals(
                portalId, _destinationPortalId, StringComparison.Ordinal);
        }

        internal void Clear()
        {
            _playerId = 0L;
            _destinationPortalId = string.Empty;
            _expiresUtcTicks = 0L;
        }
    }
}
