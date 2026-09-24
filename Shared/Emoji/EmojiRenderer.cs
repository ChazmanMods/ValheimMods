using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace Runic.Shared;

// Compiled into each mod. No global font mutation, external DLL, or runtime download.
internal sealed class EmojiRenderer : MonoBehaviour
{
    private static TMP_SpriteAsset _asset;
    private static bool _failed;
    private TMP_Text _text;
    private TMP_SpriteAsset _original;
    private bool _tint;

    internal static void Attach(TMP_Text text)
    {
        if (!text || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        if (text.GetComponent<EmojiRenderer>()) return;
        var asset = Asset;
        if (!asset) return;
        var renderer = text.gameObject.AddComponent<EmojiRenderer>();
        renderer._text = text; renderer._original = text.spriteAsset; renderer._tint = text.tintAllSprites;
        text.spriteAsset = asset; text.tintAllSprites = false;
    }

    private static TMP_SpriteAsset Asset
    {
        get
        {
            if (_asset || _failed) return _asset;
            Texture2D texture = null;
            Material material = null;
            TMP_SpriteAsset asset = null;
            try
            {
                using var stream = typeof(EmojiRenderer).Assembly.GetManifestResourceStream("Runic.Emoji.atlas.png");
                if (stream == null) throw new InvalidOperationException("Embedded emoji atlas missing.");
                using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "Runic Emoji Atlas"; texture.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(texture, buffer.ToArray(), true)) throw new InvalidOperationException("Emoji atlas could not be decoded.");
                texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                var shader = Shader.Find("TextMeshPro/Sprite");
                if (!shader) throw new InvalidOperationException("TextMeshPro sprite shader unavailable.");
                material = new Material(shader) { name = "Runic Emoji Material", mainTexture = texture, hideFlags = HideFlags.HideAndDontSave };
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                asset.name = "Runic Emoji"; asset.hideFlags = HideFlags.HideAndDontSave;
                asset.spriteSheet = texture;
                // TMP's supported legacy import path builds the glyph/character lookup tables.
                asset.spriteInfoList = new List<TMP_Sprite>();
                for (int i = 0; i < EmojiCatalog.Entries.Length; i++)
                {
                    var entry = EmojiCatalog.Entries[i];
                    asset.spriteInfoList.Add(new TMP_Sprite {
                        id = i, name = entry.Name, unicode = (int)entry.Unicode,
                        x = i % 8 * 76 + 2, y = 608 - (i / 8 * 76 + 2) - 72,
                        width = 72, height = 72, xOffset = 0, yOffset = 64, xAdvance = 78, scale = 1
                    });
                }
                asset.material = material;
                asset.UpdateLookupTables();
                _asset = asset;
            }
            catch (Exception ex)
            {
                _failed = true;
                if (asset) Destroy(asset); if (material) Destroy(material); if (texture) Destroy(texture);
                Debug.LogWarning("Runic emoji rendering unavailable; labels remain editable: " + ex.Message);
            }
            return _asset;
        }
    }

    private void OnDestroy()
    {
        if (_text && _text.spriteAsset == _asset) { _text.spriteAsset = _original; _text.tintAllSprites = _tint; }
    }

    internal static void Release()
    {
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<EmojiRenderer>(FindObjectsSortMode.None))
        { renderer.OnDestroy(); Destroy(renderer); }
        if (_asset) { Destroy(_asset.material); Destroy(_asset.spriteSheet); Destroy(_asset); }
        _asset = null; _failed = false;
    }
}
