using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using RunicSentinel.Core;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel : IDisposable
    {
        private static SentinelAdminPanel _active;
        private readonly object _callbackGate = new object();
        private readonly Queue<Action> _callbacks = new Queue<Action>();
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
        private readonly SentinelAdminControl _control;
        private Rect _window = new Rect(0f, 0f, 980f, 740f);
        private Vector2 _scroll;
        private bool _sizedWindow, _resizingWindow,_fitWindow;
        private Vector2 _resizeMouse, _resizeSize;
        private float _bodyHeight;
        private Vector2 _footerScroll;
        private SentinelAdminDocument _document;
        private bool _open;
        private bool _requesting;
        private bool _cursorVisible;
        private CursorLockMode _cursorLock;
        private int _tab;
        private bool _capEnabled;
        private string _capPlayers = "10";
        private string _commandLine = string.Empty;
        private string _commandSearch = string.Empty;
        private string _selectedCommand = "devcommands";
        private bool _commandBusy;
        private Vector2 _commandScroll;
        private Vector2 _commandOutputScroll;
        private string _lastCommandOutput="", _commandArguments="";
        private readonly List<string> _commandHistory=new List<string>();
        private int _commandHistoryIndex;
        private bool _sentinelModesActive, _modeLeasePending;
        private bool _ownsDebugMode, _ownsNoCost, _ownsFlight;
        private float _nextModeLease;
        private ZNet _modeNetwork;
        private Player _modePlayer;
        private IReadOnlyList<Terminal.ConsoleCommand> _commandCatalog;
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
            SentinelCommandConsole.Reset();
            _active = this;
            RunicSentinel.Input.ModalGameplayInput.IsOpen = () => IsOpen;
        }

        internal static bool IsOpen => _active != null && _active._open;

        internal static bool BlocksLocalPlayer(Character character) =>
            character != null && ReferenceEquals(character, Player.m_localPlayer) && RunicSentinel.Input.ModalGameplayInput.Blocked;

        internal void Tick()
        {
            DrainCallbacks();
            TickPlayers();
            TickPlayerActions();
            TickCommandLease();
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
            if(_fitWindow){_window=new Rect(16,16,Screen.width-32,Screen.height-32);_fitWindow=false;}
            if(!_sizedWindow){_window.width=Mathf.Min(1380,Screen.width-32);_window.height=Screen.height-48;_sizedWindow=true;}
            Event resizeEvent=Event.current;
            if(resizeEvent.type==EventType.MouseDown&&resizeEvent.button==0&&new Rect(_window.xMax-28,_window.yMax-28,28,28).Contains(resizeEvent.mousePosition))
            {_resizingWindow=true;_resizeMouse=resizeEvent.mousePosition;_resizeSize=_window.size;resizeEvent.Use();}
            if(_resizingWindow&&resizeEvent.type==EventType.MouseDrag){_window.size=_resizeSize+(resizeEvent.mousePosition-_resizeMouse);resizeEvent.Use();}
            if(_resizingWindow&&resizeEvent.type==EventType.MouseUp){_resizingWindow=false;resizeEvent.Use();}
            float width = Mathf.Clamp(_window.width,Mathf.Min(820,Screen.width-32),Screen.width-32);
            float height = Mathf.Clamp(_window.height,Mathf.Min(720,Screen.height-32),Screen.height-32);
            _window.width=width;_window.height=height;
            _window.x = Mathf.Clamp(_window.x, 16f, Math.Max(16f, Screen.width - width - 16f));
            _window.y = Mathf.Clamp(_window.y, 16f, Math.Max(16f, Screen.height - height - 16f));
            GUISkin previous = GUI.skin;
            Color previousColor=GUI.color, previousBackground=GUI.backgroundColor, previousContent=GUI.contentColor;
            try
            {
                GUI.color=GUI.backgroundColor=GUI.contentColor=Color.white;
                GUI.skin = _skin;
                _window = GUI.Window(
                    730311, _window, DrawWindow, GUIContent.none, _windowStyle);
            }
            finally
            {
                GUI.skin = previous;
                GUI.color=previousColor;GUI.backgroundColor=previousBackground;GUI.contentColor=previousContent;
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
            _status = global::Runic.Localization.RunicText.Get("text_c242cf73ecfb");
            _control.RequestStatus((ok, document, reason) => Enqueue(() =>
            {
                _requesting = false;
                if (!ok || document == null)
                {
                    _status = global::Runic.Localization.RunicText.Get("text_db62ea842b2c") + reason;
                    Console.instance?.AddString(global::Runic.Localization.RunicText.Get("text_73cc85d79b09") + reason);
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                        global::Runic.Localization.RunicText.Get("text_fdc92c944fa9"));
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
                RunicSentinel.Input.ModalGameplayInput.CaptureOpen();
                RenewCursorLease();
            }));
        }

        private void DrawWindow(int id)
        {
            _hoverText="";
            GUILayout.BeginArea(new Rect(18,14,_window.width-36,142));
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("Runic Sentinel", _title);
            GUILayout.Label(ServerTitle()+" · "+PT("Server administration"), _subtitle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if(GUILayout.Button(PT("Fit screen"),_button,GUILayout.Width(115),GUILayout.Height(34)))
            {_fitWindow=true;}
            if (GUILayout.Button(global::Runic.Localization.RunicText.Get("text_7d9eb7acb13e"), _button, GUILayout.Width(88f), GUILayout.Height(34f)))
                Close();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            string[] tabs = { global::Runic.Localization.RunicText.Get("text_920e413c7d41"), global::Runic.Localization.RunicText.Get("text_ce9c9bfcb7f7"), global::Runic.Localization.RunicText.Get("text_7db20897053b"), global::Runic.Localization.RunicText.Get("text_bda2e3e6c900"), global::Runic.Localization.RunicText.Get("text_af0c3ba6a2ca"), global::Runic.Localization.RunicText.Get("text_04bd3db80e01"), global::Runic.Localization.RunicText.Get("text_b269dc4e81a5"), global::Runic.Localization.RunicText.Get("text_84e12ac655dd") };
            for (int index = 0; index < tabs.Length; index++)
            {
                if (index == 4) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Button(
                        tabs[index],
                        _tab == index ? _selectedTabStyle : _tabStyle,
                        GUILayout.Height(36f)))
                    _tab = index;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.EndVertical();GUILayout.EndArea();
            _bodyHeight=_window.height-294;
            GUILayout.BeginArea(new Rect(18,160,_window.width-36,_window.height-274),_contentStyle);
            if(_tab!=7&&_tab!=6&&_tab!=2)_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tab == 0) DrawStatus();
            else if (_tab == 1) DrawMods();
            else if (_tab == 2) DrawPeople();
            else if (_tab == 3) DrawEnforcement();
            else if (_tab == 4) DrawTools();
            else if (_tab == 5) DrawCapacity();
            else if (_tab == 6) DrawCommands();
            else DrawPlayers();
            if(_tab!=7&&_tab!=6&&_tab!=2)GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(18,_window.height-106,_window.width-36,82),_footerStyle);
            _footerScroll=GUILayout.BeginScrollView(_footerScroll);
            GUILayout.Label(_status ?? string.Empty, _statusStyle);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                global::Runic.Localization.RunicText.Get("text_783e643a93b5"),
                _label);
            GUILayout.FlexibleSpace();
            GUI.enabled = !_requesting && _document.ManagedSigningKey;
            if ((_tab==1||_tab==3) && GUILayout.Button(
                    global::Runic.Localization.RunicText.Get("text_cdc94fbbd3c7"), _button, GUILayout.Width(190f), GUILayout.Height(38f)))
                Apply();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.Label(new Rect(_window.width-27,_window.height-25,24,24),"◢",_label);
            if(Event.current.type==EventType.Repaint&&!string.IsNullOrEmpty(_hoverText))
            {
                var mouse=Event.current.mousePosition;
                var tipStyle=new GUIStyle(_contentStyle){font=_label.font,fontSize=14,wordWrap=true,normal={textColor=Color.white}};
                float tipHeight=Math.Min(_window.height-40,Math.Max(70,tipStyle.CalcHeight(new GUIContent(_hoverText),395)));
                GUI.Box(new Rect(Mathf.Clamp(mouse.x+12,10,_window.width-410),Mathf.Clamp(mouse.y+18,10,_window.height-tipHeight-10),395,tipHeight),_hoverText,tipStyle);
            }
            if(_noCommand)
            {
                var popup=new Rect((_window.width-340)/2,(_window.height-120)/2,340,120);
                GUI.Box(popup,GUIContent.none,_contentStyle);
                GUI.Label(new Rect(popup.x+16,popup.y+16,308,35),PT("No command is selected."),_label);
                if(GUI.Button(new Rect(popup.x+110,popup.y+65,120,34),PT("OK"),_button))_noCommand=false;
            }
            GUI.DragWindow(new Rect(0f, 0f, _window.width - 110f, 58f));
        }

        private void DrawStatus()
        {
            Header(ServerTitle()+" "+PT("Status"));
            GUILayout.Label(PT("A quick health check: who is connected, which world is running, and whether the server is enforcing its configured mod policy."),_label);
            Row(PT("World"),_document.WorldName);Row(PT("Connected players"),_document.OnlinePlayers);Row(PT("Server uptime (days.hours:minutes:seconds)"),_document.ServerUptime);
            if(GUILayout.Button(PT("Refresh server status"),_button))Refresh();
            Row("Administrator access", _document.AdministratorSource);
            if (_document.SetupAvailable)
            {
                GUILayout.Label(global::Runic.Localization.RunicText.Get("text_3a253877c0cc"), _label);
                GUI.enabled = !_requesting;
                if (GUILayout.Button(global::Runic.Localization.RunicText.Get("text_5c1723db193f"), _button, GUILayout.Height(42f)))
                    RunTool("bootstrap");
                GUI.enabled = true;
                GUILayout.Space(8f);
            }
            Row("Signed profile", _document.Profile);
            Row("Policy sequence", _document.Sequence.ToString());
            Row("Runtime integrity", _document.Integrity);
            Row("Admission transport", _document.Status);
            Row("Last admission denial", Empty(_document.LastDenial));
            Row("Server-managed signing key", _document.ManagedSigningKey ? global::Runic.Localization.RunicText.Get("text_43f9b89c0b9d") : global::Runic.Localization.RunicText.Get("text_cff9e8597307"));
            Row("Public-key pin", Empty(_document.SigningKeyPin));
            GUILayout.Space(10f);
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_deb5c0c4ad7f") +
                            global::Runic.Localization.RunicText.Get("text_f1b3b57ae085") +
                            global::Runic.Localization.RunicText.Get("text_e5a33a5d35fa"), _section);
            if (!_document.ManagedSigningKey && !_document.SetupAvailable)
                GUILayout.Label(global::Runic.Localization.RunicText.Get("text_3ebd86d2be3d"), _statusStyle);
        }

        private void DrawMods()
        {
            Header(global::Runic.Localization.RunicText.Get("text_86cfd5006284"));
            LabeledField(global::Runic.Localization.RunicText.Get("text_d3663280e101"), ref _document.Profile);
            GUILayout.Label(PT("Policy label: a name for this server's signed mod rules. It is not your mod-manager profile, server name or world name."),_label);
            LabeledField(global::Runic.Localization.RunicText.Get("text_2b8f0dcb651f"), ref _document.ExpiresUnixSeconds);
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_3f81e2fda079"), _section);
            Choice(ref _document.UnknownMods, global::Runic.Localization.RunicText.Get("text_78342a0905a7"), global::Runic.Localization.RunicText.Get("text_bb132e07e0f3"), global::Runic.Localization.RunicText.Get("text_4b8d64fae5d1"));
            DrawNamedMods();
            if (_advancedMods) {
            PolicyArea(global::Runic.Localization.RunicText.Get("text_23bfd517366c"), ref _document.RequiredMods);
            PolicyArea(global::Runic.Localization.RunicText.Get("text_8f4856f991c3"), ref _document.OptionalMods);
            PolicyArea(global::Runic.Localization.RunicText.Get("text_ba98fbfedc19"), ref _document.GrayListMods);
            PolicyArea(global::Runic.Localization.RunicText.Get("text_4535e1d8f4f6"), ref _document.ForbiddenMods);
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_f38b150c10f1"), _section);
            GUILayout.TextArea(_document.DetectedProfile, _textArea, GUILayout.MinHeight(150f));
            }
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_8a8a65f20f55"), _section);
            GUILayout.TextArea(_document.Modules, _textArea, GUILayout.MinHeight(120f));
        }

        private void DrawPeople() => DrawPlayers();

        private void DrawEnforcement()
        {
            Header(global::Runic.Localization.RunicText.Get("text_4355872828d2"));
            GUILayout.Label(PT("Admission decides whether a client can join. Runtime enforcement decides how repeated rejected requests escalate. These controls do not detect every cheat and do not automatically ban someone for movement observations."),_label);
            Row(PT("Current policy verification"),_document.Integrity);Row(PT("Last rejected connection"),Empty(_document.LastDenial));
            if(GUILayout.Button(PT("Refresh findings"),_button))Refresh();
            GUILayout.Label(PT("Recent findings · UTC time | source | rule | confidence | action"),_section);
            GUILayout.TextArea(string.IsNullOrWhiteSpace(_document.RecentFindings)?PT("No findings in the current server evidence buffer."):_document.RecentFindings,new GUIStyle(_textArea){wordWrap=true,richText=false},GUILayout.MinHeight(100));
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_174e67d083dc"), _section);
            Row("Effective startup value", _document.AdmissionMode);
            GUILayout.Label(
                global::Runic.Localization.RunicText.Get("text_fff2b03ff839"),
                _label);
            LabeledField(global::Runic.Localization.RunicText.Get("text_fda70487ca13"),
                ref _document.IntegritySeconds);
            GUILayout.Label(PT("Seconds between integrity checks. Lower values check more often and cost more work; higher values delay detection of changes. Keep the current value unless measurements justify changing it."),_label);
            LabeledField(global::Runic.Localization.RunicText.Get("text_ce572a1e223c"),
                ref _document.VeryHighThreshold);
            GUILayout.Label(PT("Very-high-confidence rejected requests allowed in the window before disconnect. Lower is stricter; higher gives more tolerance for repeated requests."),_label);
            LabeledField(global::Runic.Localization.RunicText.Get("text_c2c87e10a280"),
                ref _document.HighThreshold);
            GUILayout.Label(PT("High-confidence rejected requests before disconnect. Very-high-confidence findings count here too. Review evidence before making this more aggressive."),_label);
            LabeledField(global::Runic.Localization.RunicText.Get("text_1a20d7b5c8c5"),
                ref _document.EnforcementWindowSeconds);
            GUILayout.Label(PT("How long repeated violations are counted together. A longer window accumulates more violations. Conclusive findings disconnect immediately. The current escalation provider is Runic Portals; this is not a generic suspicion score for every mod."),_label);
            _document.BackupTransitions = GUILayout.Toggle(_document.BackupTransitions,
                global::Runic.Localization.RunicText.Get("text_f0da290b24d3"));
            GUILayout.Space(14f);
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_ca5dbc5182f1") +
                            global::Runic.Localization.RunicText.Get("text_96dcc12cf5ff") +
                            global::Runic.Localization.RunicText.Get("text_8042abad232d"), _section);
        }

        private void DrawTools()
        {
            Header(global::Runic.Localization.RunicText.Get("text_cc258f92057a"));
            Tool(global::Runic.Localization.RunicText.Get("text_4f174908bfdf"), "report",
                global::Runic.Localization.RunicText.Get("text_0c4a58b30ef8"));
            Tool(global::Runic.Localization.RunicText.Get("text_2b739bce943f"), "networks",
                global::Runic.Localization.RunicText.Get("text_c2c69222ec18"));
            Tool(global::Runic.Localization.RunicText.Get("text_de09b77b6298"), "backup",
                global::Runic.Localization.RunicText.Get("text_55edd433edd5"));
            GUILayout.Label(PT("Reports are backed up on the server and downloaded to your machine. They explain coverage, findings and next steps. Network reports inspect existing connections; they do not create or change networks."),_label);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(PT("People & access"),_button))_tab=2;
            if(GUILayout.Button(PT("Inspect server health"),_button))_tab=0;
            if(GUILayout.Button(PT("Prepare world save"),_button)){_commandLine="save";_tab=6;}
            GUILayout.EndHorizontal();DrawReportViewer();
        }

        private void ResetCapacityDraft()
        {
            _capEnabled = _document.CapacitySavedEnabled;
            _capPlayers = _document.CapacitySavedPlayers;
        }

        private void DrawCommands() => DrawCommandsWorkspace();

        private void RunCommand()
        {
            if (_commandBusy || !_open) return;
            if(_commandLine.Contains(";")||_commandLine.Contains("<input")||RunicSentinel.Devcommands.AliasManager.AliasKeys.Contains(_commandLine.Split(' ')[0]))
            {Console.instance.TryRunCommand(_commandLine);return;}
            if(!RunicSentinel.Devcommands.TerminalUtils.SkipProcessing(_commandLine))_commandLine=RunicSentinel.Devcommands.TryRunCommand.CheckLogic(_commandLine);
            if (!SentinelCommandProvider.Available(out string provider))
            { SentinelCommandConsole.Write(provider); return; }
            string executionLine=_commandLine.StartsWith("server ",StringComparison.OrdinalIgnoreCase)?_commandLine.Substring(7).Trim():_commandLine;
            var selectedHandler=SentinelCommandConsole.Catalog().FirstOrDefault(c=>c.Command.Equals(executionLine.Split(' ')[0],StringComparison.OrdinalIgnoreCase));
            if(SentinelCommandProvider.ExecutesOnServer(_commandLine)&&selectedHandler?.IsCheat==true&&!Achievements.IsCheatedAtAll())
            {SentinelCommandConsole.Write(PT("Run confirmcheats explicitly before using cheat commands. Valheim permanently marks this character as having used cheats."));return;}
            if(SentinelCommandRequest.TryNormalize(_commandLine,out string history))
            {if(_commandHistory.Count==0||_commandHistory[_commandHistory.Count-1]!=history)_commandHistory.Add(history);if(_commandHistory.Count>50)_commandHistory.RemoveAt(0);_commandHistoryIndex=_commandHistory.Count;}
            ZNet network = ZNet.instance;
            ZRpc serverRpc = network != null && !network.IsServer() ? network.GetServerPeer()?.m_rpc : null;
            _commandBusy = true;
            _status = global::Runic.Localization.RunicText.Get("text_d7b616054795");
            string route="";
            SentinelCommandRequest.Dispatch(_commandLine,
                (command, callback) => _control.AuthorizeCommand(command,
                    (accepted, reason) => Enqueue(() => {route=reason;callback(accepted,reason);})),
                () => _open && network != null && ReferenceEquals(network, ZNet.instance) &&
                      (network.IsServer() || ReferenceEquals(serverRpc, network.GetServerPeer()?.m_rpc)),
                command=>
                {
                    if(route==SentinelCommandProvider.ClientRoute)
                    {
                        bool beforeDebug=Player.m_debugMode;
                        bool beforeNoCost=Player.m_localPlayer!=null&&Player.m_localPlayer.NoCostCheat();
                        bool beforeFlight=Player.m_localPlayer!=null&&Player.m_localPlayer.IsDebugFlying();
                        SentinelCommandConsole.Execute(command);
                        string commandName=command.Split(' ')[0].ToLowerInvariant();
                        if(commandName=="debugmode"||commandName=="nocost"||commandName=="fly")
                        {
                            if(commandName=="debugmode")_ownsDebugMode=Player.m_debugMode&&(_ownsDebugMode||!beforeDebug);
                            if(commandName=="nocost")_ownsNoCost=Player.m_localPlayer.NoCostCheat()&&(_ownsNoCost||!beforeNoCost);
                            if(commandName=="fly")_ownsFlight=Player.m_localPlayer.IsDebugFlying()&&(_ownsFlight||!beforeFlight);
                            _sentinelModesActive=_ownsDebugMode||_ownsNoCost||_ownsFlight;_modeNetwork=ZNet.instance;_modePlayer=Player.m_localPlayer;_nextModeLease=Time.realtimeSinceStartup+10;
                        }
                    }
                    else if(route.StartsWith(SentinelCommandProvider.ServerRoute,StringComparison.Ordinal))
                    {SentinelCommandConsole.Write("> "+command);SentinelCommandConsole.Write(route.Substring(SentinelCommandProvider.ServerRoute.Length));}
                    else throw new InvalidOperationException("The server did not return a compatible Sentinel command result. Update both Sentinel packages.");
                },
                (accepted, reason) =>
                {
                    _commandBusy = false;
                    _status = reason;
                    SentinelCommandConsole.Write(reason);
                });
        }
        private string ServerTitle()=>!string.IsNullOrWhiteSpace(_document?.ServerName)?_document.ServerName:!string.IsNullOrWhiteSpace(_document?.WorldName)?_document.WorldName:PT("Server");

        private void TickCommandLease()
        {
            if(!_sentinelModesActive)return;
            if(_modeNetwork==null||!ReferenceEquals(_modeNetwork,ZNet.instance)||_modePlayer==null||_modePlayer!=Player.m_localPlayer)
            {EndCommandLease();return;}
            if(_modeLeasePending||Time.realtimeSinceStartup<_nextModeLease)return;
            _modeLeasePending=true;_nextModeLease=Time.realtimeSinceStartup+10;
            _control.RequestStatus((ok,document,reason)=>Enqueue(()=>
            {_modeLeasePending=false;if(!ok)EndCommandLease();}));
        }
        private void EndCommandLease()
        {
            _sentinelModesActive=false;if(_ownsDebugMode)Player.m_debugMode=false;
            if(_modePlayer!=null)
            {if(_ownsNoCost)_modePlayer.SetNoPlacementCost(false);if(_ownsFlight&&_modePlayer.IsDebugFlying())_modePlayer.ToggleDebugFly();}
            _ownsDebugMode=_ownsNoCost=_ownsFlight=false;
            _modePlayer=null;_modeNetwork=null;
        }

        private void DrawCapacity()
        {
            Header(global::Runic.Localization.RunicText.Get("text_07b97caf5500"));
            if (!_document.CapacitySupported)
            {
                GUILayout.Label(global::Runic.Localization.RunicText.Get("text_b4318c5f814c"), _label);
            }
            else
            {
                GUILayout.Label(_document.CapacityStatus, _label);
                if (_document.CapacityAvailable)
                {
                    Row("Host World Engine", _document.CapacityVersion);
                    Row("Players currently connected", _document.CapacityCurrentPlayers);
                    Row("Running player cap", _document.CapacityActivePlayers);
                    Row("Validated running override", _document.CapacityActiveEnabled ? global::Runic.Localization.RunicText.Get("text_130011756125") : global::Runic.Localization.RunicText.Get("text_9ffabc8bbeb9"));
                    Row("Saved for next startup", _document.CapacitySavedEnabled ? _document.CapacitySavedPlayers + global::Runic.Localization.RunicText.Get("text_72fa4e4815f0") : global::Runic.Localization.RunicText.Get("text_8cb8bdc66109"));
                    Row("Restart status", _document.CapacityRestartRequired ? global::Runic.Localization.RunicText.Get("text_5d5dd678d836") : global::Runic.Localization.RunicText.Get("text_f13b36899a76"));
                    GUILayout.Space(12f);
                    GUI.enabled = !_requesting;
                    _capEnabled = GUILayout.Toggle(_capEnabled, global::Runic.Localization.RunicText.Get("text_5c3a1a8fd7fe"));
                    GUILayout.Label(global::Runic.Localization.RunicText.Get("text_10d70ff390b0"), _section);
                    _capPlayers = GUILayout.TextField(_capPlayers ?? "", 2, _textField, GUILayout.Width(110f), GUILayout.Height(32f));
                    bool valid = SentinelCapacitySettings.TryPlayers(_capPlayers, out int players);
                    if (!valid) GUILayout.Label(global::Runic.Localization.RunicText.Get("text_40ee3533484e"), _statusStyle);
                    if (_capEnabled && valid && int.TryParse(_document.CapacityCurrentPlayers, out int current) && players < current)
                        GUILayout.Label(global::Runic.Localization.RunicText.Get("text_349e2d3e9f1f"), _statusStyle);
                    bool changed = _capEnabled != _document.CapacitySavedEnabled || _capPlayers != _document.CapacitySavedPlayers;
                    GUI.enabled = !_requesting && valid && changed;
                    if (GUILayout.Button(global::Runic.Localization.RunicText.Get("text_268393176bda"), _button, GUILayout.Width(250f), GUILayout.Height(40f))) SaveCapacity(players);
                    GUI.enabled = true;
                    GUILayout.Space(8f);
                    GUILayout.Label(global::Runic.Localization.RunicText.Get("text_306da1e9a5bf"), _label);
                    GUILayout.Label(global::Runic.Localization.RunicText.Get("text_fd0ba8053f2e"), _label);
                }
            }
            GUILayout.Space(12f);
            GUI.enabled = !_requesting;
            if (GUILayout.Button(global::Runic.Localization.RunicText.Get("text_1b9fe32a224e"), _button, GUILayout.Width(330f), GUILayout.Height(36f))) Refresh();
            GUI.enabled = true;
        }

        private void SaveCapacity(int players)
        {
            _requesting = true;
            _status = global::Runic.Localization.RunicText.Get("text_36e9cfca1d27");
            _control.SaveCapacity(_document.CapacityRevision, _capEnabled, players, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? global::Runic.Localization.RunicText.Get("text_4d9f3906b5b2") : global::Runic.Localization.RunicText.Get("text_9ee721835d15")) + message;
                if (ok) Refresh();
            }));
        }

        private void Tool(string label, string tool, string explanation)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = !_requesting;
            if (GUILayout.Button(label, new GUIStyle(_button){wordWrap=true}, GUILayout.Width(Math.Min(390,_window.width*.42f)), GUILayout.Height(52f)))
                RunTool(tool);
            GUI.enabled = true;
            GUILayout.Label(explanation, _label, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void Apply()
        {
            _requesting = true;
            _status = global::Runic.Localization.RunicText.Get("text_b32435fa642e");
            _control.Apply(_document, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? global::Runic.Localization.RunicText.Get("text_ff068f9c8efc") : global::Runic.Localization.RunicText.Get("text_9ee721835d15")) + message;
                if (ok) Refresh();
            }));
        }

        private void RunTool(string tool)
        {
            _requesting = true;
            _status = tool == "bootstrap" ? global::Runic.Localization.RunicText.Get("text_150b3572d136") : global::Runic.Localization.RunicText.Get("text_1754c1cbd234");
            _control.RunTool(tool, (ok, message) => Enqueue(() =>
            {
                _requesting = false;
                _status = (ok ? global::Runic.Localization.RunicText.Get("text_ff068f9c8efc") : global::Runic.Localization.RunicText.Get("text_c5bc264875d7")) + message;
                if(ok&&(tool=="report"||tool=="networks")){ReceiveToolReport(message);return;}
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
                else _status = global::Runic.Localization.RunicText.Get("text_2cb29d4771e2") + reason;
            }));
        }

        private void Close()
        {
            if (!_open) return;
            RunicSentinel.Input.ModalGameplayInput.CaptureOpen();
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
            ApplyVanillaMenuAssets();
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
        private static string Empty(string value) => string.IsNullOrEmpty(value) ? global::Runic.Localization.RunicText.Get("text_dc937b598926") : value;
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
            EndCommandLease();
            Close();
            SentinelCommandConsole.Reset();
            if (ReferenceEquals(_active, this)) _active = null;
            RunicSentinel.Input.ModalGameplayInput.Reset();
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
