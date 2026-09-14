using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class PreviewRefreshRuntime
    {
        internal sealed class Sources
        {
            internal readonly IReadOnlyList<IMutableMaterialSource> Items;
            internal readonly string Reason;
            internal Sources(IReadOnlyList<IMutableMaterialSource> items, string reason)
            { Items = items; Reason = reason; }
        }

        internal readonly struct QueryKey : IEquatable<QueryKey>
        {
            private readonly Player _player;
            private readonly CraftingStation _station;
            private readonly Vector3 _origin;
            private readonly float _radius;
            private readonly bool _stationless;
            private readonly int _level;
            private readonly long _playerId;
            internal QueryKey(Player player, CraftingStation station, Vector3 origin, float radius, bool stationless)
            { _player = player; _playerId = player != null ? player.GetPlayerID() : 0; _station = station; _origin = origin; _radius = radius; _stationless = stationless; _level = Game.m_worldLevel; }
            public bool Equals(QueryKey other) => ReferenceEquals(_player, other._player) &&
                ReferenceEquals(_station, other._station) && _origin.Equals(other._origin) &&
                _radius.Equals(other._radius) && _stationless == other._stationless && _level == other._level && _playerId == other._playerId;
            public override bool Equals(object other) => other is QueryKey key && Equals(key);
            public override int GetHashCode() => unchecked(
                (((ReferenceEquals(_player, null) ? 0 : RuntimeHelpers.GetHashCode(_player)) * 397 ^
                  (ReferenceEquals(_station, null) ? 0 : RuntimeHelpers.GetHashCode(_station))) * 397 ^
                 _origin.GetHashCode()) * 397 ^ _radius.GetHashCode() ^ _level ^ _playerId.GetHashCode() ^ (_stationless ? 1 : 0));
        }

        internal static readonly RefreshQueryCache<QueryKey, Sources> Cache =
            new RefreshQueryCache<QueryKey, Sources>();

        // Only the private decoded inventory is excluded from mutation invalidation.
        // Another inventory changed by a prefab/mod callback still invalidates the refresh.
        [ThreadStatic] internal static Inventory LoadingPreview;
        [ThreadStatic] private static int _actionDepth;
        internal static bool InAction => _actionDepth > 0;
        internal static bool Supported = true;

        internal static long Begin() => Supported
            ? Cache.Begin(Time.frameCount, ZNet.instance, Player.m_localPlayer, ObjectDB.instance) : 0;
        internal static void End(long scope) => Cache.End(scope);
        internal static void Invalidate() => Cache.Invalidate();
        internal static void EnterAction()
        {
            _actionDepth++;
            Invalidate(); UiPreviewCache.Invalidate();
        }
        internal static void ExitAction()
        {
            if (_actionDepth > 0) _actionDepth--;
            Invalidate(); UiPreviewCache.Invalidate();
        }
        internal static void Reset() { Cache.Reset(); _actionDepth = 0; }
    }
}
