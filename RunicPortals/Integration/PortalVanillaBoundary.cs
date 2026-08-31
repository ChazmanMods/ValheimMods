using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Integration
{
    internal sealed partial class PortalRuntime
    {
        internal bool ResolvePortalVisualState(
            TeleportWorld portal,
            bool vanillaResult,
            bool requireResolvedTarget)
        {
            if (!FeatureEnabled || portal == null) return vanillaResult;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO source = view != null && view.IsValid() ? view.GetZDO() : null;
            if (source == null || source.m_uid.IsNone()) return false;
            int sourceMode = PortalZdoCodec.GetMode(source);
            if (sourceMode == (int)PortalMode.Network)
                return PortalZdoCodec.TryRead(
                           source, out PortalEndpoint endpoint, out _) &&
                       endpoint.Mode == PortalMode.Network &&
                       endpoint.OnlineState == PortalOnlineState.Online;
            if (sourceMode != (int)PortalMode.StandardPair) return false;

            ZDOID targetId = source.GetConnectionZDOID(
                ZDOExtraData.ConnectionType.Portal);
            if (targetId.IsNone()) return vanillaResult;
            ZDO target = ZDOMan.instance?.GetZDO(targetId);
            if (target == null)
                return requireResolvedTarget ? false : vanillaResult;
            // Assign the result rather than only turning false into true: an old mixed pair must
            // never look connected after one endpoint is converted to a Runic network.
            return PortalZdoCodec.GetMode(target) == (int)PortalMode.StandardPair &&
                   vanillaResult;
        }

        internal bool TryFilterVanillaPortalCandidates(
            ZDO source,
            List<ZDO> candidates,
            out List<ZDO> filtered)
        {
            filtered = null;
            if (!FeatureEnabled || source == null || candidates == null) return false;
            filtered = new List<ZDO>(candidates.Count);
            if (PortalZdoCodec.GetMode(source) != (int)PortalMode.StandardPair)
                return true;
            for (int index = 0; index < candidates.Count; index++)
            {
                ZDO candidate = candidates[index];
                if (candidate != null &&
                    PortalZdoCodec.GetMode(candidate) == (int)PortalMode.StandardPair)
                    filtered.Add(candidate);
            }
            return true;
        }

        private bool StandardPairTargetsNetwork(TeleportWorld portal)
        {
            if (!FeatureEnabled || portal == null) return false;
            ZNetView view = portal.GetComponent<ZNetView>();
            ZDO source = view != null && view.IsValid() ? view.GetZDO() : null;
            if (source == null ||
                PortalZdoCodec.GetMode(source) != (int)PortalMode.StandardPair)
                return false;
            ZDOID targetId = source.GetConnectionZDOID(
                ZDOExtraData.ConnectionType.Portal);
            if (targetId.IsNone()) return false;
            ZDO target = ZDOMan.instance?.GetZDO(targetId);
            return target != null &&
                   PortalZdoCodec.GetMode(target) != (int)PortalMode.StandardPair;
        }
    }
}
