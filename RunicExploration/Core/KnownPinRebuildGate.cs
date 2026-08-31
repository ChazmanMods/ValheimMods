using System;

namespace RunicExploration.Core
{
    internal sealed class KnownPinRebuildGate
    {
        internal const float QuietPeriodSeconds = 0.75f;
        internal const float MaximumDeferralSeconds = 2f;

        private bool _hasPending;
        private ulong _pendingFingerprint;
        private float _pendingSince;
        private float _lastPublishedAt;
        private bool _hasPublishedTime;

        internal bool ShouldRebuild(
            ulong candidateFingerprint,
            ulong publishedFingerprint,
            bool hasPublishedFingerprint,
            float now)
        {
            if (!Finite(now) || now < 0f) now = 0f;
            if (!hasPublishedFingerprint)
            {
                ClearPending();
                return true;
            }
            if (candidateFingerprint == publishedFingerprint)
            {
                ClearPending();
                return false;
            }
            if (!_hasPending || _pendingFingerprint != candidateFingerprint)
            {
                _hasPending = true;
                _pendingFingerprint = candidateFingerprint;
                _pendingSince = now;
            }

            bool quiet = Elapsed(now, _pendingSince) >= QuietPeriodSeconds;
            bool forced = !_hasPublishedTime ||
                          Elapsed(now, _lastPublishedAt) >= MaximumDeferralSeconds;
            return quiet || forced;
        }

        internal void MarkPublished(float now)
        {
            _lastPublishedAt = Finite(now) && now >= 0f ? now : 0f;
            _hasPublishedTime = true;
            ClearPending();
        }

        internal void Reset()
        {
            _lastPublishedAt = 0f;
            _hasPublishedTime = false;
            ClearPending();
        }

        private void ClearPending()
        {
            _hasPending = false;
            _pendingFingerprint = 0UL;
            _pendingSince = 0f;
        }

        private static float Elapsed(float now, float then) => now >= then ? now - then : float.MaxValue;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
