using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RunicSentinel.Core;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelAdminPanel : IDisposable
    {
        private static SentinelAdminPanel _active;
        private readonly object _callbackGate = new object();
        private readonly Queue<Action> _callbacks = new Queue<Action>();
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
        private readonly SentinelAdminControl _control;
        private Rect _window = new Rect(0f, 0f, 980f, 740f);
        private Vector2 _scroll;
        private SentinelAdminDocument _document;
        private bool _open;
        private bool _requesting;
        private bool _cursorVisible;
        private CursorLockMode _cursorLock;
        private int _tab;
        private bool _capEnabled;
        private string _capPlayers = "10";
        private string _status = "Press F3 to authenticate with the server.";
        private GUISkin _skin;
        private GUIStyle _windowStyle;
        private GUIStyle _contentStyle;
        private GUIStyle _footerStyle;
        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _heading;
        private GUIStyle _section;
        private GUIStyle _label;
        private GUIStyle _value;
        private GUIStyle _row;
        private GUIStyle _statusStyle;
        private GUIStyle _textArea;
        private GUIStyle _textField;
        private GUIStyle _button;
        private GUIStyle _tabStyle;
        private GUIStyle _selectedTabStyle;

        internal SentinelAdminPanel(SentinelAdminControl control)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _active = this;
        }

        internal static bool IsOpen => _active != null && _active._open;

        internal static bool BlocksLocalPlayer(Character character) =>
            IsOpen && character != null && ReferenceEquals(character, Player.m_localPlayer);

        internal void Tick()
        {
            DrainCallbacks();
            KeyboardShortcut shortcut = SentinelConfig.AdminPanelKey?.Value ??
                                        new KeyboardShortcut(KeyCode.F3);
            if (shortcut.IsDown())
            {
                if (_open) Close();
                else RequestOpen();
            }
            RenewCursorLease();
        }

        internal void Draw()
        {
            if (!_open || _document == null) return;
            if (Event.current != null && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();
                Close();
                return;
            }
            RenewCursorLease();
            EnsureStyles();
            float width = Mathf.Min(1020f, Screen.width - 32f);
            float height = Mathf.Min(760f, Screen.height - 32f);
            _window.width = width;
            _window.height = height;
            _window.x = Mathf.Clamp(_window.x, 16f, Math.Max(16f, Screen.width - width - 16f));
            _window.y = Mathf.Clamp(_window.y, 16f, Math.Max(16f, Screen.height - height - 16f));
            GUISkin previous = GUI.skin;
            try
            {
                GUI.skin = _skin;
                _window = GUI.Window(
                    730311, _window, DrawWindow, GUIContent.none, _windowStyle);
            }
            finally
            {
                GUI.skin = previous;
            }
        }

        internal static void RenewCursorLease()
        {
            if (!IsOpen) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RequestOpen()
        {
            if (_requesting) return;
            _requesting = true;
            _status = "Authenticating administrator with the authoritative server…";
            _control.RequestStatus((ok, document, reason) => Enqueue(() =>
            {
                _requesting = false;
                if (!ok || document == null)
                {
                    _status = "Access denied: " + reason;
                    Console.instance?.AddString("Runic Sentinel: " + reason);
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                        "Runic Sentinel: server adminlist.txt or a signed Sentinel role is required. See F5 for details.");
                    return;
                }
                _document = document;
                ResetCapacityDraft();
                _cursorVisible = Cursor.visible;
                _cursorLock = Cursor.lockState;
                _window.x = (Screen.width - _window.width) * 0.5f;
                _window.y = (Screen.height - _window.height) * 0.5f;
                _status = document.Status;
                _open = true;
                RenewCursorLease();
            }));
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("RUNIC SENTINEL FORGE", _title);
            GUILayout.Label("Server Administrator  •  Raven's Gate", _subtitle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(88f), GUILayout.Height(34f)))
                Close();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            string[] tabs = { "Status", "Mod Policy", "People", "Enforcement", "Admin Tools", "Server Cap" };
            for (int index = 0; index < tabs.Length; index++)
                if (GUILayout.Button(
                        tabs[index],
                        _tab == index ? _selectedTabStyle : _tabStyle,
                        GUILayout.Height(36f)))
                    _tab = index;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            GUILayout.BeginVertical(_contentStyle);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tab == 0) DrawStatus();
            else if (_tab == 1) DrawMods();
            else if (_tab == 2) DrawPeople();
            else if (_tab == 3) DrawEnforcement();
            else if (_tab == 4) DrawTools();
            else DrawCapacity();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(8f);

            GUILayout.BeginVertical(_footerStyle);
            GUILayout.Label(_status ?? string.Empty, _statusStyle, GUILayout.MinHeight(34f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Every operation is independently re-authorized by the server.",
                _label);
            GUILayout.FlexibleSpace();
            GUI.enabled = !_requesting && _document.ManagedSigningKey;
            if (_tab != 5 && GUILayout.Button(
                    "Apply & Sign Policy", _button, GUILayout.Width(190f), GUILayout.Height(38f)))
                Apply();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, _window.width - 110f, 58f));
        }

        private void DrawStatus()
        {
            Header("Raven's Gate status");
            Row("Administrator access", _document.AdministratorSource);
            if (_document.SetupAvailable)
            {
                GUILayout.Label("Your server administrator identity is verified. One-time setup creates a private signing key on the server and registers your authenticated account. It backs up the loaded world and leaves the admission mode unchanged.", _label);
                GUI.enabled = !_requesting;
                if (GUILayout.Button("Set Up Sentinel", _button, GUILayout.Height(42f)))
                    RunTool("bootstrap");
                GUI.enabled = true;
                GUILayout.Space(8f);
            }
            Row("Signed profile", _document.Profile);
            Row("Policy sequence", _document.Sequence.ToString());
            Row("Runtime integrity", _document.Integrity);
            Row("Admission transport", _document.Status);
            Row("Last admission denial", Empty(_document.LastDenial));
            Row("Server-managed signing key", _document.ManagedSigningKey ? "Present" : "Not initialized");
            Row("Public-key pin", Empty(_document.SigningKeyPin));
            GUILayout.Space(10f);
            GUILayout.Label("The exact DLL list is admission evidence reported by each client. It does not " +
                            "turn a client into a trusted machine. Gameplay security comes from server-owned " +
                            "authorization and validation in every Runic endpoint.", _section);
            if (!_document.ManagedSigningKey && !_document.SetupAvailable)
                GUILayout.Label("Signing is not ready. Refresh after the server snapshot loads. Existing policy or key files must be repaired by the server owner; setup will not replace them.", _statusStyle);
        }

        private void DrawMods()
        {
            Header("Signed mod passport");
            LabeledField("Profile name", ref _document.Profile);
            LabeledField("Expires (Unix seconds; 0 = never)", ref _document.ExpiresUnixSeconds);
            GUILayout.Label("Unknown mods", _section);
            Choice(ref _document.UnknownMods, "Forbidden", "Quarantined", "Unmanaged");
            PolicyArea("Required / whitelist — id|version|sha256", ref _document.RequiredMods);
            PolicyArea("Approved optional — id|version|sha256", ref _document.OptionalMods);
            PolicyArea("Gray list / unmanaged — id|version|sha256", ref _document.GrayListMods);
            PolicyArea("Forbidden — id|version|sha256", ref _document.ForbiddenMods);
            GUILayout.Label("Detected server profile and most recent client report (read-only)", _section);
            GUILayout.TextArea(_document.DetectedProfile, _textArea, GUILayout.MinHeight(150f));
            GUILayout.Label("Standalone transport (server-owned, read-only)", _section);
            GUILayout.TextArea(_document.Modules, _textArea, GUILayout.MinHeight(120f));
        }

        private void DrawPeople()
        {
            Header("Signed identities");
            GUILayout.Label("Administrators — authority|subject", _section);
            GUILayout.Label(
                "These signed roles grant Sentinel access independently of the server adminlist. Server administrators also inherit access unless UseServerAdminList is disabled on the server. To revoke all access, remove both grants. Signed bans take precedence.", _label);
            _document.Administrators = GUILayout.TextArea(_document.Administrators, _textArea,
                GUILayout.MinHeight(220f));
            GUILayout.Space(12f);
            GUILayout.Label("Banned users — authority|subject", _section);
            _document.BannedUsers = GUILayout.TextArea(_document.BannedUsers, _textArea,
                GUILayout.MinHeight(220f));
        }

        private void DrawEnforcement()
        {
            Header("Automatic enforcement");
            GUILayout.Label("Admission mode", _section);
            Row("Effective startup value", _document.AdmissionMode);
            GUILayout.Label(
                "Change Remote Admission / Policy in the server configuration, then restart.",
                _label);
            LabeledField("Runtime DLL/policy check interval (5–300 seconds)",
                ref _document.IntegritySeconds);
            LabeledField("Very-high-confidence findings before disconnect (1–10)",
                ref _document.VeryHighThreshold);
            LabeledField("High-confidence findings before disconnect (1–20)",
                ref _document.HighThreshold);
            LabeledField("Graduated-enforcement window (10–600 seconds)",
                ref _document.EnforcementWindowSeconds);
            _document.BackupTransitions = GUILayout.Toggle(_document.BackupTransitions,
                " Require a verified world backup before policy/modpack transitions");
            GUILayout.Space(14f);
            GUILayout.Label("Conclusive violations disconnect immediately. Lesser findings are blocked " +
                            "first and disconnect only after the configured threshold. All decisions are " +
                            "recorded in the bounded security flight recorder.", _section);
        }

        private void DrawTools()
        {
            Header("Server-owned administrator tools");
            Tool("Create Support Report", "report",
                "Writes bounded policy, profile, integrity, and enforcement evidence.");
            Tool("Create Production/Portal Network Map", "networks",
                "Writes the administrator-only live network topology snapshot on the server.");
            Tool("Create Verified World Backup", "backup",
                "Creates and validates a Runic Safety backup of the currently loaded world.");
        }

        private void ResetCapacityDraft()
        {
            _capEnabled = _document.CapacitySavedEnabled;
            _capPlayers = _document.CapacitySavedPlayers;
        }

        private void DrawCapacity()
        {
            Header("Server player cap");
            if (!_document.CapacitySupported)
            {
                GUILayout.Label("The server's Sentinel does not support cap administration. Update the host to RunicSentinel 1.4.0 or RunicSentinelServer 1.1.0.", _label);
            }
            else
            {
                GUILayout.Label(_document.CapacityStatus, _label);
                if (_document.CapacityAvailable)
                {
                    Row("Host World Engine", _document.CapacityVersion);
                    Row("Players currently connected", _document.CapacityCurrentPlayers);
                    Row("Running player cap", _document.CapacityActivePlayers);
                    Row("Validated running override", _document.CapacityActiveEnabled ? "On" : "Off / unavailable — see status above");
                    Row("Saved for next startup", _document.CapacitySavedEnabled ? _document.CapacitySavedPlayers + " players (override on)" : "Override off (vanilla limit)");
                    Row("Restart status", _document.CapacityRestartRequired ? "Restart required for saved settings / integrity recovery" : "No cap restart pending");
                    GUILayout.Space(12f);
                    GUI.enabled = !_requesting;
                    _capEnabled = GUILayout.Toggle(_capEnabled, " Enable World Engine's player-cap override after restart");
                    GUILayout.Label("Maximum players (2–64)", _section);
                    _capPlayers = GUILayout.TextField(_capPlayers ?? "", 2, _textField, GUILayout.Width(110f), GUILayout.Height(32f));
                    bool valid = SentinelCapacitySettings.TryPlayers(_capPlayers, out int players);
                    if (!valid) GUILayout.Label("Enter a whole number from 2 to 64.", _statusStyle);
                    if (_capEnabled && valid && int.TryParse(_document.CapacityCurrentPlayers, out int current) && players < current)
                        GUILayout.Label("This is below the current player count. Nobody will be kicked now; fewer slots will be available after restart.", _statusStyle);
                    bool changed = _capEnabled != _document.CapacitySavedEnabled || _capPlayers != _document.CapacitySavedPlayers;
                    GUI.enabled = !_requesting && valid && changed;
                    if (GUILayout.Button("Save for Next Restart", _button, GUILayout.Width(250f), GUILayout.Height(40f))) SaveCapacity(players);
                    GUI.enabled = true;
                    GUILayout.Space(8f);
                    GUILayout.Label("Only the host's cap settings are saved. This does not restart the server, disconnect players, grant administrator access, or change Sentinel's mod policy. World Engine validates the complete cap patch set at the next startup.", _label);
                    GUILayout.Label("The dedicated PlayFab host slot is reserved automatically. Enter the number of human players, not players plus one.", _label);
                }
            }
            GUILayout.Space(12f);
            GUI.enabled = !_requesting;
            if (GUILayout.Button("Refresh Server Settings (discard edits)", _button, GUILayout.Width(330f), GUILayout.Height(36f))) Refresh();
            GUI.enabled = true;
        }

        private void SaveCapacity(int players)
        {
            _requesting = true;
            _status = "Saving the authoritative server's cap settings for its next restart…";
            _control.SaveCapacity(_document.CapacityRevision, _capEnabled, players, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? "Saved: " : "Rejected: ") + message;
                if (ok) Refresh();
            }));
        }

        private void Tool(string label, string tool, string explanation)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = !_requesting;
            if (GUILayout.Button(label, _button, GUILayout.Width(280f), GUILayout.Height(40f)))
                RunTool(tool);
            GUI.enabled = true;
            GUILayout.Label(explanation, _label, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void Apply()
        {
            _requesting = true;
            _status = "Validating, backing up, signing, and applying on the server…";
            _control.Apply(_document, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? "Success: " : "Rejected: ") + message;
                if (ok) Refresh();
            }));
        }

        private void RunTool(string tool)
        {
            _requesting = true;
            _status = tool == "bootstrap" ? "Creating the server signing key and verified backup. Please wait; first-time setup can take a moment…" : "Running server tool…";
            _control.RunTool(tool, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? "Success: " : "Failed: ") + message;
                if (tool == "bootstrap") Refresh();
            }));
        }

        private void Refresh()
        {
            _requesting = true;
            _control.RequestStatus((ok, document, reason) => Enqueue(() =>
            {
                _requesting = false;
                if (ok && document != null) { _document = document; ResetCapacityDraft(); }
                else _status = "Refresh failed: " + reason;
            }));
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
        }

        private void EnsureStyles()
        {
            if (_skin != null) return;
            _skin = UnityEngine.Object.Instantiate(GUI.skin);
            _skin.name = "RunicSentinelValheimSkin";
            _skin.hideFlags = HideFlags.HideAndDontSave;

            Texture2D wood = CreateWoodTexture(
                "sentinel-wood", new Color(0.27f, 0.15f, 0.075f, 0.99f));
            Texture2D inset = CreateInsetTexture(
                "sentinel-inset", new Color(0.055f, 0.032f, 0.019f, 0.94f));
            Texture2D footer = CreateInsetTexture(
                "sentinel-footer", new Color(0.095f, 0.052f, 0.026f, 0.98f));
            Texture2D row = CreateInsetTexture(
                "sentinel-row", new Color(0.12f, 0.067f, 0.032f, 0.84f));
            Texture2D button = CreateButtonTexture(
                "sentinel-button", new Color(0.32f, 0.20f, 0.10f, 1f));
            Texture2D buttonHover = CreateButtonTexture(
                "sentinel-button-hover", new Color(0.43f, 0.28f, 0.12f, 1f));
            Texture2D buttonActive = CreateButtonTexture(
                "sentinel-button-active", new Color(0.18f, 0.095f, 0.04f, 1f));
            Texture2D selected = CreateButtonTexture(
                "sentinel-tab-selected", new Color(0.50f, 0.31f, 0.10f, 1f));
            Texture2D field = CreateInsetTexture(
                "sentinel-field", new Color(0.035f, 0.024f, 0.018f, 1f));

            Color cream = new Color(0.94f, 0.88f, 0.72f, 1f);
            Color muted = new Color(0.76f, 0.70f, 0.59f, 1f);
            Color gold = new Color(1f, 0.73f, 0.20f, 1f);

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(18, 18, 14, 16)
            };
            _windowStyle.normal.background = wood;
            _contentStyle = Box(inset, new RectOffset(14, 14, 12, 12));
            _footerStyle = Box(footer, new RectOffset(12, 12, 8, 8));
            _row = Box(row, new RectOffset(8, 8, 4, 4));

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _title.normal.textColor = gold;
            _subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _subtitle.normal.textColor = muted;
            _heading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            _heading.normal.textColor = gold;
            _section = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _section.normal.textColor = new Color(0.96f, 0.72f, 0.30f, 1f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _label.normal.textColor = cream;
            _value = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
            _value.normal.textColor = new Color(1f, 0.84f, 0.45f, 1f);
            _statusStyle = new GUIStyle(_label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                fontStyle = FontStyle.Bold
            };
            _statusStyle.normal.textColor = cream;
            _textArea = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = false,
                fontSize = 13,
                padding = new RectOffset(8, 8, 7, 7)
            };
            _textArea.normal.background = field;
            _textArea.focused.background = field;
            _textArea.normal.textColor = cream;
            _textArea.focused.textColor = Color.white;
            _textField = new GUIStyle(_textArea) { wordWrap = false };

            _button = ButtonStyle(button, buttonHover, buttonActive, cream);
            _tabStyle = new GUIStyle(_button) { fontSize = 13 };
            _selectedTabStyle = ButtonStyle(selected, buttonHover, buttonActive, Color.white);
            _selectedTabStyle.fontStyle = FontStyle.Bold;

            _skin.label = _label;
            _skin.button = _button;
            _skin.textField = _textField;
            _skin.textArea = _textArea;
            _skin.box = _contentStyle;
            _skin.toggle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(22, 4, 3, 3)
            };
            _skin.toggle.normal.textColor = cream;
            _skin.toggle.onNormal.textColor = gold;
            _skin.toggle.hover.textColor = Color.white;
            _skin.toggle.onHover.textColor = Color.white;
            _skin.settings.selectionColor = new Color(0.65f, 0.38f, 0.08f, 0.85f);
        }

        private void Header(string text) { GUILayout.Label(text, _heading); GUILayout.Space(8f); }
        private void Row(string name, string value)
        {
            GUILayout.BeginHorizontal(_row);
            GUILayout.Label(name, _label, GUILayout.Width(225f));
            GUILayout.Label(value ?? string.Empty, _value, GUILayout.MinHeight(24f));
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
        }
        private void LabeledField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _section, GUILayout.Width(430f));
            value = GUILayout.TextField(
                value ?? string.Empty, _textField, GUILayout.ExpandWidth(true), GUILayout.Height(30f));
            GUILayout.EndHorizontal();
        }
        private void PolicyArea(string label, ref string value)
        {
            GUILayout.Label(label, _section);
            value = GUILayout.TextArea(value ?? string.Empty, _textArea, GUILayout.MinHeight(145f));
        }
        private void Choice(ref string value, params string[] choices)
        {
            GUILayout.BeginHorizontal();
            foreach (string choice in choices)
                if (GUILayout.Button(
                        choice,
                        value == choice ? _selectedTabStyle : _tabStyle,
                        GUILayout.Width(155f),
                        GUILayout.Height(34f)))
                    value = choice;
            GUILayout.EndHorizontal();
        }

        private GUIStyle Box(Texture2D texture, RectOffset padding)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(7, 7, 7, 7),
                padding = padding
            };
            style.normal.background = texture;
            return style;
        }

        private static GUIStyle ButtonStyle(
            Texture2D normal,
            Texture2D hover,
            Texture2D active,
            Color text)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(10, 10, 6, 6),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = hover;
            style.normal.textColor = text;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            return style;
        }

        private Texture2D CreateWoodTexture(string name, Color baseColor)
        {
            const int width = 96;
            const int height = 64;
            var texture = NewTexture(name, width, height);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float grain = Mathf.Sin(x * 0.31f + Mathf.Sin(y * 0.17f) * 2.4f) * 0.020f;
                grain += (Mathf.PerlinNoise(x * 0.085f, y * 0.24f) - 0.5f) * 0.075f;
                float edge = Mathf.Min(Mathf.Min(x, width - 1 - x),
                    Mathf.Min(y, height - 1 - y));
                float shade = edge < 4f ? -0.14f + edge * 0.018f : grain;
                pixels[y * width + x] = Tint(baseColor, shade);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private Texture2D CreateInsetTexture(string name, Color baseColor)
        {
            const int size = 24;
            var texture = NewTexture(name, size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, size - 1 - x), Math.Min(y, size - 1 - y));
                float shade = edge < 3 ? 0.09f - edge * 0.045f : 0f;
                pixels[y * size + x] = Tint(baseColor, shade);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private Texture2D CreateButtonTexture(string name, Color baseColor)
        {
            const int width = 32;
            const int height = 20;
            var texture = NewTexture(name, width, height);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                float gradient = 0.05f - (y / (float)(height - 1)) * 0.10f;
                float shade = edge < 2 ? -0.16f : gradient;
                pixels[y * width + x] = Tint(baseColor, shade);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private Texture2D NewTexture(string name, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _ownedTextures.Add(texture);
            return texture;
        }

        private static Color Tint(Color color, float amount) => new Color(
            Mathf.Clamp01(color.r + amount),
            Mathf.Clamp01(color.g + amount * 0.72f),
            Mathf.Clamp01(color.b + amount * 0.42f),
            color.a);
        private static string Empty(string value) => string.IsNullOrEmpty(value) ? "None" : value;
        private void Enqueue(Action action) { lock (_callbackGate) _callbacks.Enqueue(action); }
        private void DrainCallbacks()
        {
            while (true)
            {
                Action action;
                lock (_callbackGate)
                {
                    if (_callbacks.Count == 0) return;
                    action = _callbacks.Dequeue();
                }
                try { action(); } catch { }
            }
        }

        public void Dispose()
        {
            Close();
            if (ReferenceEquals(_active, this)) _active = null;
            lock (_callbackGate) _callbacks.Clear();
            if (_skin != null) UnityEngine.Object.Destroy(_skin);
            _skin = null;
            for (int index = 0; index < _ownedTextures.Count; index++)
                if (_ownedTextures[index] != null)
                    UnityEngine.Object.Destroy(_ownedTextures[index]);
            _ownedTextures.Clear();
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class SentinelAdminCursorPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix() => !SentinelAdminPanel.IsOpen;

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix() => SentinelAdminPanel.RenewCursorLease();
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class SentinelAdminInputPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (!SentinelAdminPanel.BlocksLocalPlayer(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "PlayerAttackInput", new[] { typeof(float) })]
    internal static class SentinelAdminQueuedAttackPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) =>
            !SentinelAdminPanel.BlocksLocalPlayer(__instance);
    }

    [HarmonyPatch(
        typeof(Character), nameof(Character.StartAttack),
        new[] { typeof(Character), typeof(bool) })]
    internal static class SentinelAdminStartAttackPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (!SentinelAdminPanel.BlocksLocalPlayer(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) })]
    internal static class SentinelAdminPlacementPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore("chazman.RunicBuildCamera")]
        private static bool Prefix(Player __instance) =>
            !SentinelAdminPanel.BlocksLocalPlayer(__instance);
    }

    [HarmonyPatch(typeof(Player), "UpdateBuildGuiInput")]
    internal static class SentinelAdminBuildInputPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) =>
            !SentinelAdminPanel.BlocksLocalPlayer(__instance);
    }
}
