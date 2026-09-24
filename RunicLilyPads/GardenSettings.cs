using System;
using System.Globalization;
namespace RunicLilyPads
{
    internal sealed class GardenSettings
    {
        public float Width=6, Depth=1.2f, Volume=.35f, Speed=1, Range=18;
        public int Track=1, Fireflies=30;
        public bool NightOnly=true;
        public float FlowerScale=.5f,WaterfallVolume=.35f,WaterfallRange=16;
        public bool FloatLanterns=true,FloatFountains=true,FloatStatues=true;
        public long GroupRevision;
        public GardenSettings Copy() => (GardenSettings)MemberwiseClone();
        public bool Valid => Finite(Width,4,24)&&Finite(Depth,.4f,3)&&Finite(Volume,0,1)&&Finite(Speed,.5f,1.5f)&&Finite(Range,5,40)&&Track>=0&&Track<=7&&Fireflies>=0&&Fireflies<=100&&Finite(FlowerScale,.15f,1.5f)&&GroupRevision>=0&&Finite(WaterfallVolume,0,1)&&Finite(WaterfallRange,5,100);
        static bool Finite(float n,float low,float high)=>!float.IsNaN(n)&&!float.IsInfinity(n)&&n>=low&&n<=high;
        public string Encode()=>string.Join("|","4",F(Width),F(Depth),Track,F(Volume),F(Speed),F(Range),Fireflies,NightOnly?1:0,F(FlowerScale),FloatLanterns?1:0,FloatFountains?1:0,FloatStatues?1:0,GroupRevision,F(WaterfallVolume),F(WaterfallRange));
        static string F(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
        public static bool TryDecode(string raw,out GardenSettings s)
        {
            s=new GardenSettings(); if(string.IsNullOrEmpty(raw)) return true;
            if(raw.Length>256)return false; var p=raw.Split('|');
            bool old=p.Length==9&&p[0]=="1";
            if(!old){
                bool current=p.Length==16&&p[0]=="4";
                bool v3=p.Length==15&&p[0]=="3";
                if(!current&&!v3&&(p.Length!=14||p[0]!="2"))return false;
                if((current||v3)&&!float.TryParse(p[14],NumberStyles.Float,CultureInfo.InvariantCulture,out s.WaterfallVolume))return false;
                if(current&&!float.TryParse(p[15],NumberStyles.Float,CultureInfo.InvariantCulture,out s.WaterfallRange))return false;
                if(!float.TryParse(p[9],NumberStyles.Float,CultureInfo.InvariantCulture,out s.FlowerScale)||!Bool(p[10],out s.FloatLanterns)||!Bool(p[11],out s.FloatFountains)||!Bool(p[12],out s.FloatStatues)||!long.TryParse(p[13],NumberStyles.Integer,CultureInfo.InvariantCulture,out s.GroupRevision))return false;
            }
            return float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Width)&&float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Depth)&&int.TryParse(p[3],out s.Track)&&float.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Volume)&&float.TryParse(p[5],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Speed)&&float.TryParse(p[6],NumberStyles.Float,CultureInfo.InvariantCulture,out s.Range)&&int.TryParse(p[7],out s.Fireflies)&&(p[8]=="0"||p[8]=="1")&&SetNight(s,p[8])&&s.Valid;
        }
        static bool Bool(string p,out bool value){value=p=="1";return p=="0"||p=="1";}
        internal void CopyGroupFrom(GardenSettings other){FlowerScale=other.FlowerScale;FloatLanterns=other.FloatLanterns;FloatFountains=other.FloatFountains;FloatStatues=other.FloatStatues;GroupRevision=other.GroupRevision;}
        internal bool SameGroup(GardenSettings other)=>FlowerScale==other.FlowerScale&&FloatLanterns==other.FloatLanterns&&FloatFountains==other.FloatFountains&&FloatStatues==other.FloatStatues;
        static bool SetNight(GardenSettings s,string p){s.NightOnly=p=="1";return true;}
    }
    internal static class PondMath
    {
        public static double Aspect(int shape)=>shape==1?.65:1;
        public static double Radius(int shape,double angle)
        {
            if(shape==2){ angle=Math.Atan2(Math.Sin(angle),Math.Cos(angle));return 1-.40*Math.Exp(-angle*angle/.32); }
            if(shape==3)return .90+.07*Math.Sin(3*angle)+.03*Math.Cos(5*angle);
            return 1;
        }
        public static double Distance(int shape,double x,double z,double width)
        {
            x/=width*.5;z/=width*.5*Aspect(shape);return Math.Sqrt(x*x+z*z)/Radius(shape,Math.Atan2(z,x));
        }
        public static double Bed(double original,double water,double depth,double distance,double width=6)
        {
            // A level grassy margin keeps coarse terrain triangles from lifting the
            // shoreline above the water. No raised or dirt-painted rim.
            double shelf=1+4/width,outside=1+6/width;
            if(distance>=outside)return original;
            if(distance<=1){ double t=Math.Max(0,(distance-.45)/.55); t=t*t*(3-2*t); return water-depth+(depth-.025)*t; }
            if(distance<=shelf)return water-.025;
            double blend=(distance-shelf)/(outside-shelf);blend=blend*blend*(3-2*blend); return (water-.025)*(1-blend)+original*blend;
        }
    }
}

