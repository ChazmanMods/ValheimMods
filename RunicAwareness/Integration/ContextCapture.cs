using System;
using UnityEngine;

namespace RunicAwareness.Integration
{
    internal enum AwarenessContextKind
    {
        None = 0,
        Production = 1,
        Agriculture = 2,
        Building = 3,
        TamedAnimal = 4
    }

    internal readonly struct CapturedContext
    {
        internal CapturedContext(
            AwarenessContextKind kind,
            Component source,
            Component subject,
            string visibleText,
            float capturedAt)
        {
            Kind = kind;
            Source = source;
            Subject = subject;
            VisibleText = visibleText ?? string.Empty;
            CapturedAt = capturedAt;
        }

        internal AwarenessContextKind Kind { get; }
        internal Component Source { get; }
        internal Component Subject { get; }
        internal string VisibleText { get; }
        internal float CapturedAt { get; }
    }

    internal static class ContextCapture
    {
        internal const float MaximumAgeSeconds = 0.8f;
        internal const int MaximumVisibleTextCharacters = 8192;
        private static CapturedContext _current;

        internal static void Record(
            AwarenessContextKind kind,
            Component source,
            Component subject,
            string visibleText)
        {
            if (Plugin.Instance?.Runtime == null || !AwarenessConfig.Enabled.Value ||
                !IsEnabled(kind) ||
                source == null || subject == null || kind == AwarenessContextKind.None)
                return;
            if (visibleText != null && visibleText.Length > MaximumVisibleTextCharacters)
            {
                _current = default;
                return;
            }
            _current = new CapturedContext(
                kind,
                source,
                subject,
                visibleText,
                Time.unscaledTime);
        }

        internal static bool TryGet(GameObject hoverObject, float now, out CapturedContext context)
        {
            context = _current;
            if (hoverObject == null || context.Source == null || context.Subject == null ||
                now < context.CapturedAt || now - context.CapturedAt > MaximumAgeSeconds)
            {
                _current = default;
                return false;
            }
            Transform hover = hoverObject.transform;
            return SameHierarchy(hover, context.Source.transform) ||
                   SameHierarchy(hover, context.Subject.transform);
        }

        internal static void Reset() => _current = default;

        private static bool IsEnabled(AwarenessContextKind kind)
        {
            switch (kind)
            {
                case AwarenessContextKind.Production: return AwarenessConfig.ShowProduction.Value;
                case AwarenessContextKind.Agriculture: return AwarenessConfig.ShowAgriculture.Value;
                case AwarenessContextKind.Building: return AwarenessConfig.ShowBuilding.Value;
                case AwarenessContextKind.TamedAnimal: return AwarenessConfig.ShowTamedAnimals.Value;
                default: return false;
            }
        }

        private static bool SameHierarchy(Transform left, Transform right)
        {
            if (left == null || right == null) return false;
            Transform current = left;
            for (int depth = 0; current != null && depth < 16; depth++, current = current.parent)
                if (current == right) return true;
            current = right;
            for (int depth = 0; current != null && depth < 16; depth++, current = current.parent)
                if (current == left) return true;
            return false;
        }
    }

    internal static class HoverItemCapture
    {
        internal const float MaximumAgeSeconds = 0.8f;
        private static ItemDrop.ItemData _item;
        private static float _capturedAt;
        private static int _sourcePlayerInstanceId;

        internal static void Record(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (Plugin.Instance?.Runtime == null || !AwarenessConfig.Enabled.Value ||
                !AwarenessConfig.ShowItemComparison.Value || item == null || player == null)
                return;
            _item = item;
            _capturedAt = Time.unscaledTime;
            _sourcePlayerInstanceId = player.GetInstanceID();
        }

        internal static bool TryGet(Player player, float now, out ItemDrop.ItemData item)
        {
            item = _item;
            if (player != null && item != null &&
                _sourcePlayerInstanceId == player.GetInstanceID() &&
                now >= _capturedAt && now - _capturedAt <= MaximumAgeSeconds)
                return true;
            Reset();
            item = null;
            return false;
        }

        internal static void Reset()
        {
            _item = null;
            _capturedAt = 0f;
            _sourcePlayerInstanceId = 0;
        }
    }
}
