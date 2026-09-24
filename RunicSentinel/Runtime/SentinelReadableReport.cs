using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RunicSentinel.Runtime
{
    [Serializable]internal sealed class SentinelToolReport{public string file,title,path;}
    [Serializable]internal sealed class SentinelReportChunk{public string data;public int offset,total;}
    internal static class SentinelReadableReport
    {
        internal static string Format(string raw,bool networks)
        {
            string[] lines=raw.Split('\n');
            string Value(string key)=>lines.FirstOrDefault(l=>l.StartsWith(key+"=",StringComparison.Ordinal))?.Substring(key.Length+1)??"Not available";
            var output=new StringBuilder();
            output.AppendLine(networks?"PRODUCTION & PORTAL NETWORK REPORT":"SERVER HEALTH & SUPPORT REPORT");
            output.AppendLine("Created (UTC): "+Value("created-utc"));
            output.AppendLine("World: "+Value("world"));
            output.AppendLine();output.AppendLine("AT A GLANCE");
            output.AppendLine("Policy label: "+Value("policy-profile"));
            output.AppendLine("Policy verification: "+Value("integrity")+" — "+Value("integrity-reason"));
            output.AppendLine("Admission channel: "+Value("admission-transport"));
            string denial=Value("last-admission-denial");
            output.AppendLine("Most recent rejected connection: "+(string.IsNullOrWhiteSpace(denial)?"None recorded":denial));
            output.AppendLine("Loaded server plugins: "+Value("plugins"));
            output.AppendLine();output.AppendLine("HOW TO USE THIS REPORT");
            output.AppendLine("Use policy verification and the last rejected connection to diagnose joining problems. A verified policy means its signature/configuration passed checks; it does not certify every player or mod as cheat-free.");
            output.AppendLine("Review recorded security findings below before changing enforcement thresholds. Repeated provider findings can describe separate requests; they are not automatically separate people.");
            if(networks)
            {
                output.AppendLine();output.AppendLine("NETWORK COVERAGE");
                foreach(var part in Value("network-map-summary").Split(','))
                {
                    var pair=part.Split(':');if(pair.Length!=2)continue;
                    string label=pair[0]=="zdos"?"World objects inspected":pair[0]=="portals"?"Configured portals found":pair[0]=="production-edges"?"Production connections found":pair[0]=="truncated"?"Partial results":pair[0];
                    output.AppendLine(label+": "+(pair[1]=="true"?"Yes":pair[1]=="false"?"No":pair[1]));
                }
                output.AppendLine("This is a bounded snapshot of stored world objects, not a complete world scan. 'truncated:true' means additional objects or links were not inspected. An empty sample does not prove the world has no networks.");
                output.AppendLine();output.AppendLine("PORTALS");
                var portals=lines.Where(l=>l.StartsWith("portal=")).Distinct().ToArray();
                if(portals.Length==0)output.AppendLine("No configured Runic portals were found in this sample.");
                foreach(var line in portals){var p=line.Substring(7).Split('|');if(p.Length>=6)output.AppendLine("• "+(p[2].Length==0?"Unnamed portal":p[2])+" | Network: "+p[1]+" | Group: "+p[4]+" | Position x,y,z: "+p[5]);}
                output.AppendLine();output.AppendLine("PRODUCTION CONNECTIONS");
                var edges=lines.Where(l=>l.StartsWith("production-edge=")).Distinct().ToArray();
                if(edges.Length==0)output.AppendLine("No configured production connections were found in this sample.");
                foreach(var line in edges){var p=line.Substring(16).Split('|');if(p.Length>=6)output.AppendLine("• "+p[1]+" at "+p[5]+" | "+p[2]+" connection | Target: "+p[4]+(p[4]=="record-invalid"?" — ACTION: inspect and reconnect this station":""));}
                output.AppendLine("Input supplies ingredients; fuel supplies fuel; output receives finished items; replenishment restocks linked inventory. Target identifiers refer to stored links and may need inspection at the listed position.");
            }
            output.AppendLine();output.AppendLine("RECORDED SECURITY FINDINGS");
            var findings=lines.Where(l=>l.StartsWith("evidence=")).Distinct().ToArray();
            if(findings.Length==0)output.AppendLine("No findings in the bounded evidence buffer.");
            foreach(var line in findings){var p=line.Substring(9).Split('|');if(p.Length>=7)output.AppendLine("• Source: "+p[2]+" | Rule: "+p[3]+" | Confidence: "+p[4]+" | Action: "+p[5]+" | "+string.Join("|",p.Skip(6)));}
            output.AppendLine();output.AppendLine("SERVER MOD INVENTORY (for compatibility troubleshooting)");
            foreach(var line in lines.Where(l=>l.StartsWith("plugin=")).Distinct()){var p=line.Substring(7).Split('|');output.AppendLine("• "+p[0]+(p.Length>1?" — "+p[1]:""));}
            output.AppendLine();output.AppendLine("TECHNICAL REFERENCE");
            output.AppendLine("Policy sequence: "+Value("policy-sequence"));output.AppendLine("Policy digest: "+Value("policy-digest"));
            return output.ToString();
        }
    }
}
