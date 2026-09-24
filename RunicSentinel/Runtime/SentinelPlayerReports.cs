using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    [Serializable] internal sealed class SentinelPlayerRecord
    {
        public string account, name, characterId, commandTarget, activity, observedUtc, joinedUtc, location;
        public bool online,playerActionsReady;
        public bool administrator,banned,nativeAdministrator,signedAdministrator,nativeBanned,signedBanned;
        public Vector3 position;
    }
    [Serializable] internal sealed class SentinelPlayerList { public SentinelPlayerRecord[] players; public string recordingSince, generatedUtc; public int schema = 2; }
    [Serializable] internal sealed class SentinelObjectRecord
    {
        public string id, prefab, attribution;
        public Vector3 position;
    }
    [Serializable] internal sealed class SentinelActivityRecord
    {
        public string utc, account, kind, detail;
        public Vector3 position;
    }
    [Serializable] internal sealed class SentinelPlayerReport
    {
        public SentinelPlayerRecord player;
        public SentinelObjectRecord[] objects;
        public SentinelActivityRecord[] activity;
        public string[] security;
        public string recordingSince, generatedUtc, files, limitations, reportKind;
        public int inspectedObjects, matchingObjects;
        public bool truncated;
    }

    internal static class SentinelPlayerReports
    {
        private static ZNet _network;
        private static double _nextSample;
        private static string _since="";
        private static DateTime _nextReport;
        private static readonly Dictionary<string,SentinelPlayerRecord> Players=new Dictionary<string,SentinelPlayerRecord>(StringComparer.Ordinal);
        private static readonly Queue<SentinelActivityRecord> Events=new Queue<SentinelActivityRecord>();
        private static readonly System.Reflection.FieldInfo Objects=AccessTools.Field(typeof(ZDOMan),"m_objectsByID");
        private static string Root => Path.Combine(Paths.ConfigPath,"RunicSentinel","PlayerReports");

        internal static void Tick(ZNet network)
        {
            if(network==null || !network.IsServer())return;
            if(!ReferenceEquals(_network,network))
            {
                _network=network;Players.Clear();Events.Clear();_nextSample=0;_nextReport=DateTime.MinValue;_since=DateTime.UtcNow.ToString("O");
            }
            double now=Time.realtimeSinceStartup;
            if(now<_nextSample)return;_nextSample=now+5;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var peer in network.GetPeers().Take(128))
            {
                if(peer==null || !peer.IsReady() || peer.m_server)continue;
                if(!SentinelTransportIdentity.TryResolvePeer(peer,out string authority,out string subject,out _))continue;
                string account=authority+":"+subject;
                seen.Add(account);
                Observe(account,peer.m_playerName,peer.m_playerID,peer.m_refPos,peer.m_rpc.GetSocket().GetHostName());
            }
            if(Player.m_localPlayer!=null)
            {
                seen.Add("local-host");
                Observe("local-host",Player.m_localPlayer.GetPlayerName(),Player.m_localPlayer.GetPlayerID(),Player.m_localPlayer.transform.position,"self");
            }
            foreach(var record in Players.Values.Where(p=>p.online&&!seen.Contains(p.account)).ToArray())
            {
                record.online=false;record.activity="Disconnected; position is last known";record.observedUtc=DateTime.UtcNow.ToString("O");
                Record(record.account,"Connection", "Disconnected",record.position);
            }
        }

        private static void Observe(string account,string name,long id,Vector3 position,string commandTarget)
        {
            if(!Players.TryGetValue(account,out var record))
            {
                if(Players.Count>=128)
                {
                    var oldest=Players.Values.Where(p=>!p.online).OrderBy(p=>p.observedUtc,StringComparer.Ordinal).FirstOrDefault();
                    if(oldest==null)return;Players.Remove(oldest.account);
                }
                record=new SentinelPlayerRecord{account=account};Players[account]=record;
            }
            string activity=!record.online?"Connected":Vector3.Distance(record.position,position)>1?"Moving (inferred from position samples)":"Stationary (inferred from position samples)";
            if (!record.online) record.joinedUtc=DateTime.UtcNow.ToString("O");
            if(!record.online || activity!=record.activity)Record(account,"Observation",activity,position);
            record.commandTarget=Bound(commandTarget,128);record.name=Bound(name,100);record.characterId=id.ToString(CultureInfo.InvariantCulture);record.online=true;
            record.activity=activity;record.position=position;record.observedUtc=DateTime.UtcNow.ToString("O");
            try { record.location=WorldGenerator.instance!=null ? WorldGenerator.instance.GetBiome(position).ToString() : ""; } catch { record.location=""; }
        }

        internal static void Record(string account,string kind,string detail,Vector3 position=default)
        {
            var item=new SentinelActivityRecord{utc=DateTime.UtcNow.ToString("O"),account=Bound(account,128),kind=Bound(kind,64),detail=Bound(detail,512),position=position};
            Events.Enqueue(item);while(Events.Count>512)Events.Dequeue();
            try
            {
                Directory.CreateDirectory(Root);
                string path=Path.Combine(Root,"activity.jsonl"),previous=Path.Combine(Root,"activity.previous.jsonl");
                if(File.Exists(path)&&new FileInfo(path).Length>4*1024*1024)
                {if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
                File.AppendAllText(path,SentinelJson.Write(item)+"\n",Encoding.UTF8);
            }
            catch(Exception e){Debug.LogWarning("Sentinel activity journal unavailable: "+e.GetType().Name);}
        }

        internal static string List()
        {
            Tick(ZNet.instance);
            return SentinelJson.Write(new SentinelPlayerList{players=Players.Values.OrderByDescending(p=>p.online).ThenBy(p=>p.name).ToArray(),recordingSince=_since,generatedUtc=DateTime.UtcNow.ToString("O")});
        }
        internal static SentinelPlayerRecord[] Records(){Tick(ZNet.instance);return Players.Values.ToArray();}

        internal static string Detail(string account, SentinelRuntime runtime)
        {
            Tick(ZNet.instance);
            return SentinelJson.Write(Snapshot(account, runtime));
        }

        private static SentinelPlayerReport Snapshot(string account, SentinelRuntime runtime)
        {
            if(account==null || account.Length>128 || !Players.TryGetValue(account,out var player))
                throw new InvalidOperationException("Select a player observed in this server session.");
            return new SentinelPlayerReport {
                player=player, objects=Array.Empty<SentinelObjectRecord>(),
                activity=Events.Where(e=>e.account==account).TakeLast(24).ToArray(),
                security=runtime.Evidence.ReadAfter(0,256).Entries
                    .Where(e=>e.Actor==account||e.Actor.EndsWith(":"+account,StringComparison.Ordinal))
                    .TakeLast(24).Select(e=>$"{e.UnixSeconds} | {e.ProviderModuleId} | {e.Rule} | {e.Confidence} | {e.EffectiveAction} | {e.Detail}").ToArray(),
                recordingSince=_since, generatedUtc=DateTime.UtcNow.ToString("O"), files="", reportKind="snapshot",
                limitations="Activity is sampled every five seconds; moving/stationary is inferred from network positions. History covers this server session and is bounded. Creator metadata is attribution, not verified account authorship. Events before recording cannot be recovered."
            };
        }

        internal static string Create(string request,SentinelRuntime runtime)
        {
            if(!RunicSentinel.Core.SentinelCommandRouting.TryReport(request,out string account,out string kind))throw new InvalidOperationException("Invalid player report request.");
            Tick(ZNet.instance);
            var report=Snapshot(account,runtime);
            var player=report.player;
            bool includeObjects=kind=="all"||kind=="objects";
            long characterId=0;
            if(includeObjects&&(!long.TryParse(player.characterId,out characterId)||characterId==0))
                throw new InvalidOperationException("The server has not received this player's character identity yet.");
            if(DateTime.UtcNow<_nextReport)throw new InvalidOperationException("Please wait ten seconds between world object reports.");
            _nextReport=DateTime.UtcNow.AddSeconds(10);
            var found=new List<SentinelObjectRecord>();int inspected=0,matching=0;
            bool truncated=false;
            if(includeObjects && ZDOMan.instance!=null && Objects?.GetValue(ZDOMan.instance) is Dictionary<ZDOID,ZDO> objects)
                foreach(var pair in objects)
                {
                    if(inspected>=250000){truncated=true;break;} inspected++;
                    var zdo=pair.Value;if(zdo==null||!zdo.IsValid()||zdo.GetLong(ZDOVars.s_creator,0)!=characterId)continue;
                    matching++;
                    if(found.Count>=2000){truncated=true;continue;}
                    var prefab=ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
                    found.Add(new SentinelObjectRecord{id=pair.Key.ToString(),prefab=prefab!=null?prefab.name:zdo.GetPrefab().ToString(CultureInfo.InvariantCulture),
                        position=zdo.GetPosition(),attribution="World creator metadata matches reported character ID; not proof of authenticated account authorship"});
                }
            else if(includeObjects)truncated=true;
            report.reportKind=kind; report.objects=found.ToArray();
            report.activity=kind=="all"||kind=="activity"?Events.Where(e=>e.account==account).TakeLast(128).ToArray():Array.Empty<SentinelActivityRecord>();
            report.inspectedObjects=inspected;report.matchingObjects=matching;report.truncated=truncated;
            report.limitations += " Objects are a current creator-metadata snapshot, not complete creation/deletion history. Current object scan and in-memory activity/security buffers are bounded. No heuristic observation causes a ban.";
            Directory.CreateDirectory(Root);
            string prefix=Path.Combine(Root,"player-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            report.files=prefix+".{json,csv,txt}";
            File.WriteAllText(prefix+".json",SentinelJson.Write(report,true),Encoding.UTF8);
            var csv=new StringBuilder("record,id,name,x,y,z,detail\n");
            csv.Append("snapshot,").Append(Csv(player.account)).Append(',').Append(Csv(player.name)).Append(',')
                .Append(player.position.x.ToString(CultureInfo.InvariantCulture)).Append(',').Append(player.position.y.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(player.position.z.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(player.activity)).Append('\n');
            foreach(var item in report.activity)csv.Append("activity,").Append(Csv(item.utc)).Append(',').Append(Csv(item.kind)).Append(",,,,").Append(Csv(item.detail)).Append('\n');
            foreach(var obj in report.objects)csv.Append("object,").Append(Csv(obj.id)).Append(',').Append(Csv(obj.prefab)).Append(',')
                .Append(obj.position.x.ToString(CultureInfo.InvariantCulture)).Append(',').Append(obj.position.y.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(obj.position.z.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(obj.attribution)).Append('\n');
            File.WriteAllText(prefix+".csv",csv.ToString(),Encoding.UTF8);
            File.WriteAllText(prefix+".txt",player.name+"\n"+report.generatedUtc+"\n"+player.account+"\n"+player.activity+"\n"+player.position.ToString("F1")+"\n"+report.limitations+"\nObjects matched: "+matching+"; saved: "+found.Count+"; truncated: "+truncated+
                "\n\nActivity\n"+string.Join("\n",report.activity.Select(e=>e.utc+" | "+e.kind+" | "+e.detail))+"\n\nSecurity\n"+string.Join("\n",report.security),Encoding.UTF8);
            // Return a bounded preview. Complete bounded exports stay on the authoritative server.
            report.objects=report.objects.Take(24).ToArray();report.activity=report.activity.TakeLast(24).ToArray();report.security=report.security.TakeLast(24).ToArray();
            return SentinelJson.Write(report);
        }

        private static string Bound(string value,int max)=>string.IsNullOrEmpty(value)?"":value.Length>max?value.Substring(0,max):value;
        private static string Csv(string value)
        {
            string safe=(value??"").Replace("\r"," ").Replace("\n"," ");
            if(safe.Length>0 && "=+-@".IndexOf(safe[0])>=0)safe="'"+safe;
            return "\""+safe.Replace("\"","\"\"")+"\"";
        }
    }
}
