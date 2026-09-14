using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Runic.Shared;
using UnityEngine;

// Execute the production Harmony patches against minimal versions of the verified game entry
// points. This checks patch binding, caller scoping, native neutral controls and exception cleanup.
namespace UnityEngine {
    public static class Time { public static int frameCount; }
    public enum KeyCode { E = 101, Escape = 27, Mouse0 = 323, Mouse6 = 329 }
    public struct Vector2 { public float x, y; public static Vector2 zero => default; }
    public struct Vector3 { public float x, y, z; public static Vector3 zero => default; }
}
public class Player {
    public static Player m_localPlayer = new Player();
    public Vector3 LastMove; public bool LastAction;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool TakeInput() => true;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetControls(Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack,
        bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge)
    { LastMove = movedir; LastAction = attack || attackHold || secondaryAttack || secondaryAttackHold || block || blockHold || jump || crouch || run || autoRun || dodge; }
}
public class PlayerController {
    [MethodImpl(MethodImplOptions.NoInlining)] public bool TakeInput(bool gamepad) => true;
}
public class ZInput {
    [MethodImpl(MethodImplOptions.NoInlining)] public static float GetMouseScrollWheel() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static float GetJoyLeftStickX(bool smooth) => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static float GetJoyRightStickY(bool smooth) => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static float GetJoyRTrigger() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static Vector2 GetMouseDelta() => new Vector2 { x = 1, y = 1 };
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetButton(string name) => true;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetButtonDown(string name) => true;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetButtonUp(string name) => true;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetKey(KeyCode key, bool logWarning) => true;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetKeyDown(KeyCode key, bool logWarning) => true;
}
public class GameCamera {
    public bool Throw; public float Scroll, Stick; public Vector2 Look; public bool Zoom;
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateCamera(float dt) {
        Scroll = ZInput.GetMouseScrollWheel(); Look = ZInput.GetMouseDelta(); Zoom = ZInput.GetButton("CamZoomIn");
        UpdateFreeFly(dt); if (Throw) throw new InvalidOperationException("Camera failure");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateFreeFly(float dt) { Stick = ZInput.GetJoyRightStickY(false); }
}
internal static class Program {
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static int Main() {
        var harmony = new Harmony("runic.modal-input.regression");
        try {
            harmony.PatchAll(typeof(ModalGameplayInput).Assembly);
            var controller = new PlayerController(); bool open = false; ModalGameplayInput.IsOpen = () => open;
            Time.frameCount = 10;
            Check(controller.TakeInput(false) && Player.m_localPlayer.TakeInput(), "Closed panel allows native controls.");
            var camera = new GameCamera(); camera.UpdateCamera(.016f);
            Check(camera.Scroll == 1 && camera.Look.x == 1 && camera.Stick == 1 && camera.Zoom, "Closed panel allows camera input.");
            open = true; Time.frameCount++;
            Check(!controller.TakeInput(false) && !controller.TakeInput(true), "Block the actual movement gate on keyboard and controller.");
            Check(!Player.m_localPlayer.TakeInput() && new Player().TakeInput(), "Only block local Player input.");
            Player.m_localPlayer.SetControls(new Vector3 { x = 1 }, true, true, true, true, true, true, true, true, true, true, true);
            Check(Player.m_localPlayer.LastMove.x == 0 && !Player.m_localPlayer.LastAction, "Neutralize controls sampled before opening.");
            Check(!ZInput.GetButton("Left") && !ZInput.GetButtonDown("Use") && !ZInput.GetButtonUp("Attack"), "Block movement, use and action bindings.");
            Check(!ZInput.GetKeyDown(KeyCode.E, false), "Typing E cannot trigger a raw-key mod shortcut.");
            Check(ZInput.GetKeyDown(KeyCode.Escape, false) && ZInput.GetButton("JoyButtonB"), "Keep panel-owned cancel input available.");
            Check(ZInput.GetMouseScrollWheel() == 1 && ZInput.GetMouseDelta().x == 1, "UI scroll and pointer input remain available outside camera code.");
            camera.UpdateCamera(.016f);
            Check(camera.Scroll == 0 && camera.Look.x == 0 && camera.Stick == 0 && !camera.Zoom, "Camera scope blocks wheel, look, stick and zoom buttons.");
            camera.Throw = true; try { camera.UpdateCamera(.016f); } catch (InvalidOperationException) { }
            Check(ZInput.GetMouseScrollWheel() == 1 && !ModalGameplayInput.CameraBlocked, "Nested camera scopes unwind even on exception.");
            open = false; Check(!controller.TakeInput(false), "Closing frame remains consumed.");
            Time.frameCount += 2; Check(controller.TakeInput(false) && ZInput.GetKeyDown(KeyCode.E, false), "Controls resume after close grace.");
            camera.Throw = false; camera.UpdateCamera(.016f); Check(camera.Scroll == 1, "Camera resumes after close.");
            Console.WriteLine("PASS live HarmonyX modal-input patches: controller, controls, typing, camera/UI separation, nested exception cleanup and close recovery.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { harmony.UnpatchSelf(); ModalGameplayInput.Reset(); }
    }
}
