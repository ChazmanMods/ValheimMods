using System;
using System.Collections.Generic;

namespace RunicPortals.Core
{
    internal enum PortalConfirmationAdmission
    {
        NotRequired = 0,
        Approved = 1,
        Pending = 2,
        Denied = 3,
        Unavailable = 4
    }

    internal sealed class PortalOverwriteConfirmationGate
    {
        private sealed class PendingConfirmation
        {
            internal string Fingerprint;
            internal long ExpiresUtcTicks;
        }

        private readonly Dictionary<string, PendingConfirmation> _pending =
            new Dictionary<string, PendingConfirmation>(StringComparer.Ordinal);

        internal PortalConfirmationAdmission Request(
            bool overwriteRequired,
            string targetId,
            string stateFingerprint)
        {
            if (!overwriteRequired) return PortalConfirmationAdmission.NotRequired;
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(stateFingerprint))
                return PortalConfirmationAdmission.Denied;
            long now = DateTime.UtcNow.Ticks;
            if (_pending.TryGetValue(targetId, out PendingConfirmation current) &&
                current.ExpiresUtcTicks >= now &&
                string.Equals(current.Fingerprint, stateFingerprint, StringComparison.Ordinal))
            {
                _pending.Remove(targetId);
                return PortalConfirmationAdmission.Approved;
            }
            if (_pending.Count >= 64) Prune(now);
            if (_pending.Count >= 64) _pending.Clear();
            _pending[targetId] = new PendingConfirmation
            {
                Fingerprint = stateFingerprint,
                ExpiresUtcTicks = now + TimeSpan.FromSeconds(4).Ticks
            };
            return PortalConfirmationAdmission.Pending;
        }

        internal void Cancel(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId)) _pending.Remove(targetId);
        }

        internal bool RequesterIsActive() => true;

        private void Prune(long now)
        {
            var expired = new List<string>();
            foreach (KeyValuePair<string, PendingConfirmation> pair in _pending)
                if (pair.Value == null || pair.Value.ExpiresUtcTicks < now) expired.Add(pair.Key);
            for (int index = 0; index < expired.Count; index++) _pending.Remove(expired[index]);
        }
    }
}
