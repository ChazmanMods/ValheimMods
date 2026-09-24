using System;
using System.Globalization;
using System.Reflection;
using RunicSigns.Core;
using RunicSigns.Runtime;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static readonly MethodInfo TickMethod=typeof(SignRuntime).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
    private static void Main()
    {
        Run("Fine bend controls preserve existing saved shapes", CaptionCurveTests.FineControlsPreserveSavedShapes);
        Run("Storage and Signs share fine glyph and emoji bending", CaptionCurveTests.StorageAndSignsBendGlyphsAndEmojisEqually);
        Run("Container wrap follows rounded surfaces and anchors the center", CaptionCurveTests.ContainerWrapFollowsSurfaceAndKeepsCenter);
        DesignerTests();
        Run("Emoji catalog, selection, surrogate safety and limits", EmojiRegressionTests.Run);
        Run("Emoji caption saves through native ownership transport and reload", () => {
            var env = Setup(); bool success = false;
            string caption = "\U0001F525 Fuel\n\U0001F6E1\uFE0F Gear";
            ZNet.Session = 2;
            env.client.BeginSave(new SignSettings(), caption, "", "Original", (ok, _) => success = ok);
            Rpc(); Rpc(); Data(); Tick(env.client, 2);
            Check(success && env.client.NativeText == caption);
            var saved = Network.Views[2].Data;
            Check(Create(3, saved).NativeText == caption);
        });
        Run("Hammer preview scales repeatedly without compounding and respects text focus",()=>{
            var config=new BepInEx.Configuration.ConfigFile(); SignPlacement.Bind(config);
            var scale=(BepInEx.Configuration.ConfigEntry<float>)config.Entries["SignScale"];
            var player=new GameObject().AddComponent<Player>(); Player.m_localPlayer=player;
            var ghost=new GameObject("sign"); ghost.AddComponent<Sign>(); ghost.AddComponent<ZNetView>().Valid=false;
            ghost.transform.localScale=new Vector3(2,3,4);
            foreach(float size in new[]{2f,4f,.25f,2f}) {
                scale.Value=size; SignPlacement.Preview(player,ghost,false); SignPlacement.Preview(player,ghost,false);
                Near(ghost.transform.localScale.x,2*size); Near(ghost.transform.localScale.y,3*size);
            }
            BepInEx.Configuration.KeyboardShortcut.Pressed=KeyCode.RightBracket;
            player.Input=false; Time.frameCount++; SignPlacement.Preview(player,ghost,true); Near(scale.Value,2);
            player.Input=true; Time.frameCount++; SignPlacement.Preview(player,ghost,true); Near(scale.Value,2.25f);
            SignPlacement.Preview(player,ghost,true); Near(scale.Value,2.25f);
            SignEditor.BlockGameplay=true; Time.frameCount++; SignPlacement.Preview(player,ghost,true); Near(scale.Value,2.25f);
            SignEditor.BlockGameplay=false; BepInEx.Configuration.KeyboardShortcut.Pressed=null;
            ghost.GetComponent<ZNetView>().Valid=true; scale.Value=4;
            SignPlacement.Preview(player,ghost,false); Near(ghost.transform.localScale.x,4.5f);
            ghost.GetComponent<ZNetView>().Valid=false; ghost.name="other-sign";
            SignPlacement.Preview(player,ghost,false); Near(ghost.transform.localScale.x,4.5f);
        });
        Run("Native placement stamps size once and reload restores it without changing caption",()=>{
            var config=new BepInEx.Configuration.ConfigFile(); SignPlacement.Bind(config);
            ((BepInEx.Configuration.ConfigEntry<float>)config.Entries["SignScale"]).Value=3;
            Player.m_localPlayer=new GameObject().AddComponent<Player>();
            var env=Setup(); env.sign.gameObject.AddComponent<Piece>();
            env.client.OnPlaced(); Check(env.client.Raw==""); // remote-owned object
            Network.Views[2].Data.Owner=2; env.client.OnPlaced(); Near(env.client.transform.localScale.x,3);
            Check(env.sign.Writes==0); var saved=new ZDO(); saved.CopyFrom(Network.Views[2].Data);
            ((BepInEx.Configuration.ConfigEntry<float>)config.Entries["SignScale"]).Value=4;
            env.client.OnPlaced(); Near(env.client.transform.localScale.x,3);
            var reloaded=Create(3,saved); Near(reloaded.transform.localScale.x,3);
        });
        Run("First offset step respects text-local scale in all four directions",()=>{
            foreach(var offset in new[]{new Vector2(.05f,0),new Vector2(-.05f,0),new Vector2(0,.05f),new Vector2(0,-.05f)}) {
                var env=Setup(sign=>{
                    var rect=sign.m_textWidget.rectTransform;
                    rect.sizeDelta=new Vector2(1000,300); rect.localScale=new Vector3(.001f,.002f,1);
                    rect.localPosition=new Vector3(.1f,.2f,.03f);
                });
                Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Horizontal=offset.x,Vertical=offset.y}.Encode());
                env.client.Render(); var position=env.sign.m_textWidget.rectTransform.localPosition;
                Near(position.x,.1f+offset.x); Near(position.y,.2f+.6f*offset.y); Near(position.z,.03f);
            }
        });
        Run("Text offsets follow rotated text axes",()=>{
            var env=Setup(sign=>{
                var rect=sign.m_textWidget.rectTransform; rect.sizeDelta=new Vector2(1000,300);
                rect.localScale=new Vector3(.001f,.001f,1); rect.localRotation=Quaternion.Euler(0,90,0);
                rect.localPosition=new Vector3(.1f,.2f,.03f);
            });
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Horizontal=.05f,Vertical=.05f}.Encode());
            env.client.Render(); var position=env.sign.m_textWidget.rectTransform.localPosition;
            Near(position.x,.1f); Near(position.y,.215f); Near(position.z,-.02f);
        });
        Run("Board size does not multiply the local offset a second time",()=>{
            foreach(float scale in new[]{.25f,1f,4f}) {
                var env=Setup(sign=>{
                    sign.m_textWidget.rectTransform.sizeDelta=new Vector2(1000,300);
                    sign.m_textWidget.rectTransform.localScale=new Vector3(.001f,.001f,1);
                });
                Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=scale,Horizontal=.05f}.Encode());
                env.client.Render(); Near(env.sign.m_textWidget.rectTransform.localPosition.x,.05f);
                Near(env.client.transform.localScale.x,scale);
            }
        });
        Run("Offset changes never accumulate and Center restores original position",()=>{
            var env=Setup(sign=>{
                sign.m_textWidget.rectTransform.sizeDelta=new Vector2(1000,300);
                sign.m_textWidget.rectTransform.localScale=new Vector3(.001f,.001f,1);
                sign.m_textWidget.rectTransform.localPosition=new Vector3(.1f,.2f,.03f);
            });
            foreach(float offset in new[]{.05f,.1f,-.05f,0f}) {
                Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Horizontal=offset}.Encode());
                env.client.Render(); Near(env.sign.m_textWidget.rectTransform.localPosition.x,.1f+offset);
                Near(env.sign.m_textWidget.rectTransform.localPosition.y,.2f);
            }
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,""); env.client.Render();
            Near(env.sign.m_textWidget.rectTransform.localPosition.z,.03f);
        });
        Run("Closed editor never blocks movement despite permanently held action",()=>{
            var input=new EditorInputState(); input.Open(); Check(input.IsOpen);
            input.Close(100,127);
            for(int frame=0;frame<600;frame++) {
                float now=100+frame/60f;
                input.SuppressClosingAction("Attack",true,now);
                input.SuppressClosingAction("JoyUse",true,now);
                Check(!input.IsOpen);
                Check(!input.SuppressClosingAction("Forward",true,now));
            }
        });
        Run("Held action protection expires even if a controller state is stale",()=>{
            var input=new EditorInputState(); input.Open(); input.Close(100,127);
            Check(input.SuppressClosingAction("JoyUse",true,100.5f));
            Check(!input.SuppressClosingAction("JoyUse",true,101));
            Check(!input.SuppressClosingAction("Attack",true,102));
        });
        Run("Releasing closing action permits a fresh press immediately",()=>{
            var input=new EditorInputState(); input.Open(); input.Close(100,1);
            Check(input.SuppressClosingAction("Attack",true,100.1f));
            Check(!input.SuppressClosingAction("Attack",false,100.2f));
            Check(!input.SuppressClosingAction("Attack",true,100.3f));
            Check(!input.SuppressClosingAction("Use",true,100.3f));
        });
        Run("Partial construction and reset never leave a modal input lock",()=>{
            var input=new EditorInputState(); Check(!input.IsOpen);
            input.Close(0,0); Check(!input.IsOpen);
            input.Open(); input.Close(0,127); Check(!input.IsOpen);
            input.Open(); input.Reset(); Check(!input.IsOpen);
            Check(!input.SuppressClosingAction("Attack",true,0));
        });
        Run("Settings round-trip in non-English culture",()=>{
            var old=CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture=new CultureInfo("fr-FR"); var s=new SignSettings{Scale=2.25f,TextSize=.7f,Horizontal=-.1f,Vertical=.2f,Color=5,Background=2,Alignment=0,Bold=true};
                Check(SignSettings.TryDecode(s.Encode(),out var decoded)&&decoded.Encode()==s.Encode()); }
            finally { CultureInfo.CurrentCulture=old; }
        });
        Run("Missing settings use defaults",()=>Check(SignSettings.TryDecode("",out var s)&&s.Scale==1&&s.Background==0));
        Run("Future, truncated and oversized records rejected",()=>{
            foreach(var raw in new[]{"2|1|1|0|0|9|0|1|0|0","1|1",new string('x',161),"1|1|1|0|0|9|0|1|0|0|extra"})
                Check(!SignSettings.TryDecode(raw,out _));
        });
        Run("NaN, infinity, negatives and out-of-bounds rejected",()=>{
            foreach(var scale in new[]{"NaN","Infinity","-Infinity","0","-1","4.01","1e30"})
                Check(!SignSettings.TryDecode($"1|{scale}|1|0|0|9|0|1|0|0",out _));
            Check(!new SignSettings{TextSize=.09f}.Valid); Check(!new SignSettings{Horizontal=20.01f}.Valid);
            Check(!new SignSettings{Color=22}.Valid); Check(!new SignSettings{Background=3}.Valid);
            Check(!SignSettings.TryDecode("1|1|1|0|0|9|0|1|2|0",out _));
        });
        Run("Text length, multiline, Unicode and controls",()=>{
            Check(SignSettings.ValidText("Food\nMead 🍺")); Check(SignSettings.ValidText(new string('x',256)));
            Check(!SignSettings.ValidText(new string('x',SignSettings.TextLimit+1))); Check(!SignSettings.ValidText("x\0y"));
            Check(!SignSettings.ValidText("\uD800")); Check(!SignSettings.ValidText("\uDC00"));
        });
        Run("RunicStorage palette normalization",()=>{
            Check(LabelColors.Names[LabelColors.Resolve("gray")]=="Grey");
            Check(LabelColors.Names[LabelColors.Resolve("#ff0000")]=="Red");
            Check(!LabelColors.Markup("<size=999>X", "White").Contains("<size"));
        });
        Run("Expired and corrupt leases cannot permanently lock signs",()=>{
            Check(EditPolicy.LeaseActive("abc",112,100)); Check(!EditPolicy.LeaseActive("abc",100,100));
            Check(!EditPolicy.LeaseActive("abc",double.NaN,100)); Check(!EditPolicy.LeaseActive("abc",double.PositiveInfinity,100));
            Check(!EditPolicy.LeaseActive("abc",100000,100)); Check(!EditPolicy.LeaseActive("",112,100));
        });
        Run("RPC response before ownership waits for ZDO",()=>{
            var env=Setup(); bool success=false;
            Begin(env.client,(ok,_)=>success=ok);
            Rpc(); Rpc(); Tick(env.client,2); Check(env.sign.Writes==0);
            Data(); Tick(env.client,2); Check(success&&env.sign.Writes==1&&env.client.NativeText=="New caption");
        });
        Run("Ownership before RPC response waits for grant",()=>{
            var env=Setup(); bool success=false; Begin(env.client,(ok,_)=>success=ok);
            Rpc(); Data(); Tick(env.client,2); Check(env.sign.Writes==0);
            Rpc(); Tick(env.client,2); Check(success&&env.sign.Writes==1);
        });
        Run("Simultaneous save attempts cannot both receive ownership",()=>{
            var env=Setup(); var other=Create(3); Begin(env.client,(_,_)=>{});
            ZNet.Session=3; other.BeginSave(new SignSettings(),"Other","","Original",(_,_)=>{});
            Rpc(); Rpc(); // First request transfers ownership; old owner cannot grant the second.
            Check(Network.Views[1].Data.Owner==2); Check(Network.DataQueue.Count==1);
            Data(); Rpc(); Tick(env.client,2);
            Time.unscaledTime=9; Tick(other,3); Check(!other.Saving);
            Check(Network.Views[3].GetComponent<Sign>().Writes==0);
        });
        Run("Stale caption or style is refused",()=>{
            foreach(bool style in new[]{true,false}) {
                var env=Setup(); if(style) Network.Views[1].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=2}.Encode());
                else Network.Views[1].Data.Set(ZDOVars.s_text,"Changed elsewhere");
                bool rejected=false; Begin(env.client,(ok,msg)=>rejected=!ok&&msg.Contains("changed")); Rpc(); Rpc();
                Check(rejected&&env.sign.Writes==0&&Network.DataQueue.Count==0);
            }
        });
        Run("Ward revocation between grant and commit blocks save",()=>{
            var env=Setup(); bool rejected=false; Begin(env.client,(ok,_)=>rejected=!ok);
            Rpc(); Data(); Rpc(); Network.LocalAccess=false; Tick(env.client,2); Check(rejected&&env.sign.Writes==0);
        });
        Run("Owner-side access rejection does not transfer ownership",()=>{
            var env=Setup(); Network.SenderAccess=false; bool rejected=false; Begin(env.client,(ok,_)=>rejected=!ok);
            Rpc(); Rpc(); Check(rejected&&Network.Views[1].Data.Owner==1&&Network.DataQueue.Count==0);
        });
        Run("Unrelated replies cannot authorize a write",()=>{
            var env=Setup(); Begin(env.client,(_,_)=>{});
            var response=typeof(SignRuntime).GetMethod("SaveResponse",BindingFlags.Instance|BindingFlags.NonPublic);
            var token=(string)typeof(SignRuntime).GetField("_pendingToken",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(env.client);
            response.Invoke(env.client,new object[]{99L,token,""}); Tick(env.client,2); Check(env.sign.Writes==0);
        });
        Run("Timeout then late grant never writes a discarded draft",()=>{
            var env=Setup(); bool rejected=false; Begin(env.client,(ok,_)=>rejected=!ok);
            Time.unscaledTime=9; Tick(env.client,2); Check(rejected&&!env.client.Saving);
            Rpc(); Data(); Rpc(); Tick(env.client,2); Check(env.sign.Writes==0);
            ZNet.Clock=113; Check(env.client.AllowNativeWrite);
        });
        Run("Cancel before response never commits",()=>{
            var env=Setup(); Begin(env.client,(_,_)=>{}); env.client.CancelSave();
            Rpc(); Data(); Rpc(); Tick(env.client,2); Check(env.sign.Writes==0&&!env.client.Saving);
        });
        Run("Expired grant cannot commit",()=>{
            var env=Setup(); bool rejected=false; Begin(env.client,(ok,_)=>rejected=!ok);
            Rpc(); Data(); Rpc(); ZNet.Clock=113; Tick(env.client,2); Check(rejected&&env.sign.Writes==0);
        });
        Run("Native rejection leaves style unchanged",()=>{
            var env=Setup(); env.sign.RejectWrite=true; bool rejected=false; Begin(env.client,(ok,_)=>rejected=!ok);
            Rpc(); Data(); Rpc(); Tick(env.client,2); Check(rejected&&env.client.Raw=="");
        });
        Run("Style-only changes synchronize and scale never compounds",()=>{
            var env=Setup(); Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=2}.Encode());
            env.client.Render(); env.client.Render(); Check(env.client.transform.localScale.x==2);
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=.25f,Color=0}.Encode());
            env.client.Render(); Check(env.client.transform.localScale.x==.25f);
        });
        Run("Recreated sign restores saved size",()=>{
            var env=Setup(); var saved=new ZDO(); saved.CopyFrom(Network.Views[1].Data);
            saved.Set(SignRuntime.SettingsKey,new SignSettings{Scale=3}.Encode());
            var reloaded=Create(3,saved); Check(reloaded.transform.localScale.x==3);
        });
        Run("Unknown records are preserved with baseline appearance",()=>{
            var env=Setup(); Network.Views[2].Data.Set(SignRuntime.SettingsKey,"future version");
            env.client.Render(); Check(env.client.Raw=="future version"&&env.client.transform.localScale.x==1);
        });
        Run("Rendering does not reveal hidden native text",()=>{
            var env=Setup(); env.sign.m_textWidget.text="HIDDEN BY VANILLA";
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings().Encode()); env.client.Render();
            Check(env.sign.m_textWidget.text=="HIDDEN BY VANILLA");
            Check(env.sign.m_textWidget.textPreprocessor.PreprocessText(env.sign.m_textWidget.text).Contains("HIDDEN BY VANILLA"));
            Check(!env.sign.m_textWidget.textPreprocessor.PreprocessText(env.sign.m_textWidget.text).Contains("Secret"));
        });
        Run("Headless sign applies physical size without a text widget",()=>{
            var env=Setup(); env.sign.m_textWidget=null;
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=4}.Encode()); env.client.Render();
            Check(env.client.transform.localScale.x==4);
        });
        Console.WriteLine($"PASS: {_passed} tests. Production save/render code exercised with simulated peers; no live game certification implied.");
    }
    private static void DesignerTests()
    {
        Run("F8 opens only a local sign ghost and freezes placement updates",()=>{
            SignPlacement.Bind(new BepInEx.Configuration.ConfigFile());
            var player=new GameObject().AddComponent<Player>();Player.m_localPlayer=player;
            var ghost=new GameObject("sign");ghost.AddComponent<Sign>();ghost.AddComponent<ZNetView>().Valid=false;
            BepInEx.Configuration.KeyboardShortcut.Pressed=KeyCode.F8;SignEditor.OpenPlacementCalls=0;
            try {
                player.Input=false;Time.frameCount++;SignPlacement.Preview(player,ghost,true);Check(SignEditor.OpenPlacementCalls==0);
                player.Input=true;Time.frameCount++;SignPlacement.Preview(player,ghost,true);Check(SignEditor.OpenPlacementCalls==1);
                var prefix=typeof(SignPlacementUpdatePatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
                Check(!(bool)prefix.Invoke(null,new object[]{player,ghost}));
                Check(SignEditor.OpenPlacementCalls==1);
                SignEditor.BlockGameplay=false;BepInEx.Configuration.KeyboardShortcut.Pressed=null;
                Check((bool)prefix.Invoke(null,new object[]{player,ghost}));
            } finally { SignEditor.BlockGameplay=false;SignEditor.PlacementTarget=null;BepInEx.Configuration.KeyboardShortcut.Pressed=null; }
        });
        Run("Legacy settings migrate without changing offsets or caption interpretation",()=>{
            Check(SignSettings.TryDecode("1|1|1|0.4|-0.4|9|0|1|1|0",out var old));
            Near(old.Horizontal,.4f); Near(old.Vertical,-.4f); Check(old.OffsetUnit==0 && old.Fit && old.Bold);
            Check(SignSettings.TryDecode(old.Encode(),out var again) && again.Encode()==old.Encode());
        });
        Run("Designer settings preserve exact colors, opacity, runs and wide offsets",()=>{
            var settings=new SignSettings{Horizontal=-2000,Vertical=40,OffsetUnit=1,TextSize=20,Fit=false,Ink="#ABCDEF80",Opacity=.45f,Effect=2};
            settings.Runs=SignFormatting.Apply("Home supplies",settings.Runs,0,4,s=>{s.Bold=1;s.Italic=1;s.Size=2;s.Ink="#FF0000FF";});
            Check(SignSettings.TryDecode(settings.Encode(),out var copy)&&copy.Encode()==settings.Encode());
            var cloned=settings.Clone(); cloned.Runs[0].Style.Ink="#00FF00FF"; Check(settings.Runs[0].Style.Ink=="#FF0000FF");
            Check(!new SignSettings{OffsetUnit=1,Horizontal=2001}.Valid);
            Check(!new SignSettings{Opacity=float.NaN}.Valid); Check(!new SignSettings{Ink="<color=red>"}.Valid);
            Check(!new SignSettings{Effect=4}.Valid);
        });
        Run("Generated markup combines styles while captions stay literal",()=>{
            var settings=new SignSettings{Ink="#12AB34FF",Opacity=.5f};
            settings.Runs=SignFormatting.Apply("Home <size=999>",settings.Runs,0,4,s=>{s.Bold=1;s.Italic=1;s.Size=2;});
            string rendered=SignFormatting.Render("Home <size=999>",settings);
            Check(rendered.Contains("<b><i>Home</i></b>")); Check(rendered.Contains("<size=200%>"));
            Check(rendered.Contains("<noparse><</noparse>size=999>")); Check(rendered.Contains("#12AB3480"));
            Check(SignFormatting.TryColor("#abcdef",out var hex)&&hex=="#ABCDEFFF");
            Check(SignFormatting.TryColor("cyan",out hex)&&hex=="#00FFFFFF"); Check(!SignFormatting.TryColor("garbage",out hex));
        });
        Run("Selection, nested styles and editing preserve unaffected text",()=>{
            var runs=SignFormatting.Apply("Red Blue",new(),0,3,s=>s.Ink="#FF0000FF");
            runs=SignFormatting.Apply("Red Blue",runs,1,7,s=>s.Bold=1);
            var changed=SignFormatting.Edit("Red Blue","Red XXBlue",runs);
            var s=new SignSettings{Runs=changed}; Check(System.Text.RegularExpressions.Regex.Replace(SignFormatting.Render("Red XXBlue",s),"<[^>]+>","")=="Red XXBlue");
            var removed=SignFormatting.Edit("Red XXBlue","Blue",changed);
            Check(SignFormatting.ValidRuns(removed)); Check(SignFormatting.Render("Blue",new SignSettings{Runs=removed}).Contains("<b>Blu"));
            var emoji=SignFormatting.Apply("A\U0001F525B",new(),2,3,x=>x.Bold=1);
            Check(emoji.Count==1 && emoji[0].Start==1 && emoji[0].Length==2);
            Check(SignFormatting.Render("A\U0001F525B",new SignSettings{Runs=emoji}).Contains("<b>\U0001F525</b>"));
        });
        Run("Formatting without a selection changes nothing, including emoji caret",()=>{
            var settings=new SignSettings{Bold=true,Ink="#123456FF"};
            settings.Runs=SignFormatting.Apply("A\U0001F525B",new(),3,4,s=>s.Italic=1);
            string before=settings.Encode();
            for(int caret=0;caret<=4;caret++) {
                Check(!SignFormatting.ApplySelection(settings,"A\U0001F525B",caret,caret,s=>s.Ink="#FF0000FF"));
                Check(settings.Encode()==before);
            }
        });
        Run("Editor caret formatting changes defaults while preserving individual word overrides",()=>{
            var settings=new SignSettings();
            SignFormatting.ApplyEditor(settings,"Left Home",5,9,s=>s.Ink="#FF0000FF");
            SignFormatting.ApplyEditor(settings,"Left Home",0,0,s=>s.Ink="#00FFFFFF");
            Check(SignFormatting.Render("Left Home",settings)=="<color=#00FFFFFF>Left </color><color=#FF0000FF>Home</color>");
            SignFormatting.ApplyEditor(settings,"Left Home",2,2,s=>s.Size=3);
            Near(settings.TextSize,3); Check(settings.Runs.Count==1);
            SignFormatting.ApplyEditor(settings,"",0,0,s=>s.Bold=1);
            Check(settings.Bold);
            SignFormatting.ApplyEditor(settings,"Left Home",5,9,s=>s.Italic=1);
            Check(settings.Runs[0].Style.Italic==1 && !settings.Italic);
            Check(SignSettings.TryDecode(settings.Encode(),out var copy) && copy.Encode()==settings.Encode());
        });
        Run("Legacy opacity stays visually identical and editor opacity preserves word overrides",()=>{
            var settings=new SignSettings{Ink="#123456FF",Opacity=.5f};
            SignFormatting.ApplySelection(settings,"One Two",4,7,s=>s.Ink="#FF000080");
            string before=SignFormatting.Render("One Two",settings);
            SignFormatting.NormalizeEditorOpacity(settings);
            Check(SignFormatting.Render("One Two",settings)==before); Near(settings.Opacity,1);
            SignFormatting.ApplyEditor(settings,"One Two",0,0,s=>s.Ink=s.Ink.Substring(0,7)+"FF");
            Check(SignFormatting.Render("One Two",settings)=="<color=#123456FF>One </color><color=#FF000040>Two</color>");
        });
        Run("Reset Text restores uniform defaults without changing caption layout",()=>{
            var settings=new SignSettings{Scale=.5f,Horizontal=89,OffsetUnit=1,TextSize=8,Bold=true,Italic=true,Ink="#12345680",Opacity=.4f,Effect=3};
            SignFormatting.ApplyEditor(settings,"Left Home",5,9,s=>{s.Ink="#FF0000FF";s.Size=2;});
            SignFormatting.ResetText(settings);
            Check(settings.Runs.Count==0 && !settings.Bold && !settings.Italic && settings.Ink=="");
            Near(settings.TextSize,1); Near(settings.Opacity,1); Near(settings.Scale,.5f); Near(settings.Horizontal,89); Check(settings.Effect==3);
            SignFormatting.ApplyEditor(settings,"Left Home",0,0,s=>s.Ink="#00FFFFFF");
            Check(SignFormatting.Render("Left Home",settings)=="<color=#00FFFFFF>Left Home</color>");
        });
        Run("Cyan uses neutral private face material and restores original tint on cancel",()=>{
            var env=Setup(sign=>{
                sign.m_textWidget.fontSharedMaterial=new Material(null);
                sign.m_textWidget.fontSharedMaterial.SetColor("_FaceColor",new Color(.1f,.2f,.3f,1));
                sign.m_textWidget.overrideColorTags=true;
            });
            var widget=env.sign.m_textWidget; var original=widget.fontSharedMaterial;
            env.client.Preview(new SignSettings{Ink="#00FFFFFF"},"LEI");
            Check(widget.fontSharedMaterial!=original && !widget.overrideColorTags);
            Near(widget.fontSharedMaterial.Colors["_FaceColor"].r,1);
            Near(original.Colors["_FaceColor"].r,.1f);
            Check(widget.textPreprocessor.PreprocessText("LEI")=="<color=#00FFFFFF>LEI</color>");
            env.client.EndPreview(); Check(widget.fontSharedMaterial==original && widget.overrideColorTags);
        });
        Run("Every named color applies only to the selected words",()=>{
            Check(LabelColors.Names.Length==22 && LabelColors.Hex.Length==22);
            for(int i=0;i<LabelColors.Names.Length;i++) {
                Check(SignFormatting.TryColor(LabelColors.Names[i],out var hex) && hex==LabelColors.Hex[i]);
                var settings=new SignSettings();
                Check(SignFormatting.ApplySelection(settings,"Left Home Right",9,5,s=>s.Ink=hex));
                Check(settings.Runs.Count==1 && settings.Runs[0].Start==5 && settings.Runs[0].Length==4);
                Check(settings.Runs[0].Style.Ink==hex);
                Check(SignFormatting.Render("Left Home Right",settings).Contains("<color="+hex+">Home</color>"));
            }
        });
        Run("Repeating the same color on another selection still applies",()=>{
            var settings=new SignSettings();
            Check(SignFormatting.ApplySelection(settings,"One Two Three",0,3,s=>s.Ink="#FF0000FF"));
            Check(SignFormatting.ApplySelection(settings,"One Two Three",8,13,s=>s.Ink="#FF0000FF"));
            Check(settings.Runs.Count==2 && settings.Runs[0].Start==0 && settings.Runs[1].Start==8);
            Check(settings.Runs[0].Length==3 && settings.Runs[1].Length==5);
        });
        Run("Successive formatting composes on a selection and clearing preserves neighbors",()=>{
            const string caption="Left Home Right";
            var settings=new SignSettings();
            SignFormatting.ApplySelection(settings,caption,0,caption.Length,s=>s.Italic=1);
            SignFormatting.ApplySelection(settings,caption,5,9,s=>s.Ink="#FF0000FF");
            SignFormatting.ApplySelection(settings,caption,5,9,s=>s.Bold=1);
            SignFormatting.ApplySelection(settings,caption,5,9,s=>s.Size=2);
            SignFormatting.ApplySelection(settings,caption,5,9,s=>s.Ink=s.Ink.Substring(0,7)+"80");
            Check(settings.Runs.Count==3);
            var middle=settings.Runs[1];
            Check(middle.Start==5 && middle.Length==4 && middle.Style.Bold==1 && middle.Style.Italic==1 && middle.Style.Size==2 && middle.Style.Ink=="#FF000080");
            Check(settings.Runs[0].Style.Bold==-1 && settings.Runs[2].Style.Ink=="");
            SignFormatting.ApplySelection(settings,caption,5,9,s=>{s.Bold=s.Italic=-1;s.Size=0;s.Ink="";});
            Check(settings.Runs.Count==2 && settings.Runs[0].Start==0 && settings.Runs[0].Length==5 && settings.Runs[1].Start==9);
            Check(settings.Runs[0].Style.Italic==1 && settings.Runs[1].Style.Italic==1);
        });
        Run("Free text size disables wrapping at minimum scale; Fit restores wrapping",()=>{
            var env=Setup(); var widget=env.sign.m_textWidget;
            env.client.Preview(new SignSettings{Scale=.25f,TextSize=3,Fit=false},"Portal Home");
            Check(widget.textWrappingMode==TMPro.TextWrappingModes.NoWrap && !widget.enableAutoSizing);
            Near(widget.fontSize,60); Near(env.client.transform.localScale.x,.25f);
            env.client.Preview(new SignSettings{Fit=true},"Portal Home");
            Check(widget.textWrappingMode==TMPro.TextWrappingModes.Normal && widget.enableAutoSizing);
            env.client.EndPreview(); Check(widget.textWrappingMode==TMPro.TextWrappingModes.Normal);
        });
        Run("Version two migrates; combined effects and both curves persist",()=>{
            Check(SignSettings.TryDecode("2|1|1|0|0|9|0|1|0|0|0|1|2||1|",out var old));
            Check(old.Effect==2 && old.CurveVertical==0 && old.CurveDepth==0 && !old.Fit);
            old.Effect=3;old.CurveVertical=.25f;old.CurveDepth=-.5f;
            Check(SignSettings.TryDecode(old.Encode(),out var copy)&&copy.Encode()==old.Encode());
            Check(!new SignSettings{CurveVertical=float.NaN}.Valid && !new SignSettings{CurveDepth=1.01f}.Valid);
        });
        Run("Outline and shadow combine without changing shared font material",()=>{
            var env=Setup(sign=>sign.m_textWidget.fontSharedMaterial=new Material(null));
            var widget=env.sign.m_textWidget;var original=widget.fontSharedMaterial;
            env.client.Preview(new SignSettings{Effect=3},"Portal");
            Check(widget.fontSharedMaterial!=original && original.Keywords.Count==0);
            Check(widget.fontSharedMaterial.Keywords.Contains("OUTLINE_ON") && widget.fontSharedMaterial.Keywords.Contains("UNDERLAY_ON"));
            env.client.Preview(new SignSettings{Effect=1},"Portal");
            Check(widget.fontSharedMaterial.Keywords.Contains("OUTLINE_ON") && !widget.fontSharedMaterial.Keywords.Contains("UNDERLAY_ON"));
            env.client.EndPreview(); Check(widget.fontSharedMaterial==original);
        });
        Run("Curves combine across glyph and emoji meshes and restore cleanly",()=>{
            var root=new GameObject();var widget=new GameObject().AddComponent<TMPro.TextMeshProUGUI>();
            var appearance=new SignAppearance(root.transform,widget);
            TMPro.TMP_TextInfo Geometry() => new() {
                characterCount=3,
                characterInfo=new[] {
                    new TMPro.TMP_CharacterInfo{isVisible=true,materialReferenceIndex=0,vertexIndex=0},
                    new TMPro.TMP_CharacterInfo{isVisible=true,materialReferenceIndex=1,vertexIndex=0},
                    new TMPro.TMP_CharacterInfo{isVisible=false,materialReferenceIndex=0,vertexIndex=4}},
                meshInfo=new[] {
                    new TMPro.TMP_MeshInfo{vertices=new[]{new Vector3(-10,0,0),new Vector3(-10,2,0),new Vector3(0,2,0),new Vector3(0,0,0)}},
                    new TMPro.TMP_MeshInfo{vertices=new[]{new Vector3(0,0,0),new Vector3(0,2,0),new Vector3(10,2,0),new Vector3(10,0,0)}}}
            };
            appearance.Apply(new SignSettings{CurveVertical=.5f,CurveDepth=.25f});
            var first=Geometry();widget.Generate(first);
            Near(first.meshInfo[0].vertices[0].y,0);Near(first.meshInfo[0].vertices[3].y,5);Near(first.meshInfo[0].vertices[3].z,-2.5f);
            Near(first.meshInfo[1].vertices[0].y,5);Near(first.meshInfo[1].vertices[0].z,-2.5f);
            var second=Geometry();widget.Generate(second);Near(second.meshInfo[0].vertices[3].y,5);
            appearance.Apply(new SignSettings{CurveVertical=-.5f,CurveDepth=-.25f});
            var reverse=Geometry();widget.Generate(reverse);Near(reverse.meshInfo[0].vertices[3].y,-5);Near(reverse.meshInfo[0].vertices[3].z,2.5f);
            appearance.Restore();var flat=Geometry();widget.Generate(flat);Near(flat.meshInfo[0].vertices[3].y,0);
            appearance.Dispose();Check(widget.GeometryHandlers==0);
        });
        Run("Background tracks rendered caption bounds, offsets and empty text",()=>{
            var root=new GameObject();var widget=new GameObject().AddComponent<TMPro.TextMeshProUGUI>();
            widget.rectTransform.localScale=new Vector3(.025f,.025f,.025f);
            var appearance=new SignAppearance(root.transform,widget);
            appearance.Apply(new SignSettings{Scale=.25f,TextSize=3,Fit=false,Background=2,OffsetUnit=1,Horizontal=40,Depth=40});
            var background=(UnityEngine.UI.Image)typeof(SignAppearance).GetField("_background",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(appearance);
            TMPro.TMP_TextInfo Geometry(float width,float height) => new() {
                characterCount=1,characterInfo=new[]{new TMPro.TMP_CharacterInfo{isVisible=true}},
                meshInfo=new[]{new TMPro.TMP_MeshInfo{vertices=new[]{new Vector3(10,-2,0),new Vector3(10,height-2,0),new Vector3(10+width,height-2,0),new Vector3(10+width,-2,0)}}}
            };
            widget.Generate(Geometry(20,10));
            Near(background.rectTransform.localPosition.z,-.99975f);
            Near(background.rectTransform.sizeDelta.x,38);Near(background.rectTransform.sizeDelta.y,28);
            Near(background.rectTransform.localPosition.x,1.5f);Near(background.rectTransform.localPosition.y,.075f);
            widget.Generate(Geometry(60,30));
            Near(background.rectTransform.sizeDelta.x,78);Near(background.rectTransform.sizeDelta.y,48);
            Near(background.rectTransform.localPosition.x,2);Near(root.transform.localScale.x,.25f);
            widget.Generate(new TMPro.TMP_TextInfo{characterCount=0});Check(!background.gameObject.activeSelf);
            widget.Generate(Geometry(20,10));Check(background.gameObject.activeSelf);
            appearance.Restore();Check(!background.gameObject.activeSelf);appearance.Dispose();
        });
        Run("Depth offsets move along text normal, never accumulate and cancel restores",()=>{
            var env=Setup(sign=>sign.m_textWidget.rectTransform.localScale=new Vector3(.025f,.025f,.025f));
            var widget=env.sign.m_textWidget;
            env.client.Preview(new SignSettings{OffsetUnit=1,Depth=40},"Portal");
            Near(widget.rectTransform.localPosition.z,-1);Near(widget.rectTransform.localPosition.x,0);
            env.client.Preview(new SignSettings{OffsetUnit=1,Depth=40},"Portal");Near(widget.rectTransform.localPosition.z,-1);
            env.client.Preview(new SignSettings{OffsetUnit=1,Depth=-40},"Portal");Near(widget.rectTransform.localPosition.z,1);
            widget.rectTransform.localRotation=Quaternion.Euler(0,90,0);
            env.client.Preview(new SignSettings{OffsetUnit=1,Depth=40},"Portal");Near(widget.rectTransform.localPosition.x,-1);
            env.client.EndPreview();Near(widget.rectTransform.localPosition.x,0);Near(widget.rectTransform.localPosition.z,0);
        });
        Run("Depth persists, defaults to zero for old designs and validates bounds",()=>{
            Check(SignSettings.TryDecode("3|1|1|0|0|9|0|1|0|0|0|1|3||1||0.25|-0.5",out var old));
            Check(old.Depth==0 && old.CurveDepth==-.5f);
            old.Depth=-40;Check(SignSettings.TryDecode(old.Encode(),out var saved)&&saved.Depth==-40&&saved.Effect==3);
            Check(!new SignSettings{Depth=float.NaN}.Valid && !new SignSettings{Depth=21}.Valid);
            Check(new SignSettings{OffsetUnit=1,Depth=2000}.Valid && !new SignSettings{OffsetUnit=1,Depth=2001}.Valid);
            var env=Setup(sign=>{sign.m_textWidget.rectTransform.localScale=new Vector3(.025f,.025f,.025f);sign.m_textWidget.rectTransform.sizeDelta=new Vector2(100,50);});
            env.client.Preview(new SignSettings{Depth=.4f},"Portal");Near(env.sign.m_textWidget.rectTransform.localPosition.z,-1);
            env.sign.m_textWidget.isOrthographic=false;
            env.client.Preview(new SignSettings{OffsetUnit=1,Depth=40},"Portal");Near(env.sign.m_textWidget.rectTransform.localPosition.z,-.1f);
        });
        Run("Malformed and excessive style records fail closed",()=>{
            Check(!SignFormatting.TryDecodeRuns("not base64",out _));
            var bad=new SignSettings(); bad.Runs.Add(new StyleRun{Start=1024,Length=1}); Check(!bad.Valid);
            bad.Runs.Clear(); bad.Runs.Add(new StyleRun{Start=2,Length=4});bad.Runs.Add(new StyleRun{Start=3,Length=1});Check(!bad.Valid);
            Check(!SignSettings.TryDecode(new string('x',SignSettings.RecordLimit+1),out _));
        });
        Run("Text-unit offsets match TMP scaling including perspective text",()=>{
            var env=Setup(sign=>{sign.m_textWidget.rectTransform.localScale=new Vector3(.025f,.025f,.025f);});
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{OffsetUnit=1,Horizontal=-40,Vertical=40}.Encode());
            env.client.Render(); Near(env.sign.m_textWidget.rectTransform.localPosition.x,-1);Near(env.sign.m_textWidget.rectTransform.localPosition.y,1);
            env.sign.m_textWidget.isOrthographic=false;
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{OffsetUnit=1,Vertical=80}.Encode());
            env.client.Render();Near(env.sign.m_textWidget.rectTransform.localPosition.y,.2f);
        });
        Run("Local preview never writes and cancel restores authoritative appearance",()=>{
            var env=Setup(); string original=env.client.NativeText;
            env.client.Preview(new SignSettings{Scale=3,OffsetUnit=1,Vertical=40,Fit=false},"Unsaved label");
            Check(env.client.Raw=="" && env.client.NativeText==original && env.sign.Writes==0);
            Near(env.client.transform.localScale.x,3); Check(env.sign.m_textWidget.textPreprocessor.PreprocessText("Original").Contains("Unsaved label"));
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=2}.Encode());
            env.client.EndPreview();Near(env.client.transform.localScale.x,2);
            Check(!env.sign.m_textWidget.textPreprocessor.PreprocessText("Original").Contains("Unsaved label"));
        });
        Run("Permission denial cannot expose the local draft through preprocessing",()=>{
            var env=Setup(); env.client.Preview(new SignSettings(),"Secret draft"); SignAccess.Viewable=false;
            try { string output=env.sign.m_textWidget.textPreprocessor.PreprocessText("HIDDEN"); Check(output.Contains("HIDDEN")&&!output.Contains("Secret")); }
            finally { SignAccess.Viewable=true;env.client.EndPreview(); }
        });
        Run("Placement stamps caption and appearance once; reload keeps the design",()=>{
            var config=new BepInEx.Configuration.ConfigFile(); SignPlacement.Bind(config);
            Player.m_localPlayer=new GameObject().AddComponent<Player>();
            var design=new SignSettings{OffsetUnit=1,Vertical=40,Scale=2,Fit=false,Ink="#FF000080",Effect=1};
            design.Runs=SignFormatting.Apply("Portal Home",new(),7,11,s=>s.Bold=1);
            SignPlacement.UseDraft(design,"Portal Home");
            var env=Setup(); env.sign.gameObject.AddComponent<Piece>();Network.Views[2].Data.Owner=2;
            env.client.OnPlaced(); Check(env.client.NativeText=="Portal Home" && env.sign.Writes==1);
            Check(SignSettings.TryDecode(env.client.Raw,out var saved)&&saved.Runs.Count==1&&saved.Vertical==40);
            env.client.OnPlaced();Check(env.sign.Writes==1);
            var data=new ZDO();data.CopyFrom(Network.Views[2].Data);var loaded=Create(3,data);
            Check(loaded.NativeText=="Portal Home"&&loaded.Raw==design.Encode());
            SignPlacement.Bind(new BepInEx.Configuration.ConfigFile());
        });
        Run("Saving a live draft keeps plain text and restores the committed style",()=>{
            var env=Setup(); var design=new SignSettings{Scale=2,OffsetUnit=1,Vertical=40};
            design.Runs=SignFormatting.Apply("New label",new(),0,3,s=>s.Bold=1);
            env.client.Preview(design,"New label");bool success=false;ZNet.Session=2;
            env.client.BeginSave(design,"New label","","Original",(ok,_)=>success=ok);
            Rpc();Rpc();Data();Tick(env.client,2);Check(success);env.client.EndPreview();
            Check(env.client.NativeText=="New label" && env.client.Raw==design.Encode());
            Check(env.sign.m_textWidget.textPreprocessor.PreprocessText("New label").Contains("<b>New</b>"));
        });
    }
    private static void Run(string name,Action test) { test(); _passed++; Console.WriteLine("PASS "+name); }
    private static void Check(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
    private static void Near(float actual,float expected) { if(Math.Abs(actual-expected)>.00001f) throw new Exception($"Expected {expected}, received {actual}"); }
    private static (SignRuntime client,Sign sign) Setup(Action<Sign> configure=null)
    {
        Network.Views.Clear(); Network.RpcQueue.Clear(); Network.DataQueue.Clear();
        Network.LocalAccess=Network.SenderAccess=true; Time.unscaledTime=0; ZNet.Clock=100;
        Create(1); var client=Create(2,null,configure); return(client,Network.Views[2].GetComponent<Sign>());
    }
    private static SignRuntime Create(long session,ZDO saved=null,Action<Sign> configure=null)
    {
        ZNet.Session=session; var go=new GameObject(); var view=go.AddComponent<ZNetView>(); view.Data.Owner=1;
        if(saved!=null) view.Data.CopyFrom(saved);
        Network.Views[session]=view; var sign=go.AddComponent<Sign>();
        sign.m_textWidget=new GameObject().AddComponent<TMPro.TextMeshProUGUI>();
        configure?.Invoke(sign);
        return SignRuntime.Attach(sign);
    }
    private static void Begin(SignRuntime runtime,Action<bool,string> result)
    { ZNet.Session=2; runtime.BeginSave(new SignSettings{Scale=2},"New caption","","Original",result); }
    private static void Rpc()=>Network.RpcQueue.Dequeue()();
    private static void Data()=>Network.DataQueue.Dequeue()();
    private static void Tick(SignRuntime runtime,long session) { ZNet.Session=session; TickMethod.Invoke(runtime,null); }
}
