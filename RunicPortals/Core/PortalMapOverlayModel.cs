using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    /// <summary>
    /// One already-authorized map candidate. The integration boundary supplies both access
    /// decisions explicitly so the presentation layer can never infer permission from a name or
    /// from the presence of a replicated portal record.
    /// </summary>
    internal readonly struct PortalMapCandidate
    {
        internal PortalMapCandidate(
            string portalId,
            string networkName,
            string displayName,
            float x,
            float y,
            float z,
            bool isNetworkPortal,
            bool acceptsArrival,
            bool permitsDeparture,
            bool viewAuthorized,
            bool departureAuthorized)
        {
            PortalId = PortalText.Require(
                portalId,
                PortalContractLimits.MaximumPortalIdLength,
                nameof(portalId));
            NetworkName = PortalText.Require(
                networkName,
                PortalContractLimits.MaximumNetworkIdLength,
                nameof(networkName));
            DisplayName = PortalText.Require(
                displayName,
                PortalContractLimits.MaximumNameLength,
                nameof(displayName));
            X = x;
            Y = y;
            Z = z;
            IsNetworkPortal = isNetworkPortal;
            AcceptsArrival = acceptsArrival;
            PermitsDeparture = permitsDeparture;
            ViewAuthorized = viewAuthorized;
            DepartureAuthorized = departureAuthorized;
        }

        internal string PortalId { get; }
        internal string NetworkName { get; }
        internal string DisplayName { get; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
        internal bool IsNetworkPortal { get; }
        internal bool AcceptsArrival { get; }
        internal bool PermitsDeparture { get; }
        internal bool ViewAuthorized { get; }
        internal bool DepartureAuthorized { get; }

        internal bool IsFinite =>
            Finite(X) && Finite(Y) && Finite(Z);

        // Candidates cross this boundary only after the server (or authoritative host) has
        // granted ViewDiscover. The normal-map directory is informational: arrive-only Runic
        // portals and vanilla Standard Pair portals are valid entries and do not require Depart.
        internal bool IsAuthorizedDirectoryEntry => ViewAuthorized;

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Pure, bounded, session-only state for the normal-map portal directory. Every candidate in
    /// a replacement snapshot is already authorized, so all categories are presented together.
    /// </summary>
    internal sealed class PortalMapOverlayModel
    {
        internal const int MaximumContextLength = 256;

        private static readonly PortalMapCandidate[] EmptyCandidates =
            Array.Empty<PortalMapCandidate>();
        private readonly List<PortalMapCandidate> _eligible =
            new List<PortalMapCandidate>();
        private string _contextToken = string.Empty;
        private int _revision;

        internal string ContextToken => _contextToken;
        internal string SelectedNetwork => string.Empty;
        internal int NetworkCount => 0;
        internal int EligibleCount => _eligible.Count;
        internal int Revision => _revision;

        internal bool SetContext(string contextToken)
        {
            string normalized = PortalText.NormalizeOptional(
                contextToken,
                MaximumContextLength,
                nameof(contextToken));
            if (string.Equals(_contextToken, normalized, StringComparison.Ordinal))
                return false;

            _contextToken = normalized;
            _eligible.Clear();
            AdvanceRevision();
            return true;
        }

        internal bool TryReplace(IReadOnlyList<PortalMapCandidate> candidates)
        {
            if (_contextToken.Length == 0 || candidates == null ||
                candidates.Count > PortalContractLimits.MaximumGraphEndpoints)
            {
                RejectSnapshot();
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var eligible = new List<PortalMapCandidate>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                PortalMapCandidate candidate = candidates[index];
                if (!CandidateIsCanonical(candidate) || !ids.Add(candidate.PortalId))
                {
                    RejectSnapshot();
                    return false;
                }

                if (!candidate.IsAuthorizedDirectoryEntry) continue;
                eligible.Add(candidate);
            }

            eligible.Sort(CompareCandidate);
            _eligible.Clear();
            _eligible.AddRange(eligible);
            AdvanceRevision();
            return true;
        }

        internal bool NoteNetworkUsed(string networkName)
        {
            string normalized;
            try
            {
                normalized = PortalText.Require(
                    networkName,
                    PortalContractLimits.MaximumNetworkIdLength,
                    nameof(networkName));
            }
            catch (ArgumentException)
            {
                return false;
            }

            // Retained as a compatibility seam for travel code. The global directory no longer
            // selects or filters by the most recently used network.
            return false;
        }

        internal bool Cycle(int direction)
        {
            return false;
        }

        internal PortalMapCandidate[] SelectedCandidates()
        {
            return _eligible.Count == 0 ? EmptyCandidates : _eligible.ToArray();
        }

        private void RejectSnapshot()
        {
            _eligible.Clear();
            AdvanceRevision();
        }

        private static bool CandidateIsCanonical(PortalMapCandidate candidate)
        {
            if (!candidate.IsFinite || string.IsNullOrEmpty(candidate.PortalId) ||
                string.IsNullOrEmpty(candidate.NetworkName) ||
                string.IsNullOrEmpty(candidate.DisplayName))
                return false;
            try
            {
                return string.Equals(
                           candidate.PortalId,
                           PortalText.Require(
                               candidate.PortalId,
                               PortalContractLimits.MaximumPortalIdLength,
                               nameof(candidate.PortalId)),
                           StringComparison.Ordinal) &&
                       string.Equals(
                           candidate.NetworkName,
                           PortalText.Require(
                               candidate.NetworkName,
                               PortalContractLimits.MaximumNetworkIdLength,
                               nameof(candidate.NetworkName)),
                           StringComparison.Ordinal) &&
                       string.Equals(
                           candidate.DisplayName,
                           PortalText.Require(
                               candidate.DisplayName,
                               PortalContractLimits.MaximumNameLength,
                               nameof(candidate.DisplayName)),
                           StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static int CompareCandidate(
            PortalMapCandidate left,
            PortalMapCandidate right)
        {
            int network = string.Compare(
                left.NetworkName,
                right.NetworkName,
                StringComparison.Ordinal);
            if (network != 0) return network;
            int name = string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.OrdinalIgnoreCase);
            return name != 0
                ? name
                : string.Compare(left.PortalId, right.PortalId, StringComparison.Ordinal);
        }

        private void AdvanceRevision()
        {
            unchecked
            {
                _revision++;
            }
        }
    }
}
