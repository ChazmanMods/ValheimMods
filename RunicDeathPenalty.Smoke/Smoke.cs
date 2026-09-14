using System;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty.Smoke
{
    [BepInPlugin("test.runic.death.smoke", "Runic Death Isolated Smoke", "0.1.0")]
    [BepInDependency(Plugin.Guid)]
    public sealed class Smoke : BaseUnityPlugin
    {
        bool done;
        float next;
        int assertions;
        void Awake()
        {
            string save = Environment.GetEnvironmentVariable("RDP_SMOKE_SAVEDIR");
            if (string.IsNullOrEmpty(save) || !Path.IsPathRooted(save) || !save.Contains(".runic-death-smoke")) throw new InvalidOperationException("Smoke requires its isolated save folder.");
            Utils.SetSaveDataPath(save);
            SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
            Logger.LogInfo("RDP_SMOKE_ISOLATED " + save);
        }
        void Check(bool value, string text) { assertions++; if (!value) throw new Exception(text); }
        void Update()
        {
            if (done || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 2;
            if (Time.realtimeSinceStartup > 150) { done=true; Logger.LogError("RDP_SMOKE_TIMEOUT"); Application.Quit(); return; }
            if (!ZNetScene.instance || !ObjectDB.instance || ObjectDB.instance.m_recipes.Count < 100 || !ZNet.instance) return;
            done=true;
            var original = Plugin.Policy;
            try
            {
                Check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything), "Save isolation flag");
                int hooks = 0;
                foreach (var pair in RestrictionPatches.Boundaries)
                    foreach (string name in pair.Value)
                        foreach (var method in AccessTools.GetDeclaredMethods(pair.Key).Where(m=>m.Name==name && (m.ReturnType==typeof(bool)||m.ReturnType==typeof(void))))
                        { Check(Harmony.GetPatchInfo(method)?.Owners.Contains(Plugin.Guid)==true,"Missing runtime patch "+method); hooks++; }
                Plugin.Policy = new Rules {EffectiveTier=2};
                ItemCatalog.Rebuild();
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"RDP-smoke-catalog.tsv"),ItemCatalog.Tiers.OrderBy(p=>p.Value).ThenBy(p=>p.Key).Select(p=>p.Key+"\t"+p.Value));
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"RDP-smoke-unmapped.txt"),ItemCatalog.Unknown.OrderBy(x=>x));
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"RDP-smoke-recipes.tsv"),ObjectDB.instance.m_recipes.Where(r=>r && r.m_item).Select(r=>r.name+"\t"+r.m_item.name+"\t"+(r.m_craftingStation?r.m_craftingStation.gameObject.name:"")+"\t"+r.m_noCraftOnlyUpgrade+"\t"+string.Join(";",r.m_resources.Where(q=>q.m_resItem).Select(q=>q.m_resItem.name+"="+q.m_amount+"/"+q.m_amountPerLevel))));
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"RDP-smoke-bosses.tsv"),ZNetScene.instance.m_prefabs.Where(g=>g && g.GetComponent<Character>() && g.GetComponent<Character>().m_boss).Select(g=>g.name+"\t"+g.GetComponent<Character>().m_defeatSetGlobalKey));
                foreach (var item in new[]{"Silver","SilverOre","WolfMeat","SwordSilver","ArmorWolfChest","ArrowFrost","SwordGold","ArmorDeepNorthHeavyChest"})
                { Check(ObjectDB.instance.GetItemPrefab(item),"Native test prefab missing: "+item); Check(!ItemCatalog.Allowed(item,false),"Advanced item escaped: "+item); }
                foreach (var item in new[]{"Wood","Iron","WitheredBone","MaceIron"}) Check(ItemCatalog.Allowed(item,false),"Current stage item locked: "+item);
                Plugin.Policy.EffectiveTier=3;
                Check(ItemCatalog.Allowed("SwordSilver",false),"Unlock did not release silver equipment");
                Check(!ItemCatalog.Allowed("BlackMetal",false),"Plains unlocked too soon");
                Plugin.Policy.EffectiveTier=2;
                var silverRecipe=ObjectDB.instance.m_recipes.First(r=>r.m_item && r.m_item.name=="SwordSilver");
                Check(!ItemCatalog.Recipe(silverRecipe,1,new Inventory("test",null,8,4)),"Crafting silver allowed");
                ExercisePlayer();
                ExerciseNetwork();
                string report=Path.Combine(Paths.ConfigPath,"RDP-smoke-catalog.tsv");
                File.WriteAllLines(report,ItemCatalog.Tiers.OrderBy(p=>p.Value).ThenBy(p=>p.Key).Select(p=>p.Key+"\t"+p.Value));
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"RDP-smoke-unmapped.txt"),ItemCatalog.Unknown.OrderBy(x=>x));
                Logger.LogMessage("RDP_SMOKE_PASS assertions="+assertions+" runtimeHooks="+hooks+" mapped="+ItemCatalog.Tiers.Count+" unmapped="+ItemCatalog.Unknown.Count);
            }
            catch(Exception e) { Logger.LogError("RDP_SMOKE_FAIL "+e); }
            finally { Plugin.Policy=original; Application.Quit(); }
        }
        void ExercisePlayer()
        {
            var prefab = AccessTools.Field(typeof(Game),"m_playerPrefab").GetValue(Game.instance) as GameObject;
            var obj = UnityEngine.Object.Instantiate(prefab,new Vector3(0,500,0),Quaternion.identity);
            var player=obj.GetComponent<Player>();
            var prior=Player.m_localPlayer;
            Player.m_localPlayer=player;
            player.SetPlayerID(424242,"RDP isolated test");
            try
            {
                var skills=player.GetSkills();
                var skill=AccessTools.Method(typeof(Skills),"GetSkill").Invoke(skills,new object[]{Skills.SkillType.Run}) as Skills.Skill;
                skill.m_level=50;skill.m_accumulator=3;
                DeathRuntime.PenalizedDeath=null;
                skills.LowerAllSkills(.05f);
                Check(Mathf.Abs(skill.m_level-47.5f)<.001,"Normal native skill-loss path changed");
                skill.m_level=50;skill.m_accumulator=3;
                DeathRuntime.PenalizedDeath=player;
                skills.LowerAllSkills(.05f);
                Check(Mathf.Abs(skill.m_level-45)<.001 && skill.m_accumulator==0,"Runtime multiplier or partial progress reset wrong");
                Plugin.Policy.RecoveryCap=true;skill.m_level=50;
                skills.LowerAllSkills(.05f);
                Check(Mathf.Abs(skill.m_level-45)<.001,"First native capped death wrong");
                skills.LowerAllSkills(.05f);
                Check(Mathf.Abs(skill.m_level-42.75f)<.001,"Native recovery budget not applied");
                Check(player.m_customData.ContainsKey(Locations.Key("budget")),"Recovery state not attached to character");
                DeathRuntime.PenalizedDeath=null;
                AccessTools.Field(typeof(Player),"m_timeSinceDeath").SetValue(player,0f);
                Plugin.Policy.EffectiveTier=7;
                Check(!(bool)AccessTools.Method(typeof(Player),"HardDeath").Invoke(player,null),"Normal soft-death protection lost");
                player.m_customData[Locations.Key("noGrace")]="1";
                Check((bool)AccessTools.Method(typeof(Player),"HardDeath").Invoke(player,null),"Restricted death could obtain normal grace");
                player.m_customData.Remove(Locations.Key("noGrace"));
                var corpse=player.GetSEMan().AddStatusEffect(DeathRuntime.Corpse);
                Check(corpse,"Native corpse status unavailable");
                AccessTools.Field(typeof(StatusEffect),"m_time").SetValue(corpse,10f);
                float remaining=corpse.GetRemaningTime();
                string biome=WorldGenerator.instance.GetBiome(player.transform.position).ToString();
                Plugin.Policy.BiomeOverrides=biome+"=3";Plugin.Policy.EffectiveTier=2;
                DeathRuntime.UpdateProtections(player);
                Check(!player.GetSEMan().GetStatusEffect(DeathRuntime.Corpse),"Corpse buff not suppressed in restricted biome");
                Check(player.m_customData.ContainsKey(Locations.Key("corpseExpires")),"Suppressed buff expiry was not preserved");
                Check(player.GetSEMan().AddStatusEffect(DeathRuntime.Corpse)==null,"New corpse buff entered restricted biome");
                Plugin.Policy.EffectiveTier=7;
                DeathRuntime.UpdateProtections(player);
                var restored=player.GetSEMan().GetStatusEffect(DeathRuntime.Corpse);
                Check(restored && restored.GetRemaningTime()<=remaining+.1f,"Returning to allowed biome refreshed corpse buff");
                var gravePrefab=AccessTools.Field(typeof(Player),"m_tombstone").GetValue(player) as GameObject;
                var graveObject=UnityEngine.Object.Instantiate(gravePrefab,new Vector3(0,500,0),Quaternion.identity);
                var grave=graveObject.GetComponent<TombStone>();
                DeathRuntime.PenalizedDeath=player;
                grave.Setup("RDP isolated test",player.GetPlayerID());
                DeathRuntime.PenalizedDeath=null;
                Check(grave.GetComponent<ZNetView>().GetZDO().GetBool(DeathRuntime.GraveKey),"Restricted tombstone was not persistently marked");
                player.GetSEMan().RemoveStatusEffect(DeathRuntime.Corpse,true);
                AccessTools.Method(typeof(TombStone),"GiveBoost").Invoke(grave,null);
                Check(!player.GetSEMan().GetStatusEffect(DeathRuntime.Corpse),"Marked tombstone granted corpse buff");
                grave.GetComponent<ZNetView>().GetZDO().Set(DeathRuntime.GraveKey,false);
                AccessTools.Method(typeof(TombStone),"GiveBoost").Invoke(grave,null);
                Check(player.GetSEMan().GetStatusEffect(DeathRuntime.Corpse),"Ordinary tombstone lost corpse buff");
                grave.GetComponent<ZNetView>().Destroy();
                Plugin.Policy.EffectiveTier=2;
                var sword=ObjectDB.instance.GetItemPrefab("SwordSilver").GetComponent<ItemDrop>().m_itemData.Clone();
                Check(!player.EquipItem(sword),"Runtime equipment gate failed");
                var crafting=Type.GetType("RunicCrafting.Integration.CraftingRuntime, RunicCrafting",false);
                if(crafting!=null)
                {
                    var recipe=ObjectDB.instance.m_recipes.First(r=>r.m_item && r.m_item.name=="SwordSilver");
                    Check(!(bool)AccessTools.Method(crafting,"BeforeCraft").Invoke(null,new object[]{null,player,recipe,null,false,1}),"RunicCrafting direct preparation bypassed restriction");
                }
                var production=Type.GetType("RunicProduction.Integration.ExactStockInventoryMutation, RunicProduction",false);
                if(production!=null)
                {
                    var outputType=Type.GetType("RunicProduction.Integration.StockOutputDefinition, RunicProduction");
                    var output=AccessTools.GetDeclaredConstructors(outputType).Single().Invoke(new object[]{"Silver",1,1,0,0L,"",false});
                    var inv=new Inventory("rdp-test-output",null,8,4);
                    object[] arguments={inv,output,null,null};
                    Check(!(bool)AccessTools.Method(production,"TryPrepareDestination").Invoke(null,arguments),"RunicProduction output preparation bypassed restriction");
                    Check(inv.NrOfItems()==0 && (arguments[3] as string)?.Contains("RunicDeathPenalty")==true,"Production guard mutated inventory or lost failure reason");
                }
            }
            finally { DeathRuntime.PenalizedDeath=null;Player.m_localPlayer=prior;obj.GetComponent<ZNetView>().Destroy(); }
        }
        void ExerciseNetwork()
        {
            var role=AccessTools.Field(typeof(ZNet),"m_isServer");
            var status=AccessTools.Field(typeof(ZNet),"m_connectionStatus");
            object oldRole=role.GetValue(null), oldStatus=status.GetValue(null);
            var policy=Plugin.Policy;
            try
            {
                role.SetValue(null,true);
                var left=new TestSocket();var right=new TestSocket();left.Other=right;right.Other=left;
                var peer=new ZNetPeer(left,false);var remote=new ZRpc(right);Network.Register(peer);
                remote.Invoke("RDP_Hello_v1",Plugin.Version);peer.m_rpc.Update(0);
                Check(Network.Admit(peer.m_rpc),"Matching native RPC handshake rejected");
                remote.Invoke("RDP_Policy_v1",new Rules{EffectiveTier=7}.Encode());peer.m_rpc.Update(0);
                Check(ReferenceEquals(policy,Plugin.Policy),"Client could overwrite server policy");
                remote.Invoke("RDP_Hello_v1","incompatible");peer.m_rpc.Update(0);
                Check(!Network.Admit(peer.m_rpc),"Mismatched version admitted");
                var missing=new ZNetPeer(new TestSocket(),false);
                Check(!Network.Admit(missing.m_rpc),"Missing mod admitted");
                role.SetValue(null,false);Network.Reset();
                var serverPeer=new ZNetPeer(left,true);Network.Register(serverPeer);
                remote.Invoke("RDP_Hello_v1",Plugin.Version);serverPeer.m_rpc.Update(0);
                remote.Invoke("RDP_Policy_v1",new Rules{EffectiveTier=3,Multiplier=4}.Encode());serverPeer.m_rpc.Update(0);
                Check(Network.Synchronized && Plugin.Policy.EffectiveTier==3 && Plugin.Policy.Multiplier==4,"Server policy did not synchronize through native RPC");
                var untrusted=new ZNetPeer(left,false);Network.Register(untrusted);
                remote.Invoke("RDP_Hello_v1",Plugin.Version);untrusted.m_rpc.Update(0);
                remote.Invoke("RDP_Policy_v1",new Rules{EffectiveTier=7}.Encode());untrusted.m_rpc.Update(0);
                Check(Plugin.Policy.EffectiveTier==3,"Non-server peer could inject client policy");
            }
            finally { Network.Reset();Plugin.Policy=policy;role.SetValue(null,oldRole);status.SetValue(null,oldStatus); }
        }
        sealed class TestSocket : ISocket
        {
            public TestSocket Other;
            readonly System.Collections.Generic.Queue<ZPackage> queue=new System.Collections.Generic.Queue<ZPackage>();
            bool connected=true;
            public bool IsConnected()=>connected;
            public void Send(ZPackage pkg) { Other?.queue.Enqueue(new ZPackage(pkg.GetArray())); }
            public ZPackage Recv()=>queue.Count>0?queue.Dequeue():null;
            public int GetSendQueueSize()=>0;
            public int GetCurrentSendRate()=>0;
            public bool IsHost()=>false;
            public void Dispose()=>Close();
            public bool GotNewData()=>queue.Count>0;
            public void Close()=>connected=false;
            public string GetEndPointString()=>"isolated-memory";
            public string GetHostName()=>"isolated-memory";
            public void GetAndResetStats(out int sent,out int received){sent=received=0;}
            public void GetConnectionQuality(out float local,out float remote,out int ping,out float outgoing,out float incoming){local=remote=1;ping=0;outgoing=incoming=0;}
            public ISocket Accept()=>null;
            public int GetHostPort()=>0;
            public bool Flush()=>true;
            public void VersionMatch(){}
        }
    }
}
