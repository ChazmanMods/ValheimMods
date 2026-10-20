using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using Companions;
using UnityEngine;
using UnityEngine.UI;

namespace WifeMod
{
    internal sealed class BedBreedingSequence : MonoBehaviour
    {
        private static BedBreedingSequence _instance;
        private bool _running;
        private float _fadeAlpha;
        private string _overlayText;
        private float _overlayTextAlpha;
        private Canvas _canvas;
        private Image _black;
        private TextMeshProUGUI _text;
        private static readonly FieldInfo DreamField = AccessTools.Field(typeof(Hud), "m_dreamField");

        internal static bool TryStart(Bed bed, Player player, WifeCompanion wife)
        {
            if (bed == null || player == null || wife == null) return false;
            if (_instance == null)
            {
                _instance = Plugin.Instance.gameObject.GetComponent<BedBreedingSequence>();
                if (_instance == null) _instance = Plugin.Instance.gameObject.AddComponent<BedBreedingSequence>();
            }
            if (_instance._running) return false;
            _instance.StartCoroutine(_instance.Run(bed, player, wife));
            return true;
        }

        private IEnumerator Run(Bed bed, Player player, WifeCompanion wife)
        {
            _running = true;
            wife.State = WifeState.Breeding;
            float approachTimeout = 20f;
            while (bed != null && wife != null && Vector3.Distance(wife.transform.position, bed.transform.position) > 1.6f && approachTimeout > 0f)
            {
                approachTimeout -= Time.deltaTime;
                wife.MoveTo(bed.transform.position, 1.4f);
                yield return null;
            }
            if (bed == null || player == null || wife == null || approachTimeout <= 0f)
            {
                if (wife != null) wife.State = WifeState.Following;
                if (player != null) player.Message(MessageHud.MessageType.Center, "She could not reach the bed");
                _running = false;
                yield break;
            }

            Vector3 originalBedPosition = bed.transform.localPosition;
            Character wifeCharacter = wife.GetComponent<Character>();
            BaseAI wifeAi = wife.GetComponent<BaseAI>();
            bool wifeAiWasEnabled = wifeAi != null && wifeAi.enabled;
            CompanionRest wifeRest = wife.GetComponent<CompanionRest>();
            bool wifeRestWasEnabled = wifeRest != null && wifeRest.enabled;
            bool playerAttachedByUs = false;
            bool wifeAttached = false;
            GameObject partnerAnchor = null;
            try
            {
                if (wifeAi != null) wifeAi.enabled = false;
                if (wifeRest != null) wifeRest.enabled = false;
                Vector3 spawn = bed.GetSpawnPoint();

                // Let Valheim establish the player's authentic bed pose/camera first.
                BedInteractPatch.Bypass = true;
                bed.Interact(player, false, false);
                BedInteractPatch.Bypass = false;
                yield return new WaitForSeconds(1.5f);

                if (!player.IsSleeping())
                {
                    Vector3 playerLocal = bed.transform.InverseTransformPoint(spawn);
                    player.transform.position = spawn;
                    player.SetSleeping(true);
                    player.AttachStart(bed.transform, bed.gameObject, false, false, true, "sleeping", playerLocal, null);
                    playerAttachedByUs = true;
                }

                // A dedicated anchor prevents Character.AttachStart from collapsing both
                // sleepers onto the Dragon bed's single vanilla spawn point.
                partnerAnchor = new GameObject("WifeMod_DragonBedPartnerAnchor");
                partnerAnchor.transform.SetParent(bed.transform, false);
                partnerAnchor.transform.position = spawn + bed.transform.right * 0.75f;
                partnerAnchor.transform.rotation = bed.transform.rotation;
                wife.transform.position = partnerAnchor.transform.position;
                wife.transform.rotation = partnerAnchor.transform.rotation;
                if (wifeCharacter != null)
                {
                    wifeCharacter.AttachStart(partnerAnchor.transform, bed.gameObject, false, false, true, "sleeping", Vector3.zero, null);
                    wifeAttached = true;
                }

                yield return new WaitForSeconds(3f);

                // Five full seconds of vertical rocking occur before fade starts.
                float elapsed = 0f;
                while (elapsed < 5f)
                {
                    elapsed += Time.deltaTime;
                    bed.transform.localPosition = originalBedPosition + Vector3.up * (Mathf.Sin(elapsed * 5f) * 0.04f);
                    yield return null;
                }
                elapsed = 0f;
                while (elapsed < 4f)
                {
                    elapsed += Time.deltaTime;
                    _fadeAlpha = Mathf.Clamp01(elapsed / 4f);
                    bed.transform.localPosition = originalBedPosition + Vector3.up * (Mathf.Sin((elapsed + 5f) * 5f) * 0.04f);
                    yield return null;
                }
                bed.transform.localPosition = originalBedPosition;
                _fadeAlpha = 1f;
                _overlayText = "Time passes...";
                _overlayTextAlpha = 1f;
                yield return new WaitForSeconds(Mathf.Max(3f, Plugin.SequenceSeconds.Value));

                bool conceived = wife.TryConceive(WifeCompanion.GetCurrentDay(), Plugin.PregnancyChance.Value, Plugin.GestationDays.Value);
                _overlayText = conceived
                    ? $"She is pregnant. The child is due in {Plugin.GestationDays.Value} days."
                    : "No pregnancy this time.";
                yield return new WaitForSeconds(4f);

                float fade = 0f;
                while (fade < 3f)
                {
                    fade += Time.deltaTime;
                    _fadeAlpha = 1f - Mathf.Clamp01(fade / 3f);
                    yield return null;
                }
                _fadeAlpha = 0f;
                float resultHold = 0f;
                while (resultHold < 7f)
                {
                    resultHold += Time.deltaTime;
                    _overlayTextAlpha = 1f - Mathf.Clamp01((resultHold - 5f) / 2f);
                    yield return null;
                }
            }
            finally
            {
                BedInteractPatch.Bypass = false;
                _fadeAlpha = 0f;
                _overlayTextAlpha = 0f;
                _overlayText = null;
                if (bed != null) bed.transform.localPosition = originalBedPosition;
                if (wifeAttached && wifeCharacter != null) wifeCharacter.AttachStop();
                if (partnerAnchor != null) Destroy(partnerAnchor);
                if (wife != null) wife.ForceWake();
                if (playerAttachedByUs && player != null) { player.AttachStop(); player.SetSleeping(false); }
                if (wifeAi != null) wifeAi.enabled = wifeAiWasEnabled;
                if (wifeRest != null) wifeRest.enabled = wifeRestWasEnabled;
                if (wife != null) wife.State = WifeState.Following;
                _running = false;
            }
        }

