using RunicSentinel.Devcommands;
new AddStatusCommand();int passed=0;
void Check(string name,Action test){try{Helper.Player=new Player();test();passed++;System.Console.WriteLine("PASS "+name);}catch(Exception e){System.Console.WriteLine("FAIL "+name+": "+e.Message);Environment.ExitCode=1;}}
void Run(string text)=>Helper.Handler!(new Terminal.ConsoleEventArgs(text));
void Eq(float expected,float actual){if(expected!=actual)throw new Exception($"Expected {expected}; actual {actual}");}
void Reject(string text){try{Run(text);}catch(InvalidOperationException){if(Helper.Player.Manager.Applied!=0)throw new Exception("Mutated player before validation");return;}throw new Exception("Invalid input accepted");}
Check("Rested duration: 3600 seconds is one hour",()=>{Run("addstatus Rested 3600");Eq(3600,Helper.Player.Manager.Effect!.m_ttl);});
Check("Rested duration: 6000 seconds is 100 minutes",()=>{Run("addstatus Rested 6000");Eq(6000,Helper.Player.Manager.Effect!.m_ttl);});
Check("Default duration preserved when omitted",()=>{Run("addstatus Rested");Eq(600,Helper.Player.Manager.Effect!.m_ttl);});
Check("Zero duration preserves upstream no-expiry semantics",()=>{Run("addstatus Rested 0");Eq(0,Helper.Player.Manager.Effect!.m_ttl);});
Check("Decimal duration supported",()=>{Run("addstatus Rested 1.5");Eq(1.5f,Helper.Player.Manager.Effect!.m_ttl);});
Check("Reset existing effect timer",()=>{Run("addstatus Rested 10");Helper.Player.Manager.Effect!.elapsed=8;Run("addstatus Rested 30");Eq(0,Helper.Player.Manager.Effect!.elapsed);Eq(30,Helper.Player.Manager.Effect!.m_ttl);});
foreach(var value in new[]{"-1","NaN","Infinity","oops","1e99"})Check("Reject invalid duration "+value,()=>Reject("addstatus Rested "+value));
foreach(var value in new[]{"-1","NaN","Infinity","oops"})Check("Reject invalid intensity "+value,()=>Reject("addstatus Shield 10 "+value));
Check("Shield intensity",()=>{Run("addstatus Shield 10 250");Eq(250,((SE_Shield)Helper.Player.Manager.Effect!).m_absorbDamage);});
Check("Burning intensity",()=>{Run("addstatus Burning 10 80");Eq(80,((SE_Burning)Helper.Player.Manager.Effect!).m_fireDamageLeft);});
Check("Spirit intensity",()=>{Run("addstatus Spirit 10 90");Eq(90,((SE_Burning)Helper.Player.Manager.Effect!).m_spiritDamageLeft);});
Check("Poison intensity per tick",()=>{Run("addstatus Poison 10 100");Eq(20,((SE_Poison)Helper.Player.Manager.Effect!).m_damagePerHit);});
Check("Zero duration poison does not divide by zero",()=>{Run("addstatus Poison 0 100");if(!float.IsFinite(((SE_Poison)Helper.Player.Manager.Effect!).m_damagePerHit))throw new Exception("Nonfinite poison tick");});
Check("Reject excess parameters",()=>Reject("addstatus Rested 1 1 extra"));
System.Console.WriteLine($"{passed}/21 status-effect tests passed.");
public class Terminal{public class ConsoleEventArgs{public string[] Args;public int Length=>Args.Length;public string this[int index]=>Args[index];public ConsoleEventArgs(string line){Args=line.Split(' ');}}}
public class StatusEffect{public float m_ttl=600,elapsed;}
public class SE_Shield:StatusEffect{public float m_absorbDamage;}
public class SE_Burning:StatusEffect{public float m_fireDamageLeft,m_spiritDamageLeft;public void AddFireDamage(float value)=>m_fireDamageLeft+=value;public void AddSpiritDamage(float value)=>m_spiritDamageLeft+=value;}
public class SE_Poison:StatusEffect{public float m_damageLeft,m_damagePerHit,m_damageInterval=2;}
public class Player{public SEMan Manager=new();public SEMan GetSEMan()=>Manager;}
public class SEMan{public StatusEffect? Effect;public int Applied;public void AddStatusEffect(int hash,bool reset){Applied++;Effect=hash=="Shield".GetStableHashCode()?new SE_Shield():hash=="Burning".GetStableHashCode()||hash=="Spirit".GetStableHashCode()?new SE_Burning():hash=="Poison".GetStableHashCode()?new SE_Poison():new StatusEffect();}public StatusEffect? GetStatusEffect(int hash)=>Effect;}
public static class Hash{public static int GetStableHashCode(this string name)=>name.GetHashCode();}
namespace RunicSentinel.Devcommands{
public static class Helper{public static Player Player=new();public static Action<Terminal.ConsoleEventArgs>? Handler;public static void Command(string name,string description,Action<Terminal.ConsoleEventArgs> action)=>Handler=action;public static Player GetPlayer()=>Player;public static void ArgsCheck(Terminal.ConsoleEventArgs args,int count,string reason){if(args.Length<count)throw new InvalidOperationException(reason);}}
public static class AutoComplete{public static void Register(string name,Func<int,List<string>> fetch){}}
public static class ParameterInfo{public static List<string> StatusEffects=new();public static List<string> None=new();public static List<string> Create(string text)=>new(){text};}
}
