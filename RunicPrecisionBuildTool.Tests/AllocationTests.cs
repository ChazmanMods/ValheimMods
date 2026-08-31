using System;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class AllocationTests
    {
        internal static void Register()
        {
            TestRunner.Run("Ordinary qualified input frames allocate zero managed bytes", OrdinaryInputFrame);
            TestRunner.Run("Ordinary pose composition allocates zero managed bytes", OrdinaryPoseFrame);
            TestRunner.Run("Complete ordinary core frames allocate zero managed bytes", CompleteCoreFrame);
        }

        private static void OrdinaryInputFrame()
        {
            FakeInputSource input = new FakeInputSource();
            InputRouter router = new InputRouter(input);
            InputBindings bindings = InputBindings.Default;
            InputContext context = InputRouterTests.NewContext();
            int checksum = 0;

            for (int index = 0; index < 10000; index++)
                checksum += (int)router.ProcessFrame(context, bindings).ActiveAxis;

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100000; index++)
                checksum += (int)router.ProcessFrame(context, bindings).ActiveAxis;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            GC.KeepAlive(checksum);
            TestAssert.Equal(0L, allocated, $"Input routing allocated {allocated} bytes.");
        }

        private static void OrdinaryPoseFrame()
        {
            PlacementSession session = new PlacementSession();
            PoseController controller = new PoseController(session);
            Quaternion baseRotation = QuaternionMath.AngleAxis(22.5f, 0f, 1f, 0f);
            controller.ObserveSelection(77, baseRotation, 1);
            Vector3 candidate = new Vector3(2f, 3f, 4f);
            float checksum = 0f;

            for (int index = 0; index < 10000; index++)
            {
                Quaternion rotation = controller.ComposeEffectiveRotation(baseRotation);
                checksum += controller.ComposePosition(candidate, rotation).x;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100000; index++)
            {
                Quaternion rotation = controller.ComposeEffectiveRotation(baseRotation);
                checksum += controller.ComposePosition(candidate, rotation).x;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            GC.KeepAlive(checksum);
            TestAssert.Equal(0L, allocated, $"Pose composition allocated {allocated} bytes.");
        }

        private static void CompleteCoreFrame()
        {
            FakeInputSource input = new FakeInputSource();
            InputRouter router = new InputRouter(input);
            InputBindings bindings = InputBindings.Default;
            InputContext context = InputRouterTests.NewContext();
            PlacementSession session = new PlacementSession();
            PoseController pose = new PoseController(session);
            Quaternion vanilla = QuaternionMath.AngleAxis(37f, 0f, 1f, 0f);
            pose.ObserveSelection(88, vanilla, 2);
            float checksum = 0f;

            for (int index = 0; index < 10000; index++)
                checksum += RunCoreFrame(router, bindings, context, pose, vanilla);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100000; index++)
                checksum += RunCoreFrame(router, bindings, context, pose, vanilla);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            GC.KeepAlive(checksum);
            TestAssert.Equal(0L, allocated, $"Complete core frames allocated {allocated} bytes.");
        }

        private static float RunCoreFrame(
            InputRouter router,
            InputBindings bindings,
            InputContext context,
            PoseController pose,
            Quaternion vanilla)
        {
            pose.ObserveSelection(88, vanilla, 2);
            InputFrameResult frame = router.ProcessFrame(context, bindings);
            pose.UpdateWheelPreview(frame.ActiveAxis, frame.ActiveStep, frame.ActiveStepIsFine);
            Quaternion effective = pose.ComposeEffectiveRotation(vanilla);
            OrientationAngles angles = OrientationAngles.FromQuaternion(effective);
            return angles.Yaw + pose.ComposePosition(Vector3.zero, effective).x;
        }
    }
}
