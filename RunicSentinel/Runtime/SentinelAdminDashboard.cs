using System;
using System.Linq;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private string _modSearch="", _contentSearch="", _contentMod="All mods", _contentKind="All types";
        private int _modPage, _contentPage;
        private bool _advancedMods, _showModFilter;
        private string _quantity="1", _level="1";
        private const string AllMods="All mods";
        private static readonly string[] ContentKinds={"All types","Pieces","Stations","Items","Food","Ingredients","Potions","Creatures"};
        private SentinelContentEntry _selectedContent;

        private void DrawNamedMods()
        {
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_f6298c6132ee"), _section);
            GUILayout.Label(global::Runic.Localization.RunicText.Get("text_53d3bd77d191"), _label);
            string search=GUILayout.TextField(_modSearch,128,_textField);
            if(search!=_modSearch){_modSearch=search;_modPage=0;}
            var mods=SentinelModChoices.Read(_document).Where(m=>m.Name.IndexOf(_modSearch,StringComparison.OrdinalIgnoreCase)>=0 || m.Id.IndexOf(_modSearch,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            _modPage=Math.Min(_modPage,Math.Max(0,(mods.Length-1)/12));
            foreach(var mod in mods.Skip(_modPage*12).Take(12))
            {
                GUILayout.BeginVertical(_row);
                GUILayout.Label(mod.Name+"  ·  "+mod.Version, _section);
                GUILayout.Label(mod.Source+"  ·  "+mod.Id,_label);
                int before=SentinelModChoices.Category(_document,mod.Id);
                int after=GUILayout.SelectionGrid(before,new[]{global::Runic.Localization.RunicText.Get("text_bc96567e77ca"),global::Runic.Localization.RunicText.Get("text_4850b174b713"),global::Runic.Localization.RunicText.Get("text_1bb201d18835"),global::Runic.Localization.RunicText.Get("text_d7f48773024a"),global::Runic.Localization.RunicText.Get("text_18f2a0947f9d")},5,_tabStyle);
                if(before!=after)SentinelModChoices.Assign(_document,mod.Id,after);
                GUILayout.EndVertical();
            }
            DrawPager(ref _modPage,mods.Length,12);
            _advancedMods=GUILayout.Toggle(_advancedMods,global::Runic.Localization.RunicText.Get("text_e9b9d56fb7e7"));
        }

        private void DrawPager(ref int page,int total,int pageSize)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled=page>0;
            if(GUILayout.Button(global::Runic.Localization.RunicText.Get("text_a57b08a480b8"),_button))page--;
            GUI.enabled=true;
            GUILayout.Label($"{(total==0?0:page*pageSize+1)}–{Math.Min(total,(page+1)*pageSize)} / {total}",_label);
            GUI.enabled=(page+1)*pageSize<total;
            if(GUILayout.Button(global::Runic.Localization.RunicText.Get("text_1ff57a29d7c9"),_button))page++;
            GUI.enabled=true;
            GUILayout.EndHorizontal();
        }

        private void DrawContentCatalog()
        {
            var all=SentinelContentCatalog.Read();
            GUILayout.BeginHorizontal();GUILayout.Label(PT("Loaded content")+" · "+all.Count,_section);
            if(GUILayout.Button(PT("Refresh catalog"),_button,GUILayout.Width(175))) {all=SentinelContentCatalog.Read(true);_selectedContent=null;}
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            string search=GUILayout.TextField(_contentSearch,128,_textField);
            if(search!=_contentSearch){_contentSearch=search;_contentPage=0;}
            if(GUILayout.Button(global::Runic.Localization.RunicText.TranslateEnglish(_contentMod),_button,GUILayout.Width(240)))_showModFilter=!_showModFilter;
            GUILayout.EndHorizontal();
            if(_showModFilter)
                foreach(string mod in new[]{AllMods}.Concat(all.Select(e=>e.Mod).Distinct().OrderBy(v=>v)))
                    if(GUILayout.Button(global::Runic.Localization.RunicText.TranslateEnglish(mod),_tabStyle)){_contentMod=mod;_contentPage=0;_showModFilter=false;}
            string[] kinds=ContentKinds;
            int kind=GUILayout.SelectionGrid(Array.IndexOf(kinds,_contentKind),kinds.Select(global::Runic.Localization.RunicText.TranslateEnglish).ToArray(),4,_tabStyle);
            if(kind>=0 && kinds[kind]!=_contentKind){_contentKind=kinds[kind];_contentPage=0;}
            var entries=all.Where(e=>(_contentMod=="All mods"||e.Mod==_contentMod) && (_contentKind=="All types"||e.Kind==_contentKind) &&
                (e.Name.IndexOf(_contentSearch,StringComparison.OrdinalIgnoreCase)>=0||e.Id.IndexOf(_contentSearch,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            _contentPage=Math.Min(_contentPage,Math.Max(0,(entries.Length-1)/24));
            var visible=entries.Skip(_contentPage*24).Take(24).ToArray();
            if(all.Count==0)GUILayout.Label(PT("No content registered yet. Refresh after your character has entered the world.")+" "+SentinelContentCatalog.Diagnostics,_label);
            else if(entries.Length==0)GUILayout.Label(PT("No matches. Clear the search or select All mods / All types."),_label);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(_row,GUILayout.Width(Math.Max(190,(_window.width-100)/3)));
            foreach(var entry in visible)
                if(GUILayout.Button(entry.Name,entry==_selectedContent?LeftSelected:LeftButton,GUILayout.Height(36)))
                {_selectedContent=entry;_contentStatement="";_quantity="1";_level="1";_radius="0.5";_qualifier="";}
            DrawPager(ref _contentPage,entries.Length,24);
            GUILayout.EndVertical();GUILayout.BeginVertical(_row,GUILayout.ExpandWidth(true));
            if(_selectedContent==null)GUILayout.Label(PT("Select an item to view its details, recipe and command arguments."),_label);
            else DrawContentDetails();
            GUILayout.EndVertical();GUILayout.EndHorizontal();
        }

        private string _contentStatement="", _radius="0.5", _qualifier="";
        private bool _qualifierOpen, _noCommand;
        private GUIStyle LeftButton=>new GUIStyle(_button){alignment=TextAnchor.MiddleLeft,wordWrap=true};
        private GUIStyle LeftSelected=>new GUIStyle(_selectedTabStyle){alignment=TextAnchor.MiddleLeft,wordWrap=true};
        private void DrawContentDetails()
        {
            var entry=_selectedContent;
            GUILayout.BeginHorizontal();
            Rect icon=GUILayoutUtility.GetRect(64,64,GUILayout.Width(64),GUILayout.Height(64));
            if(entry.Icon!=null)
            {var r=entry.Icon.textureRect;var t=entry.Icon.texture;GUI.DrawTextureWithTexCoords(icon,t,new Rect(r.x/t.width,r.y/t.height,r.width/t.width,r.height/t.height));}
            GUILayout.Label(entry.Name,_heading);GUILayout.EndHorizontal();
            GUILayout.Label(entry.Details(),_label);
            GUILayout.Label(PT("Command arguments"),_section);
            GUILayout.Label(PT("Order: internal_id amount level radius qualifiers. Optional positions use defaults when needed. Append inserts text; Run executes it."),_label);
            if(GUILayout.Button(PT("Select Item"),_button)){_contentStatement=entry.Id;_noCommand=false;}
            GUILayout.BeginHorizontal();_quantity=GUILayout.TextField(_quantity,9,_textField);
            if(HoverButton("Amount","Number of objects to spawn; positive whole number. Example: Coins 2000 p",_button,GUILayout.Width(125)))SetContentArgument(1,_quantity);
            GUILayout.EndHorizontal();
            bool creature=entry.Prefab.GetComponent<Character>()!=null;
            int quality=entry.Item==null?1:Math.Max(1,entry.Item.m_itemData.m_shared.m_maxQuality);
            int max=creature?9:Math.Min(4,quality);
            if(max>1)
            {
                GUILayout.BeginHorizontal();_level=GUILayout.TextField(_level,3,_textField);
                if(HoverButton(PT(creature?"Level":"Quality")+" (1–"+max+")",creature?"Native creature levels: 1–9.":"Registered maximum quality: "+quality+". Native spawn caps quality at 4; mods may alter execution.",_button,GUILayout.Width(160)))
                {if(int.TryParse(_level,out int value)&&value>=1&&value<=max)SetContentArgument(2,_level);else _status="Choose a level from 1 to "+max+".";}
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();_radius=GUILayout.TextField(_radius,16,_textField);
            if(HoverButton("Radius","Scatter radius in metres for multiple objects; default 0.5. Requires amount and level positions first.",_button,GUILayout.Width(125)))SetContentArgument(3,_radius);
            GUILayout.EndHorizontal();
            if(entry.Item!=null)
            {
                if(GUILayout.Button(PT("Qualifiers")+": "+(_qualifier.Length==0?PT("None"):_qualifier)+" ▾",_button))_qualifierOpen=!_qualifierOpen;
                if(_qualifierOpen)
                    foreach(var option in new[]{"None","p — Pick up into inventory","e — Pick up and use / equip","i — Pick up only if not already owned"})
                        if(HoverButton(option,option=="None"?"Remove inventory qualifier.":option.Substring(4)+". Pickup can fail when inventory has no room.",LeftButton))
                        {_qualifier=option=="None"?"":option.Substring(0,1);SetContentQualifier();_qualifierOpen=false;}
            }
            GUILayout.Label(PT("Advanced native syntax: ID* matches multiple prefabs; Component.field::value overrides a field (numbers, true/false, text, or x::y::z). These depend on the prefab's actual component fields; edit the construction field when needed."),_label);
            _contentStatement=GUILayout.TextField(_contentStatement,900,_textField);
            if(GUILayout.Button(PT("Append to console"),_button,GUILayout.Height(36)))
            {
                if(string.IsNullOrWhiteSpace(_commandLine))_noCommand=true;
                else if(string.IsNullOrWhiteSpace(_contentStatement))_status=PT("Select an item first.");
                else if((_commandLine.TrimEnd()+" "+_contentStatement.Trim()).Length>1024)_status=PT("Command is too long.");
                else {_commandLine=_commandLine.TrimEnd()+" "+_contentStatement.Trim();_noCommand=false;}
            }
        }
        private void SetContentArgument(int index,string value)
        {
            if(index<3&&(!int.TryParse(value,out int number)||number<1)){_status=PT("Enter a positive whole number.");return;}
            if(index==3&&(!float.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float radius)||float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)){_status=PT("Enter a finite, nonnegative radius.");return;}
            var parts=_contentStatement.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Where(v=>v!="p"&&v!="e"&&v!="i").ToList();
            if(parts.Count==0)parts.Add(_selectedContent.Id);
            while(parts.Count<=index)parts.Add(parts.Count==3?"0.5":"1");
            parts[index]=value;_contentStatement=string.Join(" ",parts);SetContentQualifier();
        }
        private void SetContentQualifier()
        {
            var parts=_contentStatement.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Where(v=>v!="p"&&v!="e"&&v!="i").ToList();
            if(parts.Count==0)parts.Add(_selectedContent.Id);
            if(_qualifier.Length>0)parts.Add(_qualifier);
            _contentStatement=string.Join(" ",parts);
        }

        private void SelectBuildPiece()
        {
            var selected=_selectedContent;var network=ZNet.instance;var player=Player.m_localPlayer;
            _control.RequestStatus((ok,document,reason)=>Enqueue(()=>
            {
                if(!ok || !_open || network==null || !ReferenceEquals(network,ZNet.instance) || player==null || player!=Player.m_localPlayer)
                {_status=global::Runic.Localization.RunicText.Get("text_54a1958eb9e3");return;}
                try
                {
                    if(!player.InPlaceMode() || !player.SetSelectedPiece(selected.Piece))
                    {_status=global::Runic.Localization.RunicText.Get("text_c16ec778c272");return;}
                    Close();
                }
                catch(Exception error){_status=error.Message;}
            }));
        }
    }
}
