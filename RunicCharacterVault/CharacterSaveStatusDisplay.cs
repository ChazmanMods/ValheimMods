using System.Collections;
using TMPro;
using UnityEngine;

namespace RunicCharacterVault
{
    internal sealed class CharacterSaveStatusDisplay
    {
        private const float ActiveDisplaySeconds = 30f;
        private const float CommitTimeoutSeconds = 20f;
        private const float ResultDisplaySeconds = 3f;
        private readonly SaveStatusLifecycle _lifecycle = new SaveStatusLifecycle();
        private TextMeshProUGUI _label;

        internal void Attach(Minimap minimap)
        {
            if (_label != null || minimap?.m_mapImageSmall == null ||
                minimap.m_biomeNameSmall == null)
            {
                return;
            }

            // Clone Valheim's already initialized biome label instead of adding a fresh
            // TextMeshProUGUI component.  Unity 6 tries to assign the removed TMP default
            // LiberationSans asset while AddComponent runs, which produces a missing-font
            // warning before we have a chance to copy Valheim's live font and material.
            GameObject target = Object.Instantiate(
                minimap.m_biomeNameSmall.gameObject,
                minimap.m_smallRoot.transform,
                false);
            target.name = "CharacterVaultSaveStatus";
            RectTransform rect = target.GetComponent<RectTransform>();
            _label = target.GetComponent<TextMeshProUGUI>();
            if (rect == null || _label == null || _label.font == null)
            {
                Object.Destroy(target);
                _label = null;
                return;
            }
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(480f, 30f);
            RectTransform mapRect = minimap.m_mapImageSmall.rectTransform;
            rect.position = mapRect.TransformPoint(
                new Vector3(mapRect.rect.center.x, mapRect.rect.yMin - 8f, 0f));
            rect.SetAsLastSibling();

            _label.fontSize = minimap.m_biomeNameSmall.fontSize;
            _label.fontSizeMax = minimap.m_biomeNameSmall.fontSize;
            _label.fontSizeMin = 8f;
            _label.enableAutoSizing = true;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Color.white;
            _label.raycastTarget = false;
            _label.text = string.Empty;
            target.SetActive(false);
        }

        internal void ShowSaving(string requestId)
        {
            Show(requestId, SaveStatusMessages.Saving, ActiveDisplaySeconds, false);
        }

        internal void ShowAccepted(string requestId)
        {
            int version = Show(requestId, SaveStatusMessages.Accepted, ActiveDisplaySeconds, true);
            CharacterVaultPlugin.Instance?.Run(FailWithoutCommit(requestId, version));
        }

        internal void ShowCommitted(string requestId)
        {
            if (_lifecycle.CanCommit(requestId))
            {
                Show(requestId, SaveStatusMessages.Saved, ResultDisplaySeconds, false);
            }
        }

        internal void Hide()
        {
            _lifecycle.Clear();
            if (_label != null)
            {
                _label.gameObject.SetActive(false);
            }
        }

        internal void Dispose()
        {
            _lifecycle.Clear();
            if (_label != null)
            {
                Object.Destroy(_label.gameObject);
                _label = null;
            }
        }

        private int Show(string requestId, string message, float duration, bool waitingForCommit)
        {
            int version = _lifecycle.Begin(requestId, waitingForCommit);
            Attach(Minimap.instance);
            if (_label == null)
            {
                return version;
            }

            _label.text = message;
            _label.gameObject.SetActive(true);
            CharacterVaultPlugin.Instance?.Run(HideAfterDelay(version, duration));
            return version;
        }

        private IEnumerator FailWithoutCommit(string requestId, int version)
        {
            yield return new WaitForSecondsRealtime(CommitTimeoutSeconds);
            if (_lifecycle.CanFail(requestId, version))
            {
                Show(requestId, SaveStatusMessages.Failed, ResultDisplaySeconds, false);
            }
        }

        private IEnumerator HideAfterDelay(int version, float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            if (_lifecycle.IsCurrent(version))
            {
                Hide();
            }
        }
    }
}
