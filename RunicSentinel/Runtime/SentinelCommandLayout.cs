using System;
using UnityEngine;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private float _commandTextScale = 1, _commandDrawScale = 1;
        private float _commandColumns = .55f, _commandRows = .6f;
        private Vector2 _dividerStart;
        private float _dividerFraction;
        private GUIStyle[] _commandTextStyles;
        private int[] _commandFontSizes;
        private int _commandDividerControl;
        private GUIStyle _dividerStyle;

        private void DrawResizableCommands()
        {
            if (_commandTextStyles == null)
            {
                _commandTextStyles = new[] { _heading, _section, _label, _value, _textArea, _textField, _button, _tabStyle, _selectedTabStyle };
                _commandFontSizes = new int[_commandTextStyles.Length];
                for (int i = 0; i < _commandTextStyles.Length; i++)
                    _commandFontSizes[i] = _commandTextStyles[i].fontSize;
            }
            _commandDrawScale = _commandTextScale;
            try
            {
                for (int i = 0; i < _commandTextStyles.Length; i++)
                    _commandTextStyles[i].fontSize = Mathf.RoundToInt((_commandFontSizes[i] > 0 ? _commandFontSizes[i] : 13) * _commandDrawScale);
                DrawCommandsWorkspace();
            }
            finally
            {
                for (int i = 0; i < _commandTextStyles.Length; i++)
                    _commandTextStyles[i].fontSize = _commandFontSizes[i];
            }
        }

        private float CommandSize(float size) => size * _commandDrawScale;

        private void DrawCommandLayoutControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(PT("Text") + " " + Mathf.RoundToInt(_commandTextScale * 100) + "%", _label);
            if (GUILayout.Button("A-", _button, GUILayout.Width(58))) _commandTextScale = Mathf.Max(.8f, _commandTextScale - .1f);
            if (GUILayout.Button("A+", _button, GUILayout.Width(58))) _commandTextScale = Mathf.Min(1.5f, _commandTextScale + .1f);
            if (GUILayout.Button(PT("Reset layout"), _button))
            { _commandTextScale = 1; _commandColumns = .55f; _commandRows = .6f; _scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
        }

        private void DrawCommandDivider(bool columns, CommandWorkspaceLayout layout)
        {
            Rect rect = columns
                ? GUILayoutUtility.GetRect(CommandWorkspaceLayout.Divider, layout.TopHeight, GUILayout.Width(CommandWorkspaceLayout.Divider), GUILayout.Height(layout.TopHeight))
                : GUILayoutUtility.GetRect(layout.Width, CommandWorkspaceLayout.Divider, GUILayout.Width(layout.Width), GUILayout.Height(CommandWorkspaceLayout.Divider));
            int id = GUIUtility.GetControlID(columns ? 730321 : 730322, FocusType.Passive, rect);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _commandDividerControl = id;
                        _dividerStart = e.mousePosition;
                        _dividerFraction = columns ? layout.ConsoleWidth / (layout.Width - CommandWorkspaceLayout.Divider) : layout.TopHeight / (layout.Height - CommandWorkspaceLayout.Divider);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float delta = columns ? e.mousePosition.x - _dividerStart.x : e.mousePosition.y - _dividerStart.y;
                        float length = (columns ? layout.Width : layout.Height) - CommandWorkspaceLayout.Divider;
                        float fraction = Mathf.Clamp01(_dividerFraction + delta / length);
                        if (columns) _commandColumns = fraction; else _commandRows = fraction;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; _commandDividerControl = 0; e.Use(); }
                    break;
            }
            if (_dividerStyle == null)
                _dividerStyle = new GUIStyle(_row) { padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            GUI.Box(rect, columns ? "|" : "---", _dividerStyle);
            if (rect.Contains(e.mousePosition)) _hoverText = PT("Drag to resize sections");
        }
    }
}
