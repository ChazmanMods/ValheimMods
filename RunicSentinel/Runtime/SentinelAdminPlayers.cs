using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private string _selectedPlayer="", _playerSearch="", _objectSearch="", _playerError="", _detailError="", _reportStatus="";
        private SentinelPlayerList _playerList;
        private SentinelPlayerReport _playerDetail, _playerReport;
        private SentinelAdminDocument _playersContext;
        private bool _playerBusy, _playerListBusy, _playerDetailBusy, _showPlayerIdentity;
        private float _nextPlayerRefresh, _nextDetailRefresh;
        private Vector2 _playerRosterScroll, _playerDetailScroll;
        private int _reportChoice, _fileChoice, _objectPage;
        private int _peopleFilter;
        private bool _personBusy;
        private readonly HashSet<string> _expandedObjects=new HashSet<string>();
        private readonly Dictionary<string,SentinelContentEntry> _objectCatalog=new Dictionary<string,SentinelContentEntry>();
        private GUIStyle _playerNameStyle, _rosterStyle, _rosterSelectedStyle;
        private static string PT(string text)=>global::Runic.Localization.RunicText.TranslateEnglish(text);

        private void TickPlayers()
        {
            if(!_open || (_tab!=7&&_tab!=2&&_tab!=6) || _document==null)return;
            if(!ReferenceEquals(_playersContext,_document))
            {
                _playersContext=_document;_playerList=null;_playerDetail=null;_playerReport=null;
                _playerError="";_detailError="";_reportStatus="";_nextPlayerRefresh=0;_nextDetailRefresh=0;
                if(!string.IsNullOrEmpty(_document.Players))AcceptPlayerList(_document.Players);
            }
            if(!_playerListBusy && Time.realtimeSinceStartup>=_nextPlayerRefresh)RefreshPlayerList();
            if((_tab==2||_tab==7)&&!_playerDetailBusy && !string.IsNullOrEmpty(_selectedPlayer) && Time.realtimeSinceStartup>=_nextDetailRefresh&&(_playerList?.players.Any(p=>p.account==_selectedPlayer&&!string.IsNullOrEmpty(p.observedUtc))??false))
            {
                _playerDetailBusy=true;_nextDetailRefresh=Time.realtimeSinceStartup+10;
                var document=_document;string account=_selectedPlayer;
                _control.RequestPlayerDetail(account,(ok,result)=>Enqueue(()=>
                {
                    _playerDetailBusy=false;
                    if(!ReferenceEquals(document,_document)||account!=_selectedPlayer)return;
                    if(!ok){_detailError=result;return;}
                    try
                    {
                        var report=SentinelJson.Read<SentinelPlayerReport>(result);
                        if(report?.player?.account!=account)throw new FormatException();
                        _playerDetail=report;_detailError="";
                    }
                    catch{_detailError=PT("The server returned unreadable player details. Retry refresh.");}
                }));
            }
        }
        private void AcceptPlayerList(string json)
        {
            try
            {
                var list=SentinelJson.Read<SentinelPlayerList>(json);
                if(list?.players==null)throw new FormatException();
                list.players=list.players.Where(p=>p!=null&&!string.IsNullOrEmpty(p.account)).ToArray();
                _playerList=list;_playerError="";
                if(!list.players.Any(p=>p.account==_selectedPlayer))SelectPlayer(list.players.FirstOrDefault()?.account??"");
            }
            catch(Exception error){Debug.LogWarning("Sentinel player-list decode failed: "+error.GetType().Name+"; characters="+(json?.Length??0));_playerError=PT("Could not decode the server roster. Both client and server need this dashboard update. See the client log for the decode error.");}
        }
        private void SelectPlayer(string account)
        {
            _selectedPlayer=account;_playerDetail=null;_playerReport=null;_detailError="";_reportStatus="";
            _nextDetailRefresh=0;_playerDetailScroll=Vector2.zero;_objectPage=0;_objectSearch="";_expandedObjects.Clear();
        }
        private void RefreshPlayerList()
        {
            if(_playerListBusy)return;
            _playerListBusy=true;_nextPlayerRefresh=Time.realtimeSinceStartup+10;
            var document=_document;
            _control.RequestPlayers((ok,result)=>Enqueue(()=>
            {
                _playerListBusy=false;
                if(!ReferenceEquals(document,_document))return;
                if(ok){_document.Players=result;AcceptPlayerList(result);}else _playerError=result;
            }));
        }
        private void GeneratePlayerReport(string kind)
        {
            if(_playerBusy||string.IsNullOrEmpty(_selectedPlayer))return;
            string account=_selectedPlayer;var document=_document;_playerBusy=true;_reportStatus=PT("Generating report on the server…");
            _control.RequestPlayerReport(account+"\n"+kind,(ok,result)=>Enqueue(()=>
            {
                _playerBusy=false;
                if(!ReferenceEquals(document,_document)||account!=_selectedPlayer)return;
                if(!ok){_reportStatus=result;return;}
                try
                {
                    var report=SentinelJson.Read<SentinelPlayerReport>(result);
                    if(report?.player?.account!=account)throw new FormatException();
                    _playerReport=report;_objectPage=0;_reportStatus=PT("JSON, CSV and TXT files created on the server.");
                    _objectCatalog.Clear();
                    foreach(var entry in SentinelContentCatalog.Read())if(!_objectCatalog.ContainsKey(entry.Id))_objectCatalog.Add(entry.Id,entry);
                }
                catch{_reportStatus=PT("The server returned an unreadable report. Retry generation.");}
            }));
        }
        private void DrawPlayers()
        {
            if(_playerNameStyle==null)
            {
                _playerNameStyle=new GUIStyle(_heading){fontSize=28,richText=false};
                _rosterStyle=new GUIStyle(_button){alignment=TextAnchor.MiddleLeft,wordWrap=true,richText=false,fontSize=15,padding=new RectOffset(14,10,10,10)};
                _rosterSelectedStyle=new GUIStyle(_selectedTabStyle){alignment=TextAnchor.MiddleLeft,wordWrap=true,richText=false,fontSize=15,padding=new RectOffset(14,10,10,10)};
            }
            var players=_playerList?.players??Array.Empty<SentinelPlayerRecord>();
            GUILayout.BeginHorizontal();GUILayout.Label(_tab==2?PT("People directory"):PT("Players"),_heading);GUILayout.FlexibleSpace();
            GUILayout.Label(players.Count(p=>p.online)+" "+PT("online")+"  /  "+players.Length+" "+PT("observed"),_label);
            GUI.enabled=!_playerListBusy;
            if(GUILayout.Button(PT("Refresh"),_button,GUILayout.Width(100))){_nextDetailRefresh=0;RefreshPlayerList();}
            GUI.enabled=true;GUILayout.EndHorizontal();
            if(!string.IsNullOrEmpty(_playerError))GUILayout.Label(_playerError,_label);
            if(_tab==2)_peopleFilter=GUILayout.SelectionGrid(_peopleFilter,new[]{PT("All People"),PT("Online"),PT("Administrators"),PT("Banned")},4,_tabStyle);
            GUILayout.BeginHorizontal();
            float errorHeight=string.IsNullOrEmpty(_playerError)?0:_label.CalcHeight(new GUIContent(_playerError),_window.width-76)+6;
            float bodyHeight=Math.Max(100,_bodyHeight-(_tab==2?85:48)-errorHeight);
            GUILayout.BeginVertical(_row,GUILayout.Width(235),GUILayout.Height(bodyHeight));
            GUILayout.Label(PT("PLAYERS"),_section);GUILayout.Label(PT("Search by player name"),_label);
            GUILayout.BeginHorizontal();_playerSearch=GUILayout.TextField(_playerSearch,128,_textField);
            if(SearchIconButton())GUI.FocusControl(null);GUILayout.EndHorizontal();
            _playerRosterScroll=GUILayout.BeginScrollView(_playerRosterScroll);
            var visiblePlayers=players.Where(p=>(_tab==7?p.online:((_peopleFilter==0)||(_peopleFilter==1&&p.online)||(_peopleFilter==2&&p.administrator)||(_peopleFilter==3&&p.banned)))&&((p.name??"").IndexOf(_playerSearch,StringComparison.OrdinalIgnoreCase)>=0||p.account.IndexOf(_playerSearch,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            foreach(var p in visiblePlayers)
                if(GUILayout.Button((p.name??PT("Unknown player"))+(p.administrator?" - "+PT("Administrator"):"")+"\n"+(p.online?PT("Online"):PT("Offline"))+" · "+ActivityTitle(p),p.account==_selectedPlayer?_rosterSelectedStyle:_rosterStyle,GUILayout.Height(76)))SelectPlayer(p.account);
            if(visiblePlayers.Length==0)GUILayout.Label(_playerListBusy?PT("Loading server observations…"):PT(_tab==7?"No online players match your search.":"No people match this filter and search."),_label);
            GUILayout.EndScrollView();GUILayout.Label(PT("Updated")+" "+TimeLabel(_playerList?.generatedUtc),_label);
            GUILayout.Label(PT("Automatically refreshes every 10 seconds."),_label);GUILayout.EndVertical();
            GUILayout.Space(12);
            _playerDetailScroll=GUILayout.BeginScrollView(_playerDetailScroll,GUILayout.Height(bodyHeight));
            var selected=visiblePlayers.FirstOrDefault(p=>p.account==_selectedPlayer);
            if(selected==null)
            {
                GUILayout.Label(PT("Player dossier"),_playerNameStyle);
                GUILayout.Label(PT(_tab==2?"Select a person to manage access, view recent activity and create reports.":"Select an online player to view their current activity, location and gameplay commands."),_label);
            }
            else DrawPlayerDossier(selected);
            GUILayout.EndScrollView();GUILayout.EndHorizontal();
        }
        private void DrawPlayerDossier(SentinelPlayerRecord p)
        {
            GUILayout.BeginVertical(_row);
            GUILayout.Label((p.name??"")+(p.administrator?" - "+PT("Administrator"):""),_playerNameStyle);
            GUILayout.Label((p.online?PT("Online"):PT("Offline · last known information"))+"  ·  "+PT("Session started")+" "+TimeLabel(p.joinedUtc),_label);
            GUILayout.EndVertical();GUILayout.Space(8);
            GUILayout.BeginHorizontal();GUI.enabled=!_personBusy;
            if(_tab==2)
            {
                if(GUILayout.Button(PT(p.administrator?"Revoke Administrator Rights":"Grant Administrator Rights"),new GUIStyle(_button){wordWrap=true},GUILayout.Height(48)))ChangePerson("admin",p.account,!p.administrator);
                if(GUILayout.Button(PT(p.banned?"Unban From Server":"Ban From Server"),new GUIStyle(_button){wordWrap=true},GUILayout.Height(48)))ChangePerson("ban",p.account,!p.banned);
            }
            GUI.enabled=!_personBusy&&p.online;
            if(GUILayout.Button(PT("Kick From Server"),_button,GUILayout.Height(34)))ChangePerson("kick",p.account,true);
            GUI.enabled=true;GUILayout.EndHorizontal();
            if(_tab==2)
            {
            GUILayout.Label(PT("Administrator sources")+": "+(p.nativeAdministrator?"adminlist.txt ":"")+(p.signedAdministrator?PT("Signed Sentinel policy"):"")+(p.administrator?"":PT("None")),_label);
            if(p.banned)GUILayout.Label(PT("Ban sources")+": "+(p.nativeBanned?"bannedlist.txt ":"")+(p.signedBanned?PT("Signed Sentinel policy"):""),_label);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(PT("Copy account ID"),_button))GUIUtility.systemCopyBuffer=p.account;
            if(GUILayout.Button(PT("Identity details"),_button))_showPlayerIdentity=!_showPlayerIdentity;
            GUILayout.EndHorizontal();
            if(_showPlayerIdentity){GUILayout.Label(PT("Account")+": "+p.account,_label);GUILayout.Label(PT("Character")+": "+p.characterId,_label);}
            }
            if(string.IsNullOrEmpty(p.observedUtc))
            {GUILayout.Label(PT("This account is in the server's access lists. No character activity or location has been observed in this server session."),_label);if(_tab==2)DrawPlayerReportFiles();return;}
            if(_tab==7)
            {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(_row,GUILayout.MinHeight(100));GUILayout.Label(PT("CURRENT ACTIVITY"),_section);
            GUILayout.Label(ActivityTitle(p),_heading);GUILayout.Label(PT("Movement inferred from five-second position samples."),_label);GUILayout.EndVertical();
            GUILayout.BeginVertical(_row,GUILayout.MinHeight(100));GUILayout.Label(PT("LOCATION"),_section);
            GUILayout.Label(string.IsNullOrEmpty(p.location)?PT("Unknown biome"):p.location,_heading);
            GUILayout.Label(Coordinates(p.position),_label);GUILayout.Label(PT("Observed")+" "+TimeLabel(p.observedUtc),_label);GUILayout.EndVertical();GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(PT("Copy coordinates"),_button))GUIUtility.systemCopyBuffer=Coordinates(p.position);
            GUILayout.EndHorizontal();
            DrawPlayerCommandTools(p);
            return;
            }
            GUILayout.Space(10);GUILayout.Label(PT("RECENT ACTIVITY"),_section);
            if(!string.IsNullOrEmpty(_detailError))GUILayout.Label(_detailError,_label);
            var activity=_playerDetail?.activity??Array.Empty<SentinelActivityRecord>();
            foreach(var item in activity.Reverse().Take(5))
            {GUILayout.BeginHorizontal(_row);GUILayout.Label(TimeLabel(item.utc),_value,GUILayout.Width(82));GUILayout.Label(item.detail,_label);GUILayout.EndHorizontal();}
            if(activity.Length==0)GUILayout.Label(_playerDetailBusy?PT("Loading activity…"):PT("No recorded activity available."),_label);
            GUILayout.Space(12);GUI.enabled=!_playerBusy;
            if(GUILayout.Button(PT("Create a list of objects created by this player"),_selectedTabStyle,GUILayout.Height(42)))GeneratePlayerReport("objects");
            GUI.enabled=true;
            if(_playerReport!=null&&(_playerReport.reportKind=="objects"||_playerReport.reportKind=="all"))DrawPlayerObjects();
            GUILayout.Space(12);DrawPlayerReportFiles();
        }
        private void DrawPlayerObjects()
        {
            GUILayout.Label(PT("CREATIONS"),_section);
            GUILayout.Label(_playerReport.matchingObjects+" "+PT("matching objects at scan time")+" · "+_playerReport.inspectedObjects+" "+PT("world objects inspected"),_label);
            GUILayout.Label(PT("Creator metadata indicates attribution, not verified account authorship."),_label);
            GUILayout.Label(PT("Search displayed objects"),_label);
            string search=GUILayout.TextField(_objectSearch,128,_textField);if(search!=_objectSearch){_objectSearch=search;_objectPage=0;}
            var objects=(_playerReport.objects??Array.Empty<SentinelObjectRecord>()).Where(o=>ObjectName(o).IndexOf(_objectSearch,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Math.Max(1,(objects.Length+5)/6);_objectPage=Math.Min(_objectPage,pages-1);
            foreach(var obj in objects.Skip(_objectPage*6).Take(6))
            {
                _objectCatalog.TryGetValue(obj.prefab??"",out var entry);
                GUILayout.BeginVertical(_row);GUILayout.BeginHorizontal();
                Rect icon=GUILayoutUtility.GetRect(42,42,GUILayout.Width(42),GUILayout.Height(42));
                if(entry?.Icon!=null)
                {
                    var sprite=entry.Icon;var rect=sprite.textureRect;var tex=sprite.texture;
                    GUI.DrawTextureWithTexCoords(icon,tex,new Rect(rect.x/tex.width,rect.y/tex.height,rect.width/tex.width,rect.height/tex.height));
                }
                else GUI.Label(icon,"◇",_heading);
                GUILayout.BeginVertical();GUILayout.Label(ObjectName(obj),_section);GUILayout.Label((entry?.Mod??PT("Unresolved mod"))+" · "+PT("Present at scan"),_label);GUILayout.EndVertical();
                if(GUILayout.Button(PT("Details"),_button,GUILayout.Width(80)))if(!_expandedObjects.Remove(obj.id))_expandedObjects.Add(obj.id);
                GUILayout.EndHorizontal();
                if(_expandedObjects.Contains(obj.id)){GUILayout.Label(obj.prefab+" · "+obj.id,_label);GUILayout.Label(Coordinates(obj.position)+"\n"+obj.attribution,_label);}
                GUILayout.EndVertical();
            }
            GUILayout.BeginHorizontal();GUI.enabled=_objectPage>0;if(GUILayout.Button(PT("Previous"),_button))_objectPage--;
            GUI.enabled=true;GUILayout.Label((_objectPage+1)+" / "+pages,_label);GUI.enabled=_objectPage+1<pages;if(GUILayout.Button(PT("Next"),_button))_objectPage++;GUI.enabled=true;GUILayout.EndHorizontal();
            GUILayout.Label(PT("Dashboard preview: up to 24 objects. Server files contain up to 2,000 matches."),_label);
            if(_playerReport.truncated)GUILayout.Label(PT("Scan or export limit reached; this is a partial list."),_label);
        }
        private string ObjectName(SentinelObjectRecord obj)=>_objectCatalog.TryGetValue(obj.prefab??"",out var entry)?entry.Name:obj.prefab??PT("Unknown object");
        private void DrawPlayerReportFiles()
        {
            GUILayout.BeginVertical(_row);GUILayout.Label(PT("PLAYER REPORTS"),_section);
            _reportChoice=GUILayout.SelectionGrid(_reportChoice,new[]{PT("Full report"),PT("Objects"),PT("Activity history"),PT("Current snapshot")},2,_tabStyle);
            GUI.enabled=!_playerBusy;
            if(GUILayout.Button(PT("Generate JSON, CSV and TXT files"),_button))GeneratePlayerReport(new[]{"all","objects","activity","snapshot"}[_reportChoice]);
            GUI.enabled=true;if(!string.IsNullOrEmpty(_reportStatus))GUILayout.Label(_reportStatus,_label);
            if(_playerReport!=null)
            {
                GUILayout.Label(PT("Server files")+": "+_playerReport.files,_label);
                _fileChoice=GUILayout.SelectionGrid(_fileChoice,new[]{"JSON","CSV","TXT"},3,_tabStyle);
                string preview=PlayerReportPreview();
                GUILayout.Label(PT("Local preview contains only the records received by this dashboard."),_label);
                GUILayout.TextArea(preview.Length>5000?preview.Substring(0,5000):preview,new GUIStyle(_textArea){wordWrap=true},GUILayout.Height(130));
                if(GUILayout.Button(PT("Save displayed report locally"),_button))
                {
                    try
                    {
                        string directory=Path.Combine(Paths.ConfigPath,"RunicSentinel","PlayerReportPreviews");Directory.CreateDirectory(directory);
                        string path=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+new[]{".json",".csv",".txt"}[_fileChoice]);
                        File.WriteAllText(path,preview);_reportStatus=PT("Local preview saved")+": "+path;
                    }
                    catch(Exception e){_reportStatus=PT("Could not save preview")+": "+e.Message;}
                }
                GUILayout.Label(_playerReport.limitations,_label);
            }
            GUILayout.EndVertical();
        }
        private string PlayerReportPreview()
        {
            if(_fileChoice==0)return SentinelJson.Write(_playerReport,true);
            if(_fileChoice==1)
                return "record,name,detail,x,y,z\n"+string.Join("\n",(_playerReport.objects??Array.Empty<SentinelObjectRecord>()).Select(o=>"object,"+CsvCell(o.prefab)+","+CsvCell(o.id)+","+string.Format(CultureInfo.InvariantCulture,"{0},{1},{2}",o.position.x,o.position.y,o.position.z)))+"\n"+string.Join("\n",(_playerReport.activity??Array.Empty<SentinelActivityRecord>()).Select(a=>"activity,"+CsvCell(a.utc)+","+CsvCell(a.detail)+",,,"));
            return (_playerReport.player.name??"")+"\n"+_playerReport.generatedUtc+"\n"+Coordinates(_playerReport.player.position)+"\n\n"+string.Join("\n",(_playerReport.activity??Array.Empty<SentinelActivityRecord>()).Select(a=>a.utc+"  "+a.detail))+"\n"+string.Join("\n",(_playerReport.objects??Array.Empty<SentinelObjectRecord>()).Select(o=>o.prefab+"  "+Coordinates(o.position)))+"\n\n"+_playerReport.limitations;
        }
        private static string CsvCell(string value){value=(value??"").Replace("\r"," ").Replace("\n"," ");if(value.Length>0&&"=+-@".IndexOf(value[0])>=0)value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
        private static string Coordinates(Vector3 p)=>string.Format(CultureInfo.InvariantCulture,"X {0:0.0}   Z {1:0.0}   Y {2:0.0}",p.x,p.z,p.y);
        private static string TimeLabel(string value)=>DateTime.TryParse(value,null,DateTimeStyles.RoundtripKind,out var time)?time.ToLocalTime().ToString("HH:mm:ss"):"—";
        private static string ActivityTitle(SentinelPlayerRecord p)=>!p.online?PT("Offline"):(p.activity??"").StartsWith("Moving",StringComparison.Ordinal)?PT("Moving"):(p.activity??"").StartsWith("Stationary",StringComparison.Ordinal)?PT("Stationary"):PT("Connected");
        private void ChangePerson(string action,string account,bool enabled)
        {
            if(_personBusy)return;_personBusy=true;
            _control.ChangePerson(action,account.Trim(),enabled,(ok,message)=>Enqueue(()=>
            {_personBusy=false;_status=message;if(ok){Refresh();_nextDetailRefresh=0;}}));
        }
    }
}
