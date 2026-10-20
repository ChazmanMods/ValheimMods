using System;
using System.Globalization;

namespace ArcaneWaterWorks;
internal sealed class HotSpringSettings
{
    internal int Sound=1,Music=1;
    internal float Volume=.4f,MusicVolume=.5f,Steam=.5f,Bubbles=.5f;
    internal bool Warded;
    internal HotSpringSettings Copy()=>(HotSpringSettings)MemberwiseClone();
    internal bool Valid=>Sound>=0&&Sound<=5&&Music>=0&&Music<=6&&Unit(Volume)&&Unit(MusicVolume)&&Unit(Steam)&&Unit(Bubbles);
    static bool Unit(float n)=>!float.IsNaN(n)&&n>=0&&n<=1;
    static string F(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
    internal string Encode()=>string.Join("|","1",Sound,Music,F(Volume),F(MusicVolume),F(Steam),F(Bubbles),Warded?1:0);
    internal static bool TryDecode(string raw,out HotSpringSettings s){
        s=new HotSpringSettings();if(string.IsNullOrEmpty(raw))return true;
        if(raw.Length>128)return false;var p=raw.Split('|');
        return p.Length==8&&p[0]=="1"&&int.TryParse(p[1],out s.Sound)&&int.TryParse(p[2],out s.Music)&&
            float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Volume)&&
            float.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out s.MusicVolume)&&
            float.TryParse(p[5],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Steam)&&
            float.TryParse(p[6],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Bubbles)&&
            (p[7]=="0"||p[7]=="1")&&SetWard(s,p[7])&&s.Valid;
    }
    static bool SetWard(HotSpringSettings s,string value){s.Warded=value=="1";return true;}
    // Seat density follows pool perimeter rather than unbounded area growth.
    internal static int SeatCount(float width,int shape)=>Math.Clamp((int)Math.Round(width*(shape==1?.8:1)),4,24);
    internal static float SeatContour(float depth){
        double bed=Math.Min(.45,depth*.4)+.25;
        double target=Math.Clamp((depth-bed)/(depth-.025),0,1),low=0,high=1;
        for(int i=0;i<24;i++){double mid=(low+high)*.5;if(mid*mid*(3-2*mid)<target)low=mid;else high=mid;}
        return (float)(.45+.55*(low+high)*.5);
    }
}
