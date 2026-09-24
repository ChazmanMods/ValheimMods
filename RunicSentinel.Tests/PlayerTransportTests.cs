using System;
using RunicSentinel.Runtime;
using UnityEngine;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private static void PlayerJsonRoundTrip()
        {
            var player=new SentinelPlayerRecord{account="steam:76561198000000001",name="Drákvaldr \"The Builder\"",online=true,administrator=true,nativeAdministrator=true,position=new Vector3(123.5f,-8.25f,908f)};
            var list=new SentinelPlayerList{players=new[]{player},schema=3,generatedUtc="2026-09-24T12:00:00Z"};
            string json=SentinelJson.Write(list);
            False(json.Contains("normalized"));False(json.Contains("magnitude"));
            var result=SentinelJson.Read<SentinelPlayerList>(json);
            Equal(1,result.players.Length);Equal(player.name,result.players[0].name);
            Equal(player.position.x,result.players[0].position.x);Equal(player.position.y,result.players[0].position.y);Equal(player.position.z,result.players[0].position.z);
            True(result.players[0].administrator&&result.players[0].nativeAdministrator&&result.players[0].online);
            var report=new SentinelPlayerReport{player=player,objects=new[]{new SentinelObjectRecord{prefab="Cuisine_Food",position=player.position}},activity=new[]{new SentinelActivityRecord{detail="Moved\nthen stopped",position=player.position}},security=new[]{"rule: none"}};
            var copy=SentinelJson.Read<SentinelPlayerReport>(SentinelJson.Write(report));
            Equal("Cuisine_Food",copy.objects[0].prefab);Equal("Moved\nthen stopped",copy.activity[0].detail);Equal("rule: none",copy.security[0]);
            var empty=SentinelJson.Read<SentinelPlayerList>("{\"players\":[],\"schema\":2}");Equal(0,empty.players.Length);
            try{SentinelJson.Read<SentinelPlayerList>(new string('x',123000));throw new Exception("Oversize accepted");}catch(FormatException){}
            var chunk=new SentinelReportChunk{data=Convert.ToBase64String(new byte[32768]),offset=32768,total=70000};
            var decoded=SentinelJson.Read<SentinelReportChunk>(SentinelJson.Write(chunk));Equal(32768,Convert.FromBase64String(decoded.data).Length);Equal(70000,decoded.total);
        }
        private static void ReadableOperatorReports()
        {
            string raw="created-utc=2026-09-24\nworld=Drakheim\npolicy-profile=My rules\nintegrity=Verified\nintegrity-reason=signature passed\nplugins=1\nplugin=Example.Mod|1.0|hash\nplugin=Example.Mod|1.0|hash\nnetwork-map-summary=zdos:16384,portals:1,production-edges:1,truncated:true\nportal=id|Shared|Home|1|Builders|1,2,3\nproduction-edge=id|smelter|fuel|link|record-invalid|1,2,3\nevidence=1|123|runic.portals|access|High|Observe|Access denied\n";
            string text=SentinelReadableReport.Format(raw,true);
            Contains(text,"World: Drakheim");Contains(text,"Partial results: Yes");Contains(text,"World objects inspected: 16384");Contains(text,"Home | Network: Shared");Contains(text,"ACTION: inspect and reconnect");Contains(text,"Rule: access");
            Equal(1,text.Split(new[]{"Example.Mod"},StringSplitOptions.None).Length-1);
            False(text.Contains("plugin="));False(text.Contains("production-edge="));
            string health=SentinelReadableReport.Format(raw,false);Contains(health,"SERVER HEALTH & SUPPORT REPORT");False(health.Contains("PRODUCTION CONNECTIONS"));
        }
        private static void ReportDownloadBounds()
        {
            string folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sentinel-report-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(folder);
            try
            {
                var command=(SentinelOperatorCommands)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SentinelOperatorCommands));
                typeof(SentinelOperatorCommands).GetField("_reportRoot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(command,folder);
                string name="sentinel-health-20260924-120000-12345678.txt";
                byte[] original=System.Text.Encoding.UTF8.GetBytes(new string('é',40000));System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name),original);
                using var result=new System.IO.MemoryStream();
                while(result.Length<original.Length)
                {
                    var chunk=SentinelJson.Read<SentinelReportChunk>(command.ReadReportChunk(name+"\n"+result.Length));
                    Equal((int)result.Length,chunk.offset);Equal(original.Length,chunk.total);
                    byte[] part=Convert.FromBase64String(chunk.data);True(part.Length<=32768);result.Write(part);
                }
                True(System.Linq.Enumerable.SequenceEqual(original,result.ToArray()));
                foreach(string request in new[]{"../"+name+"\n0",name+"\n-1",name+"\n80001","adminlist.txt\n0",name+"\n0\nextra"})
                {try{command.ReadReportChunk(request);throw new Exception("Accepted invalid report request");}catch(System.IO.InvalidDataException){}}
            }
            finally{System.IO.Directory.Delete(folder,true);}
        }
    }
}
