using System;
using System.Linq;
using System.Reflection;
using RunicBuildCamera.Core;
using RunicBuildCamera.Integration;

namespace RunicBuildCamera.Tests
{
    internal static class ExitInputTests
    {
        internal static void Register()
        {
            for (int slot = 1; slot <= 8; slot++)
            {
                string button = "Hotbar" + slot;
                TestRunner.Run(button + " releases input for vanilla unequip/switch", () =>
                {
                    int stops = 0;
                    TestAssert.True(CameraExitInput.ReleaseIfRequested(true,
                        () => CameraExitInput.HotbarRequested(name => name == button), () => stops++));
                    TestAssert.Equal(1, stops);
                });
            }
            TestRunner.Run("native UI denial does not poll or replay item actions", () =>
                TestAssert.False(CameraExitInput.ReleaseIfRequested(false,
                    () => throw new Exception("Polled while input denied"),
                    () => throw new Exception("Stopped while input denied"))));
            TestRunner.Run("movement and building without exit retain camera isolation", () =>
                TestAssert.False(CameraExitInput.ReleaseIfRequested(true,
                    () => CameraExitInput.HotbarRequested(name => name == "Forward"),
                    () => throw new Exception("Unexpected stop"))));
            TestRunner.Run("keyboard hide works while in build mode", () =>
                TestAssert.True(CameraExitInput.HideRequested(true, false, false, true, false)));
            TestRunner.Run("controller hide preserves vanilla layout and placement restrictions", () =>
            {
                for (int mask = 0; mask < 32; mask++)
                {
                    bool key = (mask & 1) != 0, alternate = (mask & 2) != 0,
                        released = (mask & 4) != 0, building = (mask & 8) != 0, alt = (mask & 16) != 0;
                    bool expected = alternate ? !building && released && alt : key || released && !alt && !building;
                    TestAssert.Equal(expected, CameraExitInput.HideRequested(key, alternate, released, building, alt));
                }
            });
            TestRunner.Run("exit key reaches native handler with either plugin update order", () =>
            {
                // Tick no longer consumes/stops on Hide. Native TakeInput owns the exit
                // in both schedules, so the very same frame can reach native Hide/Hotbar.
                foreach (bool tickFirst in new[] { false, true })
                {
                    bool active = true;
                    int nativeActions = 0;
                    Action tick = () => { };
                    if (tickFirst) tick();
                    bool takeInput = true;
                    if (active && !CameraExitInput.ReleaseIfRequested(takeInput, () => true, () => active = false))
                        takeInput = false;
                    if (takeInput) nativeActions++;
                    if (!tickFirst) tick();
                    TestAssert.False(active);
                    TestAssert.Equal(1, nativeActions);
                }
            });
            TestRunner.Run("runtime wires exit before suppression without synthetic equip", () =>
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                MethodInfo postfix = typeof(PlayerTakeInputIsolationPatch).GetMethod("Postfix", flags);
                TestAssert.True(IlReader.Calls(postfix, typeof(CameraExitInput), "ReleaseIfRequested"));
                MethodInfo tick = typeof(BuildCameraRuntime).GetMethod("Tick", flags);
                TestAssert.False(IlReader.LoadsString(tick, "Hide"));
                TestAssert.False(IlReader.LoadsString(tick, "JoyHide"));
                MethodInfo controller = typeof(PlayerHotbarCameraExitPatch).GetMethod("Prefix", flags);
                TestAssert.Equal(typeof(void), controller.ReturnType);
                TestAssert.True(IlReader.Calls(controller, typeof(BuildCameraRuntime), "ForceStop"));
                TestAssert.False(IlReader.Calls(controller).Any(call => call.Name == "UseHotbarItem"));
                MethodInfo suppress = typeof(PlayerUpdateInputIsolation).GetMethod("ShouldSuppress", flags);
                TestAssert.True(IlReader.Calls(suppress, typeof(BuildCameraRuntime), "ShouldFreezePlayer"));
            });
        }
    }
}
