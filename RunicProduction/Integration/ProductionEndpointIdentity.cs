using System;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal enum ProductionIdentityStatus
    {
        Ready = 0,
        Missing = 1,
        Invalid = 2,
        Unavailable = 3,
        Ambiguous = 4
    }

    /// <summary>
    /// Reads the legacy stable-token fields directly so existing Production links keep working
    /// without loading the former shared runtime. Resolution is deliberately limited to loaded
    /// Containers and never changes network ownership.
    /// </summary>
    internal static class ProductionEndpointIdentity
    {
        internal const string TokenStorageKey =
            "runic.transactions.world-object.token";
        internal const string PresenceStorageKey =
            "runic.transactions.world-object.token-present";
        internal const float LegacyPositionToleranceMeters = 0.05f;
        private const float MaximumResolutionRadiusMeters = 3f;

        internal static bool IsCanonicalToken(string value) =>
            value != null && value.Length == 32 &&
            Guid.TryParseExact(value, "N", out Guid parsed) &&
            parsed != Guid.Empty &&
            string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);

        internal static bool TryCaptureCurrentTarget(
            ZDOID target,
            out string targetToken,
            out int targetPrefabHash,
            out Vector3 rootPosition,
            out string failure)
        {
            targetToken = string.Empty;
            targetPrefabHash = 0;
            rootPosition = Vector3.zero;
            failure = string.Empty;
            if (target.IsNone())
                return Fail(global::Runic.Localization.RunicText.Get("text_f31985cbb325"), out failure);

            GameObject root = ZNetScene.instance?.FindInstance(target);
            if (!ValheimAccess.TryResolveExactContainer(root, target, out Container container))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_b0617eabebc7"),
                    out failure);
            ZNetView view = ValheimAccess.View(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || zdo.m_uid != target || !view.IsOwner())
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_09f04f89a3e3"),
                    out failure);
            int prefab = zdo.GetPrefab();
            Vector3 position = zdo.GetPosition();
            if (prefab == 0 || !ValheimAccess.IsFinite(position))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_24fa1cdbd109"),
                    out failure);
            if (!TryGetOrEnsureToken(view, out string token))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_84abc5d2da41"),
                    out failure);

            targetToken = token;
            targetPrefabHash = prefab;
            rootPosition = position;
            return true;
        }

        internal static ProductionIdentityStatus ResolveStable(
            StoredProductionLink link,
            out Container container,
            out ZDO targetZdo,
            out string failure)
        {
            container = null;
            targetZdo = null;
            failure = string.Empty;
            if (link == null || !IsCanonicalToken(link.TargetToken))
            {
                failure = global::Runic.Localization.RunicText.Get("text_ee539e48c048");
                return string.IsNullOrEmpty(link?.TargetToken)
                    ? ProductionIdentityStatus.Missing
                    : ProductionIdentityStatus.Invalid;
            }
            if (link.TargetPrefabHash == 0 ||
                !ValheimAccess.IsFinite(link.ExpectedPosition))
            {
                failure = global::Runic.Localization.RunicText.Get("text_ece49757e1f7");
                return ProductionIdentityStatus.Invalid;
            }

            if (!link.Target.IsNone())
            {
                GameObject cached = ZNetScene.instance?.FindInstance(link.Target);
                if (TryMatchContainer(
                        cached, link.Target, link.TargetToken,
                        link.TargetPrefabHash, out container, out targetZdo))
                    return ProductionIdentityStatus.Ready;
            }

            NearbyIngredientContainerQueryResult query =
                NearbyIngredientContainerIndex.Query(
                    link.ExpectedPosition,
                    MaximumResolutionRadiusMeters,
                    NearbyIngredientContainerIndex.HardMaximumSourceChests);
            if (query.Truncated)
            {
                failure = global::Runic.Localization.RunicText.Get("text_3287de6f663c");
                return ProductionIdentityStatus.Ambiguous;
            }
            foreach (NearbyIngredientContainerCandidate candidate in query.Candidates)
            {
                Container current = candidate.Container;
                ZNetView view = ValheimAccess.View(current);
                ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null || zdo.GetPrefab() != link.TargetPrefabHash ||
                    !TokenMatches(zdo, link.TargetToken)) continue;
                if (container != null && !ReferenceEquals(container, current))
                {
                    container = null;
                    targetZdo = null;
                    failure = global::Runic.Localization.RunicText.Get("text_444a62597e9e");
                    return ProductionIdentityStatus.Ambiguous;
                }
                container = current;
                targetZdo = zdo;
            }
            if (container == null)
            {
                failure = global::Runic.Localization.RunicText.Get("text_efc09579824c");
                return ProductionIdentityStatus.Unavailable;
            }
            return ProductionIdentityStatus.Ready;
        }

        internal static bool TryResolveUniqueLegacyTarget(
            StoredProductionLink link,
            out Container container,
            out ZDO targetZdo,
            out string failure)
        {
            container = null;
            targetZdo = null;
            failure = string.Empty;
            if (link == null || !string.IsNullOrEmpty(link.TargetToken) ||
                !ValheimAccess.IsFinite(link.ExpectedPosition) ||
                link.TargetOwnerId == 0L)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_8dcb7a4f6064"),
                    out failure);

            NearbyIngredientContainerQueryResult query =
                NearbyIngredientContainerIndex.Query(
                    link.ExpectedPosition,
                    2f,
                    NearbyIngredientContainerIndex.HardMaximumSourceChests);
            if (query.Truncated)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_5f5468ea3474"),
                    out failure);
            float epsilon = LegacyPositionToleranceMeters * LegacyPositionToleranceMeters;
            foreach (NearbyIngredientContainerCandidate candidate in query.Candidates)
            {
                Container current = candidate.Container;
                ZDO zdo = ValheimAccess.Zdo(current);
                if (current == null || zdo == null ||
                    !NearbyIngredientContainerIndex.IsStaticNonWagon(current) ||
                    link.TargetPrefabHash != 0 &&
                    zdo.GetPrefab() != link.TargetPrefabHash ||
                    ValheimAccess.Creator(current) != link.TargetOwnerId ||
                    (zdo.GetPosition() - link.ExpectedPosition).sqrMagnitude > epsilon)
                    continue;
                if (container != null)
                {
                    container = null;
                    targetZdo = null;
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_837bbaa73f02"),
                        out failure);
                }
                container = current;
                targetZdo = zdo;
            }
            return container != null ||
                   Fail(
                       global::Runic.Localization.RunicText.Get("text_195f58252ab4"),
                       out failure);
        }

        internal static bool TryGetOrEnsureToken(
            ZNetView view,
            out string tokenValue)
        {
            tokenValue = string.Empty;
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid()) return false;
            string value = zdo.GetString(TokenStorageKey, string.Empty);
            int present = zdo.GetInt(PresenceStorageKey, 0);
            if (present == 1 && IsCanonicalToken(value))
            {
                tokenValue = value;
                return true;
            }
            if (!view.IsOwner()) return false;
            if (present != 0 || value.Length != 0 && !IsCanonicalToken(value)) return false;
            if (value.Length == 0)
            {
                value = Guid.NewGuid().ToString("N");
                zdo.Set(TokenStorageKey, value);
                if (!string.Equals(
                        zdo.GetString(TokenStorageKey, string.Empty),
                        value,
                        StringComparison.Ordinal)) return false;
            }
            zdo.Set(PresenceStorageKey, 1);
            if (zdo.GetInt(PresenceStorageKey, 0) != 1 ||
                !string.Equals(
                    zdo.GetString(TokenStorageKey, string.Empty),
                    value,
                    StringComparison.Ordinal)) return false;
            tokenValue = value;
            return true;
        }

        internal static bool TokenMatches(ZDO zdo, string token) =>
            zdo != null && zdo.IsValid() && IsCanonicalToken(token) &&
            zdo.GetInt(PresenceStorageKey, 0) == 1 &&
            string.Equals(
                zdo.GetString(TokenStorageKey, string.Empty),
                token,
                StringComparison.Ordinal);

        private static bool TryMatchContainer(
            GameObject root,
            ZDOID expected,
            string token,
            int prefabHash,
            out Container container,
            out ZDO zdo)
        {
            zdo = null;
            if (!ValheimAccess.TryResolveExactContainer(root, expected, out container))
                return false;
            zdo = ValheimAccess.Zdo(container);
            if (zdo != null && zdo.GetPrefab() == prefabHash && TokenMatches(zdo, token))
                return true;
            container = null;
            zdo = null;
            return false;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
