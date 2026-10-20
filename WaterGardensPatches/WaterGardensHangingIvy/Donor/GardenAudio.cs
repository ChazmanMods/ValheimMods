using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace RunicLilyPads
{
    internal static class GardenAudio
    {
        static readonly Dictionary<int,AudioClip> Clips=new Dictionary<int,AudioClip>();
        internal static AudioClip Get(int number)
        {
            if(number<1||number>10)return null;
            if(Clips.TryGetValue(number,out var cached))return cached;
            try {
                string name=number==10?"waterfall_002":number==9?"waterfall_001":number==8?"fountain_001":$"night_ambience_{number:000}";
                // Embedded clips survive mod managers flattening directories during local import.
                var stream=typeof(Plugin).Assembly.GetManifestResourceStream("WaterGardens.Audio."+name+".wav");
                if(stream==null)throw new FileNotFoundException("Embedded recording missing: "+name);
                using(var r=new BinaryReader(stream)) {
                    if(new string(r.ReadChars(4))!="RIFF")throw new InvalidDataException("RIFF expected");r.ReadInt32();
                    if(new string(r.ReadChars(4))!="WAVE")throw new InvalidDataException("WAVE expected");
                    int rate=0,channels=0,bits=0,format=0;byte[] data=null;
                    while(r.BaseStream.Position+8<=r.BaseStream.Length){var tag=new string(r.ReadChars(4));int size=r.ReadInt32();long end=r.BaseStream.Position+size;
                        if(size<0||end>r.BaseStream.Length)throw new InvalidDataException("Truncated WAV");
                        if(tag=="fmt "){format=r.ReadInt16();channels=r.ReadInt16();rate=r.ReadInt32();r.ReadInt32();r.ReadInt16();bits=r.ReadInt16();}
                        if(tag=="data")data=r.ReadBytes(size);r.BaseStream.Position=end+(size%2);
                    }
                    if(format!=1||channels!=1||bits!=16||rate<=0||data==null)throw new InvalidDataException("Expected mono 16-bit PCM");
                    float[] samples=new float[data.Length/2];double energy=0;float peak=0;
                    for(int i=0;i<samples.Length;i++){samples[i]=(short)(data[i*2]|data[i*2+1]<<8)/32768f;energy+=samples[i]*samples[i];peak=Math.Max(peak,Math.Abs(samples[i]));}
                    // Some supplied field recordings are almost inaudible at the same slider
                    // setting (track 001 is about -50 dB RMS). Raise quiet tracks without clipping.
                    double rms=Math.Sqrt(energy/Math.Max(1,samples.Length));
                    float gain=(float)Math.Min(24,Math.Min(.08/Math.Max(.000001,rms),.95/Math.Max(.000001,peak)));
                    // Waterfalls need a stronger body than background crickets. A soft limiter
                    // keeps their peaks below full scale while raising average loudness.
                    if(number==9||number==10){
                        gain=(float)Math.Min(24,.24/Math.Max(.000001,rms));
                        for(int i=0;i<samples.Length;i++)samples[i]=.95f*(float)Math.Tanh(samples[i]*gain/.95f);
                    }else for(int i=0;i<samples.Length;i++)samples[i]*=gain;
                    Plugin.Log.LogInfo(name+" loaded; loudness gain "+gain.ToString("0.00")+"x");
                    var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);Clips[number]=clip;return clip;
                }
            }catch(Exception e){Plugin.Log.LogError("Ambience "+number+": "+e.Message);Clips[number]=null;return null;}
        }
    }
}
