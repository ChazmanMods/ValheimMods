using System;
using System.Text;

namespace RunicAwareness.Core
{
    internal enum AwarenessPanel
    {
        Food = 0,
        Effects = 1,
        Comfort = 2,
        ItemComparison = 3,
        Production = 4,
        Agriculture = 5,
        Building = 6,
        TamedAnimal = 7
    }

    internal sealed class PanelCache
    {
        private static string[] Titles => new[] {
            global::Runic.Localization.RunicText.Get("text_e4eb18069d65"),
            global::Runic.Localization.RunicText.Get("text_358511c8c098"),
            global::Runic.Localization.RunicText.Get("text_040d4645676f"),
            global::Runic.Localization.RunicText.Get("text_3bdbe9d40080"),
            global::Runic.Localization.RunicText.Get("text_13a7c655a7d4"),
            global::Runic.Localization.RunicText.Get("text_f0b1b07aa8f1"),
            global::Runic.Localization.RunicText.Get("text_87c5912f2cef"),
            global::Runic.Localization.RunicText.Get("text_0a7cc2687346")
        };

        private readonly string[] _bodies = new string[Titles.Length];
        private readonly int[] _bodyLines = new int[Titles.Length];
        private readonly StringBuilder _builder =
            new StringBuilder(AwarenessConfig.HardMaximumPanelCharacters);
        private int _version;
        private int _composedVersion = -1;
        private bool _composedInventoryOnly;
        private string _composed = string.Empty;
        private int _composedLines;

        internal int Version => _version;
        internal string ComposedText => _composed;
        internal int ComposedLines => _composedLines;

        internal bool Set(AwarenessPanel panel, string body)
        {
            int index = (int)panel;
            body = body ?? string.Empty;
            if (string.Equals(_bodies[index], body, StringComparison.Ordinal)) return false;
            _bodies[index] = body;
            _bodyLines[index] = BoundedText.CountLines(body);
            _version++;
            return true;
        }

        internal bool Clear(AwarenessPanel panel) => Set(panel, string.Empty);

        internal bool Compose(bool inventoryOnly)
        {
            if (_composedVersion == _version && _composedInventoryOnly == inventoryOnly) return false;
            _builder.Clear();
            int lines = 0;
            for (int index = 0; index < _bodies.Length; index++)
            {
                bool itemPanel = index == (int)AwarenessPanel.ItemComparison;
                if (inventoryOnly != itemPanel || string.IsNullOrEmpty(_bodies[index])) continue;

                int neededLines = _bodyLines[index] + 1 + (_builder.Length == 0 ? 0 : 1);
                if (lines + neededLines > AwarenessConfig.HardMaximumPanelLines) break;
                int neededCharacters = (_builder.Length == 0 ? 0 : 2) +
                                       3 + Titles[index].Length + 5 + _bodies[index].Length;
                if (_builder.Length + neededCharacters > AwarenessConfig.HardMaximumPanelCharacters)
                    break;

                if (_builder.Length > 0)
                {
                    _builder.Append("\n\n");
                    lines++;
                }
                _builder.Append("<b>").Append(Titles[index]).Append("</b>\n");
                _builder.Append(_bodies[index]);
                lines += _bodyLines[index] + 1;
            }

            _composed = _builder.ToString();
            _composedLines = lines;
            _composedVersion = _version;
            _composedInventoryOnly = inventoryOnly;
            return true;
        }
    }
}
