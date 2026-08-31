using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunicProduction.Contracts;
using RunicProduction.Integration;

namespace RunicProduction.Core
{
    /// <summary>
    /// A bounded, ordered set of chest relations for one simple Production role. Replenishment
    /// also uses this catalog as its link-only layer; its separately signed plan catalog contains
    /// only destinations that currently have usable physical exemplars.
    /// </summary>
    internal sealed class ProductionRoleLinkCatalog
    {
        internal const int HardMaximumLinks = 16;

        private readonly ReadOnlyCollection<StoredProductionLink> _links;

        internal ProductionRoleLinkCatalog(
            ProductionLinkRole role,
            int revision,
            IEnumerable<StoredProductionLink> links)
        {
            if (role != ProductionLinkRole.Input && role != ProductionLinkRole.Fuel &&
                role != ProductionLinkRole.Output && role != ProductionLinkRole.Replenishment)
                throw new ArgumentOutOfRangeException(nameof(role));
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));

            var copy = new List<StoredProductionLink>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            if (links != null)
                foreach (StoredProductionLink source in links)
                {
                    StoredProductionLink link = ReplenishmentDestinationRecord.CopyLink(source);
                    if (link == null || link.Role != role || link.Target.IsNone() ||
                        link.Revision < 1 || string.IsNullOrWhiteSpace(link.LinkId) ||
                        !ProductionEndpointIdentity.IsCanonicalToken(link.TargetToken) ||
                        link.TargetPrefabHash == 0 || !IsFinite(link.ExpectedPosition))
                        throw new ArgumentException("Every role link must be complete and exact.", nameof(links));
                    string target = link.TargetToken + ":" + link.TargetPrefabHash;
                    if (!ids.Add(link.LinkId) || !targets.Add(target))
                        throw new ArgumentException("Role link IDs and targets must be unique.", nameof(links));
                    copy.Add(link);
                    if (copy.Count > HardMaximumLinks)
                        throw new ArgumentOutOfRangeException(nameof(links));
                }

            Role = role;
            Revision = revision;
            _links = copy.AsReadOnly();
        }

        internal ProductionLinkRole Role { get; }
        internal int Revision { get; }
        internal IReadOnlyList<StoredProductionLink> Links => _links;

        internal StoredProductionLink FindTarget(string token, int prefabHash)
        {
            if (!ProductionEndpointIdentity.IsCanonicalToken(token) || prefabHash == 0) return null;
            foreach (StoredProductionLink link in _links)
                if (link.TargetPrefabHash == prefabHash &&
                    string.Equals(link.TargetToken, token, StringComparison.Ordinal))
                    return ReplenishmentDestinationRecord.CopyLink(link);
            return null;
        }

        private static bool IsFinite(UnityEngine.Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    internal static class ProductionRoleLinkCatalogPolicy
    {
        internal static ProductionRoleLinkCatalog Empty(ProductionLinkRole role) =>
            new ProductionRoleLinkCatalog(role, 1, Array.Empty<StoredProductionLink>());

        internal static bool TryAddOrRefresh(
            ProductionRoleLinkCatalog catalog,
            StoredProductionLink candidate,
            int softMaximum,
            out ProductionRoleLinkCatalog updated,
            out StoredProductionLink published,
            out string failure)
        {
            updated = catalog;
            published = null;
            failure = string.Empty;
            if (catalog == null || candidate == null || candidate.Role != catalog.Role ||
                softMaximum < 1 || softMaximum > ProductionRoleLinkCatalog.HardMaximumLinks)
                return Fail("The role-link change is invalid.", out failure);

            var links = new List<StoredProductionLink>(catalog.Links.Count + 1);
            int match = -1;
            for (int index = 0; index < catalog.Links.Count; index++)
            {
                StoredProductionLink current = catalog.Links[index];
                if (current.TargetPrefabHash == candidate.TargetPrefabHash &&
                    string.Equals(current.TargetToken, candidate.TargetToken, StringComparison.Ordinal))
                {
                    match = index;
                    published = ReplenishmentDestinationRecord.CopyLink(candidate);
                    links.Add(published);
                }
                else links.Add(ReplenishmentDestinationRecord.CopyLink(current));
            }
            if (match < 0)
            {
                if (links.Count >= ProductionRoleLinkCatalog.HardMaximumLinks)
                    return Fail("The hard 16-link role limit was reached.", out failure);
                if (links.Count >= softMaximum)
                    return Fail("The configured per-role link limit was reached.", out failure);
                published = ReplenishmentDestinationRecord.CopyLink(candidate);
                links.Add(published);
            }
            if (catalog.Revision == int.MaxValue)
                return Fail("The role-link revision is exhausted.", out failure);
            updated = new ProductionRoleLinkCatalog(
                catalog.Role,
                catalog.Revision + 1,
                links);
            return true;
        }

        internal static bool TryRemove(
            ProductionRoleLinkCatalog catalog,
            string token,
            int prefabHash,
            out ProductionRoleLinkCatalog updated,
            out string failure)
        {
            updated = catalog;
            failure = string.Empty;
            if (catalog == null || !ProductionEndpointIdentity.IsCanonicalToken(token) ||
                prefabHash == 0)
                return Fail("The role-link removal is invalid.", out failure);
            var links = new List<StoredProductionLink>();
            bool removed = false;
            foreach (StoredProductionLink link in catalog.Links)
            {
                if (!removed && link.TargetPrefabHash == prefabHash &&
                    string.Equals(link.TargetToken, token, StringComparison.Ordinal))
                {
                    removed = true;
                    continue;
                }
                links.Add(ReplenishmentDestinationRecord.CopyLink(link));
            }
            if (!removed) return Fail("That chest is not linked for the selected role.", out failure);
            if (catalog.Revision == int.MaxValue)
                return Fail("The role-link revision is exhausted.", out failure);
            updated = new ProductionRoleLinkCatalog(
                catalog.Role,
                catalog.Revision + 1,
                links);
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