        private void LateUpdate()
        {
            EnsureOverlay();
            if (_black != null) _black.color = new Color(0f, 0f, 0f, _fadeAlpha);
            if (_text != null)
            {
                _text.text = _overlayText ?? string.Empty;
                _text.color = new Color(0.14705884f, 0.75294137f, 1f, _overlayTextAlpha);
            }
            if (_canvas != null) _canvas.enabled = _fadeAlpha > 0f || _overlayTextAlpha > 0f;
        }

        private void EnsureOverlay()
        {
            if (_canvas != null) return;
            GameObject root = new GameObject("WifeMod_BedOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            root.GetComponent<GraphicRaycaster>().enabled = false;

            GameObject blackObject = new GameObject("Black", typeof(RectTransform), typeof(Image));
            blackObject.transform.SetParent(root.transform, false);
            RectTransform blackRect = blackObject.GetComponent<RectTransform>();
            blackRect.anchorMin = Vector2.zero; blackRect.anchorMax = Vector2.one;
            blackRect.offsetMin = Vector2.zero; blackRect.offsetMax = Vector2.zero;
            _black = blackObject.GetComponent<Image>();
            _black.raycastTarget = false;

            GameObject textObject = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.15f, 0.35f); textRect.anchorMax = new Vector2(0.85f, 0.65f);
            textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
            _text = textObject.GetComponent<TextMeshProUGUI>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = 42f;
            _text.enableWordWrapping = true;
            _text.raycastTarget = false;
            TMP_Text source = Hud.instance != null && DreamField != null ? DreamField.GetValue(Hud.instance) as TMP_Text : null;
            if (source == null && MessageHud.instance != null) source = MessageHud.instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (source != null)
            {
                _text.font = source.font;
                _text.fontSharedMaterial = source.fontSharedMaterial;
                _text.fontStyle = source.fontStyle;
                _text.fontSize = source.fontSize;
            }
            else _text.fontSize = 32f;
            _text.color = new Color(0.14705884f, 0.75294137f, 1f, 0f);
            _canvas.enabled = false;
        }
    }
}
