using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using RunicAwareness.Core;

namespace RunicAwareness.Integration
{
    internal readonly struct CapturedComfort
    {
        internal CapturedComfort(int level, bool sheltered, string detail, ulong signature, int version)
            : this(level, sheltered, detail, signature, version, 0, 0f)
        {
        }

        internal CapturedComfort(
            int level,
            bool sheltered,
            string detail,
            ulong signature,
            int version,
            int sourceInstanceId,
            float capturedAt)
        {
            Level = level;
            Sheltered = sheltered;
            Detail = detail ?? string.Empty;
            Signature = signature;
            Version = version;
            SourceInstanceId = sourceInstanceId;
            CapturedAt = capturedAt;
        }

        internal int Level { get; }
        internal bool Sheltered { get; }
        internal string Detail { get; }
        internal ulong Signature { get; }
        internal int Version { get; }
        internal int SourceInstanceId { get; }
        internal float CapturedAt { get; }
    }

    internal static class ComfortCapture
    {
        internal const float MaximumAgeSeconds = 5f;
        private static readonly StringBuilder Builder = new StringBuilder(512);
        private static CapturedComfort _current;

        internal static CapturedComfort Current => _current;

        internal static bool TryGet(
            Player player,
            int level,
            bool sheltered,
            float now,
            out CapturedComfort captured)
        {
            captured = _current;
            if (player != null && captured.Version > 0 &&
                captured.SourceInstanceId == player.GetInstanceID() &&
                captured.Level == level && captured.Sheltered == sheltered &&
                now >= captured.CapturedAt && now - captured.CapturedAt <= MaximumAgeSeconds)
                return true;
            captured = default;
            return false;
        }

        internal static void Record(Player player, int level)
        {
            if (Plugin.Instance?.Runtime == null || !AwarenessConfig.Enabled.Value ||
                !AwarenessConfig.ShowComfort.Value ||
                player == null || !ReferenceEquals(player, Player.m_localPlayer))
                return;
            bool sheltered = player.InShelter();
            FieldInfo field = ValheimContracts.ComfortPiecesField;
            // Vanilla does not clear or scan s_tempPieces when unsheltered. Reading it in that
            // branch would disclose stale winners from a previous sheltered calculation.
            var pieces = sheltered ? field.GetValue(null) as List<Piece> : null;
            int count = pieces?.Count ?? 0;
            int sourceInstanceId = player.GetInstanceID();
            ulong signature = Hash(level, sheltered, pieces, count);
            signature = Mix(signature, sourceInstanceId);
            if (_current.Version > 0 && _current.Signature == signature)
            {
                // A completed unchanged vanilla pass is fresh evidence. Refresh only its age;
                // retain the version and bounded text so the overlay does not rebuild or flicker.
                _current = new CapturedComfort(
                    _current.Level,
                    _current.Sheltered,
                    _current.Detail,
                    _current.Signature,
                    _current.Version,
                    _current.SourceInstanceId,
                    UnityEngine.Time.unscaledTime);
                return;
            }

            Builder.Clear();
            if (count > AwarenessConfig.HardMaximumComfortPieces)
            {
                Builder.Append("Nearby category details withheld: vanilla returned ")
                    .Append(count).Append(" comfort pieces (hard limit ")
                    .Append(AwarenessConfig.HardMaximumComfortPieces).Append(").");
            }
            else
            {
                AppendWinners(Builder, pieces, count, sheltered);
                if (!sheltered)
                {
                    if (Builder.Length > 0) Builder.Append('\n');
                    Builder.Append("Known opportunity: shelter is not currently counted.");
                }
            }

            _current = new CapturedComfort(
                Math.Max(0, level),
                sheltered,
                BoundedText.Sanitize(Builder.ToString(), 768, 10),
                signature,
                _current.Version + 1,
                sourceInstanceId,
                UnityEngine.Time.unscaledTime);
        }

        internal static void Reset()
        {
            _current = default;
            Builder.Clear();
        }

        private static void AppendWinners(
            StringBuilder builder,
            List<Piece> pieces,
            int count,
            bool sheltered)
        {
            int visibleWinners = Math.Max(
                1,
                Math.Min(8, AwarenessConfig.MaximumComfortRows.Value));
            int winnerCount = 0;
            int ignored = 0;
            Piece previous = null;
            builder.Append("Counted: base +1");
            if (sheltered) builder.Append(", shelter +1");
            for (int index = 0; index < count; index++)
            {
                Piece current = pieces[index];
                if (current == null) continue;
                bool duplicate = previous != null &&
                    (current.m_comfortGroup.ToString() != "None" &&
                     current.m_comfortGroup.Equals(previous.m_comfortGroup) ||
                     string.Equals(current.m_name, previous.m_name, StringComparison.Ordinal));
                int comfort = current.GetComfort();
                if (duplicate || comfort <= 0)
                {
                    ignored++;
                }
                else
                {
                    winnerCount++;
                    if (winnerCount <= visibleWinners)
                    {
                        builder.Append(", ")
                            .Append(BoundedLocalization.Label(current.m_name))
                            .Append(" +").Append(comfort);
                    }
                }
                previous = current;
            }
            if (winnerCount > visibleWinners)
                builder.Append(" (+").Append(winnerCount - visibleWinners).Append(" more winners)");
            if (ignored > 0)
                builder.Append("\nNearby alternatives not counted: ").Append(ignored)
                    .Append(" (same category/name or zero comfort).");
        }

        private static ulong Hash(int level, bool sheltered, List<Piece> pieces, int count)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = Mix(hash, level);
                hash = Mix(hash, sheltered ? 1 : 0);
                hash = Mix(hash, count);
                if (pieces == null || count > AwarenessConfig.HardMaximumComfortPieces) return hash;
                for (int index = 0; index < count; index++)
                {
                    Piece piece = pieces[index];
                    if (piece == null)
                    {
                        hash = Mix(hash, -1);
                        continue;
                    }
                    hash = Mix(hash, piece.m_comfortGroup.GetHashCode());
                    hash = Mix(hash, piece.GetComfort());
                    hash ^= BoundedText.HashBounded(piece.m_name, BoundedText.MaximumLabelCharacters);
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        private static ulong Mix(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * 1099511628211UL;
            }
        }
    }
}
