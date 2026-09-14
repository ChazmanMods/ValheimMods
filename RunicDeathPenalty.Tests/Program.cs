using RunicDeathPenalty.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;

int assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
void Near(float expected, float actual, string message) => Check(Math.Abs(expected - actual) < 0.0001f, message + $" expected {expected}, got {actual}");
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (FormatException) { rejected=true; } Check(rejected,message); }
var r = new Rules();
for (int unlocked=0;unlocked<=7;unlocked++)
{
    var keys = Rules.Bosses.Take(unlocked).ToHashSet();
    Check(Rules.AutomaticTier(keys.Contains)==unlocked,"Sequential progression");
    r.EffectiveTier=r.ComputeTier(unlocked);
    for (int biome=0;biome<=7;biome++) Check(r.Restricted(biome)==(biome>unlocked),"One biome ahead threshold");
}
Check(Rules.AutomaticTier(k => k=="defeated_bonemass")==0,"Out of order Bonemass must not unlock progression");
r.CapTier=2; Check(r.ComputeTier(6)==2,"Admin cap");
r.CapTier=6; r.AllowedLead=1; Check(r.ComputeTier(2)==3,"Optional biome lead");
r.Mode=ProgressionMode.Manual;r.ManualTier=4;r.AllowedLead=0;Check(r.ComputeTier(0)==4,"Manual mode");
r = new Rules{EffectiveTier=2};
Check(!r.Restricted(-1),"Ocean exemption");
Check(!r.Restricted(2) && r.Restricted(3),"Elder example");
r.Enabled=false; Check(!r.Restricted(6),"Master switch"); r.Enabled=true;
var wire=r.Encode(); Check(Rules.Decode(wire).Encode()==wire,"Policy roundtrip");
r.ItemOverrides="Silver=3;Wood=-1;CustomOre=5";r.BiomeOverrides="DeepNorth=6";
Check(Rules.Decode(r.Encode()).ItemOverrides==r.ItemOverrides,"Custom tier map roundtrip");
Check(Rules.ParseMap(r.ItemOverrides)["Wood"]==-1,"Allowlist override");
Check(Rules.ParseMap("# comment; with separator\nSilver=3")["Silver"]==3,"Catalog comments can contain separators");
Check(Rules.AutomaticTier(k=>true)==7,"Fader unlocks the installed Deep North tier");
Reject(()=>Rules.Decode(wire+"|unexpected"),"Policy field count");
Reject(()=>Rules.Decode("RDP2"+wire.Substring(4)),"Protocol version");
Reject(()=>Rules.ParseMap("Silver=999"),"Tier bounds");
Reject(()=>Rules.ParseMap("=3"),"Missing prefab");
Reject(()=>new Rules{Multiplier=float.NaN}.Validate(),"NaN multiplier");
Reject(()=>new Rules{Multiplier=float.PositiveInfinity}.Validate(),"Infinity multiplier");
Reject(()=>new Rules{Multiplier=0}.Validate(),"Multiplier cannot silently reduce normal death loss");
Reject(()=>new Rules{RecoveryMinutes=0}.Validate(),"Zero recovery window");
Reject(()=>new Rules{EffectiveTier=99}.Validate(),"Synced effective tier bounds");
var budget=new RecoveryBudget();
Near(5,budget.Loss(1,50,.05f,r),"2x death removes five levels from level 50");
Near(4.5f,budget.Loss(1,45,.05f,r),"Repeated death without cap remains multiplied");
Near(0,budget.Loss(1,50,0,r),"Zero vanilla rate remains zero");
r.Multiplier=20;Near(50,budget.Loss(1,50,.5f,r),"Maximum loss never exceeds current skill");
r.Multiplier=1;Near(2.5f,budget.Loss(1,50,.05f,r),"1x baseline");
r.Multiplier=2;r.RecoveryCap=true;
budget.Begin(1000,r,.05f,new[]{new KeyValuePair<int,float>(1,50),new KeyValuePair<int,float>(2,20)});
Near(5,budget.Loss(1,50,.05f,r),"First capped death includes extra loss");
Near(2.25f,budget.Loss(1,45,.05f,r),"Second capped death still loses normal 5 percent");
Near(2,budget.Loss(2,20,.05f,r),"Independent per-skill budget");
double expiry=budget.Expires;
budget.Begin(1100,r,.05f,new[]{new KeyValuePair<int,float>(1,100)});
Check(budget.Expires==expiry,"Repeated deaths must not extend recovery window");
Near(2.25f,budget.Loss(1,45,.05f,r),"Repeated begin must not replenish budget");
var restored=RecoveryBudget.Decode(budget.Encode());
Check(restored.Expires==expiry,"Reconnect preserves deadline");
Near(2.25f,restored.Loss(1,45,.05f,r),"Reconnect preserves depleted budget");
restored.Begin(expiry,r,.05f,new[]{new KeyValuePair<int,float>(1,40)});
Near(4,restored.Loss(1,40,.05f,r),"New window begins only after expiry");
Near(.5f,restored.Loss(99,10,.05f,r),"New skills do not get a mid-window extra allowance");
Reject(()=>RecoveryBudget.Decode("NaN|1=3"),"Corrupt deadline rejected");
Reject(()=>RecoveryBudget.Decode("1|1=-4"),"Negative saved budget rejected");
Reject(()=>RecoveryBudget.Decode("1|1=Infinity"),"Infinite saved budget rejected");
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
var catalog=Rules.ParseMap(File.ReadAllText(Path.Combine(root,"RunicDeathPenalty/Data/ItemTiers.txt")));
foreach(var item in new[]{"Silver","SilverOre","WolfMeat","DragonEgg"}) Check(catalog[item]==3,"Mountain base item tier: "+item);
foreach(var item in new[]{"Iron","Turnip","WitheredBone"}) Check(catalog[item]==2,"Current boss stage items remain available: "+item);
Check(catalog["TrophyTheElder"]==1 && catalog["CryptKey"]==2,"Progression-enabling boss exceptions");
var managed=args.Length>0?args[0]:@"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed";
using var game=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"assembly_valheim.dll"));
TypeDefinition Type(string name)=>game.MainModule.Types.Single(t=>t.Name==name);
var source=File.ReadAllText(Path.Combine(root,"RunicDeathPenalty/RestrictionPatches.cs"));
foreach(Match entry in Regex.Matches(source,@"\{typeof\((\w+)\), new\[\]\{([^}]+)\}\}"))
    foreach(Match name in Regex.Matches(entry.Groups[2].Value,"\"([^\"]+)\""))
        Check(Type(entry.Groups[1].Value).Methods.Any(m=>m.Name==name.Groups[1].Value && (m.ReturnType.FullName=="System.Void" || m.ReturnType.FullName=="System.Boolean")),"Installed action hook: "+entry.Groups[1]+"."+name.Groups[1]);
