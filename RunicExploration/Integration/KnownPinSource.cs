using System;
using System.Collections.Generic;
using RunicExploration.Core;
using UnityEngine;

namespace RunicExploration.Integration
{
    internal enum KnownPinCaptureStatus
    {
        Ready = 0,
        Oversized = 1,
        InvalidMap = 2
    }

    internal static class KnownPinSource
    {
        internal const int HardMaximumTextureSize = 4096;

        internal static KnownPinCaptureStatus Capture(
            Minimap minimap,
            bool includeSharedCoverage,
            List<PinEvidence> destination,
            out ulong fingerprint,
            out int sourceCount)
        {
            fingerprint = 1469598103934665603UL;
            sourceCount = 0;
            destination?.Clear();
            if (destination == null || !ValheimContracts.TryRead(minimap, out MinimapReadView view) ||
                !ValidCoverage(view, includeSharedCoverage))
                return KnownPinCaptureStatus.InvalidMap;
            sourceCount = view.Pins.Count;
            fingerprint = Mix(fingerprint, includeSharedCoverage ? 1 : 0);
            if (sourceCount > KnownPinIndexer.HardMaximumPins)
                return KnownPinCaptureStatus.Oversized;

            for (int index = 0; index < sourceCount; index++)
            {
                Minimap.PinData pin = view.Pins[index];
                if (pin == null) continue;

                Vector3 position = pin.m_pos;
                bool saved = pin.m_save;
                bool known = IsKnown(view, position, includeSharedCoverage);
                // Unknown pins are never inspected beyond the coordinates needed to apply the
                // explored-cell gate. Names, types, authors, and state remain unread.
                if (!saved || !known) continue;
                // Coordinates contribute to the retained-state fingerprint only after the same
                // disclosure gate. Moving unknown/unsaved third-party pins cannot churn the index.
                fingerprint = Mix(fingerprint, Quantize(position.x));
                fingerprint = Mix(fingerprint, Quantize(position.y));
                fingerprint = Mix(fingerprint, Quantize(position.z));

                var evidence = new PinEvidence(
                    index,
                    pin.m_name,
                    (int)pin.m_type,
                    position.x,
                    position.y,
                    position.z,
                    true,
                    true,
                    pin.m_checked,
                    pin.m_ownerID);
                destination.Add(evidence);
                fingerprint ^= KnownPinIndexer.EvidenceHash(evidence);
                fingerprint *= 1099511628211UL;
            }
            return KnownPinCaptureStatus.Ready;
        }

        internal static bool TryResolve(
            Minimap minimap,
            bool includeSharedCoverage,
            KnownPinRecord record,
            out Vector3 position)
        {
            position = default;
            if (!ValheimContracts.TryRead(minimap, out MinimapReadView view) ||
                !ValidCoverage(view, includeSharedCoverage) ||
                record.SourceIndex < 0 || record.SourceIndex >= view.Pins.Count)
                return false;
            Minimap.PinData pin = view.Pins[record.SourceIndex];
            if (pin == null || !pin.m_save || !IsKnown(view, pin.m_pos, includeSharedCoverage))
                return false;
            var current = new PinEvidence(
                record.SourceIndex,
                pin.m_name,
                (int)pin.m_type,
                pin.m_pos.x,
                pin.m_pos.y,
                pin.m_pos.z,
                true,
                true,
                pin.m_checked,
                pin.m_ownerID);
            if (KnownPinIndexer.EvidenceHash(current) != record.EvidenceHash) return false;
            position = pin.m_pos;
            return IsFinite(position);
        }

        internal static bool IsKnown(
            MinimapReadView view,
            Vector3 position,
            bool includeSharedCoverage)
        {
            if (!IsFinite(position) || !Finite(view.PixelSize) || view.PixelSize <= 0f ||
                view.TextureSize <= 0) return false;
            float half = view.TextureSize / 2f;
            float mapX = position.x / view.PixelSize + half;
            float mapY = position.z / view.PixelSize + half;
            if (!Finite(mapX) || !Finite(mapY) || mapX < int.MinValue || mapX > int.MaxValue ||
                mapY < int.MinValue || mapY > int.MaxValue) return false;
            int x = Utils.RoundToInt(mapX);
            int y = Utils.RoundToInt(mapY);
            if (x < 0 || x >= view.TextureSize || y < 0 || y >= view.TextureSize) return false;
            int offset = y * view.TextureSize + x;
            return view.Explored[offset] || includeSharedCoverage &&
                   view.ExploredOthers != null && view.ExploredOthers[offset];
        }

        private static bool ValidCoverage(MinimapReadView view, bool includeSharedCoverage)
        {
            int size = view.TextureSize;
            if (size <= 0 || size > HardMaximumTextureSize || !Finite(view.PixelSize) ||
                view.PixelSize <= 0f) return false;
            long required = (long)size * size;
            if (required > int.MaxValue || view.Explored == null || view.Explored.Length < required)
                return false;
            return !includeSharedCoverage ||
                   view.ExploredOthers != null && view.ExploredOthers.Length >= required;
        }

        private static bool IsFinite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static int Quantize(float value)
        {
            if (!Finite(value)) return int.MinValue;
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return rounded > int.MaxValue ? int.MaxValue :
                rounded < int.MinValue ? int.MinValue : (int)rounded;
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
