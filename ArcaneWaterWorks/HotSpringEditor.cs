using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;
internal sealed class HotSpringEditor:MonoBehaviour
{
    internal static HotSpring Target;
    static PondMenu menu;
    static GardenSettings garden;
    static HotSpringSettings settings;
    static int page;
    internal static void Show(HotSpring pool){
        RiverEditor.Close();GardenIntegration.CloseEditors();Close();Target=pool;
        garden=GardenIntegration.Settings(pool.Pond);settings=pool.Settings.Copy();
        var group=GardenIntegration.GroupSettings(pool.Pond);
        garden.FlowerScale=group.FlowerScale;garden.FloatLanterns=group.FloatLanterns;garden.FloatFountains=group.FloatFountains;garden.FloatStatues=group.FloatStatues;
        page=0;Panel();
    }
    internal static void Close(){menu?.Dispose();menu=null;Target=null;HotSpringAudio.StopPreview();}
    static void Page(int next){menu?.Dispose();menu=null;page=next;Panel();}
    static string Percent(float value)=>Mathf.RoundToInt(value*100)+"%";
    static void Panel(){
        menu=new PondMenu(960);menu.Heading("ARCANE WATERS");menu.Heading(Target.PoolName,65,23,40);
        float y=120;
        if(page==0){
            menu.Range("Pool width",4,24,garden.Width,v=>garden.Width=Mathf.Round(v*2)/2,()=>garden.Width.ToString("0.0")+" m",ref y);
            menu.Range("Pool depth",.8f,3,garden.Depth,v=>garden.Depth=Mathf.Round(v*10)/10,()=>garden.Depth.ToString("0.0")+" m",ref y);
            menu.Range("Rising steam",0,1,settings.Steam,v=>settings.Steam=v,()=>Percent(settings.Steam),ref y);
            menu.Range("Bubbling water",0,1,settings.Bubbles,v=>settings.Bubbles=v,()=>Percent(settings.Bubbles),ref y);
            menu.Option("Warded pool",()=>settings.Warded,v=>settings.Warded=v,ref y);
            menu.Action("Bubbling sounds and music",y,()=>Page(1));y+=48;
            menu.Action("Pool ambience",y,()=>Page(2));y+=48;
            menu.Heading("Naturally heated - no fuel. Submerged white stones provide individual sitting spots; larger pools receive more stones. Everyone must stand before resizing.\n\nThe optional ward keeps creatures, including pets, outside the pool and a 4 m margin. Players can enter freely.",y,18,230);
        }else if(page==1){
            menu.Range("Bubbling loop",0,5,settings.Sound,v=>settings.Sound=Mathf.RoundToInt(v),()=>HotSpringAudio.SoundNames[settings.Sound],ref y);
            menu.Range("Bubbling volume",0,1,settings.Volume,v=>settings.Volume=v,()=>Percent(settings.Volume),ref y);
            menu.Range("Playback speed",.5f,1.5f,garden.Speed,v=>garden.Speed=v,()=>garden.Speed.ToString("0.00")+"x",ref y);
            menu.Range("Audible distance",5,40,garden.Range,v=>garden.Range=Mathf.Round(v),()=>garden.Range.ToString("0")+" m",ref y);
            menu.Action("Preview bubbling loop",y,()=>HotSpringAudio.Preview(settings.Sound,settings.Volume,garden.Speed));y+=48;
            menu.Range("Pool music",0,6,settings.Music,v=>settings.Music=Mathf.RoundToInt(v),()=>settings.Music==0?"Valheim music":"AW_HSMusic_"+settings.Music.ToString("000"),ref y);
            menu.Range("Pool music volume",0,1,settings.MusicVolume,v=>settings.MusicVolume=v,()=>Percent(settings.MusicVolume),ref y);
            menu.Heading("Five seamless bubbling loops from your three recordings. Music choices use all six supplied songs. Pool music replaces Valheim music only while inside this pool and follows the game's music-volume setting.",y,18,110);
        }else{
            menu.Range("Waterfall volume",0,1,garden.WaterfallVolume,v=>garden.WaterfallVolume=v,()=>Percent(garden.WaterfallVolume),ref y);
            menu.Range("Waterfall distance",5,100,garden.WaterfallRange,v=>garden.WaterfallRange=Mathf.Round(v),()=>garden.WaterfallRange.ToString("0")+" m",ref y);
            menu.Range("Garden ambience",0,7,garden.Track,v=>garden.Track=Mathf.RoundToInt(v),()=>garden.Track==0?"Off":"Night ambience "+garden.Track.ToString("000"),ref y);
            menu.Range("Garden ambience volume",0,1,garden.Volume,v=>garden.Volume=v,()=>Percent(garden.Volume),ref y);
            menu.Range("Fireflies",0,100,garden.Fireflies,v=>garden.Fireflies=Mathf.RoundToInt(v),()=>garden.Fireflies.ToString(),ref y);
            menu.Option("Fireflies / garden ambience at night only",()=>garden.NightOnly,v=>garden.NightOnly=v,ref y);
            menu.Heading("Bubbling and music also work during the day.",y,17,50);
        }
        if(page!=0)menu.Action("Back to pool settings",802,()=>Page(0));
        menu.Action("Save all hot spring settings",850,()=>{if(Target&&Target.Save(garden,settings))Close();else Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"Unable to save: check access and occupied seats.");});
        menu.Action("Cancel",900,Close);
    }
    void Update(){if(Target&&(!GardenIntegration.CanEdit(Target.Pond)||UnityEngine.Input.GetKeyDown(KeyCode.Escape)))Close();}
    void OnDisable()=>Close();
}
