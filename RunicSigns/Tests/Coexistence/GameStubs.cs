using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

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
