using System;

namespace Runic.Foundation.Core.Tests
{
    internal static class NotificationTests
    {
        internal static void Register()
        {
            TestRunner.Run("Notification bus rate-limits by source code and context", RateLimiting);
            TestRunner.Run("Publishers cannot shorten the configured notification floor", GlobalRateLimitFloor);
            TestRunner.Run("Notification requests require a reason and remedy", ActionableValidation);
            TestRunner.Run("Notification subscribers are isolated", SubscriberIsolation);
            TestRunner.Run("Notification rate-limit tracking stays bounded", BoundedTracking);
        }

        private static void RateLimiting()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
            NotificationBus bus = new NotificationBus(() => now, TimeSpan.FromSeconds(10));
            NotificationRequest request = Request("same-container");

            TestAssert.True(bus.Publish(request).Published);
            NotificationPublishResult blocked = bus.Publish(request);
            TestAssert.True(blocked.RateLimited);
            TestAssert.Equal(TimeSpan.FromSeconds(10), blocked.RetryAfter);

            now = now.AddSeconds(4);
            TestAssert.Equal(TimeSpan.FromSeconds(6), bus.Publish(request).RetryAfter);
            TestAssert.True(bus.Publish(Request("different-container")).Published);

            now = now.AddSeconds(6);
            TestAssert.True(bus.Publish(request).Published);
        }

        private static void ActionableValidation()
        {
            TestAssert.Throws<ArgumentException>(() => new NotificationRequest(
                "test.module",
                "denied",
                NotificationSeverity.Warning,
                " ",
                "Ask the owner."));
            TestAssert.Throws<ArgumentException>(() => new NotificationRequest(
                "test.module",
                "denied",
                NotificationSeverity.Warning,
                "Access was denied.",
                " "));
        }

        private static void GlobalRateLimitFloor()
        {
            DateTimeOffset now = DateTimeOffset.UnixEpoch;
            NotificationBus bus = new NotificationBus(() => now, TimeSpan.FromSeconds(10));
            NotificationRequest request = new NotificationRequest(
                "test.module",
                "access.denied",
                NotificationSeverity.Warning,
                "Access was denied.",
                "Ask the owner.",
                minimumInterval: TimeSpan.Zero);
            TestAssert.True(bus.Publish(request).Published);
            TestAssert.Equal(TimeSpan.FromSeconds(10), bus.Publish(request).RetryAfter);
        }

        private static void SubscriberIsolation()
        {
            DateTimeOffset now = DateTimeOffset.UnixEpoch;
            NotificationBus bus = new NotificationBus(() => now, TimeSpan.Zero);
            int called = 0;
            bus.Published += (_, __) => throw new InvalidOperationException("listener");
            bus.Published += (_, arguments) =>
            {
                called++;
                TestAssert.Equal("Access was denied.", arguments.Notification.Reason);
                TestAssert.Equal("Ask the owner.", arguments.Notification.Remedy);
            };
            TestAssert.True(bus.Publish(Request()).Published);
            TestAssert.Equal(1, called);
        }

        private static void BoundedTracking()
        {
            DateTimeOffset now = DateTimeOffset.UnixEpoch;
            NotificationBus bus = new NotificationBus(
                () => now,
                TimeSpan.FromMinutes(1),
                maxTrackedKeys: 2);
            bus.Publish(Request("one"));
            now = now.AddSeconds(1);
            bus.Publish(Request("two"));
            now = now.AddSeconds(1);
            bus.Publish(Request("three"));
            TestAssert.Equal(2, bus.TrackedRateLimitKeyCount);
        }

        private static NotificationRequest Request(string key = "container") =>
            new NotificationRequest(
                "test.module",
                "access.denied",
                NotificationSeverity.Warning,
                "Access was denied.",
                "Ask the owner.",
                key);
    }
}
