using System;
using System.Globalization;
using System.Linq;

namespace RunicSentinel.PlayerActions
{
    internal static class PlayerActionProtocol
    {
        internal const string CapabilityRpc="runic.sentinel.player.capability.v1",ActionRpc="runic.sentinel.player.action.v1",ResultRpc="runic.sentinel.player.result.v1";
        internal const int Schema=2,MaximumBytes=4096;
        internal static readonly string[] Actions={"raiseskill","resetskill","heal","puke","clearstatus","adrenaline","addstatus","god","ghost","fly","nocost","modes"};
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
            else if(action=="god"||action=="ghost"||action=="fly"||action=="nocost")
            {if(args!="on"&&args!="off")throw new InvalidOperationException("Choose On or Off.");}
            else if(action=="addstatus")
            {
                if(parts.Length<1||parts.Length>3||parts[0].Any(c=>!(char.IsLetterOrDigit(c)||c=='_'||c=='-'||c=='.')))throw new InvalidOperationException("Choose a registered status effect name, optionally followed by duration and intensity.");
                for(int i=1;i<parts.Length;i++)if(!float.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||float.IsNaN(value)||float.IsInfinity(value)||value<0)throw new InvalidOperationException("Duration and intensity must be finite, nonnegative numbers.");
            }
            else if(args.Length!=0)throw new InvalidOperationException("This action takes no arguments.");
            return string.Join(" ",parts);
        }
    }
}
