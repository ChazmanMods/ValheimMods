using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    internal readonly struct AgricultureHintRow
    {
        internal AgricultureHintRow(string key, string action)
        {
            Key = key ?? string.Empty;
            Action = action ?? string.Empty;
        }

        internal string Key { get; }
        internal string Action { get; }
    }

    internal sealed class AgricultureControlBarContent
    {
        internal AgricultureControlBarContent(params AgricultureHintRow[] rows)
        {
            Rows = rows ?? Array.Empty<AgricultureHintRow>();
        }

        internal IReadOnlyList<AgricultureHintRow> Rows { get; }
    }

    /// <summary>
    /// Leases Valheim's own build-hint rows while a crop preview is active. This deliberately
    /// avoids a second IMGUI panel: fonts, key caps, anchoring, scaling, and controller layout all
    /// remain the installed game's native presentation. Every touched label is restored when the
    /// crop context ends.
    /// </summary>
    internal sealed class AgricultureControlBar : IDisposable
    {
        private readonly Dictionary<TMP_Text, string> _originalText =
            new Dictionary<TMP_Text, string>();
        private GameObject _root;
        private Vector3 _originalScale;
        private bool _hasOriginalScale;

        internal bool Apply(
            KeyHints hints,
            AgricultureControlBarContent content,
            float requestedScale)
        {
            GameObject root = hints != null ? hints.m_buildHints : null;
            if (root == null || content == null || content.Rows.Count == 0)
            {
                Restore();
                return false;
            }

            if (!ReferenceEquals(_root, root))
            {
                Restore();
                _root = root;
                _originalScale = root.transform.localScale;
                _hasOriginalScale = true;
                TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
                for (int index = 0; index < labels.Length; index++)
                    if (labels[index] != null && !_originalText.ContainsKey(labels[index]))
                        _originalText.Add(labels[index], labels[index].text ?? string.Empty);
            }

            float scale = Mathf.Clamp(requestedScale, 0.75f, 1.75f);
            root.transform.localScale = Vector3.Scale(
                _originalScale,
                new Vector3(scale, scale, scale));

            TMP_Text[] keys =
            {
                hints.m_buildMenuKey,
                hints.m_buildRotateKey,
                hints.m_buildAlternativePlacingKey,
                hints.m_dodgeKey,
                hints.m_cycleSnapKey
            };
            var knownKeys = new HashSet<TMP_Text>(keys.Where(value => value != null));
            int applied = 0;
            int count = Math.Min(keys.Length, content.Rows.Count);
            for (int index = 0; index < count; index++)
            {
                TMP_Text key = keys[index];
                if (key == null) continue;
                key.text = content.Rows[index].Key;
                TMP_Text action = FindActionLabel(key, root.transform, knownKeys);
                if (action != null) action.text = content.Rows[index].Action;
                applied++;
            }
            return applied > 0;
        }

        internal void Restore()
        {
            foreach (KeyValuePair<TMP_Text, string> pair in _originalText)
                if (pair.Key != null) pair.Key.text = pair.Value;
            if (_root != null && _hasOriginalScale)
                _root.transform.localScale = _originalScale;
            _originalText.Clear();
            _root = null;
            _hasOriginalScale = false;
        }

        public void Dispose() => Restore();

        private static TMP_Text FindActionLabel(
            TMP_Text key,
            Transform root,
            ISet<TMP_Text> knownKeys)
        {
            Transform candidate = key.transform.parent;
            while (candidate != null)
            {
                TMP_Text[] labels = candidate.GetComponentsInChildren<TMP_Text>(true);
                int keyCount = 0;
                for (int index = 0; index < labels.Length; index++)
                    if (knownKeys.Contains(labels[index])) keyCount++;
                if (keyCount == 1 && labels.Length >= 2)
                {
                    TMP_Text best = null;
                    for (int index = 0; index < labels.Length; index++)
                    {
                        TMP_Text label = labels[index];
                        if (label == null || ReferenceEquals(label, key) || knownKeys.Contains(label))
                            continue;
                        if (best == null || (label.text?.Length ?? 0) > (best.text?.Length ?? 0))
                            best = label;
                    }
                    if (best != null) return best;
                }
                if (ReferenceEquals(candidate, root)) break;
                candidate = candidate.parent;
            }
            return null;
        }
    }
}
