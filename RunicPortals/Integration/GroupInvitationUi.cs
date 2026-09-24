using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RunicPermissions.Groups;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal sealed class GroupInvitationUi
    {
        private readonly PortalGroupRuntime _groups;
        private readonly ManualLogSource _log;
        private readonly NativeGroupInvitationPopup _popup = new NativeGroupInvitationPopup();
        private readonly HashSet<string> _handled = new HashSet<string>(StringComparer.Ordinal);
        private GroupInvitationChoice[] _choices = Array.Empty<GroupInvitationChoice>();
        private string _context;
        private string _shown;
        private int _generation;
        private float _nextPoll;
        private float _freshUntil;
        private bool _polling;
        private bool _responding;
        private bool _failed;

        internal GroupInvitationUi(PortalGroupRuntime groups, ManualLogSource log) { _groups = groups; _log = log; }

        internal void Tick()
        {
            if (_failed) return;
            try
            {
                Player player = Player.m_localPlayer;
                if (ZNet.instance == null || player == null || player.GetPlayerID() == 0)
                { Reset(); return; }
                string context = ZNet.instance.GetInstanceID() + ":" + player.GetPlayerID();
                if (_context != context) { Reset(); _context = context; }
                // Learn support through the existing Active response. Never send a new
                // opcode to older servers (which correctly reject unknown requests).
                if (!_groups.SupportsInvitationUi) return;
                float now = Time.realtimeSinceStartup;
                if (!_polling && !_responding && now >= _nextPoll)
                {
                    _polling = true;
                    _nextPoll = now + 5f;
                    int generation = _generation;
                    _groups.RequestInvitations(response =>
                    {
                        if (generation != _generation) return;
                        _polling = false;
                        if (response == null || !GroupInvitationSnapshot.TryDecode(response.Text, out var choices))
                        {
                            _choices = Array.Empty<GroupInvitationChoice>();
                            _nextPoll = Time.realtimeSinceStartup + 30f;
                            return;
                        }
                        _choices = choices;
                        _freshUntil = Time.realtimeSinceStartup + 15f;
                        _handled.RemoveWhere(token => !choices.Any(choice => choice.Token == token));
                    });
                }
                bool unavailable = player.IsDead() || player.IsTeleporting() || player.InCutscene();
                if (_shown != null && (unavailable || now >= _freshUntil ||
                    !_choices.Any(choice => choice.Token == _shown && choice.Expires > DateTime.UtcNow.Ticks)))
                { _popup.Close(); _shown = null; }
                _popup.Refresh();
                if (_shown != null && !_popup.Opened) _shown = null;
                if (_popup.Opened || _responding || unavailable || now >= _freshUntil ||
                    !Application.isFocused || UnifiedPopup.IsVisible() || PortalEditorInputGuard.IsOpen ||
                    InventoryGui.IsVisible() || Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible() ||
                    Minimap.IsOpen() || StoreGui.IsVisible() || Chat.instance != null && Chat.instance.HasFocus()) return;
                if (ZInput.GetButton("Attack") || ZInput.GetButton("JoyHotbarUse") ||
                    Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape)) return;
                GroupInvitationChoice next = _choices.FirstOrDefault(choice =>
                    choice.Expires > DateTime.UtcNow.Ticks && !_handled.Contains(choice.Token));
                if (next == null) return;
                _popup.Open(next, accept => Respond(next, accept));
                if (_popup.Opened) _shown = next.Token;
            }
            catch (Exception exception)
            {
                Reset();
                _failed = true;
                _log?.LogWarning("Group invitation popup unavailable; /group accept and /group decline remain available. " + exception.Message);
            }
        }

        private void Respond(GroupInvitationChoice choice, bool accept)
        {
            if (_responding) return;
            _shown = null;
            _responding = true;
            _handled.Add(choice.Token);
            int generation = _generation;
            _groups.RespondToInvitation(choice, accept, response =>
            {
                if (generation != _generation) return;
                _responding = false;
                _nextPoll = 0f;
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                    response?.Text ?? global::Runic.Localization.RunicText.Get("text_51ed807b359d"));
                // A timed-out request may have committed; query before offering it again.
                if (response == null) _handled.Remove(choice.Token);
                _choices = Array.Empty<GroupInvitationChoice>();
            });
        }

        internal void Reset()
        {
            _generation++;
            _popup.Close();
            _choices = Array.Empty<GroupInvitationChoice>();
            _handled.Clear();
            _context = null; _shown = null;
            _polling = false; _responding = false;
            _nextPoll = 0f; _freshUntil = 0f;
        }
    }
}
