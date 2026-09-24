using System;
using RunicSentinel.Runtime;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private static string ReadLocalizedPanel()
        {
            var catalog=System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,string>>(Read("RunicSentinel","Translations","RunicSentinel","English.json"));
            return System.Text.RegularExpressions.Regex.Replace(Read("RunicSentinel","Runtime","SentinelAdminPanel.cs"),
                "global::Runic\\.Localization\\.RunicText\\.Get\\(\"([^\"]+)\"\\)", m=>System.Text.Json.JsonSerializer.Serialize(catalog[m.Groups[1].Value]));
        }
        private static void NamedModAssignment()
        {
            var d=new SentinelAdminDocument{RequiredMods="mod.a|2.1.0|abc\nmod.b|*|*",OptionalMods="mod.c|*|*"};
            SentinelModChoices.Assign(d,"mod.a",3);
            Equal("mod.b|*|*",d.RequiredMods);Equal("mod.a|2.1.0|abc",d.GrayListMods);
            Equal(3,SentinelModChoices.Category(d,"mod.a"));
            SentinelModChoices.Assign(d,"mod.a",4);Equal("",d.GrayListMods);Equal("mod.a|2.1.0|abc",d.ForbiddenMods);
            SentinelModChoices.Assign(d,"mod.a",0);Equal("",d.ForbiddenMods);
            SentinelModChoices.Assign(d,"mod.new",2);True(d.OptionalMods.Contains("mod.new|*|*"));
            try { SentinelModChoices.Assign(d,"mod\nforged|*|*",2);throw new Exception("Accepted invalid identity"); }
            catch(ArgumentException){}
        }
        private static void DashboardDocument()
        {
            var d=new SentinelAdminDocument{NamedMods="id|Friendly name|1.0",Players="bounded observations",RequiredMods="id|*|*"};
            True(SentinelAdminProtocol.TryDecode(SentinelAdminProtocol.Encode(d),out var copy));
            Equal(d.NamedMods,copy.NamedMods);Equal(d.Players,copy.Players);Equal(d.RequiredMods,copy.RequiredMods);
        }
    }
}
