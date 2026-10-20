using HarmonyLib;
using ArcaneWaterWorks.Pools;
using TMPro;
using UnityEngine;

namespace ArcaneWaterWorks;
internal sealed class RiverEditor:MonoBehaviour
{
    internal static Spring Target;
    internal static bool Open;
    static RiverEditor instance;
    static bool guiding;
    static float width=4,depth=.8f,length=48;
    static string status="";
    PondMenu menu;
    TMP_Text feedback;
    bool appearance;
    void Awake(){instance=this;}
    internal static void Show(Spring spring){
        GardenIntegration.CloseEditors();HotSpringEditor.Close();Close();Target=spring;width=spring.Route?.Width??4;depth=spring.Route?.Depth??.8f;status=spring.Status;Open=true;
        RiverHoe.Select(spring);if(instance){instance.appearance=false;instance.EnsurePanel();}
    }
    internal static void Close(){Open=false;guiding=false;Target=null;if(instance){instance.menu?.Dispose();instance.menu=null;}}
    static void Tell(string text){status=text;if(Player.m_localPlayer)Player.m_localPlayer.Message(MessageHud.MessageType.Center,text);}
    void Update(){
        if(!Target||!Player.m_localPlayer||!Target.Live){if(Open||guiding)Close();return;}
        if(Open){EnsurePanel();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(feedback)feedback.text=Target.Busy?"Saving river...":status;
            if(UnityEngine.Input.GetKeyDown(KeyCode.Escape))Close();return;}
        if(!guiding||InventoryGui.IsVisible()||Menu.IsVisible()||global::Console.IsVisible()||Chat.instance&&Chat.instance.HasFocus())return;
        if(Plugin.Finish.Value.IsDown()){Tell("River saved.");Close();return;}
        if(Plugin.AddPoint.Value.IsDown()){RiverHoe.Extend(Player.m_localPlayer.transform.position);if(RiverHoe.Selected)Target=RiverHoe.Selected;}
    }
    void Page(bool next){menu?.Dispose();menu=null;appearance=next;EnsurePanel();}
    void EnsurePanel(){
        if(menu!=null)return;
        if(appearance){AppearancePanel();return;}
        menu=new PondMenu(970);menu.Heading("ARCANE WATERS");
        menu.Heading("Shape this section; extend as far as you wish with the hoe.",65,18,48);
        float y=120;
        menu.Range("Channel width",2,12,width,v=>width=Mathf.Round(v*2)/2,()=>width.ToString("0.0")+" m",ref y);
        menu.Range("Riverbed depth",.4f,8,depth,v=>depth=Mathf.Round(v*10)/10,()=>depth.ToString("0.0")+" m",ref y);
        menu.Range("Automatic downhill section",4,96,length,v=>length=Mathf.Round(v/2)*2,()=>length.ToString("0")+" m",ref y);
        menu.Action("Apply width and depth",y,()=>{if(!Target||Target.Busy)return;var r=Target.Route?.Copy();if(r==null)return;r.Width=width;r.Depth=depth;status=Target.Save(r)?"Section updated.":Target.Status;});y+=48;
        menu.Action("Create downhill section",y,()=>Generate(length));y+=48;
        menu.Action("Select section for hoe tools",y,()=>{RiverHoe.Select(Target);Tell("River Start chooses a branch. Alt + mouse wheel resizes trench reach. Configure River adjusts width.");Close();});y+=48;
        menu.Action("Guide on foot ("+Plugin.AddPoint.Value+")",y,()=>{RiverHoe.Select(Target);Open=false;guiding=true;menu.Dispose();menu=null;Tell(Plugin.AddPoint.Value+": extend at your feet. "+Plugin.Finish.Value+": finish.");});y+=48;
        menu.Action("Undo last segment in this section",y,()=>{RiverHoe.Select(Target);RiverHoe.Undo();status=Target.Status;});y+=48;
        menu.Action("Configure nearby River Ambience Stone",y,()=>{var p=Player.m_localPlayer.transform.position;if(RiverGardens.AtSection(p)){Close();if(!RiverGardens.OpenSection(p))Tell("Move closer to the River Ambience Stone.");}else status="Place a River Ambience Stone from Hammer > Arcane Natural Waters beside this river.";});y+=48;
        menu.Action("River sound, whitewater and muddy bed",y,()=>Page(true));y+=48;
        menu.Action("Configure nearby hot spring",y,()=>{
            HotSpring closest=null;float best=float.MaxValue;
            foreach(var pool in HotSpring.All)if(pool&&GardenIntegration.CanEdit(pool.Pond)){float d=(pool.transform.position-Player.m_localPlayer.transform.position).sqrMagnitude;if(d<best){best=d;closest=pool;}}
            if(closest)HotSpringEditor.Show(closest);else status="Place an Arcane Hot Spring from Hammer > Arcane Natural Waters, or move closer to its control stone.";
        });y+=48;
        menu.Heading("River Ambience Stones control sounds and fireflies along 20 m of river.\nNew branches have independent settings. Hoe: Configure River, End River Branch, Reopen River End.",y,18,104);y+=108;
        feedback=menu.Heading(status,y,17,48);menu.Action("Close",912,Close);
    }
    void AppearancePanel(){
        var r=Target.Route?.Copy();if(r==null){appearance=false;EnsurePanel();return;}
        menu=new PondMenu(820);menu.Heading("ARCANE WATERS");
        menu.Heading("Sound and appearance - this river section",65,20,48);
        float y=120;
        menu.Range("River volume",0,1,r.Volume,v=>r.Volume=v,()=>Mathf.RoundToInt(r.Volume*100)+"%",ref y);
        string[] sounds={"Automatic by width","Brook","Mid-Size River","Large River"};
        menu.Range("River sound",-1,2,r.Sound,v=>r.Sound=Mathf.RoundToInt(v),()=>sounds[r.Sound+1],ref y);
        menu.Range("River whitewater",0,1,r.Whitewater,v=>r.Whitewater=v,()=>Mathf.RoundToInt(r.Whitewater*100)+"%",ref y);
        menu.Range("Native dirt riverbed",0,1,r.Mud?1:0,v=>r.Mud=v>=.5f,()=>r.Mud?"On":"Off",ref y);
        menu.Action("Save sound and appearance",y,()=>{if(!Target||Target.Busy)return;var current=Target.Route?.Copy();if(current==null)return;current.CopyAppearance(r);status=Target.Save(current)?"Sound and appearance saved.":Target.Status;});y+=48;
        menu.Heading("Automatic sound: under 4 m = Brook; 4 to under 8 m = Mid-Size; 8-12 m = Large. Whitewater increases on steeper reaches at every width. Volume 0 mutes. Local audio and River Ambience Stone volume also apply.\n\nHoe > Smooth River Banks softens an 8 m area. Undo Bank Smoothing reverses the last stroke. At a join, select and smooth each adjoining section.",y,18,168);y+=174;
        feedback=menu.Heading(status,y,17,48);
        menu.Action("Back to river shaping",710,()=>Page(false));menu.Action("Close",760,Close);
    }
    void Generate(float metres){if(!Target||Target.Busy)return;var route=Target.Trace(metres,width,depth,out status);if(route!=null){if(Target.Route!=null)route.CopyAppearance(Target.Route);if(!Target.Save(route))status=Target.Status;}}
    void OnDestroy(){Close();instance=null;}
}
[HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
internal static class RiverCursor
{
    static void Postfix(){if(RiverEditor.Open){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
}
