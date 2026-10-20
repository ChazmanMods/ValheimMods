using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;

namespace RunicStorage.Runtime;

// Observe the reported hitch without changing pickups, physics, or dungeon streaming.
internal static class SmartSortDiagnostics
{
    private static ConfigEntry<bool> _enabled;
    private static double _nextReport;
    private static readonly Dictionary<string, Sample> Samples = new();
    private static readonly Dictionary<string, Recorder> Recorders = new();
    private static readonly HashSet<string> EnabledHere = new();
    private static readonly Dictionary<MethodBase, string> MethodNames = new();
    private static bool _recordersStarted;
    private static int _frames, _slowFrames;
    private static double _maxFrame;
    private sealed class Sample { internal int Calls, MaxFrame; internal double Total, Max; internal string At = ""; }
    internal static bool Enabled => _enabled?.Value == true && Player.m_localPlayer;
    internal static void Bind(ConfigFile config) => _enabled = config.Bind("SmartSort Diagnostics", "PerformanceLogging", false,
        "Optional diagnostics: aggregate pickup, inventory callbacks, dungeon spawn, physics markers, and slow frames at most once per 10 seconds. Does not alter gameplay.");

    internal static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    internal static void Finish(string name, long start)
    {
        if (start == 0) return;
        double elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        Record(name, elapsed);
    }
    private static void Record(string name, double ms)
    {
        if (!Samples.TryGetValue(name, out var sample)) Samples[name] = sample = new Sample();
        sample.Calls++; sample.Total += ms;
        if (ms > sample.Max)
        {
            sample.Max = ms; sample.MaxFrame = Time.frameCount;
            if (ms >= 25) sample.At = DateTime.Now.ToString("HH:mm:ss.fff");
        }
    }
    internal static void Tick()
    {
        if (!Enabled) { Stop(); return; }
        if (!_recordersStarted)
        {
            _recordersStarted = true;
            foreach (string name in new[] { "Physics.Simulate", "Physics.Processing", "GC.Collect", "Camera.Render", "Loading.AwakeFromLoad", "Gfx.WaitForPresentOnGfxThread" })
            {
                try
                {
                    var recorder = Recorder.Get(name);
                    if (recorder != null && recorder.isValid)
                    {
                        if (!recorder.enabled) { recorder.enabled = true; EnabledHere.Add(name); }
                        Recorders[name] = recorder;
                    }
                }
                catch { /* Some release players omit profiler markers. Timed methods still work. */ }
            }
            Plugin.Log.LogInfo("SmartSort timing enabled; available native markers=" + string.Join(",", Recorders.Keys) + ". Method timings include nested calls and must not be added together.");
            _nextReport = Time.realtimeSinceStartupAsDouble + 10;
        }
        _frames++;
        double frame = Time.unscaledDeltaTime * 1000d;
        _maxFrame = Math.Max(_maxFrame, frame);
        if (frame >= 50) _slowFrames++;
        foreach (var pair in Recorders)
            if (pair.Value.sampleBlockCount > 0) Record(pair.Key, pair.Value.elapsedNanoseconds / 1000000d);
        if (Time.realtimeSinceStartupAsDouble < _nextReport) return;
        _nextReport = Time.realtimeSinceStartupAsDouble + 10;
        // Avoid a feedback loop where routine 50ms notices themselves produce synchronous log I/O hitches.
        if (_maxFrame >= 100 || Samples.Values.Any(s => s.Max >= 25))
        {
            string timings = string.Join("; ", Samples.OrderBy(p => p.Key).Select(p =>
                $"{p.Key}: calls={p.Value.Calls} totalMs={p.Value.Total:0.0} maxMs={p.Value.Max:0.0} maxFrame={p.Value.MaxFrame} at={p.Value.At}"));
            string unsampled = string.Join(",", Recorders.Keys.Where(key => !Samples.ContainsKey(key)));
            Plugin.Log.LogInfo($"SmartSort performance: time={DateTime.Now:HH:mm:ss} frames={_frames} framesOver50ms={_slowFrames} maxFrameMs={_maxFrame:0.0}; {timings}; nativeMarkersWithoutSamples=[{unsampled}] (not zero cost)");
        }
        Samples.Clear(); _frames = _slowFrames = 0; _maxFrame = 0;
    }
    internal static void Stop()
    {
        foreach (var name in EnabledHere) Recorders[name].enabled = false;
        EnabledHere.Clear();
        Recorders.Clear(); Samples.Clear(); _recordersStarted = false;
        _frames = _slowFrames = 0; _maxFrame = 0;
    }

    [HarmonyPatch]
    private static class TimedMethods
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var pair in new[] {
                (typeof(Player), "AutoPickup"), (typeof(Humanoid), "Pickup"),
                (typeof(Player), "OnInventoryChanged"), (typeof(DungeonGenerator), "Spawn"),
                (typeof(DungeonGenerator), "Load"), (typeof(DungeonGenerator), "OnRoomLoaded"),
                (typeof(ZNetScene), "CreateDestroyObjects"), (typeof(ZNetScene), "CreateObjects"),
                (typeof(ZNetScene), "RemoveObjects"), (typeof(ZoneSystem), "Update"), (typeof(SmartSortOrganizer), "Handle"),
                (typeof(ChestExteriorLabel), "Update"), (typeof(InventoryTrash), "Tick") })
            {
                var method = AccessTools.Method(pair.Item1, pair.Item2);
                if (method != null) { MethodNames[method] = method.DeclaringType.Name + "." + method.Name; yield return method; }
            }
        }
        private static void Prefix(out long __state) => __state = Start();
        private static Exception Finalizer(MethodBase __originalMethod, long __state, Exception __exception)
        {
            if (__state != 0 && MethodNames.TryGetValue(__originalMethod, out string name)) Finish(name, __state);
            return __exception;
        }
    }
}
