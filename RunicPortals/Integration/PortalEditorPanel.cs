using System;
using System.Collections.Generic;
using System.Linq;
using GUIFramework;
using RunicPortals.Api;
using RunicPortals.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RunicPortals.Integration
{
    /// <summary>
    /// A transient, native uGUI editor. It owns presentation and input only; all portal authority,
    /// confirmation, and world mutation remain in PortalRuntime.SubmitEditorDraft.
    /// </summary>
    internal sealed class PortalEditorPanel : IDisposable
    {
        private const float WindowWidth = 720f;
        private const float WindowHeight = 760f;
        private const float GroupListHeight = 132f;
        private const int BodyFontSize = 20;

        private readonly PortalGroupRuntime _groups;
        private readonly Func<PortalEditorDraft, PortalEditorSubmitResult> _submit;
        private readonly Action _cancel;
        private readonly List<PortalGroupChoice> _groupChoices = new List<PortalGroupChoice>();
        private readonly List<Button> _groupChoiceButtons = new List<Button>();

        private PortalEditorDraft _draft;
        private PortalEditorTheme _theme;
        private bool _standardCurrentlyConnected;
        private bool _open;
        private bool _disposed;
        private bool _groupListOpen;
        private bool _selectOnNextTick;
        private bool _groupSnapshotAvailable;
        private float _nextGroupRefresh;
        private string _groupSnapshotSignature = string.Empty;
        private bool _cursorVisible;
        private CursorLockMode _cursorLock;
        private GameObject _previousSelection;

        private GameObject _canvasObject;
        private RectTransform _panelRect;
        private GameObject _standardContent;
        private GameObject _networkContent;
        private GameObject _groupRow;
        private GameObject _groupList;
        private RectTransform _groupListContent;
        private Toggle _standardToggle;
        private GuiInputField _tagInput;
        private GuiInputField _networkInput;
        private GuiInputField _portalInput;
        private TMP_Text _tagLabel;
        private TMP_Text _statusText;
        private TMP_Text _groupButtonText;
        private TMP_Text _saveButtonText;
        private Button _publicButton;
        private Button _privateButton;
        private Button _groupButton;
        private Button _bothButton;
        private Button _arrivalsButton;
        private Button _departuresButton;
        private Button _groupDropdownButton;
        private Button _saveButton;
        private Button _cancelButton;

        internal PortalEditorPanel(
            PortalGroupRuntime groups,
            Func<PortalEditorDraft, PortalEditorSubmitResult> submit,
            Action cancel)
        {
            _groups = groups;
            _submit = submit ?? throw new ArgumentNullException(nameof(submit));
            _cancel = cancel;
        }

        internal bool IsOpen => _open;

        internal void Open(PortalEditorDraft draft, bool standardCurrentlyConnected)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PortalEditorPanel));
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            Close();
            _draft = Clone(draft);
            NormalizeDraftSelection();
            _standardCurrentlyConnected = standardCurrentlyConnected;
            _cursorVisible = Cursor.visible;
            _cursorLock = Cursor.lockState;
            _previousSelection = EventSystem.current?.currentSelectedGameObject;
            _groupListOpen = false;
            _groupSnapshotAvailable = false;
            _groupSnapshotSignature = string.Empty;
            _nextGroupRefresh = 0f;
            _open = true;
            PortalEditorInputGuard.Attach(this);
            RenewCursorLease();
            try
            {
                RefreshGroupChoices(true);
                BuildNativeView();
                RefreshVisibleMode(false);
                _selectOnNextTick = true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception,
                    "Runic Portals could not create the portal editor; it closed safely.");
                Close();
                _cancel?.Invoke();
            }
        }

        internal void Tick()
        {
            if (!_open) return;
            try
            {
                RenewCursorLease();
                RefreshGroupChoices(false);
                if (_selectOnNextTick)
                {
                    _selectOnNextTick = false;
                    SelectInitialControl();
                }
                if (!ZInput.VirtualKeyboardOpen && PortalEditorInputGuard.PollCancel())
                    CancelFromUi();
            }
            catch (Exception exception)
            {
                FailClosed(exception, "update");
            }
        }

        internal void Draw()
        {
            if (!_open) return;
            try { PortalEditorInputGuard.CapturePointer(Event.current); }
            catch (Exception exception) { FailClosed(exception, "pointer input"); }
        }

        internal void Close()
        {
            if (!_open && !_canvasObject) return;
            _open = false;
            _selectOnNextTick = false;
            _groupListOpen = false;
            if (_tagInput) _tagInput.DeactivateInputField();
            if (_networkInput) _networkInput.DeactivateInputField();
            if (_portalInput) _portalInput.DeactivateInputField();
            PortalEditorInputGuard.Detach(this);
            DestroyNativeView();
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
            if (EventSystem.current != null && _previousSelection &&
                _previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(_previousSelection);
            _previousSelection = null;
            _draft = null;
            _groupChoices.Clear();
            _groupChoiceButtons.Clear();
            _groupSnapshotSignature = string.Empty;
            try { PlayerController.SetTakeInputDelay(0.1f); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }

        internal void RenewCursorLease()
        {
            if (!_open) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void BuildNativeView()
        {
            _theme = PortalEditorTheme.Create();
            _canvasObject = new GameObject(
                "RunicPortalsNativeEditorCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasObject.hideFlags = HideFlags.HideAndDontSave;
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas sourceCanvas = TextInput.instance
                ? TextInput.instance.GetComponentInParent<Canvas>()
                : null;
            canvas.sortingOrder = sourceCanvas ? sourceCanvas.sortingOrder + 200 : 1200;
            CopyCanvasScale(_theme.SourceScaler, _canvasObject.GetComponent<CanvasScaler>());

            RectTransform root = _canvasObject.GetComponent<RectTransform>();
            Stretch(root);
            Image scrim = CreateImage(
                "SceneScrim", root, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.7f));
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;

            Image panel = CreateImage(
                "PortalEditor", root, _theme.PanelSprite, _theme.PanelType, _theme.PanelColor);
            if (_theme.PanelMaterial) panel.material = _theme.PanelMaterial;
            _panelRect = panel.rectTransform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(30, 30, 26, 24);
            panelLayout.spacing = 8f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            TMP_Text title = CreateText(
                panel.transform, "Configure Portal", 28, _theme.TextColor,
                TextAlignmentOptions.Center);
            SetHeight(title.gameObject, 40f);
            TMP_Text description = CreateText(
                panel.transform,
                "Choose normal Valheim pairing or configure a Runic travel endpoint.",
                18, _theme.MutedTextColor, TextAlignmentOptions.Center);
            SetHeight(description.gameObject, 30f);

            _standardToggle = CreateStandardToggle(panel.transform);
            _standardToggle.SetIsOnWithoutNotify(_draft.StandardPair);
            _standardToggle.onValueChanged.AddListener(OnStandardChanged);

            _standardContent = CreateSection("StandardFields", panel.transform).gameObject;
            _tagInput = CreateField(
                _standardContent.transform,
                "Portal tag",
                "matching tags connect",
                PortalEditCommand.MaximumVanillaTagLength,
                "$piece_portal_tag",
                false,
                out _tagLabel);
            _tagInput.SetTextWithoutNotify(_draft.VanillaTag ?? string.Empty);
            _tagInput.onValueChanged.AddListener(OnVanillaTagChanged);
            UpdateTagLabel();

            _networkContent = CreateSection("RunicFields", panel.transform).gameObject;
            _networkInput = CreateField(
                _networkContent.transform,
                "Network name",
                "portals with the exact same network name travel together",
                PortalContractLimits.MaximumNetworkIdLength,
                "Runic network name",
                true,
                out _);
            _networkInput.SetTextWithoutNotify(_draft.NetworkName ?? string.Empty);
            _networkInput.onValueChanged.AddListener(value =>
            {
                _draft.NetworkName = value ?? string.Empty;
                DraftChanged();
            });

            _portalInput = CreateField(
                _networkContent.transform,
                "Portal name",
                "the destination name shown on the map",
                PortalContractLimits.MaximumNameLength,
                "Runic portal name",
                true,
                out _);
            _portalInput.SetTextWithoutNotify(_draft.PortalName ?? string.Empty);
            _portalInput.onValueChanged.AddListener(value =>
            {
                _draft.PortalName = value ?? string.Empty;
                DraftChanged();
            });

            CreateAccessSelector(_networkContent.transform);
            CreateDirectionSelector(_networkContent.transform);
            CreateGroupSelector(_networkContent.transform);

            _statusText = CreateText(
                panel.transform, string.Empty, 18, _theme.MutedTextColor,
                TextAlignmentOptions.TopLeft);
            _statusText.textWrappingMode = TextWrappingModes.Normal;
            SetHeight(_statusText.gameObject, 54f);

            RectTransform actionRow = CreateRect("Actions", panel.transform);
            SetHeight(actionRow.gameObject, Math.Max(42f, _theme.ControlHeight));
            var actionLayout = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 12f;
            actionLayout.childAlignment = TextAnchor.MiddleCenter;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = true;
            _cancelButton = CreateButton(actionRow, "Cancel", out _);
            _cancelButton.onClick.AddListener(CancelFromUi);
            _saveButton = CreateButton(actionRow, "Save", out _saveButtonText);
            _saveButton.onClick.AddListener(SaveFromUi);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
        }

        private Toggle CreateStandardToggle(Transform parent)
        {
            Image background = CreateImage(
                "StandardPairToggle", parent, _theme.ButtonSprite, _theme.ButtonType,
                _theme.ButtonColor);
            if (_theme.ButtonMaterial) background.material = _theme.ButtonMaterial;
            SetHeight(background.gameObject, Math.Max(42f, _theme.ControlHeight));
            var toggle = background.gameObject.AddComponent<Toggle>();
            ApplySelectableTheme(toggle);
            toggle.targetGraphic = background;

            var layout = background.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 6, 6);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            Image box = CreateImage(
                "Checkbox", background.transform, _theme.InputSprite, _theme.InputType,
                _theme.InputSprite ? _theme.InputColor : new Color32(0x18, 0x10, 0x0B, 0xFF));
            SetSize(box.gameObject, 28f, 28f);
            TMP_Text check = CreateText(
                box.transform, "X", 21, _theme.ConfirmationColor, TextAlignmentOptions.Center);
            Stretch(check.rectTransform, 1f, 1f, 1f, 1f);
            toggle.graphic = check;

            TMP_Text label = CreateText(
                background.transform,
                "Standard Pair (normal Valheim portal)",
                BodyFontSize,
                _theme.TextColor,
                TextAlignmentOptions.MidlineLeft);
            LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            return toggle;
        }

        private void CreateAccessSelector(Transform parent)
        {
            RectTransform row = CreateSelectorRow(parent, "Access");
            _publicButton = CreateButton(row, "Public", out _);
            _privateButton = CreateButton(row, "Private", out _);
            _groupButton = CreateButton(row, "Group", out _);
            _publicButton.onClick.AddListener(() => SetAccess(PortalNetworkKind.Public));
            _privateButton.onClick.AddListener(() => SetAccess(PortalNetworkKind.Personal));
            _groupButton.onClick.AddListener(() => SetAccess(PortalNetworkKind.Group));
        }

        private void CreateDirectionSelector(Transform parent)
        {
            RectTransform row = CreateSelectorRow(parent, "Travel");
            _bothButton = CreateButton(row, "Both", out _);
            _arrivalsButton = CreateButton(row, "Arrivals Only", out _);
            _departuresButton = CreateButton(row, "Departures Only", out _);
            _bothButton.onClick.AddListener(() => SetDirection(PortalEditorDirection.Both));
            _arrivalsButton.onClick.AddListener(
                () => SetDirection(PortalEditorDirection.ArrivalsOnly));
            _departuresButton.onClick.AddListener(
                () => SetDirection(PortalEditorDirection.DeparturesOnly));
        }

        private void CreateGroupSelector(Transform parent)
        {
            RectTransform section = CreateSection("GroupSelector", parent);
            _groupRow = section.gameObject;
            TMP_Text label = CreateText(
                section, "Runic Group", 17, _theme.MutedTextColor,
                TextAlignmentOptions.MidlineLeft);
            SetHeight(label.gameObject, 24f);
            _groupDropdownButton = CreateButton(section, string.Empty, out _groupButtonText);
            _groupDropdownButton.onClick.AddListener(ToggleGroupList);

            Image listBackground = CreateImage(
                "GroupChoices", section, _theme.InsetSprite, _theme.InsetType,
                _theme.InsetSprite ? _theme.InsetColor : new Color32(0x16, 0x0F, 0x0A, 0xFC));
            if (_theme.InsetMaterial) listBackground.material = _theme.InsetMaterial;
            _groupList = listBackground.gameObject;
            SetHeight(_groupList, GroupListHeight);
            ScrollRect scroll = _groupList.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            RectTransform viewport = CreateRect("Viewport", listBackground.transform);
            Stretch(viewport, 5f, 5f, 5f, 5f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _groupListContent = CreateRect("Content", viewport);
            _groupListContent.anchorMin = new Vector2(0f, 1f);
            _groupListContent.anchorMax = new Vector2(1f, 1f);
            _groupListContent.pivot = new Vector2(0.5f, 1f);
            _groupListContent.anchoredPosition = Vector2.zero;
            _groupListContent.sizeDelta = Vector2.zero;
            var contentLayout = _groupListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(3, 3, 3, 3);
            contentLayout.spacing = 4f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            var fitter = _groupListContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _groupListContent;
            RebuildGroupChoiceButtons();
            _groupList.SetActive(false);
        }

        private GuiInputField CreateField(
            Transform parent,
            string labelText,
            string placeholderText,
            int characterLimit,
            string virtualKeyboardTitle,
            bool rejectPipe,
            out TMP_Text label)
        {
            RectTransform block = CreateRect(labelText.Replace(" ", string.Empty) + "Field", parent);
            SetHeight(block.gameObject, 66f);
            var layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            label = CreateText(
                block, labelText, 17, _theme.MutedTextColor,
                TextAlignmentOptions.MidlineLeft);
            SetHeight(label.gameObject, 23f);

            Image inputImage = CreateImage(
                "Input", block, _theme.InputSprite, _theme.InputType, _theme.InputColor);
            if (_theme.InputMaterial) inputImage.material = _theme.InputMaterial;
            SetHeight(inputImage.gameObject, Math.Max(38f, _theme.ControlHeight));
            var input = inputImage.gameObject.AddComponent<GuiInputField>();
            input.targetGraphic = inputImage;
            input.characterLimit = characterLimit;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.ColorTint;
            input.colors = _theme.ButtonColors;
            input.customCaretColor = true;
            input.caretColor = _theme.TextColor;
            input.selectionColor = new Color(
                _theme.TextColor.r, _theme.TextColor.g, _theme.TextColor.b, 0.38f);
            input.VirtualKeyboardOnActivate = true;
            input.CaretOnGamepadUsage = true;
            input.VirtualKeyboardTitle = virtualKeyboardTitle;
            input.onValidateInput = (_, __, character) =>
                char.IsControl(character) || rejectPipe && character == '|' ? '\0' : character;

            RectTransform textArea = CreateRect("Text Area", inputImage.transform);
            Stretch(textArea, 10f, 10f, 3f, 3f);
            textArea.gameObject.AddComponent<RectMask2D>();
            TMP_Text value = CreateText(
                textArea, string.Empty, BodyFontSize, _theme.TextColor,
                TextAlignmentOptions.MidlineLeft);
            Stretch(value.rectTransform);
            TMP_Text placeholder = CreateText(
                textArea, placeholderText, 17,
                new Color(
                    _theme.MutedTextColor.r,
                    _theme.MutedTextColor.g,
                    _theme.MutedTextColor.b,
                    0.62f),
                TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform);
            placeholder.fontStyle = FontStyles.Italic;
            input.textViewport = textArea;
            input.textComponent = value;
            input.placeholder = placeholder;
            return input;
        }

        private RectTransform CreateSelectorRow(Transform parent, string labelText)
        {
            RectTransform row = CreateRect(labelText + "Selector", parent);
            SetHeight(row.gameObject, Math.Max(42f, _theme.ControlHeight));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            TMP_Text label = CreateText(
                row, labelText, 17, _theme.MutedTextColor,
                TextAlignmentOptions.MidlineLeft);
            LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 94f;
            labelLayout.flexibleWidth = 0f;
            return row;
        }

        private Button CreateButton(Transform parent, string label, out TMP_Text labelText)
        {
            Image image = CreateImage(
                label.Length == 0 ? "Button" : label + "Button",
                parent,
                _theme.ButtonSprite,
                _theme.ButtonType,
                _theme.ButtonColor);
            if (_theme.ButtonMaterial) image.material = _theme.ButtonMaterial;
            var button = image.gameObject.AddComponent<Button>();
            ApplySelectableTheme(button);
            button.targetGraphic = image;
            labelText = CreateText(
                image.transform, label, BodyFontSize, _theme.TextColor,
                TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform, 8f, 8f, 3f, 3f);
            LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
            element.minHeight = Math.Max(34f, _theme.ControlHeight);
            element.preferredHeight = Math.Max(38f, _theme.ControlHeight);
            element.flexibleWidth = 1f;
            return button;
        }

        private void ApplySelectableTheme(Selectable selectable)
        {
            selectable.transition = _theme.ButtonTransition;
            selectable.colors = _theme.ButtonColors;
            selectable.spriteState = _theme.ButtonSprites;
        }

        private void OnStandardChanged(bool standard)
        {
            if (_draft == null) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            _draft.StandardPair = standard;
            if (standard) CloseGroupList();
            RefreshVisibleMode(true);
        }

        private void OnVanillaTagChanged(string value)
        {
            if (_draft == null) return;
            _draft.VanillaTag = value ?? string.Empty;
            UpdateTagLabel();
            DraftChanged();
        }

        private void SetAccess(PortalNetworkKind access)
        {
            if (_draft == null) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            _draft.Access = access;
            if (access != PortalNetworkKind.Group)
                CloseGroupList();
            else if (_draft.GroupId.Length == 0 && _groupChoices.Count != 0)
                _draft.GroupId = _groupChoices[0].GroupId;
            RefreshAccessButtons();
            RefreshGroupVisibility();
            DraftChanged();
            RebuildNavigation();
        }

        private void SetDirection(PortalEditorDirection direction)
        {
            if (_draft == null) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            _draft.Direction = direction;
            RefreshDirectionButtons();
            DraftChanged();
        }

        private void ToggleGroupList()
        {
            if (_draft == null || _draft.Access != PortalNetworkKind.Group) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            if (!_groupSnapshotAvailable || _groupChoices.Count == 0)
            {
                SetStatus(
                    _groupSnapshotAvailable
                        ? "You are not currently a member of a Runic Group. Manage Groups through Valheim chat with /group."
                        : "Group information is still loading from the server.",
                    false,
                    false);
                return;
            }
            _groupListOpen = !_groupListOpen;
            if (_groupList) _groupList.SetActive(_groupListOpen);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
            RebuildNavigation();
            if (_groupListOpen && _groupChoiceButtons.Count != 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_groupChoiceButtons[0].gameObject);
        }

        private void SelectGroup(PortalGroupChoice choice)
        {
            PortalEditorInputGuard.CapturePrimaryPointer();
            _draft.GroupId = choice.GroupId;
            CloseGroupList();
            RebuildGroupChoiceButtons();
            RefreshGroupButtonText();
            DraftChanged();
            RebuildNavigation();
            if (_groupDropdownButton && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_groupDropdownButton.gameObject);
        }

        private void CloseGroupList()
        {
            _groupListOpen = false;
            if (_groupList) _groupList.SetActive(false);
        }

        private void SaveFromUi()
        {
            if (!_open || _draft == null) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            SyncInputsToDraft();
            PortalEditCommand command = _draft.BuildCommand();
            if (command == null || command.Kind == PortalEditKind.Invalid)
            {
                SetStatus(command?.Error ?? "Complete the required portal fields.", true, false);
                return;
            }
            try
            {
                PortalEditorSubmitResult result = _submit(_draft);
                if (result.ClosesEditor)
                {
                    Close();
                    return;
                }
                bool confirmation = result.State == PortalEditorSubmitState.ConfirmationRequired;
                SetStatus(result.Message, !confirmation, confirmation);
                if (_saveButtonText)
                    _saveButtonText.text = confirmation ? "Confirm" : "Save";
            }
            catch (Exception exception)
            {
                SetStatus("The portal could not be saved: " + exception.Message, true, false);
                Diagnostics.Error(exception, "Runic portal editor submission failed.");
            }
        }

        private void CancelFromUi()
        {
            if (!_open) return;
            PortalEditorInputGuard.CapturePrimaryPointer();
            Close();
            try { _cancel?.Invoke(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, "Runic portal editor cancellation failed.");
            }
        }

        private void RefreshVisibleMode(bool draftChanged)
        {
            if (!_standardContent || !_networkContent || _draft == null) return;
            _standardContent.SetActive(_draft.StandardPair);
            _networkContent.SetActive(!_draft.StandardPair);
            RefreshAccessButtons();
            RefreshDirectionButtons();
            RefreshGroupVisibility();
            if (draftChanged) DraftChanged();
            else RefreshContextStatus();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
            RebuildNavigation();
        }

        private void RefreshGroupVisibility()
        {
            if (!_groupRow || _draft == null) return;
            bool visible = !_draft.StandardPair && _draft.Access == PortalNetworkKind.Group;
            _groupRow.SetActive(visible);
            if (!visible) CloseGroupList();
            RefreshGroupButtonText();
        }

        private void RefreshAccessButtons()
        {
            if (_draft == null) return;
            SetButtonSelected(_publicButton, _draft.Access == PortalNetworkKind.Public);
            SetButtonSelected(_privateButton, _draft.Access == PortalNetworkKind.Personal);
            SetButtonSelected(_groupButton, _draft.Access == PortalNetworkKind.Group);
        }

        private void RefreshDirectionButtons()
        {
            if (_draft == null) return;
            SetButtonSelected(_bothButton, _draft.Direction == PortalEditorDirection.Both);
            SetButtonSelected(_arrivalsButton,
                _draft.Direction == PortalEditorDirection.ArrivalsOnly);
            SetButtonSelected(_departuresButton,
                _draft.Direction == PortalEditorDirection.DeparturesOnly);
        }

        private void SetButtonSelected(Button button, bool selected)
        {
            if (!button || !button.image) return;
            ColorBlock colors = _theme.ButtonColors;
            if (selected)
            {
                colors.normalColor = _theme.SelectedColor;
                colors.highlightedColor = Color.Lerp(_theme.SelectedColor, Color.white, 0.14f);
                colors.pressedColor = Color.Lerp(_theme.SelectedColor, Color.black, 0.2f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(
                    _theme.SelectedColor.r,
                    _theme.SelectedColor.g,
                    _theme.SelectedColor.b,
                    0.45f);
            }
            button.colors = colors;
            if (button.transition != Selectable.Transition.ColorTint)
                button.image.color = selected ? _theme.SelectedColor : _theme.ButtonColor;
        }

        private void DraftChanged()
        {
            if (_saveButtonText) _saveButtonText.text = "Save";
            RefreshContextStatus();
        }

        private void RefreshContextStatus()
        {
            if (!_statusText || _draft == null) return;
            if (_draft.StandardPair)
            {
                SetStatus(
                    "Standard Pair uses Valheim's normal portal linking. Two portals with the same tag connect.",
                    false,
                    false);
                return;
            }
            if (_standardCurrentlyConnected)
            {
                SetStatus(
                    "This Standard Pair is connected. Select Standard Pair, give it a unique tag, and save before converting it.",
                    false,
                    true);
                return;
            }
            if (_draft.Access == PortalNetworkKind.Group)
            {
                SetStatus(
                    _groupSnapshotAvailable
                        ? _groupChoices.Count == 0
                            ? "No Runic Groups are available. Manage Groups through Valheim chat with /group."
                            : "Only current members of the selected Runic Group may discover and use this portal."
                        : "Loading your Runic Groups from the server...",
                    false,
                    false);
                return;
            }
            SetStatus(
                _draft.Access == PortalNetworkKind.Personal
                    ? "Private portals are visible and usable only by their owner."
                    : "Public portals are visible and usable by every player who passes normal route and ward checks.",
                false,
                false);
        }

        private void SetStatus(string message, bool error, bool confirmation)
        {
            if (!_statusText) return;
            _statusText.text = message ?? string.Empty;
            _statusText.color = error
                ? _theme.ErrorColor
                : confirmation ? _theme.ConfirmationColor : _theme.MutedTextColor;
        }

        private void RefreshGroupChoices(bool force)
        {
            if (_groups == null) return;
            float now = Time.unscaledTime;
            if (!force && now < _nextGroupRefresh) return;
            _nextGroupRefresh = now + 0.75f;
            bool available = _groups.TryGetLocalGroupChoices(
                out IReadOnlyList<PortalGroupChoice> choices);
            var sorted = available && choices != null
                ? choices.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.GroupId, StringComparer.Ordinal)
                    .ToList()
                : new List<PortalGroupChoice>();
            string signature = available
                ? string.Join("\n", sorted.Select(value => value.GroupId + "\t" + value.DisplayName))
                : "unavailable";
            if (_groupSnapshotAvailable == available &&
                string.Equals(_groupSnapshotSignature, signature, StringComparison.Ordinal)) return;
            _groupSnapshotAvailable = available;
            _groupSnapshotSignature = signature;
            _groupChoices.Clear();
            _groupChoices.AddRange(sorted);
            if (_draft != null && _draft.Access == PortalNetworkKind.Group &&
                _draft.GroupId.Length == 0 && _groupChoices.Count != 0)
                _draft.GroupId = _groupChoices[0].GroupId;
            if (_groupListContent) RebuildGroupChoiceButtons();
            RefreshGroupButtonText();
            if (_statusText) RefreshContextStatus();
            if (_panelRect) RebuildNavigation();
        }

        private void RebuildGroupChoiceButtons()
        {
            if (!_groupListContent) return;
            for (int index = _groupListContent.childCount - 1; index >= 0; index--)
                UnityEngine.Object.Destroy(_groupListContent.GetChild(index).gameObject);
            _groupChoiceButtons.Clear();
            for (int index = 0; index < _groupChoices.Count; index++)
            {
                PortalGroupChoice choice = _groupChoices[index];
                string label = string.Equals(
                    choice.GroupId, _draft?.GroupId, StringComparison.Ordinal)
                    ? choice.DisplayName + "  [selected]"
                    : choice.DisplayName;
                Button button = CreateButton(_groupListContent, label, out _);
                PortalGroupChoice captured = choice;
                button.onClick.AddListener(() => SelectGroup(captured));
                _groupChoiceButtons.Add(button);
            }
        }

        private void RefreshGroupButtonText()
        {
            if (!_groupButtonText || _draft == null) return;
            PortalGroupChoice? selected = null;
            for (int index = 0; index < _groupChoices.Count; index++)
                if (string.Equals(
                        _groupChoices[index].GroupId,
                        _draft.GroupId,
                        StringComparison.Ordinal))
                {
                    selected = _groupChoices[index];
                    break;
                }
            if (selected.HasValue)
                _groupButtonText.text = selected.Value.DisplayName + "   ▼";
            else if (!_groupSnapshotAvailable)
                _groupButtonText.text = "Loading Groups...";
            else if (_groupChoices.Count == 0)
                _groupButtonText.text = "No Groups available";
            else if (_draft.GroupId.Length != 0)
                _groupButtonText.text = "Previously selected Group unavailable";
            else
                _groupButtonText.text = "Choose a Group   ▼";
        }

        private void UpdateTagLabel()
        {
            if (!_tagLabel || !_tagInput) return;
            _tagLabel.text = "Portal tag (" + _tagInput.text.Length + "/" +
                             PortalEditCommand.MaximumVanillaTagLength + ")";
        }

        private void SyncInputsToDraft()
        {
            if (_draft == null) return;
            if (_tagInput) _draft.VanillaTag = _tagInput.text ?? string.Empty;
            if (_networkInput) _draft.NetworkName = _networkInput.text ?? string.Empty;
            if (_portalInput) _draft.PortalName = _portalInput.text ?? string.Empty;
        }

        private void NormalizeDraftSelection()
        {
            if (_draft.Access != PortalNetworkKind.Public &&
                _draft.Access != PortalNetworkKind.Personal &&
                _draft.Access != PortalNetworkKind.Group)
                _draft.Access = PortalNetworkKind.Public;
            if (!Enum.IsDefined(typeof(PortalEditorDirection), _draft.Direction))
                _draft.Direction = PortalEditorDirection.Both;
            _draft.VanillaTag = Bounded(_draft.VanillaTag,
                PortalEditCommand.MaximumVanillaTagLength);
            _draft.NetworkName = Bounded(_draft.NetworkName,
                PortalContractLimits.MaximumNetworkIdLength);
            _draft.PortalName = Bounded(_draft.PortalName,
                PortalContractLimits.MaximumNameLength);
            _draft.GroupId = _draft.GroupId ?? string.Empty;
        }

        private void RebuildNavigation()
        {
            if (!_open || !_standardToggle) return;
            var rows = new List<List<Selectable>>
            {
                new List<Selectable> { _standardToggle }
            };
            if (_draft.StandardPair)
            {
                rows.Add(new List<Selectable> { _tagInput });
            }
            else
            {
                rows.Add(new List<Selectable> { _networkInput });
                rows.Add(new List<Selectable> { _portalInput });
                rows.Add(Valid(_publicButton, _privateButton, _groupButton));
                rows.Add(Valid(_bothButton, _arrivalsButton, _departuresButton));
                if (_draft.Access == PortalNetworkKind.Group)
                {
                    rows.Add(new List<Selectable> { _groupDropdownButton });
                    if (_groupListOpen)
                        for (int index = 0; index < _groupChoiceButtons.Count; index++)
                            rows.Add(new List<Selectable> { _groupChoiceButtons[index] });
                }
            }
            rows.Add(Valid(_cancelButton, _saveButton));
            rows = rows.Where(row => row.Any(value => value && value.gameObject.activeInHierarchy))
                .ToList();
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<Selectable> row = rows[rowIndex]
                    .Where(value => value && value.gameObject.activeInHierarchy)
                    .ToList();
                if (row.Count == 0) continue;
                List<Selectable> previous = rows[(rowIndex - 1 + rows.Count) % rows.Count]
                    .Where(value => value && value.gameObject.activeInHierarchy).ToList();
                List<Selectable> next = rows[(rowIndex + 1) % rows.Count]
                    .Where(value => value && value.gameObject.activeInHierarchy).ToList();
                for (int column = 0; column < row.Count; column++)
                {
                    Navigation navigation = row[column].navigation;
                    navigation.mode = Navigation.Mode.Explicit;
                    navigation.selectOnLeft = row[(column - 1 + row.Count) % row.Count];
                    navigation.selectOnRight = row[(column + 1) % row.Count];
                    navigation.selectOnUp = previous[Math.Min(column, previous.Count - 1)];
                    navigation.selectOnDown = next[Math.Min(column, next.Count - 1)];
                    row[column].navigation = navigation;
                }
            }
        }

        private void SelectInitialControl()
        {
            if (!_open || EventSystem.current == null) return;
            GameObject selected = _draft.StandardPair
                ? _tagInput ? _tagInput.gameObject : _standardToggle.gameObject
                : _networkInput ? _networkInput.gameObject : _standardToggle.gameObject;
            EventSystem.current.SetSelectedGameObject(selected);
        }

        private void FailClosed(Exception exception, string operation)
        {
            Diagnostics.Error(exception,
                "Runic Portals editor faulted during " + operation + "; it closed safely.");
            Close();
            try { _cancel?.Invoke(); }
            catch { }
        }

        private void DestroyNativeView()
        {
            if (_canvasObject) UnityEngine.Object.Destroy(_canvasObject);
            _canvasObject = null;
            _panelRect = null;
            _standardContent = null;
            _networkContent = null;
            _groupRow = null;
            _groupList = null;
            _groupListContent = null;
            _standardToggle = null;
            _tagInput = null;
            _networkInput = null;
            _portalInput = null;
            _tagLabel = null;
            _statusText = null;
            _groupButtonText = null;
            _saveButtonText = null;
            _publicButton = null;
            _privateButton = null;
            _groupButton = null;
            _bothButton = null;
            _arrivalsButton = null;
            _departuresButton = null;
            _groupDropdownButton = null;
            _saveButton = null;
            _cancelButton = null;
            _theme = null;
        }

        private static PortalEditorDraft Clone(PortalEditorDraft source) =>
            new PortalEditorDraft
            {
                StandardPair = source.StandardPair,
                VanillaTag = source.VanillaTag ?? string.Empty,
                NetworkName = source.NetworkName ?? string.Empty,
                PortalName = source.PortalName ?? string.Empty,
                Access = source.Access,
                GroupId = source.GroupId ?? string.Empty,
                Direction = source.Direction
            };

        private static string Bounded(string value, int maximum)
        {
            string text = value ?? string.Empty;
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }

        private static List<Selectable> Valid(params Selectable[] values) =>
            values.Where(value => value).ToList();

        private RectTransform CreateSection(string name, Transform parent)
        {
            RectTransform rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleHeight = 0f;
            return rect;
        }

        private TMP_Text CreateText(
            Transform parent,
            string text,
            int fontSize,
            Color color,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect("Text", parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (_theme?.Font) label.font = _theme.Font;
            return label;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value.GetComponent<RectTransform>();
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

        private static void CopyCanvasScale(CanvasScaler source, CanvasScaler target)
        {
            if (!target) return;
            if (!source)
            {
                target.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                target.referenceResolution = new Vector2(1920f, 1080f);
                target.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
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
        }

        private static void SetHeight(GameObject value, float height)
        {
            LayoutElement element = value.GetComponent<LayoutElement>() ??
                                    value.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
        }

        private static void SetSize(GameObject value, float width, float height)
        {
            LayoutElement element = value.GetComponent<LayoutElement>() ??
                                    value.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.minHeight = height;
            element.preferredHeight = height;
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
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
