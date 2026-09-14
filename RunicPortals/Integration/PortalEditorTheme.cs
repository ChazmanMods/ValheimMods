using GUIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Borrows live Valheim UI assets without copying or rasterizing atlas textures. Every
    /// reference is optional so a UI overhaul can fall back to a quiet, readable native canvas.
    /// </summary>
    internal sealed class PortalEditorTheme
    {
        private PortalEditorTheme()
        {
            PanelColor = new Color32(0x3C, 0x2A, 0x1D, 0xFF);
            InsetColor = new Color32(0x19, 0x11, 0x0C, 0xF4);
            InputColor = new Color32(0x25, 0x18, 0x10, 0xFF);
            ButtonColor = new Color32(0x5A, 0x3C, 0x23, 0xFF);
            SelectedColor = new Color32(0x9B, 0x68, 0x2F, 0xFF);
            TextColor = new Color32(0xEE, 0xD8, 0xAE, 0xFF);
            MutedTextColor = new Color32(0xC2, 0xAA, 0x82, 0xFF);
            ErrorColor = new Color32(0xFF, 0xA1, 0x91, 0xFF);
            ConfirmationColor = new Color32(0xFF, 0xCF, 0x72, 0xFF);
            ColorBlock fallbackButtons = ColorBlock.defaultColorBlock;
            fallbackButtons.normalColor = ButtonColor;
            fallbackButtons.highlightedColor = Color.Lerp(ButtonColor, TextColor, 0.14f);
            fallbackButtons.pressedColor = Color.Lerp(ButtonColor, Color.black, 0.2f);
            fallbackButtons.selectedColor = fallbackButtons.highlightedColor;
            fallbackButtons.disabledColor = new Color(
                ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.45f);
            ButtonColors = fallbackButtons;
            ButtonSprites = default;
            ButtonTransition = Selectable.Transition.ColorTint;
            ControlHeight = 38f;
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
        internal Image.Type ButtonType { get; private set; } = Image.Type.Sliced;
        internal Color ButtonColor { get; private set; }
        internal SpriteState ButtonSprites { get; private set; }
        internal ColorBlock ButtonColors { get; private set; }
        internal Selectable.Transition ButtonTransition { get; private set; }
        internal Color SelectedColor { get; }
        internal Color TextColor { get; private set; }
        internal Color MutedTextColor { get; private set; }
        internal Color ErrorColor { get; }
        internal Color ConfirmationColor { get; }
        internal TMP_FontAsset Font { get; private set; }
        internal CanvasScaler SourceScaler { get; private set; }
        internal float ControlHeight { get; private set; }

        internal static PortalEditorTheme Create()
        {
            var theme = new PortalEditorTheme();
            try
            {
                TextInput textInput = TextInput.instance;
                InventoryGui inventory = InventoryGui.instance;
                Canvas sourceCanvas = textInput
                    ? textInput.GetComponentInParent<Canvas>()
                    : inventory ? inventory.GetComponentInParent<Canvas>() : null;
                theme.SourceScaler = sourceCanvas ? sourceCanvas.GetComponent<CanvasScaler>() : null;

                TMP_Text preferredText = textInput && textInput.m_topic
                    ? textInput.m_topic
                    : inventory && inventory.m_playerName ? inventory.m_playerName : null;
                if (preferredText && preferredText.font)
                {
                    theme.Font = preferredText.font;
                    Color sourceText = preferredText.color;
                    if (sourceText.a > 0.1f)
                    {
                        theme.TextColor = sourceText;
                        theme.MutedTextColor = new Color(
                            sourceText.r, sourceText.g, sourceText.b, sourceText.a * 0.72f);
                    }
                }

                Image panel = FindPanelImage(textInput, inventory);
                if (Usable(panel))
                {
                    theme.PanelSprite = panel.sprite;
                    theme.PanelMaterial = panel.material;
                    theme.PanelType = SafeType(panel);
                    theme.PanelColor = panel.color;
                }

                Image inset = FindInsetImage(inventory, panel);
                if (Usable(inset))
                {
                    theme.InsetSprite = inset.sprite;
                    theme.InsetMaterial = inset.material;
                    theme.InsetType = SafeType(inset);
                    theme.InsetColor = inset.color;
                }

                GuiInputField input = textInput ? textInput.m_inputField : null;
                Image inputImage = input ? input.GetComponent<Image>() : null;
                if (Usable(inputImage))
                {
                    theme.InputSprite = inputImage.sprite;
                    theme.InputMaterial = inputImage.material;
                    theme.InputType = SafeType(inputImage);
                    theme.InputColor = inputImage.color;
                }
                if (!theme.Font && input && input.textComponent && input.textComponent.font)
                    theme.Font = input.textComponent.font;

                Button button = inventory && inventory.m_takeAllButton
                    ? inventory.m_takeAllButton
                    : inventory ? inventory.m_craftButton : null;
                if (button && Usable(button.image))
                {
                    theme.ButtonSprite = button.image.sprite;
                    theme.ButtonMaterial = button.image.material;
                    theme.ButtonType = SafeType(button.image);
                    theme.ButtonColor = button.image.color;
                    theme.ButtonColors = button.colors;
                    theme.ButtonSprites = button.spriteState;
                    theme.ButtonTransition = button.transition;
                    RectTransform rect = button.GetComponent<RectTransform>();
                    if (rect)
                        theme.ControlHeight = Mathf.Clamp(rect.rect.height, 32f, 52f);
                }
            }
            catch
            {
                // The source hierarchy may be replaced or destroyed while it is inspected. The
                // fallback palette remains completely functional and is intentionally opaque.
            }
            return theme;
        }

        private static Image FindPanelImage(TextInput textInput, InventoryGui inventory)
        {
            Image best = null;
            int bestScore = int.MinValue;
            ScoreImages(textInput && textInput.m_panel ? textInput.m_panel.transform : null,
                220, ref best, ref bestScore, null);
            ScoreImages(inventory && inventory.m_splitDialog
                    ? inventory.m_splitDialog.transform
                    : null,
                160, ref best, ref bestScore, null);
            ScoreImages(inventory && inventory.m_player ? inventory.m_player : null,
                80, ref best, ref bestScore, null);
            return best;
        }

        private static Image FindInsetImage(InventoryGui inventory, Image excluded)
        {
            Image best = null;
            int bestScore = int.MinValue;
            ScoreImages(inventory && inventory.m_textsDialog
                    ? inventory.m_textsDialog.transform
                    : null,
                120, ref best, ref bestScore, excluded);
            ScoreImages(inventory && inventory.m_recipeListRoot
                    ? inventory.m_recipeListRoot
                    : null,
                70, ref best, ref bestScore, excluded);
            return best;
        }

        private static void ScoreImages(
            Transform root,
            int rootBonus,
            ref Image best,
            ref int bestScore,
            Image excluded)
        {
            if (!root) return;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int index = 0; index < images.Length; index++)
            {
                Image candidate = images[index];
                if (!Usable(candidate) || candidate == excluded) continue;
                Rect rect = candidate.rectTransform.rect;
                float area = Mathf.Abs(rect.width * rect.height);
                if (area < 1200f) continue;
                int score = rootBonus;
                if (candidate.type == Image.Type.Sliced) score += 80;
                if (candidate.sprite.border.sqrMagnitude > 0f) score += 50;
                string identity = ((candidate.name ?? string.Empty) + " " +
                                   (candidate.sprite.name ?? string.Empty)).ToLowerInvariant();
                if (identity.Contains("background") || identity.Contains("panel") ||
                    identity.Contains("wood") || identity.Contains("bkg")) score += 90;
                if (identity.Contains("icon") || identity.Contains("glow") ||
                    identity.Contains("selection")) score -= 130;
                if (score <= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
        }

        private static bool Usable(Image image) =>
            image && image.sprite && image.color.a > 0.02f;

        private static Image.Type SafeType(Image image) =>
            image && image.sprite && image.sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : image ? image.type : Image.Type.Simple;
    }
}
