using System;
using System.Globalization;
using System.Linq;

namespace RunicSentinel.PlayerActions
{
    internal static class PlayerActionProtocol
    {
        internal const string CapabilityRpc="runic.sentinel.player.capability.v1",ActionRpc="runic.sentinel.player.action.v1",ResultRpc="runic.sentinel.player.result.v1";
        internal const int Schema=1,MaximumBytes=4096;
        internal static readonly string[] Actions={"raiseskill","resetskill","heal","puke","clearstatus","adrenaline","addstatus"};
        internal static string[] SkillsList()=>Enum.GetNames(typeof(Skills.SkillType)).Where(n=>n!="None").ToArray();
        internal static string Validate(string action,string args)
        {
            args=args??"";
            if(!Actions.Contains(action)||args.Length>160||args.Any(char.IsControl))throw new InvalidOperationException("Unsupported player action or invalid arguments.");
            var parts=args.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            if(action=="raiseskill"||action=="resetskill")
            {
                int expected=action=="raiseskill"?2:1;
                if(parts.Length!=expected||!SkillsList().Any(s=>s.Equals(parts[0],StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Choose a valid skill name.");
                if(expected==2&&(!int.TryParse(parts[1],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int amount)||amount< -100||amount>100||amount==0))throw new InvalidOperationException("Skill change must be a nonzero whole number from -100 to 100.");
            }
            else if(action=="adrenaline")
            {if(!int.TryParse(args,NumberStyles.None,CultureInfo.InvariantCulture,out int level)||level<0||level>100)throw new InvalidOperationException("Adrenaline must be from 0 to 100.");}
            else if(action=="addstatus")
            {if(args.Length==0||args.Any(c=>!(char.IsLetterOrDigit(c)||c=='_'||c=='-'||c=='.')))throw new InvalidOperationException("Choose a registered status effect name.");}
            else if(args.Length!=0)throw new InvalidOperationException("This action takes no arguments.");
            return string.Join(" ",parts);
        }
    }
}
