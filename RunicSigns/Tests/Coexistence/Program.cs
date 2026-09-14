using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

internal static class Program
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
    private static bool _storageOpen, _signsOpen;
    private static int Main(string[] args)
    {
        bool expectLegacyFailure = args.Contains("--expect-legacy-failure");
        bool sawFailure = false;
        foreach (bool reverseOrder in new[] { false, true })
        {
            var storage = Assembly.Load("StorageHooks"); var signs = Assembly.Load("SignsHooks");
            var storageState = storage.GetTypes().Single(t => t.Name == "ModalGameplayInput");
            var signsState = signs.GetTypes().Single(t => t.Name == "ModalGameplayInput");
            var h1 = new Harmony("test.runicstorage"); var h2 = new Harmony("test.runicsigns");
            try
            {
                storageState.GetMethod("Reset", Flags).Invoke(null, null);
                signsState.GetMethod("Reset", Flags).Invoke(null, null);
                storageState.GetField("IsOpen", Flags).SetValue(null, (Func<bool>)(() => _storageOpen));
                signsState.GetField("IsOpen", Flags).SetValue(null, (Func<bool>)(() => _signsOpen));
                if (reverseOrder) { h2.PatchAll(signs); h1.PatchAll(storage); }
                else { h1.PatchAll(storage); h2.PatchAll(signs); }
                foreach (bool openStorage in new[] { false, true })
                {
                    storageState.GetField("_cameraDepth", Flags).SetValue(null, 0);
                    signsState.GetField("_cameraDepth", Flags).SetValue(null, 0);
                    _storageOpen = _signsOpen = false; Time.frameCount += 10;
                    var camera = new GameCamera();
                    _storageOpen = openStorage; _signsOpen = !openStorage;
                    camera.UpdateCamera(.016f);
                    if (camera.Look.x != 0) throw new Exception("Editor did not block camera input.");
                    _storageOpen = _signsOpen = false; Time.frameCount += 10;
                    camera.UpdateCamera(.016f);
                    bool recovered = ZInput.GetButton("Forward") && ZInput.GetButtonDown("Use") && camera.Look.x == 1 &&
                        !(bool)storageState.GetProperty("CameraBlocked", Flags).GetValue(null) &&
                        !(bool)signsState.GetProperty("CameraBlocked", Flags).GetValue(null);
                    if (!recovered)
                    {
                        sawFailure = true;
                        Console.WriteLine("REPRODUCED stuck input: reverseOrder=" + reverseOrder + ", openStorage=" + openStorage);
                        if (!expectLegacyFailure) throw new Exception("Camera/action input remained blocked after closing.");
                    }
                    else Console.WriteLine("PASS both-mod close recovery: reverseOrder=" + reverseOrder + ", openStorage=" + openStorage);
                    if (!expectLegacyFailure)
                    {
                        _signsOpen = true; Time.frameCount += 10; camera.Throw = true;
                        try { camera.UpdateCamera(.016f); } catch (InvalidOperationException) { }
                        _signsOpen = false; Time.frameCount += 10;
                        if (!ZInput.GetButton("Forward") || ZInput.GetMouseDelta().x != 1)
                            throw new Exception("Exception path leaked camera input scope.");
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { h1.UnpatchSelf(); h2.UnpatchSelf(); }
        }
        if (expectLegacyFailure && !sawFailure) { Console.Error.WriteLine("Legacy bug was not reproduced."); return 1; }
        Console.WriteLine(expectLegacyFailure ? "CONFIRMED legacy duplicate-type Harmony state collision." :
            "PASS production hooks coexist in both installation orders, either editor, nested camera calls and exception cleanup.");
        return 0;
    }
}
