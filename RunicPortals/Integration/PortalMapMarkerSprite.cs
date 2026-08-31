using System;
using System.IO;
using System.Resources;
using UnityEngine;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Owns the cosmetic sprite used only by Runic map pins. PinType.Icon3 remains the native
    /// filter category; replacing an individual PinData icon avoids altering vanilla or user pins.
    /// </summary>
    internal static class PortalMapMarkerSprite
    {
        internal const string ResourceName = "RunicPortals.Assets.RunicPortalMapPin.png";
        internal const int ExpectedWidth = 128;
        internal const int ExpectedHeight = 128;
        internal const int MaximumPngBytes = 256 * 1024;

        private static Texture2D _texture;
        private static Sprite _sprite;
        private static bool _loadAttempted;
        private static bool _failureLogged;

        internal static bool Apply(Minimap.PinData pin)
        {
            if (pin == null) return false;
            try
            {
                Sprite sprite = GetOrCreate();
                if (!sprite) return false;
                pin.m_icon = sprite;
                if (pin.m_iconElement) pin.m_iconElement.sprite = sprite;
                return true;
            }
            catch (Exception exception)
            {
                LogFallback(exception);
                return false;
            }
        }

        internal static void Shutdown()
        {
            Sprite sprite = _sprite;
            Texture2D texture = _texture;
            _sprite = null;
            _texture = null;
            _loadAttempted = false;
            _failureLogged = false;
            if (sprite) UnityEngine.Object.Destroy(sprite);
            if (texture) UnityEngine.Object.Destroy(texture);
        }

        private static Sprite GetOrCreate()
        {
            if (_sprite) return _sprite;
            if (_loadAttempted) return null;
            _loadAttempted = true;

            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                byte[] png = ReadEmbeddedPng();
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "Runic Portal Map Pin Texture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, png, true))
                    throw new InvalidDataException("Unity rejected the embedded portal marker PNG.");
                if (texture.width != ExpectedWidth || texture.height != ExpectedHeight)
                    throw new InvalidDataException(
                        "Embedded portal marker dimensions are not 128x128.");

                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0u,
                    SpriteMeshType.FullRect);
                if (!sprite)
                    throw new InvalidOperationException("Unity did not create the portal marker sprite.");
                sprite.name = "Runic Portal Map Pin";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                _texture = texture;
                _sprite = sprite;
                return sprite;
            }
            catch (Exception exception)
            {
                if (sprite) UnityEngine.Object.Destroy(sprite);
                if (texture) UnityEngine.Object.Destroy(texture);
                LogFallback(exception);
                return null;
            }
        }

        private static byte[] ReadEmbeddedPng()
        {
            using Stream stream = typeof(PortalMapMarkerSprite).Assembly
                .GetManifestResourceStream(ResourceName);
            if (stream == null)
                throw new MissingManifestResourceException(ResourceName);
            long length = stream.Length;
            if (length < 32 || length > MaximumPngBytes)
                throw new InvalidDataException("Embedded portal marker PNG size is invalid.");
            var bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Embedded portal marker PNG ended early.");
                offset += read;
            }
            return bytes;
        }

        private static void LogFallback(Exception exception)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            Diagnostics.Warning(
                "The custom portal map marker could not be loaded; Runic Portals will use " +
                "Valheim's fallback marker for this session. " + exception.GetType().Name +
                ": " + exception.Message);
        }
    }
}
