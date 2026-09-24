using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace RunicSentinel.Runtime
{
    internal static class SentinelPeople
    {
        private static SyncedList Native(string field)=>AccessTools.Field(typeof(ZNet),field)?.GetValue(ZNet.instance) as SyncedList;
        private static string Account(string native)
        {
            if(native.StartsWith("Steam_",StringComparison.Ordinal))native=native.Substring(6);
            else if(native.StartsWith("V_",StringComparison.Ordinal))native=native.Substring(2);
            return native.Length==17&&native.All(char.IsDigit)?"steam:"+native:"native:"+native;
        }
        private static string[] Lines(string text)=>(text??"").Split('\n').Select(s=>s.Trim()).Where(s=>s.Length>0).ToArray();
        internal static string List(SentinelRuntime runtime,SentinelManagedPolicyService managed)
        {
            var doc=managed.CreateDocument("");
            var records=SentinelPlayerReports.Records().ToDictionary(p=>p.account,p=>p,StringComparer.Ordinal);
            SentinelPlayerRecord Ensure(string account)
            {
                if(!records.TryGetValue(account,out var p)){p=new SentinelPlayerRecord{account=account,name=account,activity="Not connected",observedUtc="",location="",characterId=""};records.Add(account,p);}
                return p;
            }
            foreach(var p in records.Values){p.nativeAdministrator=p.signedAdministrator=p.nativeBanned=p.signedBanned=false;}
            string Resolve(string id)=>records.Values.FirstOrDefault(p=>p.commandTarget==id)?.account??Account(id);
            foreach(string id in Native("m_adminList")?.GetList()??new List<string>())Ensure(Resolve(id)).nativeAdministrator=true;
            foreach(string id in Native("m_bannedList")?.GetList()??new List<string>())Ensure(Resolve(id)).nativeBanned=true;
            foreach(string line in Lines(doc.Administrators)){var parts=line.Split('|');if(parts.Length==2)Ensure(parts[0]+":"+parts[1]).signedAdministrator=true;}
            foreach(string line in Lines(doc.BannedUsers)){var parts=line.Split('|');if(parts.Length==2)Ensure(parts[0]+":"+parts[1]).signedBanned=true;}
            foreach(var p in records.Values){p.playerActionsReady=SentinelPlayerCommandService.Current?.Ready(p.account,p.characterId)??false;p.administrator=p.signedAdministrator||((SentinelConfig.UseServerAdminList?.Value??true)&&p.nativeAdministrator);p.banned=p.signedBanned||p.nativeBanned;}
            return SentinelJson.Write(new SentinelPlayerList{players=records.Values.OrderByDescending(p=>p.online).ThenBy(p=>p.name).Take(256).ToArray(),generatedUtc=DateTime.UtcNow.ToString("O"),recordingSince="current server session",schema=3});
        }
        internal static string Apply(string request,string callerAuthority,string callerSubject,SentinelRuntime runtime,SentinelManagedPolicyService managed)
        {
            var fields=request.Split('\n');if(fields.Length!=3)throw new InvalidOperationException("Invalid person action.");
            string action=fields[0],account=fields[1];bool enabled=fields[2]=="1";
            if(fields[2]!="0"&&fields[2]!="1")throw new InvalidOperationException("Invalid status value.");
            int colon=account.IndexOf(':');if(colon<1||account.Length>128||account.Any(char.IsWhiteSpace)||account.Any(char.IsControl))throw new InvalidOperationException("Enter an account as steam:7656… or select a known person.");
            string authority=account.Substring(0,colon),subject=account.Substring(colon+1);
            if(subject.Length==0)throw new InvalidOperationException("Missing account ID.");
            if(authority=="steam"&&(subject.Length!=17||!subject.All(c=>c>='0'&&c<='9')))throw new InvalidOperationException("Steam account IDs must contain exactly 17 digits.");
            if(account==callerAuthority+":"+callerSubject&&(action=="kick"||(action=="ban"&&enabled)||(action=="admin"&&!enabled)))
                throw new InvalidOperationException("Self-removal is blocked here to avoid locking you out. Use another administrator or the server files.");
            var peer=ZNet.instance.GetPeers().FirstOrDefault(p=>p.IsReady()&&SentinelTransportIdentity.TryResolvePeer(p,out string a,out string s,out _)&&a==authority&&s==subject);
            if(action=="kick")
            {
                if(peer==null)throw new InvalidOperationException("This person is not connected.");
                AccessTools.Method(typeof(ZNet),"InternalKick",new[]{typeof(ZNetPeer)}).Invoke(ZNet.instance,new object[]{peer});
                SentinelPlayerReports.Record(callerAuthority+":"+callerSubject,"Person action","Kicked "+account);return "Kick sent for "+account;
            }
            if(action!="admin"&&action!="ban")throw new InvalidOperationException("Unknown person action.");
            if(action=="admin"&&enabled&&!(SentinelConfig.UseServerAdminList?.Value??true))throw new InvalidOperationException("This server has native administrator roles disabled. Enable UseServerAdminList in the server configuration before granting native roles here.");
            string nativeId=authority=="steam"?subject:authority=="native"?subject:peer?.m_socket?.GetHostName();
            if(string.IsNullOrEmpty(nativeId))throw new InvalidOperationException("This backend identity must be online before adding it to Valheim's native list.");
            var list=Native(action=="admin"?"m_adminList":"m_bannedList")??throw new InvalidOperationException("Server identity list is unavailable.");
            var aliases=authority=="steam"?new[]{subject,"Steam_"+subject,"V_"+subject}:new[]{nativeId};
            string[] previous=list.GetList().ToArray();
            var doc=managed.CreateDocument("");string identity=authority+"|"+subject;
            var signed=Lines(action=="admin"?doc.Administrators:doc.BannedUsers).ToList();
            bool removeSigned=!enabled&&signed.Contains(identity);
            if(removeSigned&&!doc.ManagedSigningKey)throw new InvalidOperationException("A signed policy also grants this status. Its signing key is required to remove both sources.");
            if(action=="admin"&&!enabled&&previous.All(s=>aliases.Contains(s))&&Lines(doc.Administrators).All(s=>s==identity))throw new InvalidOperationException("Cannot remove the final administrator.");
            var location=(FileHelpers.FileLocation)AccessTools.Field(typeof(SyncedList),"m_fileLocation").GetValue(list);
            if(location.m_fileSource!=FileHelpers.FileSource.Local)throw new InvalidOperationException("Only server-local identity files can be changed here.");
            string backup=location.m_path+".sentinel-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".bak";
            File.Copy(location.m_path,backup,false);
            try
            {
                if(enabled)list.Add(nativeId);else foreach(string alias in aliases)list.Remove(alias);
                string[] persisted=File.ReadAllLines(location.m_path).Select(s=>s.Trim()).ToArray();
                if(enabled?!persisted.Contains(nativeId):aliases.Any(persisted.Contains))throw new IOException("The server list did not persist the requested change.");
                if(removeSigned){signed.Remove(identity);if(action=="admin")doc.Administrators=string.Join("\n",signed);else doc.BannedUsers=string.Join("\n",signed);managed.Apply(doc,callerAuthority,callerSubject);}
            }
            catch
            {foreach(string current in list.GetList().ToArray())if(!previous.Contains(current))list.Remove(current);foreach(string old in previous)list.Add(old);throw;}
            SentinelPlayerReports.Record(callerAuthority+":"+callerSubject,"Person action",action+"="+enabled+" for "+account);
            return "Saved "+action+"="+enabled+" for "+account+" to "+Path.GetFileName(location.m_path)+". Backup: "+backup;
        }
    }
}
