using System;
using System.Collections.Generic;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    // Aedis's cross-frame memo, integrated without retaining any consumable MaterialPlan.
    internal static class UiPreviewCache
    {
        internal sealed class Answer
        {
            internal readonly bool Available;
            internal readonly string Reason;
            internal readonly int Nearby;
            internal Answer(bool available, string reason, int nearby = 0)
            { Available = available; Reason = reason; Nearby = nearby; }
        }

        internal readonly struct Key : IEquatable<Key>
        {
            private readonly PreviewRefreshRuntime.QueryKey _scope;
            private readonly string _purpose, _materials;
            internal Key(PreviewRefreshRuntime.QueryKey scope, string purpose, string materials)
            { _scope = scope; _purpose = purpose; _materials = materials; }
            public bool Equals(Key other) => _scope.Equals(other._scope) &&
                string.Equals(_purpose, other._purpose, StringComparison.Ordinal) &&
                string.Equals(_materials, other._materials, StringComparison.Ordinal);
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => unchecked((_scope.GetHashCode() * 397 ^
                (_purpose?.GetHashCode() ?? 0)) * 397 ^ (_materials?.GetHashCode() ?? 0));
        }

        private static readonly PreviewAnswerCache<Key, Answer> Answers = new PreviewAnswerCache<Key, Answer>();
        private static object _network, _player, _database;
        internal static long Epoch => Answers.Epoch;

        internal static void Maintain()
        {
            if (!ReferenceEquals(_network, ZNet.instance) || !ReferenceEquals(_player, Player.m_localPlayer) ||
                !ReferenceEquals(_database, ObjectDB.instance))
            {
                Reset();
                _network = ZNet.instance; _player = Player.m_localPlayer; _database = ObjectDB.instance;
                PreviewRefreshRuntime.Invalidate();
            }
            Answers.Expire(Time.realtimeSinceStartup);
        }

        internal static bool TryKey(Player player, CraftingStation station, Vector3 origin, float range,
            IEnumerable<MaterialRequirement> requirements, string purpose, bool stationless, out Key key)
        {
            key = default;
            Maintain();
            if (!PreviewRefreshRuntime.Supported || !PreviewRefreshRuntime.Cache.Active ||
                PreviewRefreshRuntime.InAction || !ValheimReflection.CanMutateLocalPlayer(player) ||
                !PreviewAnswerCache<Key, Answer>.TrySignature(requirements, out string signature)) return false;
            if (Answers.BeginWindow(Time.realtimeSinceStartup)) PreviewRefreshRuntime.Invalidate();
            Answers.Watch(player.GetInventory());
            Answers.Watch(ValheimReflection.StationZdo(station));
            key = new Key(new PreviewRefreshRuntime.QueryKey(player, station, origin, range, stationless), purpose, signature);
            return true;
        }

        internal static bool TryGet(Key key, out Answer answer)
        {
            bool hit = Answers.TryGet(key, Time.realtimeSinceStartup, out answer);
            if (hit) CachePerformance.UiHits++; else CachePerformance.UiMisses++;
            return hit;
        }
        internal static void Store(Key key, bool available, string reason, long epoch, int nearby = 0) =>
            Answers.Store(key, new Answer(available, reason, nearby), Time.realtimeSinceStartup, epoch);
        internal static void Watch(object dependency) => Answers.Watch(dependency);
        internal static void Changed(object dependency) => Answers.Changed(dependency);
        internal static void Invalidate() => Answers.Invalidate();
        internal static void Reset()
        {
            Answers.Reset(); _network = _player = _database = null;
        }
    }
}
