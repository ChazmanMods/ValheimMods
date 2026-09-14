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
            Check(!new SignSettings{TextSize=.1f}.Valid); Check(!new SignSettings{Horizontal=.5f}.Valid);
            Check(!new SignSettings{Color=22}.Valid); Check(!new SignSettings{Background=3}.Valid);
            Check(!SignSettings.TryDecode("1|1|1|0|0|9|0|1|2|0",out _));
        });
        Run("Text length, multiline, Unicode and controls",()=>{
            Check(SignSettings.ValidText("Food\nMead 🍺")); Check(SignSettings.ValidText(new string('x',256)));
            Check(!SignSettings.ValidText(new string('x',257))); Check(!SignSettings.ValidText("x\0y"));
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
            Check(env.sign.m_textWidget.text=="HIDDEN BY VANILLA"&&!env.sign.m_textWidget.richText);
        });
        Run("Headless sign applies physical size without a text widget",()=>{
            var env=Setup(); env.sign.m_textWidget=null;
            Network.Views[2].Data.Set(SignRuntime.SettingsKey,new SignSettings{Scale=4}.Encode()); env.client.Render();
            Check(env.client.transform.localScale.x==4);
        });
        Console.WriteLine($"PASS: {_passed} tests. Production save/render code exercised with simulated peers; no live game certification implied.");
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
