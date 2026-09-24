using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicSentinel.Runtime
{
    internal static class SentinelCommandConsole
    {
        private static readonly FieldInfo Commands = AccessTools.Field(typeof(Terminal), "commands");
        private static readonly Queue<string> Lines = new Queue<string>();
        private static DateTime _captureUntil;
        private static ZNet _captureNetwork;
        private const int MaximumLines = 100;

        internal static IReadOnlyList<Terminal.ConsoleCommand> Catalog()
        {
            return SentinelCommandProvider.Catalog();
        }

        internal static string Output => string.Join("\n", Lines.ToArray());
        internal static void Reset()
        {
            Lines.Clear();
            _captureUntil = DateTime.MinValue;
            _captureNetwork = null;
        }
        internal static void Write(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (message.Length > 2048) message = message.Substring(0, 2048) + "…";
            Lines.Enqueue(message);
            while (Lines.Count > MaximumLines) Lines.Dequeue();
        }

        internal static void Execute(string command)
        {
            if (!SentinelCommandProvider.Available(out string status)) throw new InvalidOperationException(status);
            Console terminal = Console.instance;
            if (terminal == null || Player.m_localPlayer == null || ZNet.instance == null)
                throw new InvalidOperationException("Join a world with your administrator character before running commands.");
            string name = command.Split(' ')[0];
            if (!Catalog().Any(value => string.Equals(value.Command, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Unknown command. Choose a command from the loaded command guide.");
            _captureNetwork = ZNet.instance;
            _captureUntil = DateTime.UtcNow.AddSeconds(30);
            Write("> " + command);
            // Called only after this exact command received Sentinel server authorization.
            // RunAction preserves native argument handling and achievement confirmation;
            // no global IsServer/IsValid patch grants permission to other callers.
            var handler=Catalog().First(value=>string.Equals(value.Command,name,StringComparison.OrdinalIgnoreCase));
            handler.RunAction(new Terminal.ConsoleEventArgs(command,terminal,handler));
        }

        internal static void Observe(Terminal source, string text)
        {
            if (DateTime.UtcNow <= _captureUntil && ReferenceEquals(_captureNetwork, ZNet.instance) &&
                ReferenceEquals(source, Console.instance)) Write(text);
        }

        internal static string Guide(string command)
        {
            switch (command.ToLowerInvariant())
            {
                case "devcommands": return global::Runic.Localization.RunicText.Get("text_7095a5f35451");
                case "tp": return global::Runic.Localization.RunicText.Get("text_3ec06d8d658d");
                case "spawn": return global::Runic.Localization.RunicText.Get("text_54468b74a0ed");
                case "debugmode": return global::Runic.Localization.RunicText.Get("text_c1e778608efb");
                case "nocost": return global::Runic.Localization.RunicText.Get("text_182d95e8ce99");
                case "server": return global::Runic.Localization.RunicText.Get("text_634315859acd");
                case "confirmcheats": return global::Runic.Localization.RunicText.Get("text_51c47106bef2");
                default: return global::Runic.Localization.RunicText.Get("text_9bebd0895d99");
            }
        }
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), new[] { typeof(string) })]
    internal static class SentinelCommandOutputPatch
    {
        private static void Postfix(Terminal __instance, string text) => SentinelCommandConsole.Observe(__instance, text);
    }
}
