using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicStorage.Runtime
{
    /// <summary>
    /// References Valheim's live uGUI assets without rasterizing, recoloring, or copying them.
    /// Packed atlas sprites must stay Sprites: converting their textureRect to an IMGUI texture
    /// caused the bright-yellow search window seen in game.
    /// </summary>
    internal sealed class StorageSearchVanillaTheme
    {
        private StorageSearchVanillaTheme()
        {
            ButtonColors = ColorBlock.defaultColorBlock;
            ButtonTransition = Selectable.Transition.ColorTint;
            PanelColor = Color.white;
            InsetColor = new Color32(0x16, 0x10, 0x0C, 0xF0);
            InputColor = new Color32(0x24, 0x18, 0x10, 0xFF);
            ButtonImageColor = Color.white;
            ScrollbarColor = new Color32(0x21, 0x16, 0x0F, 0xF0);
            ScrollbarHandleColor = Color.white;
            ControlHeight = 36;
        }

        internal Sprite PanelSprite { get; private set; }
        internal Material PanelMaterial { get; private set; }
        internal Image.Type PanelType { get; private set; } = Image.Type.Sliced;
        internal Color PanelColor { get; private set; }
        internal Sprite InsetSprite { get; private set; }
        internal Material InsetMaterial { get; private set; }
        internal Image.Type InsetType { get; private set; } = Image.Type.Sliced;
        internal Color InsetColor { get; private set; }
        internal Sprite InputSprite { get; private set; }
        internal Material InputMaterial { get; private set; }
        internal Image.Type InputType { get; private set; } = Image.Type.Sliced;
        internal Color InputColor { get; private set; }
        internal Sprite ButtonSprite { get; private set; }
        internal Material ButtonMaterial { get; private set; }
        internal Image.Type ButtonImageType { get; private set; } = Image.Type.Sliced;
        internal Color ButtonImageColor { get; private set; }
        internal SpriteState ButtonSprites { get; private set; }
        internal ColorBlock ButtonColors { get; private set; }
        internal Selectable.Transition ButtonTransition { get; private set; }
        internal Sprite ScrollbarSprite { get; private set; }
        internal Material ScrollbarMaterial { get; private set; }
        internal Image.Type ScrollbarType { get; private set; } = Image.Type.Sliced;
        internal Color ScrollbarColor { get; private set; }
        internal Sprite ScrollbarHandleSprite { get; private set; }
        internal Material ScrollbarHandleMaterial { get; private set; }
        internal Image.Type ScrollbarHandleType { get; private set; } = Image.Type.Sliced;
        internal Color ScrollbarHandleColor { get; private set; }
        internal TMP_FontAsset Font { get; private set; }
        internal CanvasScaler SourceScaler { get; private set; }
        internal int ControlHeight { get; private set; }
        internal bool HasNativePanel => PanelSprite;
        internal bool HasNativeButton => ButtonSprite;
        internal bool HasNativeFont => Font;

        internal static StorageSearchVanillaTheme Create()
        {
            var theme = new StorageSearchVanillaTheme();
            try
            {
                InventoryGui inventory = InventoryGui.instance;
                if (!inventory) return theme;

                theme.SourceScaler = inventory.GetComponentInParent<CanvasScaler>();

                TMP_Text text = PreferredText(inventory);
                if (text && text.font) theme.Font = text.font;

                Image panel = FindBestPanelImage(inventory);
                if (Usable(panel))
                {
                    theme.PanelSprite = panel.sprite;
                    theme.PanelMaterial = panel.material;
                    theme.PanelType = SafeImageType(panel);
                    theme.PanelColor = panel.color;
                }

                Image inset = FindBestInsetImage(inventory, panel);
                if (Usable(inset))
                {
                    theme.InsetSprite = inset.sprite;
                    theme.InsetMaterial = inset.material;
                    theme.InsetType = SafeImageType(inset);
                    theme.InsetColor = inset.color;
                }

                TryAdoptInput(theme);
                TryAdoptButton(theme, PreferredButton(inventory));
                TryAdoptScrollbar(theme, PreferredScrollbar(inventory));
            }
            catch
            {
                // A UI replacement can destroy a source while the hierarchy is being inspected.
                // Keep whatever native references were proven and let native opaque fallbacks fill
                // the rest; search behavior must not depend on one visual asset.
            }
            return theme;
        }

        internal static StorageSearchVanillaTheme CreateFallback() =>
            new StorageSearchVanillaTheme();

        internal static int CurrentSourceToken()
        {
            try
            {
                InventoryGui inventory = InventoryGui.instance;
                if (!inventory) return 0;
                unchecked
                {
                    int token = inventory.GetInstanceID();
                    Image panel = FindBestPanelImage(inventory);
                    if (Usable(panel)) token = token * 397 ^ panel.sprite.GetInstanceID();
                    Button button = PreferredButton(inventory);
                    if (button && button.image && button.image.sprite)
                        token = token * 397 ^ button.image.sprite.GetInstanceID();
                    TextInput textInput = TextInput.instance;
                    Image inputImage = textInput && textInput.m_inputField
                        ? textInput.m_inputField.GetComponent<Image>()
                        : null;
                    if (Usable(inputImage))
                        token = token * 397 ^ inputImage.sprite.GetInstanceID();
                    Scrollbar scrollbar = PreferredScrollbar(inventory);
                    if (scrollbar && scrollbar.handleRect)
                    {
                        Image handle = scrollbar.handleRect.GetComponent<Image>();
                        if (Usable(handle)) token = token * 397 ^ handle.sprite.GetInstanceID();
                    }
                    TMP_Text text = PreferredText(inventory);
                    if (text && text.font) token = token * 397 ^ text.font.GetInstanceID();
                    return token;
                }
            }
            catch { return 0; }
        }

        private static void TryAdoptButton(StorageSearchVanillaTheme theme, Button source)
        {
            if (!source || !source.image || !source.image.sprite) return;
            theme.ButtonSprite = source.image.sprite;
            theme.ButtonMaterial = source.image.material;
            theme.ButtonImageType = SafeImageType(source.image);
            theme.ButtonImageColor = source.image.color;
            theme.ButtonSprites = source.spriteState;
            theme.ButtonColors = source.colors;
            theme.ButtonTransition = source.transition;
            RectTransform rect = source.GetComponent<RectTransform>();
            if (rect)
                theme.ControlHeight = Mathf.Clamp(Mathf.RoundToInt(rect.rect.height), 28, 56);
        }

        private static void TryAdoptInput(StorageSearchVanillaTheme theme)
        {
            TextInput textInput = TextInput.instance;
            if (!textInput || !textInput.m_inputField) return;
            Image image = textInput.m_inputField.GetComponent<Image>();
            if (!Usable(image)) return;
            theme.InputSprite = image.sprite;
            theme.InputMaterial = image.material;
            theme.InputType = SafeImageType(image);
            theme.InputColor = image.color;
        }

        private static void TryAdoptScrollbar(
            StorageSearchVanillaTheme theme,
            Scrollbar source)
        {
            if (!source) return;
            Image background = source.GetComponent<Image>();
            if (Usable(background))
            {
                theme.ScrollbarSprite = background.sprite;
                theme.ScrollbarMaterial = background.material;
                theme.ScrollbarType = SafeImageType(background);
                theme.ScrollbarColor = background.color;
            }
            Image handle = source.handleRect
                ? source.handleRect.GetComponent<Image>()
                : null;
            if (Usable(handle))
            {
                theme.ScrollbarHandleSprite = handle.sprite;
                theme.ScrollbarHandleMaterial = handle.material;
                theme.ScrollbarHandleType = SafeImageType(handle);
                theme.ScrollbarHandleColor = handle.color;
            }
        }

        private static Image FindBestPanelImage(InventoryGui inventory)
        {
            Image best = null;
            int bestScore = int.MinValue;
            ScoreImages(inventory.m_splitPanel, 180, ref best, ref bestScore);
            ScoreImages(inventory.m_textsDialog ? inventory.m_textsDialog.transform : null,
                120, ref best, ref bestScore);
            ScoreImages(inventory.m_player, 80, ref best, ref bestScore);
            ScoreImages(inventory.m_container, 60, ref best, ref bestScore);
            ScoreImages(inventory.m_crafting, 40, ref best, ref bestScore);
            return best;
        }

        private static Image FindBestInsetImage(InventoryGui inventory, Image panel)
        {
            Image best = null;
            int bestScore = int.MinValue;
            ScoreImages(inventory.m_textsDialog ? inventory.m_textsDialog.m_listRoot : null,
                120, ref best, ref bestScore, panel);
            ScoreImages(inventory.m_recipeListRoot, 80, ref best, ref bestScore, panel);
            ScoreImages(inventory.m_player, 0, ref best, ref bestScore, panel, true);
            return best;
        }

        private static void ScoreImages(
            Transform root,
            int rootBonus,
            ref Image best,
            ref int bestScore,
            Image excluded = null,
            bool preferSmall = false)
        {
            if (!root) return;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int index = 0; index < images.Length; index++)
            {
                Image image = images[index];
                if (!Usable(image) || image == excluded) continue;
                Rect rect = image.rectTransform.rect;
                float area = Mathf.Abs(rect.width * rect.height);
                if (area < 1600f) continue;
                int score = rootBonus;
                if (image.type == Image.Type.Sliced) score += 90;
                if (image.sprite.border.sqrMagnitude > 0f) score += 60;
                string identity = ((image.name ?? string.Empty) + " " +
                                   (image.sprite.name ?? string.Empty)).ToLowerInvariant();
                if (identity.Contains("background") || identity.Contains("panel") ||
                    identity.Contains("bkg") || identity.Contains("wood")) score += 100;
                if (identity.Contains("icon") || identity.Contains("glow") ||
                    identity.Contains("selection")) score -= 150;
                if (preferSmall)
                    score += area <= 250000f ? 40 : -40;
                else
                    score += area >= 80000f ? 35 : 0;
                if (score <= bestScore) continue;
                best = image;
                bestScore = score;
            }
        }

        private static TMP_Text PreferredText(InventoryGui inventory) =>
            inventory && inventory.m_playerName
                ? inventory.m_playerName
                : inventory ? inventory.m_containerName : null;

        private static Button PreferredButton(InventoryGui inventory) =>
            inventory && inventory.m_takeAllButton
                ? inventory.m_takeAllButton
                : inventory ? inventory.m_craftButton : null;

        private static Scrollbar PreferredScrollbar(InventoryGui inventory) =>
            inventory && inventory.m_trophyListScroll
                ? inventory.m_trophyListScroll
                : inventory ? inventory.m_recipeListScroll : null;

        private static bool Usable(Image image) =>
            image && image.sprite && image.color.a > 0.02f;

        private static Image.Type SafeImageType(Image source) =>
            source && source.sprite && source.sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : source ? source.type : Image.Type.Simple;
    }
}
