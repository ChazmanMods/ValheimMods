using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelModChoice
    {
        internal string Id, Name, Version, Source;
    }

    internal static class SentinelModChoices
    {
        internal static string Installed() => string.Join("\n", Chainloader.PluginInfos.Values
            .OrderBy(p => p.Metadata.GUID, StringComparer.Ordinal).Take(256)
            .Select(p => string.Join("|", Clean(p.Metadata.GUID), Clean(p.Metadata.Name),
                Clean(p.Metadata.Version.ToString()))));

        private static string Clean(string value) => (value ?? "").Replace("|", " ").Replace("\n", " ").Replace("\r", " ");

        internal static IReadOnlyList<SentinelModChoice> Read(SentinelAdminDocument document)
        {
            var entries = new Dictionary<string, SentinelModChoice>(StringComparer.Ordinal);
            void Add(string text, string source)
            {
                foreach (string line in (text ?? "").Split('\n'))
                {
                    string[] f = line.Split('|');
                    if (f.Length != 3 || string.IsNullOrWhiteSpace(f[0])) continue;
                    entries[f[0]] = new SentinelModChoice { Id=f[0], Name=f[1], Version=f[2], Source=source };
                }
            }
            Add(Installed(), "Administrator client");
            Add(document.NamedMods, "Server");
            foreach (string text in new[] {document.RequiredMods, document.OptionalMods, document.GrayListMods,
                         document.ForbiddenMods, document.DetectedProfile})
                foreach (string line in (text ?? "").Split('\n'))
                {
                    string[] f = line.Trim().Split('|');
                    if (f.Length != 3 || entries.ContainsKey(f[0])) continue;
                    entries[f[0]] = new SentinelModChoice {Id=f[0], Name=f[0], Version=f[1], Source="Policy / client report; display name unavailable"};
                }
            return entries.Values.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static int Category(SentinelAdminDocument d, string id)
        {
            string[] lists={d.RequiredMods,d.OptionalMods,d.GrayListMods,d.ForbiddenMods};
            for(int i=0;i<lists.Length;i++) if(Lines(lists[i]).Any(v=>v.Split('|')[0]==id)) return i+1;
            return 0;
        }

        private static IEnumerable<string> Lines(string value) => (value??"").Split('\n').Select(s=>s.Trim()).Where(s=>s.Length>0);

        internal static void Assign(SentinelAdminDocument d, string id, int category)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(new[]{'|','\r','\n'})>=0 || category<0 || category>4)
                throw new ArgumentException("Invalid mod category assignment.");
            string[] lists={d.RequiredMods,d.OptionalMods,d.GrayListMods,d.ForbiddenMods};
            string rule=lists.SelectMany(Lines).FirstOrDefault(s=>s.Split('|')[0]==id) ?? id+"|*|*";
            for(int i=0;i<lists.Length;i++)
            {
                var values=Lines(lists[i]).Where(s=>s.Split('|')[0]!=id).ToList();
                if(category==i+1) values.Add(rule);
                lists[i]=string.Join("\n",values);
            }
            d.RequiredMods=lists[0]; d.OptionalMods=lists[1]; d.GrayListMods=lists[2]; d.ForbiddenMods=lists[3];
        }
    }
}
