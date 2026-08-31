using System;

namespace Runic.Foundation.Persistence
{
    /// <summary>
    /// One exception barrier shared by every Harmony/network callback. Even the failure callback
    /// is isolated so a logger, socket, or cleanup fault cannot escape Valheim's network thread.
    /// </summary>
    internal static class RpcTransportBoundary
    {
        internal static void Run(Action action, Action<Exception> failClosed)
        {
            try { action?.Invoke(); }
            catch (Exception exception)
            {
                try { failClosed?.Invoke(exception); }
                catch { }
            }
        }

        internal static bool RunGate(
            Func<bool> action,
            Action<Exception> failClosed,
            bool failureResult = false)
        {
            try { return action != null && action(); }
            catch (Exception exception)
            {
                try { failClosed?.Invoke(exception); }
                catch { }
                return failureResult;
            }
        }
    }
}
