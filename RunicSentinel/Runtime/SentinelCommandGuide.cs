using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private bool _showCommandArguments;
        private string _argumentFilter="", _suggestionLine="", _suggestions="";
        private Vector2 _catalogScroll;
        private int _argumentPage;
        private int _selectedArgument;
        private readonly Dictionary<string,string[]> _commandOptions=new Dictionary<string,string[]>();
        private string[] CommandOptions(Terminal.ConsoleCommand command)
        {
            if(command==null)return Array.Empty<string>();
            if(command.Command=="tp")return (_playerList?.players??Array.Empty<SentinelPlayerRecord>()).Where(p=>p.online).Select(p=>p.name).Distinct().ToArray();
            if(_commandOptions.TryGetValue(command.Command,out var options))return options;
            try{options=(RunicSentinel.Devcommands.AutoComplete.GetOptions(command.Command,0)??command.GetTabOptions()??new List<string>()).Where(s=>!string.IsNullOrEmpty(s)&&!s.StartsWith("?")).Distinct().OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();}
            catch{options=Array.Empty<string>();}
            _commandOptions[command.Command]=options;return options;
        }
        private string[] ParameterNames(Terminal.ConsoleCommand command)
        {
            if(command.Command=="spawn")return new[]{"prefab","amount","level","radius","p / e / i","Component.field::value"};
            if(command.Command=="tp")return new[]{"player","destination player or x,z,y"};
            if(command.Command=="goto")return new[]{"x","z"};
            return Regex.Matches(command.Description??"",@"\[([^\]]+)\]|<([^>]+)>").Cast<Match>()
                .Select(m=>m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value).Distinct().ToArray();
        }
        private bool HasArguments(Terminal.ConsoleCommand command)=>ParameterNames(command).Length>0||CommandOptions(command).Length>0;
        private string ArgumentHelp(Terminal.ConsoleCommand command,string argument)
        {
            if(command.Command=="spawn")
            {if(argument=="prefab")return PT("Choose the item, creature or object to spawn. The content catalog inserts its exact ID.");if(argument=="amount")return PT("Number to spawn. Start with 1; large batches affect server performance.");if(argument=="level")return PT("Creature level or item quality, as supported by the native spawn command.");}
            if(command.Command=="addstatus")return "addstatus "+(argument=="duration"||argument=="intensity"?"Rested":argument)+" [duration seconds] [intensity]\nExample: addstatus Rested 3600 = 1 hour; 6000 = 100 minutes. Zero duration means no expiry. Intensity applies to shield absorption, burning/spirit damage and poison; it does not increase Rested.";
            if(command.Command=="spawn"&&argument=="radius")return "Scatter radius in metres; default 0.5. Syntax: spawn ID amount level radius.";
            if(command.Command=="spawn"&&argument=="p / e / i")return "p: try pickup into inventory. e: pickup and use/equip. i: pickup only if you do not already own the item. Inventory space is required.";
            if(command.Command=="spawn"&&argument=="Component.field::value")return "Optional prefab component field override, e.g. Component.field::value. Field names depend on the prefab. Values may be numeric, boolean, text or x::y::z.";
            if(command.Command=="tp")return PT("Choose an online player by name. Use their account ID if names are ambiguous. Separate source and destination with |. Coordinates use x,z,y.");
            if(command.Command=="goto")return PT("World coordinate for your destination. X is east/west; Z is north/south. This teleports your character.");
            int parameter=Array.IndexOf(ParameterNames(command),argument);
            if(parameter>=0)
            {
                var help=RunicSentinel.Devcommands.AutoComplete.GetOptions(command.Command,parameter);
                if(help!=null&&help.Any(v=>v.StartsWith("?")))return Regex.Replace(string.Join("\n",help).TrimStart('?'),"<[^>]+>","");
            }
            return (command.Description??"")+"\n"+PT("The installed command supplies this argument. Bracketed values may be optional; follow its syntax.");
        }
        private string BuildSuggestions()
        {
            if(_suggestionLine==_commandLine)return _suggestions;
            _suggestionLine=_commandLine;_suggestions="";
            string line=_commandLine.TrimStart();if(line.Length==0)return "";
            int space=line.IndexOf(' ');IEnumerable<string> values;
            if(space<0)values=_commandCatalog.Where(c=>c.Command.StartsWith(line,StringComparison.OrdinalIgnoreCase)).Select(c=>c.Command);
            else
            {
                string name=line.Substring(0,space);var command=_commandCatalog.FirstOrDefault(c=>c.Command.Equals(name,StringComparison.OrdinalIgnoreCase));
                if(command==null)return "";
                string argument=line.Substring(space+1);
                if(name=="tp"&&argument.Contains("|"))argument=argument.Substring(argument.LastIndexOf('|')+1).TrimStart();
                else if(name!="tp"&&argument.Contains(" "))
                {
                    var parts=argument.Split(' ');var options=RunicSentinel.Devcommands.AutoComplete.GetOptions(name,parts.Length-1);
                    if(options==null)return "";
                    _suggestions="\n\n"+Regex.Replace(string.Join("  ·  ",options.Where(v=>v.StartsWith("?")||v.StartsWith(parts.Last(),StringComparison.OrdinalIgnoreCase)).Take(30)),"<[^>]+>","").TrimStart('?');
                    return _suggestions.Trim().Length==0?"":_suggestions;
                }
                values=CommandOptions(command).Where(s=>s.StartsWith(argument,StringComparison.OrdinalIgnoreCase));
            }
            var matches=values.Take(31).ToArray();
            if(matches.Length>0)_suggestions="\n\n"+PT("Matching arguments / commands")+"\n"+string.Join("  ·  ",matches.Take(30))+(matches.Length>30?" …":"");
            return _suggestions;
        }
        private void DrawCommandsWorkspace()
        {
            _commandCatalog=SentinelCommandConsole.Catalog();
            float top=Math.Max(250,_bodyHeight*.48f);
            GUILayout.BeginHorizontal(GUILayout.Height(top));
            GUILayout.BeginVertical(_row,GUILayout.Width((_window.width-90)*.50f),GUILayout.Height(top));
            GUILayout.Label(PT("Console"),_heading);
            string output=SentinelCommandConsole.Output+BuildSuggestions();
            if(output!=_lastCommandOutput){_lastCommandOutput=output;_commandOutputScroll.y=float.MaxValue;}
            _commandOutputScroll=GUILayout.BeginScrollView(_commandOutputScroll,GUILayout.Height(Math.Max(65,top-166)));
            GUILayout.TextArea(output.Length==0?PT("Type a command or choose one from the guide."):output,new GUIStyle(_textArea){wordWrap=true,richText=false},GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
            GUI.SetNextControlName("sentinel-command-input");_commandLine=GUILayout.TextField(_commandLine,1024,_textField,GUILayout.Height(30));
            bool enter=Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Return&&GUI.GetNameOfFocusedControl()=="sentinel-command-input";
            if(enter)Event.current.Use();
            if(Event.current.type==EventType.KeyDown&&GUI.GetNameOfFocusedControl()=="sentinel-command-input"&&_commandHistory.Count>0&&(Event.current.keyCode==KeyCode.UpArrow||Event.current.keyCode==KeyCode.DownArrow))
            {_commandHistoryIndex=Mathf.Clamp(_commandHistoryIndex+(Event.current.keyCode==KeyCode.UpArrow?-1:1),0,_commandHistory.Count);_commandLine=_commandHistoryIndex==_commandHistory.Count?"":_commandHistory[_commandHistoryIndex];Event.current.Use();}
            GUILayout.BeginHorizontal();GUI.enabled=!_commandBusy;
            if(GUILayout.Button(_commandBusy?PT("Running…"):PT("Run"),_button,GUILayout.Height(32))||enter)RunCommand();
            GUI.enabled=true;if(GUILayout.Button(PT("Clear Output"),_button,GUILayout.Width(160),GUILayout.Height(32))){SentinelCommandConsole.Reset();_commandLine="";_suggestionLine="";_suggestions="";}
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();GUI.enabled=!_commandBusy;
            foreach(string name in new[]{"devcommands","debugmode","nocost"})if(GUILayout.Button(name,_button,GUILayout.Height(30))){_commandLine=name;RunCommand();}
            GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.EndVertical();
            GUILayout.BeginVertical(_row,GUILayout.Height(top));GUILayout.Label(PT("Command guide"),_heading);
            _commandSearch=GUILayout.TextField(_commandSearch,128,_textField);
            var selected=_commandCatalog.FirstOrDefault(c=>c.Command==_selectedCommand);
            _commandScroll=GUILayout.BeginScrollView(_commandScroll,GUILayout.Height(Math.Max(60,top-182)));
            if(!_showCommandArguments)
            {
                foreach(var command in _commandCatalog.Where(c=>c.Command.IndexOf(_commandSearch,StringComparison.OrdinalIgnoreCase)>=0||(c.Description??"").IndexOf(_commandSearch,StringComparison.OrdinalIgnoreCase)>=0))
                {
                    GUILayout.BeginHorizontal();if(HoverButton(command.Command,command.Description,command.Command==_selectedCommand?LeftSelected:LeftButton)){_selectedCommand=command.Command;_commandArguments="";}
                    if(HasArguments(command)&&GUILayout.Button(PT("Arguments"),_button,GUILayout.Width(125))){if(_selectedCommand!=command.Command)_commandArguments="";_selectedCommand=command.Command;_showCommandArguments=true;_argumentFilter="";_argumentPage=0;_selectedArgument=0;}
                    GUILayout.EndHorizontal();
                }
            }
            else if(selected!=null)
            {
                if(GUILayout.Button(PT("Back to commands"),_button))_showCommandArguments=false;
                GUILayout.Label(selected.Command+" — "+(selected.Description??""),_label);
                var parameters=ParameterNames(selected);
                for(int index=0;index<parameters.Length;index++)
                    if(HoverButton(parameters[index],ArgumentHelp(selected,parameters[index]),_selectedArgument==index?LeftSelected:LeftButton))_selectedArgument=index;
                GUILayout.Label(PT("Hover over a parameter or value for help. Click a value to insert it."),_label);
                string argumentFilter=GUILayout.TextField(_argumentFilter,128,_textField);if(argumentFilter!=_argumentFilter){_argumentFilter=argumentFilter;_argumentPage=0;}
                var choices=RunicSentinel.Devcommands.AutoComplete.GetOptions(selected.Command,_selectedArgument);
                if(choices!=null&&choices.Any(v=>v.StartsWith("?")))GUILayout.Label(Regex.Replace(string.Join("\n",choices).TrimStart('?'),"<[^>]+>",""),_label);
                var argumentOptions=(choices==null?CommandOptions(selected):choices.Where(v=>!v.StartsWith("?")).ToArray()).Where(s=>s.IndexOf(_argumentFilter,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                if(selected.Command=="spawn"&&_selectedArgument>0)
                {argumentOptions=Array.Empty<string>();GUILayout.Label(PT("Enter the numeric amount or level in the arguments field below: prefab amount level. These values do not have a finite completion list."),_label);}
                DrawPager(ref _argumentPage,argumentOptions.Length,50);
                foreach(string option in argumentOptions.Skip(_argumentPage*50).Take(50))
                    if(HoverButton(option,ArgumentHelp(selected,option),LeftButton))
                    {
                        if(selected.Command=="tp")
                        {
                            var parts=_commandArguments.Split('|');string source=parts[0].Trim(),destination=parts.Length>1?parts[1].Trim():"";
                            if(_selectedArgument==0)source=option;else destination=option;
                            _commandArguments=source+" | "+destination;
                        }
                        else if(selected.Command=="spawn")
                        {
                            var parts=_commandArguments.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                            _commandArguments=option+" "+(parts.Length>1?parts[1]:"1")+" "+(parts.Length>2?parts[2]:"1");
                        }
                        else
                        {
                            var parts=_commandArguments.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).ToList();
                            while(parts.Count<=_selectedArgument)parts.Add("");
                            parts[_selectedArgument]=option;_commandArguments=string.Join(" ",parts);
                        }
                    }
            }
            GUILayout.EndScrollView();
            GUILayout.Label(selected==null?PT("Select a command"):selected.Command+" · "+(SentinelCommandProvider.ExecutesOnServer(selected.Command)?PT("Server"):PT("Your character")),_section);
            _commandArguments=GUILayout.TextField(_commandArguments,900,_textField,GUILayout.Height(28));
            GUI.enabled=selected!=null;
            if(GUILayout.Button(PT("Insert into console"),_button,GUILayout.Height(34)))
            {_commandLine=selected.Command+(string.IsNullOrWhiteSpace(_commandArguments)?"":" "+_commandArguments.Trim());GUI.FocusControl("sentinel-command-input");}
            GUI.enabled=true;GUILayout.EndVertical();GUILayout.EndHorizontal();
            GUILayout.Space(6);_catalogScroll=GUILayout.BeginScrollView(_catalogScroll,GUILayout.Height(Math.Max(70,_bodyHeight-top-12)));
            DrawContentCatalog();GUILayout.EndScrollView();
        }
    }
}
