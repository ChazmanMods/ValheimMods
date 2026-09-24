using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runic.Shared;

internal static class EmojiPicker
{
    internal static GameObject Open(Transform parent, TMP_InputField input, TMP_FontAsset font)
    {
        int anchor = input.selectionStringAnchorPosition, focus = input.selectionStringFocusPosition;
        var root = Rect(parent, "EmojiPicker", 0, 0, 0, 0);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var shade = root.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .75f);
        var panel = Rect(root, "EmojiPanel", 0, 0, 640, 410);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.gameObject.AddComponent<Image>().color = new Color(.1f, .12f, .13f);
        Text(panel, "Choose an emoji", 18, 10, 490, 32, font, 24);
        Action close = () => { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); };
        Button(panel, "Close", 530, 12, 92, 30, font, close);
        var status = Text(panel, "Select an icon to insert at the cursor. Each icon uses 2 character units.", 18, 336, 604, 32, font, 15);
        Text(panel, "Twemoji · Twitter and contributors · CC BY 4.0", 18, 377, 604, 20, font, 13);
        var grid = Rect(panel, "Icons", 18, 98, 604, 230);
        void Show(string group)
        {
            foreach (Transform child in grid) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            int i = 0;
            foreach (var entry in EmojiCatalog.Entries.Where(e => e.Group == group))
            {
                var selected = entry;
                Button(grid, entry.Text + " " + entry.Name, i % 4 * 152, i / 4 * 57, 146, 49, font, () => {
                    if (!input || !input.interactable) { close(); return; }
                    if (!EmojiText.Insert(input.text, anchor, focus, selected.Text, input.characterLimit, out var value, out int caret))
                    { status.text = "Label is full. Close this picker and shorten the text first."; return; }
                    input.text = value;
                    input.ActivateInputField();
                    input.stringPosition = caret;
                    close();
                });
                i++;
            }
        }
        string[] groups = { "Food", "Materials", "Equipment", "Places" };
        for (int i = 0; i < groups.Length; i++)
        {
            string group = groups[i];
            Button(panel, group, 18 + i * 152, 55, 146, 32, font, () => Show(group));
        }
        Show(groups[0]);
        return root.gameObject;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
    }
    private static TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, TMP_FontAsset font, int size)
    {
        var text = Rect(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.text = value; text.color = Color.white;
        text.richText = false; text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        EmojiRenderer.Attach(text); return text;
    }
    private static void Button(Transform parent, string value, float x, float y, float w, float h, TMP_FontAsset font, Action action)
    {
        var rect = Rect(parent, value, x, y, w, h);
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.23f, .26f, .28f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        Text(rect, value, 6, 0, w - 12, h, font, 17).alignment = TextAlignmentOptions.Center;
    }
}
