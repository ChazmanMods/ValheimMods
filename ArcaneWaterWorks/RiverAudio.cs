using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace ArcaneWaterWorks
{
    internal static class RiverAudio
    {
        static readonly Dictionary<int,AudioClip> Clips=new Dictionary<int,AudioClip>();
        internal static AudioClip Get(int number)
        {
            if(number<0||number>2)return null;
            if(Clips.TryGetValue(number,out var cached))return cached;
            try {
                string name=new[]{"Brook","MidSize","Large"}[number];
                var stream=typeof(Plugin).Assembly.GetManifestResourceStream("ArcaneWaterWorks."+name);
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
                    // Consistent, restrained loudness; never apply the old waterfall boost/limiter.
                    double rms=Math.Sqrt(energy/Math.Max(1,samples.Length));
                    float gain=(float)Math.Min(4,Math.Min(.045/Math.Max(.000001,rms),.8/Math.Max(.000001,peak)));
                    for(int i=0;i<samples.Length;i++)samples[i]*=gain;
                    Plugin.Log.LogInfo(name+" loaded; loudness gain "+gain.ToString("0.00")+"x");
                    var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);Clips[number]=clip;return clip;
                }
            }catch(Exception e){Plugin.Log.LogError("Ambience "+number+": "+e.Message);Clips[number]=null;return null;}
        }
    }
}

