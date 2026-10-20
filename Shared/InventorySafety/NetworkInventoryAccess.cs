using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RunicAutomation.Operations
{
    internal static class NetworkInventoryAccess
    {
        internal static bool TryActor(long sender, out string actor)
        {
            actor = null;
            ZNet network = ZNet.instance;
            if (network == null || !network.IsServer()) return false;
            if (sender == ZDOMan.GetSessionID() && Player.m_localPlayer != null)
            {
                actor = Player.m_localPlayer.GetPlayerID().ToString(CultureInfo.InvariantCulture);
                return true;
            }
            ZNetPeer peer = network.GetPeer(sender);
            if (!TryPeerPrincipal(peer, out long principal, out _)) return false;
            if (network.GetPeers().Count(x => TryPeerPrincipal(x, out long candidate, out _) && candidate == principal) != 1) return false;
            actor = principal.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal static bool TryPeerPrincipal(ZNetPeer peer, out long principal, out Vector3 position)
        {
            principal = 0;
            position = Vector3.zero;
            if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone()) return false;
            ZDO character = ZDOMan.instance?.GetZDO(peer.m_characterID);
            if (character == null || character.GetOwner() != peer.m_uid) return false;
            // Current Valheim sends CharacterID and stores the player identity on that ZDO;
            // the obsolete PlayerID RPC does not populate ZNetPeer.m_playerID during a normal join.
            long identity = character.GetLong(ZDOVars.s_playerID, 0L);
            Vector3 location = character.GetPosition();
            if (identity == 0 || !Finite(location)) return false;
            principal = identity;
            position = location;
            return true;
        }

        internal static bool TryPosition(string actor, out long principal, out Vector3 position)
        {
            position = Vector3.zero;
            if (!long.TryParse(actor, NumberStyles.Integer, CultureInfo.InvariantCulture, out principal) || principal == 0 ||
                ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == principal)
            { position = Player.m_localPlayer.transform.position; return Finite(position); }
            int matches = 0;
            Vector3 matchedPosition = Vector3.zero;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (!TryPeerPrincipal(peer, out long candidate, out Vector3 candidatePosition) || candidate != principal) continue;
                if (++matches > 1) return false;
                matchedPosition = candidatePosition;
            }
            if (matches != 1) return false;
            position = matchedPosition;
            return true;
        }

        internal static Container Descriptor(ZDO chest)
        {
            GameObject prefab = chest == null ? null : ZNetScene.instance?.GetPrefab(chest.GetPrefab());
            Container descriptor = prefab == null ? null : prefab.GetComponent<Container>();
            if (descriptor == null || descriptor.m_wagon != null || descriptor.m_rootObjectOverride != null ||
                descriptor.m_autoDestroyEmpty || prefab.GetComponent<Piece>() == null ||
                !NativeInventoryCodec.ValidDimensions(descriptor.m_width, descriptor.m_height))
                return null;
            return descriptor;
        }

        internal static bool CanAccess(string actor, ZDO chest, float range, bool requireWard, bool excludePrivate, out string reason)
        {
            reason = "actor-unavailable";
            if (!TryPosition(actor, out long principal, out Vector3 position)) return false;
            return CanAccessPrincipal(principal, position, chest, range, requireWard, excludePrivate, out reason);
        }
        internal static bool CanAccessPrincipal(long principal, Vector3 position, ZDO chest, float range, bool requireWard, bool excludePrivate, out string reason)
        {
            Container descriptor = Descriptor(chest);
            reason = "unsupported-container";
            if (principal == 0 || !Finite(position) || descriptor == null || !Finite(chest.GetPosition())) return false;
            reason = "out-of-range";
            if (!Finite(range) || range < 0 || range > 100 || (chest.GetPosition() - position).sqrMagnitude > range * range) return false;
            reason = "access-denied";
            switch (descriptor.m_privacy)
            {
                case Container.PrivacySetting.Public: break;
                case Container.PrivacySetting.Private:
                    if (excludePrivate || chest.GetLong(ZDOVars.s_creator) != principal) return false;
                    break;
                default: return false;
            }
            if ((requireWard || descriptor.m_checkGuardStone) && !WardAllows(chest.GetPosition(), principal)) return false;
            reason = null; return true;
        }

        internal static IReadOnlyList<ZDO> Nearby(Vector3 point, float radius)
        {
            if (!Finite(point) || !Finite(radius) || radius < 0 || radius > 2048 || ZDOMan.instance == null || ZoneSystem.instance == null)
                throw new InvalidOperationException("World spatial index unavailable");
            var candidates = new List<ZDO>();
            int sectors = Mathf.CeilToInt(radius / 64f) + 1;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(point), new SimulationDistance(sectors, 0, true), candidates);
            return candidates.Where(x => x != null && (x.GetPosition() - point).sqrMagnitude <= radius * radius).ToArray();
        }

        internal static bool WardAllows(Vector3 point, long principal)
        {
            if (ZNetScene.instance == null) return false;
            var wards = new Dictionary<int, PrivateArea>();
            float maximumRadius = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                PrivateArea area = prefab == null ? null : prefab.GetComponent<PrivateArea>();
                if (area == null) continue;
                if (!Finite(area.m_radius) || area.m_radius < 0 || area.m_radius > 2048) return false;
                wards[prefab.name.GetStableHashCode()] = area;
                maximumRadius = Math.Max(maximumRadius, area.m_radius);
            }
            bool denied = false, allowed = false;
            // Native wards test horizontal distance, not spherical distance. Enumerate full sectors
            // so a ward on another floor still protects this point.
            var candidates = new List<ZDO>();
            int sectors = Mathf.CeilToInt(maximumRadius / 64f) + 1;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(point), new SimulationDistance(sectors, 0, true), candidates);
            foreach (ZDO ward in candidates)
            {
                if (!wards.TryGetValue(ward.GetPrefab(), out var descriptor) || !ward.GetBool(ZDOVars.s_enabled)) continue;
                Vector3 delta = ward.GetPosition() - point;
                if (delta.x * delta.x + delta.z * delta.z >= descriptor.m_radius * descriptor.m_radius) continue;
                bool permitted = ward.GetLong(ZDOVars.s_creator) == principal;
                int count = ward.GetInt(ZDOVars.s_permitted);
                if (count < 0 || count > 1024) return false;
                for (int n = 0; n < count && !permitted; n++) permitted = ward.GetLong("pu_id" + n, 0) == principal;
                allowed |= permitted; denied |= !permitted;
            }
            // Match native CheckAccess's default overlapping-ward rule.
            return allowed || !denied;
        }
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
