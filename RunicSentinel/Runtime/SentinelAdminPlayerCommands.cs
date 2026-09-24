using System;
using System.Globalization;
using System.Linq;
using RunicSentinel.PlayerActions;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private string _skill="Run",_skillFilter="",_skillAmount="1",_playerActionMessage="",_playerActionId="";
        private string _teleportX="",_teleportY="",_teleportZ="",_teleportAccount="",_adrenaline="100",_statusEffect="",_statusFilter="";
        private bool _playerActionBusy,_playerActionPolling,_showSkills,_showDestinations,_showStatuses;
        private int _skillPage,_statusPage;
        private float _playerActionNextPoll;
        private ZNet _playerActionNetwork;
        private ZRpc _playerActionServer;
        private void DrawPlayerCommandTools(SentinelPlayerRecord player)
        {
            GUILayout.Space(8);GUILayout.BeginVertical(_row);
            GUILayout.Label(PT("PLAYER COMMANDS")+" · "+player.name,_section);
            GUILayout.Label(PT("Every action below targets this selected character. Results appear here; there is no need to switch to Commands."),_label);
            GUI.enabled=player.online&&!_playerActionBusy;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(new GUIContent(PT("Teleport to player"),PT("Move your administrator character to the selected player's current server position.")),_button))
            {
                var self=OwnPlayer();if(self==null)_playerActionMessage=PT("Your character is not in the latest roster. Refresh Players first.");
                else PlayerTeleport("tp "+self.account+" | "+player.account);
            }
            if(GUILayout.Button(new GUIContent(PT("Bring to me"),PT("Move the selected player to your current server position.")),_button))
            {
                var self=OwnPlayer();if(self==null)_playerActionMessage=PT("Your character is not in the latest roster. Refresh Players first.");
                else PlayerTeleport("tp "+player.account+" | "+self.account);
            }
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            GUILayout.Label("X",_label,GUILayout.Width(18));_teleportX=GUILayout.TextField(_teleportX,20,_textField);
            GUILayout.Label("Y",_label,GUILayout.Width(18));_teleportY=GUILayout.TextField(_teleportY,20,_textField);
            GUILayout.Label("Z",_label,GUILayout.Width(18));_teleportZ=GUILayout.TextField(_teleportZ,20,_textField);
            GUILayout.EndHorizontal();
            if(GUILayout.Button(PT("Teleport selected player to coordinates"),_button))PlayerTeleport("tp "+player.account+" | "+_teleportX+","+_teleportZ+","+_teleportY);
            if(GUILayout.Button(PT("Choose destination player"),_button))_showDestinations=!_showDestinations;
            if(_showDestinations)foreach(var destination in (_playerList?.players??Array.Empty<SentinelPlayerRecord>()).Where(p=>p.online))
                if(GUILayout.Button(destination.name+" · "+destination.account,destination.account==_teleportAccount?_selectedTabStyle:_tabStyle)){_teleportAccount=destination.account;_showDestinations=false;}
            if(!string.IsNullOrEmpty(_teleportAccount)&&GUILayout.Button(PT("Teleport selected player to")+" "+((_playerList?.players.FirstOrDefault(p=>p.account==_teleportAccount)?.name)??_teleportAccount),_button))PlayerTeleport("tp "+player.account+" | "+_teleportAccount);
            GUI.enabled=true;GUILayout.Space(8);
            if(!player.playerActionsReady)GUILayout.Label(PT("Character actions require this player's updated Sentinel client. Teleport works through the native server connection."),_label);
            GUILayout.Label(PT("Client-side cheat actions require the affected player's own confirmcheats acknowledgment. Skill changes are saved with their character; they are not temporary session bonuses."),_label);
            GUI.enabled=player.online&&player.playerActionsReady&&!_playerActionBusy;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(PT("Skill")+": "+_skill,_button))_showSkills=!_showSkills;
            GUILayout.Label(PT("Change"),_label,GUILayout.Width(65));_skillAmount=GUILayout.TextField(_skillAmount,5,_textField,GUILayout.Width(65));
            GUILayout.EndHorizontal();
            if(_showSkills)
            {
                string filter=GUILayout.TextField(_skillFilter,64,_textField);if(filter!=_skillFilter){_skillFilter=filter;_skillPage=0;}
                var skills=PlayerActionProtocol.SkillsList().Where(s=>s.IndexOf(_skillFilter,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                // Keep these controls within the existing details scroll area.
                foreach(var skill in skills.Skip(_skillPage*12).Take(12))
                    if(GUILayout.Button(new GUIContent(skill,skill=="All"?PT("Applies to every native skill."):PT("Select this skill for the selected character.")),_tabStyle)){_skill=skill;_showSkills=false;}
                if(skills.Length>12){GUILayout.BeginHorizontal();if(GUILayout.Button(PT("Previous"),_button))_skillPage=Math.Max(0,_skillPage-1);if(GUILayout.Button(PT("Next"),_button))_skillPage=Math.Min((skills.Length-1)/12,_skillPage+1);GUILayout.EndHorizontal();}
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(new GUIContent(PT("Raise / lower skill"),PT("Adds the signed amount, from -100 to 100. Native skill levels clamp to 0–100 and native skill-cap rules apply.")),_button))PlayerAction(player,"raiseskill",_skill+" "+_skillAmount);
            if(GUILayout.Button(new GUIContent(PT("Reset skill"),PT("Resets the chosen skill to zero. Choosing All resets all native skills.")),_button))PlayerAction(player,"resetskill",_skill);
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            if(GUILayout.Button(new GUIContent(PT("Heal"),PT("Restores this player's health, stamina and eitr to their current maximums.")),_button))PlayerAction(player,"heal","");
            if(GUILayout.Button(new GUIContent(PT("Clear food"),PT("Puke: removes the selected player's active food bonuses.")),_button))PlayerAction(player,"puke","");
            if(GUILayout.Button(new GUIContent(PT("Clear status"),PT("Clears hard-death state and all removable status effects using the native clearstatus behavior.")),_button))PlayerAction(player,"clearstatus","");
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            _adrenaline=GUILayout.TextField(_adrenaline,3,_textField,GUILayout.Width(60));
            if(GUILayout.Button(PT("Set adrenaline (0–100)"),_button))PlayerAction(player,"adrenaline",_adrenaline);
            GUILayout.EndHorizontal();
            if(GUILayout.Button(PT("Choose status effect")+": "+_statusEffect,_button))_showStatuses=!_showStatuses;
            if(_showStatuses)
            {
                string filter=GUILayout.TextField(_statusFilter,64,_textField);if(filter!=_statusFilter){_statusFilter=filter;_statusPage=0;}
                var effects=(ObjectDB.instance?.m_StatusEffects??new System.Collections.Generic.List<StatusEffect>()).Where(e=>e!=null&&e.name.IndexOf(_statusFilter,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(e=>e.name).ToArray();
                foreach(var effect in effects.Skip(_statusPage*12).Take(12))if(GUILayout.Button(new GUIContent(effect.name,Localization.instance.Localize(effect.m_tooltip)),_tabStyle)){_statusEffect=effect.name;_showStatuses=false;}
                if(effects.Length>12){GUILayout.BeginHorizontal();if(GUILayout.Button(PT("Previous"),_button))_statusPage=Math.Max(0,_statusPage-1);if(GUILayout.Button(PT("Next"),_button))_statusPage=Math.Min((effects.Length-1)/12,_statusPage+1);GUILayout.EndHorizontal();}
            }
            if(GUILayout.Button(PT("Apply selected status effect"),_button))PlayerAction(player,"addstatus",_statusEffect);
            GUI.enabled=true;if(!string.IsNullOrEmpty(_playerActionMessage))GUILayout.Label(_playerActionMessage,_statusStyle);
            GUILayout.EndVertical();
        }
        private SentinelPlayerRecord OwnPlayer()=>Player.m_localPlayer==null?null:_playerList?.players.FirstOrDefault(p=>p.online&&p.characterId==Player.m_localPlayer.GetPlayerID().ToString(CultureInfo.InvariantCulture));
        private void PlayerTeleport(string command)
        {
            if(_playerActionBusy)return;_playerActionBusy=true;_playerActionMessage=PT("Requesting teleport…");
            _control.AuthorizeCommand(command,(ok,result)=>Enqueue(()=>
            {_playerActionBusy=false;_playerActionMessage=ok&&result.StartsWith(SentinelCommandProvider.ServerRoute,StringComparison.Ordinal)?result.Substring(SentinelCommandProvider.ServerRoute.Length):result;_nextDetailRefresh=0;}));
        }
        private void PlayerAction(SentinelPlayerRecord player,string action,string arguments)
        {
            if(_playerActionBusy)return;
            try{arguments=PlayerActionProtocol.Validate(action,arguments);}catch(Exception error){_playerActionMessage=error.Message;return;}
            _playerActionNetwork=ZNet.instance;_playerActionServer=_playerActionNetwork?.GetServerPeer()?.m_rpc;
            _playerActionBusy=true;_playerActionMessage=player.name+": "+PT("Requesting player action…");
            _control.PlayerAction(player.account+"\n"+player.characterId+"\n"+action+"\n"+arguments,false,(ok,message)=>Enqueue(()=>
            {
                if(!ok){_playerActionBusy=false;_playerActionMessage=player.name+": "+message;return;}
                _playerActionId=message;_playerActionNextPoll=0;
            }));
        }
        private void TickPlayerActions()
        {
            if(string.IsNullOrEmpty(_playerActionId))return;
            if(!ReferenceEquals(_playerActionNetwork,ZNet.instance)||!ReferenceEquals(_playerActionServer,ZNet.instance?.GetServerPeer()?.m_rpc))
            {_playerActionId="";_playerActionBusy=false;_playerActionMessage=PT("Connection changed; action completion is unknown.");return;}
            if(_playerActionPolling||Time.realtimeSinceStartup<_playerActionNextPoll)return;
            string id=_playerActionId;_playerActionPolling=true;_playerActionNextPoll=Time.realtimeSinceStartup+1;
            _control.PlayerAction(id,true,(ok,message)=>Enqueue(()=>
            {
                _playerActionPolling=false;if(_playerActionId!=id)return;
                int newline=message.IndexOf('\n');string state=newline<0?"failed":message.Substring(0,newline);
                _playerActionMessage=newline<0?message:message.Substring(newline+1);
                if(!ok||state!="pending"){_playerActionId="";_playerActionBusy=false;_nextDetailRefresh=0;}
            }));
        }
    }
}
