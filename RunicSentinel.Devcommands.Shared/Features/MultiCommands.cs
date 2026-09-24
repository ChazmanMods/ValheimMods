#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicSentinel.Devcommands;

public class MultiCommands(Terminal terminal, string[] commands)
{
  public static string[] Split(string text) => Settings.MultiCommand ? text.Split(';').Select(s => s.Trim()).ToArray() : [text];
  private static readonly List<MultiCommands> Groups = [];
  public static void Clear()=>Groups.Clear();
  public static void Handle(Terminal terminal, string[] commands)
  {
    if(commands.Length>128||Groups.Count>=16){terminal.AddString("Too many queued commands (maximum 128 per chain, 16 chains).");return;}
    MultiCommands cmd = new(terminal, commands);
    cmd.Run(0);
    if (cmd.IsDone()) return;
    Groups.Add(cmd);
  }
  public static void Execute(float dt)
  {
    for (var i = 0; i < Groups.Count; i++)
    {
      Groups[i].Run(dt);
      if (Groups[i].IsDone())
      {
        Groups.RemoveAt(i);
        i--;
      }
    }
  }


  private readonly Queue<string> Commands = new(commands);
  private readonly Terminal Terminal = terminal;
  private float WaitTimer = 0f;

  public bool IsDone() => Commands.Count() == 0;

  public void Run(float dt)
  {
    if(SentinelBridge.Busy)return;
    if (WaitTimer > -0.01)
      WaitTimer -= dt;
    // Another check to execute at the same frame as the timer is done.
    if (WaitTimer > -0.01)
      return;
    while (Commands.Count() > 0)
    {
      var command = Commands.Dequeue();
      if (command.StartsWith("wait ", StringComparison.InvariantCultureIgnoreCase))
      {
        var args = command.Split(' ');
        if (args.Length < 2) continue;
        if(!float.TryParse(args[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var delay)||float.IsNaN(delay)||float.IsInfinity(delay)||delay<0){Terminal.AddString("wait requires finite, nonnegative milliseconds.");Commands.Clear();return;}
        WaitTimer = delay / 1000f;
        // Might need some tweaks to take in account frame time.
        break;
      }
      Terminal.TryRunCommand(command);
      if(SentinelBridge.Busy)break;
    }
  }
}