foreach(var pair in new[]{("Player","m_timeSinceDeath"),("StatusEffect","m_time"),("SEMan","m_character"),("InventoryGui","m_craftRecipe"),("InventoryGui","m_craftUpgradeItem"),("Attack","m_character"),("Attack","m_weapon"),("CookingStation","m_fuelItem"),("StoreGui","m_selectedItem")})
    Check(Type(pair.Item1).Fields.Any(f=>f.Name==pair.Item2),"Installed field contract "+pair);
Check(Type("ZNet").Fields.Single(f=>f.Name=="m_connectionStatus").IsStatic,"Connection error status is static");
foreach(var pair in new[]{("Player","HardDeath"),("TombStone","GiveBoost"),("TombStone","Setup"),("Smelter","FindCookableItem"),("Attack","FindAmmo"),("ZNet","SendPeerInfo"),("ZNet","RPC_PeerInfo"),("ZNet","OnNewConnection")})
    Check(Type(pair.Item1).Methods.Count(m=>m.Name==pair.Item2)==1,"Unique patched/reflected method "+pair);
var death=Type("Player").Methods.Single(m=>m.Name=="OnDeath");
Check(death.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="Skills" && m.Name=="OnDeath"),"Native death still delegates skills");
var skills=Type("Skills").Methods.Single(m=>m.Name=="OnDeath");
Check(skills.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="LowerAllSkills"),"Native death still passes effective world factor to LowerAllSkills");
Check(Type("Player").Methods.Single(m=>m.Name=="CreateTombStone").Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.Name=="TombStone" && m.Name=="Setup"),"Grave setup is inside death transaction");
Console.WriteLine($"PASS {assertions} assertions: progression, server policy codec, death budget persistence, item seeds, and installed game contracts ({managed}).");
