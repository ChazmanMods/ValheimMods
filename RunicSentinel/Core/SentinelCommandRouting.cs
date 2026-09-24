using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicSentinel.Core
{
    internal static class SentinelCommandRouting
    {
        private static readonly HashSet<string> WorldCommands=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {"save","kick","ban","unban","banned","genloc","players","setkey","removekey","resetkeys","resetworldkeys","setworldpreset","setworldmodifier","listkeys","skiptime","time","sleep","randomevent","event","stopevent","noportals","cinematicsleep"};
        internal static bool IsWorldCommand(string name)=>WorldCommands.Contains(name);
        internal static bool TryCoordinates(string text,out float x,out float y,out float z)
        {
            x=y=z=0;string[] parts=(text??"").Split(',');if(parts.Length!=3)return false;
            var values=new float[3];
            for(int i=0;i<3;i++)if(!float.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||float.IsNaN(values[i])||float.IsInfinity(values[i])||Math.Abs(values[i])>20000)return false;
            x=values[0];z=values[1];y=values[2];return true;
        }
        internal static bool TryReport(string request,out string account,out string kind)
        {
            account="";kind="";string[] parts=(request??"").Split('\n');
            if(parts.Length<1||parts.Length>2||parts[0].Length==0||parts[0].Length>128)return false;
            foreach(char c in parts[0])if(char.IsControl(c)||char.IsWhiteSpace(c))return false;
            kind=parts.Length==1?"all":parts[1];
            if(kind!="all"&&kind!="objects"&&kind!="activity"&&kind!="snapshot")return false;
            account=parts[0];return true;
        }
    }
}
