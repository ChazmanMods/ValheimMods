using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicClock;
using UnityEngine;
using BepInEx.Configuration;

internal static class Program
{
    private static int Main()
    {
        Action[] tests = { MidnightNoonAndBoundaryFormatting, EveryMinuteFormatsWithoutOverflow,
            InvalidTimesAreRejected, AnchorsAndOffsetsStayInsideSafeArea, LayoutHandlesBadConfig,
            DedicatedProcessDoesNothing, WorldLifecycleClearsOldDisplay, RefreshIsBoundedAndConfigIsImmediate,
            DisplayUsesNativeDayAndPhase, ToggleAndTypingAreIsolated, VisibilityAndLoadingAreRespected,
            RealTimeFormatsIndependently, InvalidTimeAndErrorsNeverBreakGameplay, DestroyDisposesView,
            NativeContractsAndClientOnlyAssembly, PackageMetadataMatches };
        int passed = 0;
        foreach (Action test in tests)
            try { test(); System.Console.WriteLine("PASS " + test.Method.Name); passed++; }
            catch (Exception error) { System.Console.WriteLine("FAIL " + test.Method.Name + ": " + error); }
        System.Console.WriteLine($"{passed}/{tests.Length} checks passed.");
        return passed == tests.Length ? 0 : 1;
    }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static string GameTime(double fraction, bool format = true) { True(ClockModel.TryGameTime(fraction, format, out string text)); return text; }
    private static void MidnightNoonAndBoundaryFormatting()
    {
        Equal("00:00", GameTime(0)); Equal("00:00", GameTime(1)); Equal("06:00", GameTime(0.25));
        Equal("12:00", GameTime(0.5)); Equal("18:00", GameTime(0.75)); Equal("23:59", GameTime(0.999999));
        Equal("12:00 AM", GameTime(0, false)); Equal("12:00 PM", GameTime(0.5, false)); Equal("6:00 PM", GameTime(0.75, false));
    }
    private static void EveryMinuteFormatsWithoutOverflow()
    {
        for (int minute = 0; minute < 1440; minute++)
            Equal(ClockModel.FormatTime(minute / 60, minute % 60, true), GameTime((minute + 0.1) / 1440));
    }
    private static void InvalidTimesAreRejected()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1, 1.1 })
            True(!ClockModel.TryGameTime(value, true, out _));
    }
    private static void AnchorsAndOffsetsStayInsideSafeArea()
    {
        for (int anchor = 0; anchor < 9; anchor++)
        {
            var p = ClockModel.Position((ClockAnchor)anchor, 10, 20, 1000, 800, 200, 100, 0, 0);
            Equal(10.0 + 400 * (anchor % 3), p.X); Equal(20.0 + 350 * (anchor / 3), p.Y);
            foreach (int offset in new[] { -9000, -100, 100, 9000 })
            {
                p = ClockModel.Position((ClockAnchor)anchor, 10, 20, 1000, 800, 200, 100, offset, offset);
                True(p.X >= 10 && p.X <= 810 && p.Y >= 20 && p.Y <= 720);
            }
        }
    }
    private static void LayoutHandlesBadConfig()
    {
        Equal(1.0, ClockModel.Clamp(double.NaN, 0.6, 2, 1));
        Equal((400.0, 0.0), ClockModel.Position((ClockAnchor)999, 0, 0, 1000, 800, 200, 100, double.NaN, double.PositiveInfinity));
        Equal((0.0, 0.0), ClockModel.Position(ClockAnchor.BottomRight, 0, 0, 10, 10, 200, 100, 0, 0));
    }
    private static Plugin New(bool batch = false)
    {
        Application.isBatchMode = batch; Time.unscaledTime = 0; Event.current = new() { type = EventType.Repaint };
        KeyboardShortcut.Pressed = false; Menu.Visible = TextInput.Visible = InventoryGui.Visible = Minimap.Visible = false;
        ZInput.VirtualKeyboardOpen = Game.Paused = false; Chat.instance = new(); TextViewer.instance = null;
        Player.m_localPlayer = new(); ZNet.instance = new(); EnvMan.instance = new(); Hud.instance = new(); ClockView.Last = null;
        var plugin = new Plugin(); Call(plugin, "Awake"); return plugin;
    }
    private static void Call(Plugin plugin, string method) => typeof(Plugin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(plugin, null);
    private static void Tick(Plugin plugin, float seconds = 0.3f) { Time.unscaledTime += seconds; Call(plugin, "Update"); Call(plugin, "OnGUI"); }
    private static void Set<T>(Plugin plugin, string key, T value) => plugin.Config.Get<T>(key).Value = value;
    private static void DedicatedProcessDoesNothing()
    {
        var p = New(true); Tick(p); True(ClockView.Last == null); Equal(0, p.Config.Entries.Count); Equal(0, EnvMan.instance.Reads);
        Call(p, "OnDestroy");
    }
    private static void WorldLifecycleClearsOldDisplay()
    {
        var p = New(); Tick(p); var view = ClockView.Last; Equal(1, view.Draws);
        Player.m_localPlayer = null; Tick(p); Equal(1, view.Draws);
        Player.m_localPlayer = new(); EnvMan.instance = new() { Day = 9 }; ZNet.instance = new();
        Call(p, "OnGUI"); Equal(1, view.Draws); Tick(p, 0.01f); Equal("Day 9", view.Day); Equal(2, view.Draws);
    }
    private static void RefreshIsBoundedAndConfigIsImmediate()
    {
        var p = New(); Tick(p); var view = ClockView.Last;
        for (int i = 0; i < 20; i++) Tick(p, 0.005f);
        Equal(1, view.Updates); Set(p, "Clock/Use24Hour", false); Tick(p, 0.001f); Equal("12:00 PM", view.Time); Equal(2, view.Updates);
    }
    private static void DisplayUsesNativeDayAndPhase()
    {
        var p = New(); EnvMan.instance.Day = 500; EnvMan.instance.Fraction = 0; Tick(p);
        Equal("Day 500", ClockView.Last.Day); True(!ClockView.Last.IsDay);
        EnvMan.instance.Fraction = 0.5f; Tick(p); True(ClockView.Last.IsDay);
    }
    private static void ToggleAndTypingAreIsolated()
    {
        var p = New(); Tick(p); KeyboardShortcut.Pressed = true; Chat.instance.Focus = true; Tick(p);
        True(p.Config.Get<bool>("General/Visible").Value);
        Chat.instance.Focus = false; TextInput.Visible = true; Tick(p); True(p.Config.Get<bool>("General/Visible").Value);
        TextInput.Visible = false; Tick(p); True(!p.Config.Get<bool>("General/Visible").Value);
        Tick(p); True(p.Config.Get<bool>("General/Visible").Value);
    }
    private static void VisibilityAndLoadingAreRespected()
    {
        var p = New(); Tick(p); var view = ClockView.Last;
        Menu.Visible = true; Tick(p); Equal(1, view.Draws);
        Set(p, "Display/HideInMenus", false); Tick(p); Equal(2, view.Draws);
        Hud.instance.m_userHidden = true; Tick(p); Equal(2, view.Draws);
        Hud.instance.m_userHidden = false; Hud.instance.m_loadingScreen.gameObject.activeInHierarchy = true; Tick(p); Equal(2, view.Draws);
        Hud.instance.m_loadingScreen.gameObject.activeInHierarchy = false; Event.current.type = EventType.MouseDown; Tick(p); Equal(2, view.Draws);
        Event.current.type = EventType.Repaint; Set(p, "General/Enabled", false); Tick(p); Equal(2, view.Draws);
        Set(p, "General/Enabled", true); Tick(p); Equal(3, view.Draws);
    }
    private static void RealTimeFormatsIndependently()
    {
        var p = New(); Set(p, "Clock/RealWorldUse24Hour", false); Tick(p);
        Equal("12:00", ClockView.Last.Time); True(ClockView.Last.Local.StartsWith("Local "));
        True(ClockView.Last.Local.EndsWith(" AM") || ClockView.Last.Local.EndsWith(" PM"));
        True(!p.Config.Get<bool>("Clock/ShowRealWorldTime").Value);
    }
    private static void InvalidTimeAndErrorsNeverBreakGameplay()
    {
        var p = New(); EnvMan.instance.Fraction = float.NaN; Tick(p); Equal(0, ClockView.Last.Draws);
        EnvMan.instance.Fraction = 0.5f; Tick(p); Equal(1, ClockView.Last.Draws);
        EnvMan.instance.Throw = true; Tick(p); Tick(p); Equal(1, p.Logger.Errors); Equal(1, ClockView.Last.Draws);
    }
    private static void DestroyDisposesView()
    {
        var p = New(); var view = ClockView.Last; Call(p, "OnDestroy"); True(view.Disposed); Call(p, "OnGUI"); Equal(0, view.Draws);
    }
    private static string ModRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RunicClock.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("Mod source not found");
    }
    private static void NativeContractsAndClientOnlyAssembly()
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(@"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed");
        resolver.AddSearchDirectory(Path.GetFullPath(Path.Combine(ModRoot(), @"..\artifacts\Valheim1.0\Migration-20260909\BepInExPack-5.4.2350\BepInExPack_Valheim\BepInEx\core")));
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(ModRoot(), @"bin\Release\netstandard2.1\RunicClock.dll"), new ReaderParameters { AssemblyResolver = resolver });
        Equal(Plugin.Version + ".0", assembly.Name.Version.ToString());
        True(!assembly.MainModule.AssemblyReferences.Any(r => r.Name.Contains("Harmony") || r.Name.StartsWith("Runic") || r.Name == "System.Net.Http"));
        int verified = 0;
        foreach (var type in assembly.MainModule.Types)
            foreach (var method in type.Methods.Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MemberReference member || member.DeclaringType == null ||
                        !member.DeclaringType.Scope.Name.StartsWith("assembly_")) continue;
                    if (member is MethodReference called)
                    {
                        True(called.Resolve() != null); verified++;
                        True(called.Name.StartsWith("get_") || called.Name.StartsWith("Is") || called.Name.StartsWith("Has") ||
                            called.Name == "GetDayFraction" || called.Name == "GetDay");
                    }
                    else if (member is FieldReference field) { True(field.Resolve() != null); True(instruction.OpCode != OpCodes.Stfld && instruction.OpCode != OpCodes.Stsfld); verified++; }
                }
        True(verified >= 20);
        System.Console.WriteLine("  Verified " + verified + " native read-only references; no Harmony/network dependencies.");
    }
    private static void PackageMetadataMatches()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(ModRoot(), "manifest.json")));
        Equal("RunicClock", manifest.RootElement.GetProperty("name").GetString());
        Equal(Plugin.Version, manifest.RootElement.GetProperty("version_number").GetString());
        Equal(1, manifest.RootElement.GetProperty("dependencies").GetArrayLength());
        True(manifest.RootElement.GetProperty("description").GetString()!.Length <= 250);
    }
}
