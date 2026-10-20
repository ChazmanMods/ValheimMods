using System.Globalization;
namespace RunicLilyPads
{
    internal sealed class FeatureSettings
    {
        public float Light=1.5f,Range=6,Glow=2,Volume=.4f;public bool Flow=true;
        public FeatureSettings Copy()=>(FeatureSettings)MemberwiseClone();
        static bool Between(float v,float a,float b)=>!float.IsNaN(v)&&!float.IsInfinity(v)&&v>=a&&v<=b;
        public bool Valid=>Between(Light,0,5)&&Between(Range,2,12)&&Between(Glow,0,8)&&Between(Volume,0,1);
        public string Encode()=>string.Join("|","1",Light.ToString("R",CultureInfo.InvariantCulture),Range.ToString("R",CultureInfo.InvariantCulture),Glow.ToString("R",CultureInfo.InvariantCulture),Volume.ToString("R",CultureInfo.InvariantCulture),Flow?"1":"0");
        public static bool TryDecode(string raw,out FeatureSettings s){s=new FeatureSettings();if(string.IsNullOrEmpty(raw))return true;var p=raw.Split('|');if(p.Length!=6||p[0]!="1")return false;return float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Light)&&float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Range)&&float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Glow)&&float.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Volume)&&(p[5]=="0"||p[5]=="1")&&SetFlow(s,p[5])&&s.Valid;}
        static bool SetFlow(FeatureSettings s,string value){s.Flow=value=="1";return true;}
    }
}
