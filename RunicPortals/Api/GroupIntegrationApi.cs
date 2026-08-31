using RunicPortals.Integration;

namespace RunicPortals.Api
{
    public static class GroupIntegrationApi
    {
        private static readonly object Gate = new object();
        private static PortalGroupRuntime _runtime;

        public static bool TryIsMember(string groupId, long playerId, out bool isMember)
        {
            lock (Gate)
            {
                if (_runtime != null)
                    return _runtime.TryIsMember(groupId, playerId, out isMember);
                isMember = false;
                return false;
            }
        }

        internal static void Attach(PortalGroupRuntime runtime)
        {
            lock (Gate) _runtime = runtime;
        }

        internal static void Detach(PortalGroupRuntime runtime)
        {
            lock (Gate)
                if (ReferenceEquals(_runtime, runtime)) _runtime = null;
        }
    }
}
