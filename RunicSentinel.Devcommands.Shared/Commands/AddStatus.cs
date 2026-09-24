#nullable enable
namespace RunicSentinel.Devcommands;
///<summary>Adds duration and intensity.</summary>
public class AddStatusCommand
{
  public AddStatusCommand()
  {
    AutoComplete.Register("addstatus", index =>
    {
      if (index == 0) return ParameterInfo.StatusEffects;
      if (index == 1) return ParameterInfo.Create("Effect duration in seconds.");
      if (index == 2) return ParameterInfo.Create("Effect intensity.");
      return ParameterInfo.None;
    });
    Helper.Command("addstatus", "[name] [duration] [intensity] - Adds a status effect.", (args) =>
    {
      Helper.ArgsCheck(args, 2, "Missing status name");
      float duration=0,intensity=0;
      if(args.Length>4)throw new System.InvalidOperationException("Usage: addstatus name [duration seconds] [intensity]");
      if(args.Length>2&&(!float.TryParse(args[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out duration)||float.IsNaN(duration)||float.IsInfinity(duration)||duration<0))throw new System.InvalidOperationException("Duration must be finite, nonnegative seconds. 3600 = one hour; 6000 = 100 minutes. Zero means no expiry.");
      if(args.Length>3&&(!float.TryParse(args[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out intensity)||float.IsNaN(intensity)||float.IsInfinity(intensity)||intensity<0))throw new System.InvalidOperationException("Intensity must be a finite, nonnegative number.");
      var player = Helper.GetPlayer();
      var hash = args[1].GetStableHashCode();
      player.GetSEMan().AddStatusEffect(hash, true);
      var effect = player.GetSEMan().GetStatusEffect(hash);
      if (effect == null) throw new System.InvalidOperationException("Unknown or unavailable status effect: "+args[1]);
      if (args.Length>2)
        effect.m_ttl = duration;
      if (args.Length>3)
      {
        if (effect is SE_Shield shield)
          shield.m_absorbDamage = intensity;
        if (effect is SE_Burning burning)
        {
          if (args[1] == "Burning")
          {
            burning.m_fireDamageLeft = 0;
            burning.AddFireDamage(intensity);
          }
          else
          {
            burning.m_spiritDamageLeft = 0;
            burning.AddSpiritDamage(intensity);
          }
        }
        if (effect is SE_Poison poison)
        {
          poison.m_damageLeft = intensity;
          poison.m_damagePerHit = intensity / System.Math.Max(0.001f,effect.m_ttl) * poison.m_damageInterval;
        }
      }
    });
  }
}

