using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicPortals.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicPortals.Integration
{
    // Uses Valheim's native dialog and controller navigation. Only our own popup is
    // removed or relabeled; an unrelated popup pushed above it is left alone.
    internal sealed class NativeGroupInvitationPopup
    {
        private static NativeGroupInvitationPopup _active;
        private static readonly FieldInfo Instance = AccessTools.Field(typeof(UnifiedPopup), "instance");
        private static readonly FieldInfo Stack = AccessTools.Field(typeof(UnifiedPopup), "popupStack");
        private YesNoPopup _popup;
        private UnifiedPopup _host;
        private Action<bool> _respond;
        private bool _cursorVisible;
        private CursorLockMode _cursorLock;
        private bool _focused;
        internal static bool IsOpen => _active?._popup != null;
        internal bool Opened => _popup != null;

        internal void Open(GroupInvitationChoice invitation, Action<bool> respond)
        {
            if (IsOpen || !UnifiedPopup.IsAvailable() || UnifiedPopup.IsVisible()) return;
            _host = Instance.GetValue(null) as UnifiedPopup;
            if (!_host || !(Stack.GetValue(_host) is Stack<PopupBase>)) return;
            foreach (string field in new[] { "buttonLeftText", "buttonRightText", "buttonLeft" })
                if (AccessTools.Field(typeof(UnifiedPopup), field)?.GetValue(_host) == null)
                    throw new MissingFieldException("Invitation dialog control: " + field);
            _respond = respond;
            _cursorVisible = Cursor.visible;
            _cursorLock = Cursor.lockState;
            _focused = false;
            _popup = new YesNoPopup(global::Runic.Localization.RunicText.Get("text_3733873c8398"),
                Safe(invitation.Inviter) + global::Runic.Localization.RunicText.Get("text_d8566ec98915") + Safe(invitation.Name) +
                global::Runic.Localization.RunicText.Get("text_4a1bc38da902"),
                () => Respond(true), () => Respond(false), false, true);
            _active = this;
            UnifiedPopup.Push(_popup);
            Refresh();
        }

        internal void Refresh()
        {
            if (_popup == null || !_host) return;
            var stack = Stack.GetValue(_host) as Stack<PopupBase>;
            if (stack == null || !stack.Contains(_popup)) { Close(); return; }
            if (!ReferenceEquals(stack.Peek(), _popup)) { _focused = false; return; }
            SetLabel("buttonRightText", global::Runic.Localization.RunicText.Get("text_89713b9c9c1b"));
            SetLabel("buttonLeftText", global::Runic.Localization.RunicText.Get("text_a2d285b35287"));
            RenewCursorLease();
            if (!_focused)
            {
                (AccessTools.Field(typeof(UnifiedPopup), "buttonLeft").GetValue(_host) as Button)?.Select();
                _focused = true;
            }
            if (PortalEditorInputGuard.PollCancel()) Respond(false);
        }

        private void SetLabel(string field, string value)
        {
            var label = AccessTools.Field(typeof(UnifiedPopup), field).GetValue(_host) as TMP_Text;
            if (label) label.text = value;
        }

        private void Respond(bool accept)
        {
            if (_popup == null) return;
            Action<bool> respond = _respond;
            PortalEditorInputGuard.CapturePrimaryPointer();
            Close();
            respond?.Invoke(accept);
        }

        internal void Close()
        {
            YesNoPopup popup = _popup;
            _popup = null;
            _respond = null;
            if (ReferenceEquals(_active, this)) _active = null;
            if (popup == null) return;
            if (_host && Stack.GetValue(_host) is Stack<PopupBase> stack && stack.Contains(popup))
            {
                if (ReferenceEquals(stack.Peek(), popup) && ReferenceEquals(Instance.GetValue(null), _host))
                    UnifiedPopup.Pop();
                else
                {
                    PopupBase[] remaining = stack.Where(item => !ReferenceEquals(item, popup)).Reverse().ToArray();
                    stack.Clear();
                    foreach (PopupBase item in remaining) stack.Push(item);
                }
            }
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
            PlayerController.SetTakeInputDelay(0.15f);
            _host = null;
        }

        internal static void RenewCursorLease()
        {
            if (!IsOpen) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        // Names are plain data, not TMP markup or localization directives.
        private static string Safe(string text) => text.Replace("<", "＜").Replace(">", "＞").Replace("$", "＄");
    }
}
