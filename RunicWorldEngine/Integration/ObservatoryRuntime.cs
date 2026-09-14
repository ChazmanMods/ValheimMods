using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RunicWorldEngine.Contracts;
using RunicWorldEngine.Core;

namespace RunicWorldEngine.Integration
{
    internal static class ObservatoryRuntime
    {
        private static readonly FieldInfo ObjectsField = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
        private static readonly FieldInfo PeersField = AccessTools.Field(typeof(ZDOMan), "m_peers");
        private static readonly FieldInfo SentField = AccessTools.Field(typeof(ZDOMan), "m_zdosSentLastSec");
        private static readonly FieldInfo ReceivedField = AccessTools.Field(typeof(ZDOMan), "m_zdosRecvLastSec");
        private static readonly ObservatoryCounter Counter = new ObservatoryCounter();
        private static long _nextCaptureTimestamp;
        private static bool _verified;

        internal static ZdoObservatorySnapshot Current => Counter.Current;

        internal static void Verify()
        {
            bool peersMatch = PeersField != null && !PeersField.IsStatic &&
                              PeersField.FieldType.IsGenericType &&
                              PeersField.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                              PeersField.FieldType.GetGenericArguments().Length == 1 &&
                              PeersField.FieldType.GetGenericArguments()[0].DeclaringType == typeof(ZDOMan) &&
                              PeersField.FieldType.GetGenericArguments()[0].Name == "ZDOPeer";
            if (ObjectsField == null || ObjectsField.IsStatic ||
                ObjectsField.FieldType != typeof(Dictionary<ZDOID, ZDO>) ||
                !peersMatch || SentField == null || SentField.IsStatic ||
                SentField.FieldType != typeof(int) || ReceivedField == null ||
                ReceivedField.IsStatic || ReceivedField.FieldType != typeof(int))
                throw new MissingMemberException(
                    "ZDOMan observatory fields do not match the audited Valheim 1.0 contract.");
            _verified = true;
        }

        internal static void MarkCreated(ZDO created)
        {
            if (_verified && (WorldEngineConfig.Enabled?.Value ?? false) && created != null)
                Counter.MarkCreated();
        }

        internal static bool Exists(ZDOMan manager, ZDOID id)
        {
            if (!_verified || !(WorldEngineConfig.Enabled?.Value ?? false) || manager == null)
                return false;
            try
            {
                return ObjectsField.GetValue(manager) is Dictionary<ZDOID, ZDO> objects &&
                       objects.ContainsKey(id);
            }
            catch { return false; }
        }

        internal static void MarkDestroyed(bool existedBefore, ZDOMan manager, ZDOID id)
        {
            if (_verified && existedBefore && !Exists(manager, id)) Counter.MarkDestroyed();
        }

        internal static void Capture(ZDOMan manager)
        {
            if (!_verified || manager == null || !(WorldEngineConfig.Enabled?.Value ?? false)) return;
            try
            {
                long timestamp = Stopwatch.GetTimestamp();
                if (timestamp < _nextCaptureTimestamp) return;
                _nextCaptureTimestamp = timestamp > long.MaxValue - Stopwatch.Frequency
                    ? long.MaxValue
                    : timestamp + Stopwatch.Frequency;
                int total = ObjectsField.GetValue(manager) is Dictionary<ZDOID, ZDO> objects
                    ? objects.Count
                    : 0;
                int peers = PeersField.GetValue(manager) is ICollection collection
                    ? collection.Count
                    : 0;
                int sent = Convert.ToInt32(SentField.GetValue(manager));
                int received = Convert.ToInt32(ReceivedField.GetValue(manager));
                Counter.Capture(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    total,
                    peers,
                    sent,
                    received);
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("World observatory sample failed closed: " + exception.Message);
            }
        }

        internal static long BeginTimedOperation() =>
            _verified && (WorldEngineConfig.Enabled?.Value ?? false)
                ? Stopwatch.GetTimestamp()
                : 0L;
        internal static void EndSave(long started) => Counter.RecordSaveDuration(started);
        internal static void EndLoad(long started)
        {
            Counter.RecordLoadDuration(started);
            if (ZDOMan.instance != null) Capture(ZDOMan.instance);
        }

        internal static void Reset()
        {
            Counter.Reset();
            _nextCaptureTimestamp = 0L;
            _verified = false;
        }
    }
}
