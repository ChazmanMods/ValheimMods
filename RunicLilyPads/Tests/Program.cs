using System;
using System.Globalization;
using RunicLilyPads;
int checks=0;
void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
void Near(double a,double b,string label)=>Check(Math.Abs(a-b)<.00001,label);
foreach(string culture in new[]{"en-US","de-DE","fr-FR"}){
    CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
    for(int track=0;track<=7;track++)foreach(float size in new[]{4f,6f,12f,24f})foreach(float depth in new[]{.4f,1.2f,3f}){
        var s=new GardenSettings{Track=track,Width=size,Depth=depth,NightOnly=track%2==0};
        Check(s.Valid,"valid settings");Check(GardenSettings.TryDecode(s.Encode(),out var copy)&&copy.Encode()==s.Encode(),"round trip");
        var c=s.Copy();c.Width=7;Near(s.Width,size,"copy isolation");
    }
}
foreach(string raw in new[]{"2|6|1|1|.3|1|18|30|1","1|NaN|1|1|.3|1|18|30|1","1|6|Infinity|1|.3|1|18|30|1","1|3|1|1|.3|1|18|30|1","1|6|1|8|.3|1|18|30|1","1|6|1|1|.3|1|18|101|1","1|6|1|1|.3|1|18|30|2",new string('x',257)})Check(!GardenSettings.TryDecode(raw,out _),"invalid data rejected");
for(int shape=0;shape<4;shape++)foreach(double width in new[]{4d,6d,12d,24d}){
    Near(PondMath.Distance(shape,0,0,width),0,"center");
    for(int i=0;i<360;i++){
        double a=i*Math.PI/180,r=PondMath.Radius(shape,a);Check(r>.5&&r<=1.01,"positive bounded radius");
        double x=Math.Cos(a)*r*width*.5,z=Math.Sin(a)*r*width*.5*PondMath.Aspect(shape);
        Near(PondMath.Distance(shape,x,z,width),1,"edge consistency");
        Check(PondMath.Distance(shape,x*1.4,z*1.4,width)>1.35,"outside pond footprint");
    }
    foreach(double depth in new[]{.4,1.2,3}){
        Near(PondMath.Bed(42,40,depth,0),40-depth,"configured floor depth");
        Near(PondMath.Bed(42,40,depth,1),39.975,"shore meets waterline");
        Near(PondMath.Bed(42,40,depth,2),42,"outside terrain untouched");
        Near(PondMath.Bed(42,40,depth,2-1e-8),42,"smooth outer transition");
        double previous=40-depth;
        for(int step=0;step<=100;step++){double h=PondMath.Bed(42,40,depth,step/100d);Check(h>=previous-1e-6,"monotonic bank");previous=h;}
    }
}
foreach(string culture in new[]{"en-US","de-DE","fr-FR"}){
    CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
    foreach(float light in new[]{0f,1.5f,5f})foreach(float glow in new[]{0f,2f,8f})foreach(float volume in new[]{0f,.4f,1f})foreach(bool flow in new[]{false,true}){
        var settings=new FeatureSettings{Light=light,Glow=glow,Volume=volume,Flow=flow};
        Check(settings.Valid&&FeatureSettings.TryDecode(settings.Encode(),out var restored)&&restored.Encode()==settings.Encode(),"feature settings round trip");
        var copy=settings.Copy();copy.Volume=.123f;Check(settings.Volume==volume,"feature draft isolated");
    }
}
foreach(string raw in new[]{"2|1|6|2|.4|1","1|NaN|6|2|.4|1","1|1|Infinity|2|.4|1","1|1|6|9|.4|1","1|1|6|2|1.1|1","1|1|6|2|.4|2","1|1|1|2|.4|1","1|6|6|2|.4|1"})Check(!FeatureSettings.TryDecode(raw,out _),"invalid feature settings rejected");
Check(GardenSettings.TryDecode("1|6|1.2|1|.35|1|18|30|1",out var legacy)&&legacy.FlowerScale==.5f&&legacy.FloatLanterns,"v1 saves migrate safely");
foreach(string bad in new[]{"2|25|1|1|.3|1|18|30|1|.5|1|1|1|0","2|24|1|1|.3|1|18|30|1|NaN|1|1|1|0","2|24|1|1|.3|1|18|30|1|.5|2|1|1|0","2|24|1|1|.3|1|18|30|1|.5|1|1|1|-1"})Check(!GardenSettings.TryDecode(bad,out _),"new field validation");
foreach(float size in new[]{.15f,.5f,1.5f})foreach(bool enabled in new[]{true,false}){
 var settings=new GardenSettings{FlowerScale=size,FloatLanterns=enabled,FloatFountains=!enabled,FloatStatues=enabled,GroupRevision=123456789};
 Check(GardenSettings.TryDecode(settings.Encode(),out var restored)&&restored.Encode()==settings.Encode(),"group controls survive save/load");
}
foreach(string culture in new[]{"en-US","de-DE","fr-FR"}){
 CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
 foreach(float volume in new[]{0f,.35f,1f}){
  var sound=new GardenSettings{WaterfallVolume=volume,GroupRevision=123};
  Check(GardenSettings.TryDecode(sound.Encode(),out var restored)&&restored.WaterfallVolume==volume,"waterfall volume round trip");
  var receiver=new GardenSettings();receiver.CopyGroupFrom(sound);Check(receiver.SameGroup(sound)&&receiver.WaterfallVolume==.35f,"group copy preserves pond waterfall volume");
 }
}
Check(GardenSettings.TryDecode("2|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100",out var v2)&&v2.WaterfallVolume==.35f,"v2 waterfall default");
foreach(string value in new[]{"NaN","Infinity","-0.1","1.01"})Check(!GardenSettings.TryDecode("3|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100|"+value,out _),"invalid waterfall volume rejected");
foreach(float distance in new[]{5f,16f,50f,100f})foreach(float volume in new[]{0f,.35f,1f}){
 var sound=new GardenSettings{WaterfallRange=distance,WaterfallVolume=volume};
 Check(GardenSettings.TryDecode(sound.Encode(),out var restored)&&restored.WaterfallRange==distance&&restored.WaterfallVolume==volume,"per-pond audio round trip");
 var other=new GardenSettings{WaterfallRange=37,WaterfallVolume=.72f,FlowerScale=1.2f};
 sound.CopyGroupFrom(other);
 Check(sound.FlowerScale==1.2f&&sound.WaterfallRange==distance&&sound.WaterfallVolume==volume,"group propagation leaves pond audio untouched");
}
Check(GardenSettings.TryDecode("3|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100|0.7",out var v3)&&v3.WaterfallVolume==.7f&&v3.WaterfallRange==16,"v3 retains saved volume and range defaults");
foreach(string range in new[]{"NaN","Infinity","4.99","100.01"})Check(!GardenSettings.TryDecode("4|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100|0.7|"+range,out _),"invalid waterfall distance rejected");
var first=new PondFootprint(0,0,6,10,1.2,0);var second=new PondFootprint(4,0,6,10,1.2,0);
Check(PondMergeMath.Overlap(first,second),"overlap joins");Check(!PondMergeMath.Overlap(first,new PondFootprint(20,0,6,10,1.2,0)),"separate ponds stay separate");
for(double x=-5;x<=9;x+=.2)for(double z=-4;z<=4;z+=.2){
 var forward=new[]{first,second};var reverse=new[]{second,first};
 double bed=PondMergeMath.Bed(forward,x,z,15);Near(bed,PondMergeMath.Bed(reverse,x,z,15),"union terrain independent of load order");
 if(first.Distance(x,z)<1&&second.Distance(x,z)<1)Check(bed<=9.750001,"no dry internal overlapping bank");
}
var lower=new PondFootprint(4,0,6,7,1.2,0);
Check(Math.Abs(PondMergeMath.Bed(new[]{first,lower},0,0,15)-8.8)<.001,"upper center retains configured depth");
foreach(int shape in new[]{0,1,2,3})foreach(double drop in new[]{.3,1.5,3.0})foreach(double width in new[]{6.0,12.0,24.0}){
 var high=new PondFootprint(13,-7,width,10,1.2,shape,.2);var low=new PondFootprint(13+width*.55,-7,width,10-drop,1.2,0);var group=new[]{high,low};
 Check(PondChannel.TryCreate(high,low,out var c),"overlap has flow");
 double prior=high.Water,begin=0,end=0;
 for(int i=0;i<=500;i++){
  double x=high.X+(low.X-high.X)*i/500,water=PondMergeMath.Level(group,x,high.Z),fall=(high.Water-water)/drop;
  Check(water<=prior+.00001,"continuous water height descends along the overlap");prior=water;
  if(fall<.1)begin=x;if(fall<.9)end=x;
 }
 Check(end-begin<1.6,"10%-90% cascade run is compact");
 for(double x=high.X-width*.5;x<low.X+width*.5;x+=.2)for(double z=high.Z-width*.5;z<high.Z+width*.5;z+=.2){
  if(Math.Min(high.Distance(x,z),low.Distance(x,z))>1)continue;
  double water=PondMergeMath.Level(group,x,z);Check(water>=low.Water-1e-7&&water<=high.Water+1e-7,"surface lies between pool levels");
  c.Project(x,z,out var t,out _);Near(c.Height(t),water,"foam shares exact water height field");
  Check(Math.Abs(PondMergeMath.Level(group,x+.001,z)-water)<drop*.005+.001,"no height step at either join");
  if(Math.Min(high.Distance(x,z),low.Distance(x,z))<.9)Check(PondMergeMath.Bed(group,x,z,12)<water-.02,"connected bed remains submerged");
 }
}
foreach(float threshold in new[]{.25f,1.5f,10f}){
 Check(WaterfallSoundRules.Track(threshold-.001f,threshold)==9,"small drop uses waterfall 001");
 Check(WaterfallSoundRules.Track(threshold,threshold)==10,"threshold selects waterfall 002");
 Check(WaterfallSoundRules.Track(threshold+.001f,threshold)==10,"large drop uses waterfall 002");
}
Check(WaterfallSoundRules.Track(1,1.5f)==9&&WaterfallSoundRules.Track(2,1.5f)==10,"different drops in same group select independently");
Check(WaterfallSoundRules.Track(2,1.5f)==10&&WaterfallSoundRules.Track(2,3)==9,"threshold change updates sound selection");
Console.WriteLine("PASS "+checks+" assertions: pond settings, shore height, continuous compact joins, submerged beds and culture independence.");

