using System;
using System.Linq;
using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace BetterSigns
{
    internal static class RuntimeSelfTest
    {
        internal static bool Run()
        {
            try
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab("sign");
                Sign sign = prefab != null ? prefab.GetComponent<Sign>() : null;
                TMP_Text source = sign?.m_textWidget;
                if (source == null) throw new InvalidOperationException("Vanilla sign text widget was not found.");
                if (!FontCatalog.CanRenderBasicText(source.font))
                    throw new InvalidOperationException($"Vanilla sign font '{source.font?.name}' failed the Latin glyph check.");

                TMP_FontAsset[] usable = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .Where(FontCatalog.CanRenderBasicText).GroupBy(f => f.name).Select(g => g.First()).ToArray();
                if (usable.Length == 0) throw new InvalidOperationException("No safe sign fonts were found.");

                string probe = "Better Signs TEST 0123456789";
                if (!FontCatalog.CanRender(source.font, probe))
                    throw new InvalidOperationException($"Vanilla sign font '{source.font.name}' cannot render the test phrase.");

                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
                clone.SetActive(false);
                try
                {
                    TMP_Text widget = clone.GetComponent<TMP_Text>();
                    widget.font = source.font;
                    widget.text = probe;
                    if (!string.Equals(widget.text, probe, StringComparison.Ordinal))
                        throw new InvalidOperationException("TMP text assignment did not round-trip.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }

                Plugin.Log.LogInfo($"BETTERSIGNS SELF-TEST PASS: vanilla font='{source.font.name}', safe fonts={usable.Length}, text='{probe}'.");
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError("BETTERSIGNS SELF-TEST FAIL: " + exception);
                return false;
            }
        }
    }
}
