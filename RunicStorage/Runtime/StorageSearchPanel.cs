using System;
using System.Collections.Generic;
using RunicStorage.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicStorage.Runtime
{
    internal sealed class StorageSearchEntry
    {
        internal StorageSearchEntry(
            string resourceId,
            string displayName,
            int quantity,
            IReadOnlyList<Container> containers)
        {
            ResourceId = resourceId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Quantity = Math.Max(0, quantity);
            Containers = containers ?? Array.Empty<Container>();
        }

        internal string ResourceId { get; }
        internal string DisplayName { get; }
        internal int Quantity { get; }
        internal IReadOnlyList<Container> Containers { get; }
    }

    internal sealed class StorageSearchPanel : IDisposable
    {
        private const int MaximumEntries = 256;
        private readonly List<StorageSearchEntry> _entries = new List<StorageSearchEntry>();
        private string _filter = string.Empty;
        private bool _open;
        private bool _focusFilter;
        private bool _cursorVisible;
        private CursorLockMode _cursorLock;
        private GameObject _canvasObject;
        private RectTransform _panelRect;
        private RectTransform _entryContent;
        private ScrollRect _scrollRect;
        private TMP_InputField _filterInput;
        private StorageSearchVanillaTheme _theme;
        private int _themeSourceToken = int.MinValue;
        private int _appearanceFontSize = -1;
        private StorageSearchMenuColor _appearanceFontColor;
        private float _savedScrollPosition = 1f;
        private float _nextThemeSourceCheck;

        internal bool IsOpen => _open;
        internal static int EntryLimit => MaximumEntries;

        internal void Open(IReadOnlyList<StorageSearchEntry> entries)
        {
            Close();
            _entries.Clear();
            if (entries != null)
            {
                for (int index = 0; index < entries.Count && _entries.Count < MaximumEntries; index++)
                    if (entries[index] != null) _entries.Add(entries[index]);
            }
            _filter = string.Empty;
            _savedScrollPosition = 1f;
            _cursorVisible = Cursor.visible;
            _cursorLock = Cursor.lockState;
            _open = true;
            _focusFilter = true;
            RenewCursorLease();
            EnsureNativeView(true);
        }

        internal void Tick()
        {
            if (!_open) return;
            RenewCursorLease();
            EnsureNativeView(false);
            if (_focusFilter && _filterInput)
            {
                _filterInput.Select();
                _filterInput.ActivateInputField();
                _focusFilter = false;
            }
            if (StorageSearchGameplayInputGuard.PollPickerEscape()) Close();
        }

        internal void Draw()
        {
            if (!_open) return;
            RenewCursorLease();
            // OnGUI still receives raw mouse events even though presentation is native uGUI.
            // Retaining this tiny bridge keeps the proven post-close attack suppression latch.
            StorageSearchGameplayInputGuard.CaptureGuiPointer(Event.current);
        }

        internal static void RenewCursorLease()
        {
            if (!Plugin.SearchPanelOpen) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        internal void Close()
        {
            if (!_open) return;
            _open = false;
            _focusFilter = false;
            if (_filterInput) _filterInput.DeactivateInputField();
            DestroyNativeView();
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
        }

        public void Dispose()
        {
            Close();
            DestroyNativeView();
            _entries.Clear();
            StorageSearchHighlight.ClearAll();
        }

        private void EnsureNativeView(bool force)
        {
            int sourceToken = _themeSourceToken;
            if (force || !_canvasObject || Time.unscaledTime >= _nextThemeSourceCheck)
            {
                sourceToken = StorageSearchVanillaTheme.CurrentSourceToken();
                _nextThemeSourceCheck = Time.unscaledTime + 1f;
            }
            int configuredFontSize = PluginConfig.SearchMenuFontSize?.Value ??
                                     StorageSearchMenuAppearance.DefaultFontSize;
            StorageSearchMenuColor configuredColor =
                StorageSearchMenuAppearance.ResolveFontColor(
                    PluginConfig.SearchMenuFontColor?.Value ??
                    StorageSearchMenuAppearance.DefaultFontColor);
            if (!force && _canvasObject && _themeSourceToken == sourceToken &&
                _appearanceFontSize == configuredFontSize &&
                _appearanceFontColor.Equals(configuredColor)) return;

            if (_scrollRect) _savedScrollPosition = _scrollRect.verticalNormalizedPosition;
            DestroyNativeView();
            _themeSourceToken = sourceToken;
            _appearanceFontSize = configuredFontSize;
            _appearanceFontColor = configuredColor;
            _theme = StorageSearchVanillaTheme.Create();
            StorageSearchMenuLayout layout =
                StorageSearchMenuAppearance.LayoutFor(configuredFontSize);
            Color textColor = ToColor(configuredColor);
            try
            {
                BuildNativeView(layout, textColor);
            }
            catch (Exception nativeException)
            {
                DestroyNativeView();
                _themeSourceToken = sourceToken;
                _theme = StorageSearchVanillaTheme.CreateFallback();
                try
                {
                    BuildNativeView(layout, textColor);
                }
                catch (Exception fallbackException)
                {
                    Plugin.Log?.LogError(
                        "Runic Storage could not create the native Alt+F menu; " +
                        "search closed safely. Native=" + nativeException.Message +
                        "; fallback=" + fallbackException.Message);
                    Close();
                }
            }
        }

        private void BuildNativeView(StorageSearchMenuLayout layout, Color textColor)
        {
            _canvasObject = new GameObject(
                "RunicStorageNativeSearchCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasObject.hideFlags = HideFlags.HideAndDontSave;
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas sourceCanvas = InventoryGui.instance
                ? InventoryGui.instance.GetComponentInParent<Canvas>()
                : null;
            canvas.sortingOrder = sourceCanvas ? sourceCanvas.sortingOrder + 100 : 1000;
            CopyCanvasScale(_theme.SourceScaler, _canvasObject.GetComponent<CanvasScaler>());

            RectTransform root = _canvasObject.GetComponent<RectTransform>();
            Stretch(root);
            Image scrim = CreateImage("SceneScrim", root, null, Image.Type.Simple,
                new Color(0f, 0f, 0f, 0.68f));
            Stretch(scrim.rectTransform);

            Image panel = CreateImage("ValheimSearchPanel", root, _theme.PanelSprite,
                _theme.PanelType, _theme.HasNativePanel
                    ? _theme.PanelColor
                    : new Color32(0x3C, 0x2A, 0x1D, 0xFF));
            if (_theme.PanelMaterial) panel.material = _theme.PanelMaterial;
            _panelRect = panel.rectTransform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(layout.PreferredWindowWidth,
                layout.PreferredWindowHeight);
            var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(28, 28, 34, 26);
            panelLayout.spacing = 7f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            TMP_Text title = CreateText(panel.transform, global::Runic.Localization.RunicText.Get("text_f9b3b60b739c"),
                layout.FontSize, textColor, TextAlignmentOptions.Center);
            SetHeight(title.gameObject, layout.FontSize + 18);
            TMP_Text instruction = CreateText(panel.transform,
                global::Runic.Localization.RunicText.Get("text_d509dff67028"),
                layout.FontSize, textColor, TextAlignmentOptions.Left);
            SetHeight(instruction.gameObject, layout.FontSize * 2 + 10);

            RectTransform filterRow = CreateRect("FilterRow", panel.transform);
            SetHeight(filterRow.gameObject, Math.Max(layout.ControlHeight, _theme.ControlHeight));
            var filterLayout = filterRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            filterLayout.spacing = 8f;
            filterLayout.childAlignment = TextAnchor.MiddleCenter;
            filterLayout.childControlHeight = true;
            filterLayout.childControlWidth = true;
            filterLayout.childForceExpandHeight = true;
            filterLayout.childForceExpandWidth = false;

            TMP_Text filterLabel = CreateText(filterRow, global::Runic.Localization.RunicText.Get("text_638e249f4a15"), layout.FontSize, textColor,
                TextAlignmentOptions.MidlineLeft);
            SetWidth(filterLabel.gameObject, Math.Max(52f, layout.FontSize * 4f));
            _filterInput = CreateInput(filterRow, layout, textColor);
            LayoutElement inputLayout = _filterInput.gameObject.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1f;
            inputLayout.minWidth = 120f;
            Button clear = CreateButton(filterRow, global::Runic.Localization.RunicText.Get("text_83b12c2216ef"), layout, textColor);
            clear.onClick.AddListener(ClearFilter);
            Button close = CreateButton(filterRow, global::Runic.Localization.RunicText.Get("text_7d9eb7acb13e"), layout, textColor);
            close.onClick.AddListener(Close);

            _scrollRect = CreateScrollView(panel.transform);
            LayoutElement scrollLayout = _scrollRect.gameObject.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.minHeight = 140f;
            RebuildEntryRows(layout, textColor);
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(_savedScrollPosition);
            _focusFilter = true;
        }

        private TMP_InputField CreateInput(
            Transform parent,
            StorageSearchMenuLayout layout,
            Color textColor)
        {
            Image image = CreateImage("FilterInput", parent, _theme.InputSprite,
                _theme.InputType, _theme.InputSprite ? _theme.InputColor :
                new Color32(0x25, 0x18, 0x10, 0xFF));
            if (_theme.InputMaterial) image.material = _theme.InputMaterial;
            var input = image.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.characterLimit = 64;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.ColorTint;
            input.customCaretColor = true;
            input.caretColor = textColor;
            input.selectionColor = new Color(textColor.r, textColor.g, textColor.b, 0.38f);

            RectTransform viewport = CreateRect("Text Area", image.transform);
            Stretch(viewport, 9f, 9f, 3f, 3f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text value = CreateText(viewport, _filter, layout.FontSize, textColor,
                TextAlignmentOptions.MidlineLeft);
            Stretch(value.rectTransform);
            TMP_Text placeholder = CreateText(viewport, global::Runic.Localization.RunicText.Get("text_4867cdb95ac9"), layout.FontSize,
                new Color(textColor.r, textColor.g, textColor.b, 0.52f),
                TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform);
            placeholder.fontStyle = FontStyles.Italic;
            input.textViewport = viewport;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.SetTextWithoutNotify(_filter);
            input.onValueChanged.AddListener(OnFilterChanged);
            return input;
        }

        private ScrollRect CreateScrollView(Transform parent)
        {
            Image background = CreateImage("NearbyItems", parent, _theme.InsetSprite,
                _theme.InsetType, _theme.InsetSprite ? _theme.InsetColor :
                new Color32(0x18, 0x10, 0x0B, 0xF2));
            if (_theme.InsetMaterial) background.material = _theme.InsetMaterial;
            var scroll = background.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;

            RectTransform viewport = CreateRect("Viewport", background.transform);
            Stretch(viewport, 6f, 25f, 6f, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _entryContent = CreateRect("Content", viewport);
            _entryContent.anchorMin = new Vector2(0f, 1f);
            _entryContent.anchorMax = new Vector2(1f, 1f);
            _entryContent.pivot = new Vector2(0.5f, 1f);
            _entryContent.anchoredPosition = Vector2.zero;
            _entryContent.sizeDelta = Vector2.zero;
            var contentLayout = _entryContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(3, 3, 3, 3);
            contentLayout.spacing = 5f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            var fitter = _entryContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _entryContent;
            Scrollbar scrollbar = CreateScrollbar(background.transform);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 4f;
            return scroll;
        }

        private Scrollbar CreateScrollbar(Transform parent)
        {
            Image background = CreateImage("Scrollbar", parent, _theme.ScrollbarSprite,
                _theme.ScrollbarType, _theme.ScrollbarSprite ? _theme.ScrollbarColor :
                new Color32(0x20, 0x15, 0x0E, 0xF5));
            if (_theme.ScrollbarMaterial) background.material = _theme.ScrollbarMaterial;
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-20f, 5f);
            rect.offsetMax = new Vector2(-4f, -5f);
            var scrollbar = background.gameObject.AddComponent<Scrollbar>();
            RectTransform slideArea = CreateRect("Sliding Area", rect);
            Stretch(slideArea, 2f, 2f, 2f, 2f);
            Image handle = CreateImage("Handle", slideArea, _theme.ScrollbarHandleSprite,
                _theme.ScrollbarHandleType, _theme.ScrollbarHandleSprite
                    ? _theme.ScrollbarHandleColor
                    : new Color32(0xB7, 0x7A, 0x35, 0xFF));
            if (_theme.ScrollbarHandleMaterial) handle.material = _theme.ScrollbarHandleMaterial;
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.one;
            handle.rectTransform.offsetMin = Vector2.zero;
            handle.rectTransform.offsetMax = Vector2.zero;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = _theme.ButtonColors;
            scrollbar.value = 1f;
            return scrollbar;
        }

        private void RebuildEntryRows(StorageSearchMenuLayout layout, Color textColor)
        {
            if (!_entryContent) return;
            for (int index = _entryContent.childCount - 1; index >= 0; index--)
            {
                GameObject child = _entryContent.GetChild(index).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            int shown = 0;
            for (int index = 0; index < _entries.Count; index++)
            {
                StorageSearchEntry entry = _entries[index];
                if (!MatchesFilter(entry, _filter)) continue;
                shown++;
                string label = entry.DisplayName + "  ×" + entry.Quantity +
                               "  —  " + entry.Containers.Count +
                               (entry.Containers.Count == 1 ? global::Runic.Localization.RunicText.Get("text_a43543d84a47") : global::Runic.Localization.RunicText.Get("text_4ed3d6ce7df7"));
                Button row = CreateButton(_entryContent, label, layout, textColor,
                    Math.Max(layout.ItemHeight, _theme.ControlHeight));
                StorageSearchEntry selected = entry;
                row.onClick.AddListener(() => Select(selected));
            }
            if (shown == 0)
            {
                TMP_Text empty = CreateText(_entryContent,
                    global::Runic.Localization.RunicText.Get("text_91df69917cf6"), layout.FontSize, textColor,
                    TextAlignmentOptions.Center);
                SetHeight(empty.gameObject, Math.Max(layout.ItemHeight * 2, 60));
            }
        }

        private Button CreateButton(
            Transform parent,
            string label,
            StorageSearchMenuLayout layout,
            Color textColor,
            float height = -1f)
        {
            Image image = CreateImage("Button_" + label, parent, _theme.ButtonSprite,
                _theme.ButtonImageType, _theme.HasNativeButton
                    ? _theme.ButtonImageColor
                    : new Color32(0x4A, 0x2F, 0x1E, 0xFF));
            if (_theme.ButtonMaterial) image.material = _theme.ButtonMaterial;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = _theme.ButtonColors;
            button.spriteState = _theme.ButtonSprites;
            button.transition = _theme.ButtonTransition == Selectable.Transition.Animation
                ? Selectable.Transition.ColorTint
                : _theme.ButtonTransition;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            TMP_Text text = CreateText(button.transform, label, layout.FontSize, textColor,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 8f, 8f, 2f, 2f);
            text.raycastTarget = false;
            LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height > 0f
                ? height
                : Math.Max(layout.ControlHeight, _theme.ControlHeight);
            if (height < 0f)
            {
                element.preferredWidth = layout.ActionButtonWidth;
                element.minWidth = layout.ActionButtonWidth;
            }
            return button;
        }

        private TMP_Text CreateText(
            Transform parent,
            string value,
            int fontSize,
            Color color,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect("Text", parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value ?? string.Empty;
            if (_theme != null && _theme.Font) text.font = _theme.Font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private void OnFilterChanged(string value)
        {
            _filter = value ?? string.Empty;
            StorageSearchMenuLayout layout = StorageSearchMenuAppearance.LayoutFor(
                PluginConfig.SearchMenuFontSize?.Value ??
                StorageSearchMenuAppearance.DefaultFontSize);
            RebuildEntryRows(layout, ToColor(_appearanceFontColor));
            if (_scrollRect) _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void ClearFilter()
        {
            _filter = string.Empty;
            if (_filterInput)
            {
                _filterInput.SetTextWithoutNotify(string.Empty);
                _filterInput.Select();
                _filterInput.ActivateInputField();
            }
            RebuildEntryRows(StorageSearchMenuAppearance.LayoutFor(_appearanceFontSize),
                ToColor(_appearanceFontColor));
            if (_scrollRect) _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void DestroyNativeView()
        {
            _filterInput = null;
            _scrollRect = null;
            _entryContent = null;
            _panelRect = null;
            _theme = null;
            if (_canvasObject)
            {
                _canvasObject.SetActive(false);
                UnityEngine.Object.Destroy(_canvasObject);
            }
            _canvasObject = null;
            _themeSourceToken = int.MinValue;
        }

        private static Image CreateImage(
            string name,
            Transform parent,
            Sprite sprite,
            Image.Type type,
            Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite ? type : Image.Type.Simple;
            image.color = color;
            return image;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(
            RectTransform rect,
            float left = 0f,
            float right = 0f,
            float top = 0f,
            float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetHeight(GameObject target, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ??
                                    target.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private static void SetWidth(GameObject target, float width)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ??
                                    target.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
        }

        private static void CopyCanvasScale(CanvasScaler source, CanvasScaler target)
        {
            if (!target) return;
            if (!source)
            {
                target.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                target.referenceResolution = new Vector2(1920f, 1080f);
                target.matchWidthOrHeight = 0.5f;
                return;
            }
            target.uiScaleMode = source.uiScaleMode;
            target.referencePixelsPerUnit = source.referencePixelsPerUnit;
            target.scaleFactor = source.scaleFactor;
            target.referenceResolution = source.referenceResolution;
            target.screenMatchMode = source.screenMatchMode;
            target.matchWidthOrHeight = source.matchWidthOrHeight;
            target.physicalUnit = source.physicalUnit;
            target.fallbackScreenDPI = source.fallbackScreenDPI;
            target.defaultSpriteDPI = source.defaultSpriteDPI;
            target.dynamicPixelsPerUnit = source.dynamicPixelsPerUnit;
        }

        private static Color ToColor(StorageSearchMenuColor value) =>
            new Color32(value.Red, value.Green, value.Blue, value.Alpha);

        private void Select(StorageSearchEntry entry)
        {
            StorageSearchHighlight.ClearAll();
            int marked = 0;
            for (int index = 0; index < entry.Containers.Count; index++)
            {
                Container container = entry.Containers[index];
                if (!container || !container.isActiveAndEnabled) continue;
                StorageSearchHighlight marker =
                    container.GetComponent<StorageSearchHighlight>() ??
                    container.gameObject.AddComponent<StorageSearchHighlight>();
                marker.Activate();
                marked++;
            }
            Player player = Player.m_localPlayer;
            if (player)
                player.Message(
                    MessageHud.MessageType.Center,
                    global::Runic.Localization.RunicText.Get("text_2f4c039d5569") + marked + global::Runic.Localization.RunicText.Get("text_3659f1087c17") +
                    entry.DisplayName + ".",
                    0,
                    null);
            Close();
        }

        private static bool MatchesFilter(StorageSearchEntry entry, string filter)
        {
            string query = (filter ?? string.Empty).Trim();
            return query.Length == 0 ||
                   entry.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.ResourceId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class StorageSearchHighlight : MonoBehaviour
    {
        private const float LifetimeSeconds = 15f;
        private const int Segments = 48;
        private GameObject _visualRoot;
        private LineRenderer[] _rings;
        private Material _material;
        private Light _light;
        private Vector3 _center;
        private float _radius;
        private float _height;
        private float _expiresAt;
        private bool _stopping;

        internal void Activate()
        {
            if (_stopping) return;
            _expiresAt = Time.unscaledTime + LifetimeSeconds;
            if (_visualRoot)
            {
                if (VisualsAreValid()) return;
                StopHighlight();
                return;
            }

            Bounds bounds = new Bounds(transform.position, Vector3.one);
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bool found = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer || renderer is LineRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            _center = bounds.center;
            _radius = Math.Max(0.75f, Math.Max(bounds.extents.x, bounds.extents.z) * 1.35f);
            _height = Math.Max(0.6f, bounds.size.y);

            _visualRoot = new GameObject("RunicStorageSearchGlow");
            _visualRoot.transform.SetParent(transform, true);
            _visualRoot.transform.position = _center;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null) _material = new Material(shader);
            _rings = new LineRenderer[2];
            for (int index = 0; index < _rings.Length; index++)
            {
                // Unity disallows multiple LineRenderer components on one GameObject. Give every
                // animated ring its own child so AddComponent cannot return null for later rings.
                var ringObject = new GameObject("RunicStorageSearchRing" + index);
                ringObject.transform.SetParent(_visualRoot.transform, false);
                LineRenderer ring = ringObject.AddComponent<LineRenderer>();
                if (!ring)
                {
                    StopHighlight();
                    return;
                }
                ring.useWorldSpace = true;
                ring.loop = true;
                ring.positionCount = Segments;
                ring.widthMultiplier = 0.075f;
                if (!ApplyColor(
                        ring,
                        new Color(1f, 0.80f, 0.05f, 1f),
                        new Color(1f, 0.95f, 0.30f, 1f)))
                {
                    StopHighlight();
                    return;
                }
                if (_material != null) ring.sharedMaterial = _material;
                _rings[index] = ring;
            }
            if (!_visualRoot)
            {
                StopHighlight();
                return;
            }
            _light = _visualRoot.AddComponent<Light>();
            if (!_light)
            {
                StopHighlight();
                return;
            }
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.72f, 0.05f);
            _light.range = _radius * 3f;
            _light.shadows = LightShadows.None;
            if (!TryUpdateVisuals()) StopHighlight();
        }

        private void Update()
        {
            if (_stopping) return;
            if (Time.unscaledTime >= _expiresAt)
            {
                StopHighlight();
                return;
            }
            if (!TryUpdateVisuals()) StopHighlight();
        }

        private bool TryUpdateVisuals()
        {
            if (!VisualsAreValid()) return false;
            try
            {
                float time = Time.unscaledTime;
                for (int ringIndex = 0; ringIndex < _rings.Length; ringIndex++)
                {
                    if (!_visualRoot) return false;
                    LineRenderer ring = _rings[ringIndex];
                    if (!ring) return false;
                    float direction = ringIndex == 0 ? 1f : -1f;
                    float phase = time * (1.8f + ringIndex * 0.45f) * direction;
                    float y = _center.y - _height * 0.35f + ringIndex * _height * 0.7f;
                    for (int segment = 0; segment < Segments; segment++)
                    {
                        float angle = segment * Mathf.PI * 2f / Segments + phase;
                        float wave = Mathf.Sin(angle * 3f + time * 4f) * 0.08f;
                        ring.SetPosition(
                            segment,
                            new Vector3(
                                _center.x + Mathf.Cos(angle) * (_radius + wave),
                                y + Mathf.Sin(angle * 2f + time * 3f) * 0.08f,
                                _center.z + Mathf.Sin(angle) * (_radius + wave)));
                    }
                }
                if (!_light) return false;
                _light.intensity = 1.7f + Mathf.Sin(time * 5f) * 0.45f;
                return true;
            }
            catch (Exception)
            {
                // Search highlighting is visual-only. A third-party teardown or Unity lifetime
                // edge must remove this marker once instead of emitting an exception every frame.
                return false;
            }
        }

        private bool VisualsAreValid()
        {
            if (!_visualRoot || _rings == null || _rings.Length != 2 || !_light) return false;
            for (int index = 0; index < _rings.Length; index++)
                if (!_rings[index]) return false;
            return true;
        }

        private static bool ApplyColor(LineRenderer ring, Color start, Color end)
        {
            if (!ring) return false;
            try
            {
                ring.startColor = start;
                ring.endColor = end;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void StopHighlight()
        {
            if (_stopping) return;
            _stopping = true;
            enabled = false;
            Destroy(this);
        }

        private void OnDestroy()
        {
            _stopping = true;
            enabled = false;
            if (_visualRoot) Destroy(_visualRoot);
            if (_material) Destroy(_material);
            _visualRoot = null;
            _rings = null;
            _light = null;
            _material = null;
        }

        internal static void ClearAll()
        {
            StorageSearchHighlight[] markers =
                FindObjectsByType<StorageSearchHighlight>(FindObjectsSortMode.None);
            for (int index = 0; index < markers.Length; index++)
                if (markers[index]) markers[index].StopHighlight();
        }
    }
}
