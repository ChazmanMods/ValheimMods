// Private managed-runtime probe. Does not create a world, connect, or call game/network methods.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

internal static class CapacityPatchProbe
{
    private static IEnumerable<CodeInstruction> OtherTranspiler(IEnumerable<CodeInstruction> il) { return il; }
    private static void AuthPrefix() { }
    private static int Main(string[] args)
    {
        // args: game Managed directory, BepInEx core directory, plugin DLL, private config path.
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] { args[0], args[1], Path.GetDirectoryName(args[2]) })
            { string candidate = Path.Combine(folder, name); if (File.Exists(candidate)) return Assembly.LoadFrom(candidate); }
            return null;
        };
        try
        {
            typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { Path.Combine(Path.GetDirectoryName(args[3]), "probe.exe"), Path.GetDirectoryName(args[3]), args[0], new[] { args[1], args[0] } });
            return Run(args);
        }
        catch (Exception error) { System.Console.Error.WriteLine(error); return 1; }
    }
    private static int Run(string[] args)
    {
        var plugin = Assembly.LoadFrom(args[2]);
        var log = new ManualLogSource("WorldEngineProbe");
        log.LogEvent += (s, e) => System.Console.WriteLine(e.Data);
        plugin.GetType("RunicWorldEngine.Plugin").GetProperty("Log", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, log, null);
        var config = new ConfigFile(args[3], false) { SaveOnConfigSet = false };
        plugin.GetType("RunicWorldEngine.WorldEngineConfig").GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { config });
        config["Player Capacity", "Enabled"].BoxedValue = true;
        config["Player Capacity", "MaximumPlayers"].BoxedValue = 20;
        var capacity = plugin.GetType("RunicWorldEngine.Integration.CapacityRuntime");
        Action<string> call = name => capacity.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Func<bool> valid = () => (bool)capacity.GetProperty("ValidatedOverride", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
        Func<bool> integrity = () => (bool)capacity.GetMethod("CheckIntegrity", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        var game = Assembly.LoadFrom(Path.Combine(args[0], "assembly_valheim.dll"));
        var admission = AccessTools.Method(game.GetType("ZNet"), "RPC_PeerInfo");
        var root = new Harmony("chazman.RunicWorldEngine");
        var foreign = new Harmony("private.probe.foreign");
        try
        {
            root.PatchAll(plugin);
            call("Initialize");
            Require(valid() && integrity(), "native cap initialization + all mod patch signatures");
            foreign.Patch(admission, prefix: new HarmonyMethod(typeof(CapacityPatchProbe), "AuthPrefix"));
            Require(integrity(), "independent authentication prefixes preserved");
            foreign.UnpatchSelf();
            foreign.Patch(admission, transpiler: new HarmonyMethod(typeof(CapacityPatchProbe), "OtherTranspiler"));
            Require(!integrity(), "late competing transpiler faults admissions");
            foreign.UnpatchSelf();
            Require(!integrity(), "integrity fault remains sticky until restart");
            call("Reset");
            foreign.Patch(admission, transpiler: new HarmonyMethod(typeof(CapacityPatchProbe), "OtherTranspiler"));
            call("Initialize");
            Require(!valid() && !integrity(), "startup conflict rejects entire cap override");
            Require(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains("chazman.RunicWorldEngine.capacity")), "no partial cap patches after rejection");
            call("Reset"); foreign.UnpatchSelf();
            call("Initialize"); Require(valid(), "clean restart restores validated override");
            new Harmony("chazman.RunicWorldEngine.capacity").Unpatch(admission, HarmonyPatchType.Transpiler, "chazman.RunicWorldEngine.capacity");
            Require(!integrity(), "missing cap patch faults admissions");
            System.Console.WriteLine("8/8 native managed patch probes passed.");
            return 0;
        }
        finally { foreign.UnpatchSelf(); call("Reset"); root.UnpatchSelf(); }
    }
    private static void Require(bool result, string name)
    { if (!result) throw new Exception("FAIL " + name); System.Console.WriteLine("PASS " + name); }
}
