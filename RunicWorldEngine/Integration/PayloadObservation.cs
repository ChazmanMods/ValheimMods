using System.Runtime.CompilerServices;
using HarmonyLib;
using RunicWorldEngine.Core;

namespace RunicWorldEngine.Integration
{
    internal static class PayloadObservation
    {
        // Entries are attached only to the bounded set of sampled sockets. Weak keys do not retain
        // disconnected sockets; packet hooks allocate nothing and never reset Valheim's counters.
        private static ConditionalWeakTable<ZNetStats, PayloadMeter> _meters = new ConditionalWeakTable<ZNetStats, PayloadMeter>();
        internal static PayloadMeter Meter(ZNetStats socket) => _meters.GetValue(socket, _ => new PayloadMeter());
        internal static void Sent(ZNetStats socket, int bytes)
        { if (_meters.TryGetValue(socket, out PayloadMeter meter)) meter.Sent(bytes); }
        internal static void Received(ZNetStats socket, int bytes)
        { if (_meters.TryGetValue(socket, out PayloadMeter meter)) meter.Received(bytes); }
        internal static void Reset() => _meters = new ConditionalWeakTable<ZNetStats, PayloadMeter>();
    }

    [HarmonyPatch(typeof(ZNetStats), "IncSentBytes", typeof(int))]
    internal static class SentPayloadObservationPatch
    {
        private static void Postfix(ZNetStats __instance, int __0) => PayloadObservation.Sent(__instance, __0);
    }
    [HarmonyPatch(typeof(ZNetStats), "IncRecvBytes", typeof(int))]
    internal static class ReceivedPayloadObservationPatch
    {
        private static void Postfix(ZNetStats __instance, int __0) => PayloadObservation.Received(__instance, __0);
    }
}
