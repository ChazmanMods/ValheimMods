using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PlayFab.Party;

namespace RunicCharacterVault
{
    internal static class PlayFabAckDiagnostics
    {
        private const int HistoryLimit = 32;
        private static readonly ConditionalWeakTable<ZPlayFabSocket.InFlightQueue, Queue<string>> Histories =
            new ConditionalWeakTable<ZPlayFabSocket.InFlightQueue, Queue<string>>();

        internal static void RecordEnqueue(ZPlayFabSocket.InFlightQueue queue, byte[] payload)
        {
            Record(queue, $"enqueue {DescribePayload(payload)}; {DescribeQueue(queue)}");
        }

        internal static void RecordDrop(ZPlayFabSocket.InFlightQueue queue, byte[] payload)
        {
            Record(queue, $"drop {DescribePayload(payload)}; {DescribeQueue(queue)}");
        }

        internal static void RecordReset(ZPlayFabSocket.InFlightQueue queue)
        {
            Record(queue, $"reset; {DescribeQueue(queue)}");
        }

        internal static void AckReceived(ZPlayFabSocket socket, uint messageId,
            ZPlayFabSocket.InFlightQueue queue, bool isClient)
        {
            uint outstanding = queue.Head - queue.Tail;
            uint acknowledged = messageId - queue.Tail;
            if (acknowledged <= outstanding) return;

            CharacterVaultPlugin.Log.LogError(
                $"Invalid PlayFab ACK received: ack={messageId}, acknowledged={acknowledged}, " +
                $"outstanding={outstanding}, {DescribeQueue(queue)}, " +
                $"socket={DescribeSocket(socket, isClient)}. " +
                $"Queue history:\n{GetHistory(queue)}\nCall stack:\n{Environment.StackTrace}");
        }

        internal static void RecordIncomingBuffer(ZPlayFabSocket socket, byte[] buffer,
            bool isClient, bool useCompression, string stage)
        {
            if (buffer == null || buffer.Length <= 5 || buffer[buffer.Length - 1] != 42) return;
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab ACK-shaped buffer at {stage}: {DescribePayload(buffer)}, " +
                $"compression={useCompression}, socket={DescribeSocket(socket, isClient)}, " +
                $"first={DescribeBytes(buffer, 0)}, last={DescribeBytes(buffer, Math.Max(0, buffer.Length - 32))}.");
        }

        internal static bool IsEarlyCompressedBuffer(byte[] buffer)
        {
            if (buffer == null || buffer.Length <= 5) return false;
            int header = buffer[0] << 8 | buffer[1];
            return (buffer[0] & 15) == 8 && header % 31 == 0;
        }

        internal static void EarlyCompressedBufferQueued(ZPlayFabSocket socket, byte[] buffer,
            bool isClient)
        {
            CharacterVaultPlugin.Log.LogWarning(
                $"PlayFab compressed buffer arrived before VersionMatch; queued for selective " +
                $"decompression: bytes={buffer.Length}, socket={DescribeSocket(socket, isClient)}, " +
                $"first={DescribeBytes(buffer, 0)}, last={DescribeBytes(buffer, Math.Max(0, buffer.Length - 32))}.");
        }

        internal static void AckProcessed(ZPlayFabSocket socket, uint messageId,
            ZPlayFabSocket.InFlightQueue queue, bool isClient)
        {
            if (socket.IsConnected()) return;
            CharacterVaultPlugin.Log.LogError(
                $"PlayFab ACK closed the socket: ack={messageId}, {DescribeQueue(queue)}, " +
                $"socket={DescribeSocket(socket, isClient)}. Queue history:\n{GetHistory(queue)}");
        }

        private static void Record(ZPlayFabSocket.InFlightQueue queue, string entry)
        {
            Queue<string> history = Histories.GetOrCreateValue(queue);
            history.Enqueue($"{DateTime.UtcNow:O} {entry}");
            while (history.Count > HistoryLimit) history.Dequeue();
        }

        private static string GetHistory(ZPlayFabSocket.InFlightQueue queue)
        {
            return Histories.TryGetValue(queue, out Queue<string> history) ?
                string.Join("\n", history.ToArray()) : "<empty>";
        }

        private static string DescribeQueue(ZPlayFabSocket.InFlightQueue queue)
        {
            return $"queue={RuntimeHelpers.GetHashCode(queue):X8}, head={queue.Head}, " +
                $"tail={queue.Tail}, bytes={queue.Bytes}, empty={queue.IsEmpty}";
        }

        private static string DescribeSocket(ZPlayFabSocket socket, bool isClient)
        {
            return $"object={RuntimeHelpers.GetHashCode(socket):X8}, " +
                $"side={(isClient ? "client" : "server")}, connected={socket.IsConnected()}, " +
                $"remote={PlayFabConnectionDiagnostics.Fingerprint(socket.m_remotePlayerId)}";
        }

        private static string DescribePayload(byte[] payload)
        {
            if (payload == null || payload.Length < 5) return $"bytes={payload?.Length ?? 0}";
            int offset = payload.Length - 5;
            uint id = (uint)(payload[offset] | payload[offset + 1] << 8 |
                payload[offset + 2] << 16 | payload[offset + 3] << 24);
            return $"id={id}, type={payload[payload.Length - 1]}, bytes={payload.Length}";
        }

        private static string DescribeBytes(byte[] payload, int offset)
        {
            int count = Math.Min(32, payload.Length - offset);
            return count > 0 ? BitConverter.ToString(payload, offset, count) : "<empty>";
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "OnDataMessageReceived")]
    internal static class CharacterVaultPlayFabRawReceiveDiagnosticsPatch
    {
        private static bool Prefix(ZPlayFabSocket __instance, PlayFabPlayer from,
            byte[] compressedBuffer, bool ___m_isClient, bool ___m_useCompression,
            PlayFabZLibWorkQueue ___m_zlibWorkQueue)
        {
            PlayFabAckDiagnostics.RecordIncomingBuffer(__instance, compressedBuffer,
                ___m_isClient, ___m_useCompression, "raw receive");
            if (___m_useCompression || from?.EntityKey?.Id != __instance.m_remotePlayerId ||
                !PlayFabAckDiagnostics.IsEarlyCompressedBuffer(compressedBuffer)) return true;

            // PlayFab can deliver compressed data before the preceding VersionMatch packet.
            // Decode only this buffer so it is not mistaken for an ACK, closing the client
            // socket and causing the server's subsequent 4098 "invalid handle" error.
            PlayFabAckDiagnostics.EarlyCompressedBufferQueued(
                __instance, compressedBuffer, ___m_isClient);
            ___m_zlibWorkQueue.Decompress(compressedBuffer);
            return false;
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "OnDataMessageReceivedCont")]
    internal static class CharacterVaultPlayFabDecodedReceiveDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket __instance, byte[] buffer,
            bool ___m_isClient, bool ___m_useCompression)
        {
            PlayFabAckDiagnostics.RecordIncomingBuffer(__instance, buffer,
                ___m_isClient, ___m_useCompression, "decoded receive");
        }
    }

    [HarmonyPatch(typeof(ZPlayFabSocket.InFlightQueue), nameof(ZPlayFabSocket.InFlightQueue.Enqueue))]
    internal static class CharacterVaultPlayFabQueueEnqueueDiagnosticsPatch
    {
        private static void Postfix(ZPlayFabSocket.InFlightQueue __instance, byte[] payload) =>
            PlayFabAckDiagnostics.RecordEnqueue(__instance, payload);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket.InFlightQueue), nameof(ZPlayFabSocket.InFlightQueue.Drop))]
    internal static class CharacterVaultPlayFabQueueDropDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket.InFlightQueue __instance, out byte[] __state)
        {
            __state = __instance.IsEmpty ? null : __instance.Peek();
        }

        private static void Postfix(ZPlayFabSocket.InFlightQueue __instance, byte[] __state) =>
            PlayFabAckDiagnostics.RecordDrop(__instance, __state);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket.InFlightQueue), nameof(ZPlayFabSocket.InFlightQueue.ResetAll))]
    internal static class CharacterVaultPlayFabQueueResetDiagnosticsPatch
    {
        private static void Postfix(ZPlayFabSocket.InFlightQueue __instance) =>
            PlayFabAckDiagnostics.RecordReset(__instance);
    }

    [HarmonyPatch(typeof(ZPlayFabSocket), "ProcessAck")]
    internal static class CharacterVaultPlayFabProcessAckDiagnosticsPatch
    {
        private static void Prefix(ZPlayFabSocket __instance, uint msgId,
            ZPlayFabSocket.InFlightQueue ___m_inFlightQueue, bool ___m_isClient)
        {
            PlayFabAckDiagnostics.AckReceived(
                __instance, msgId, ___m_inFlightQueue, ___m_isClient);
        }

        private static void Postfix(ZPlayFabSocket __instance, uint msgId,
            ZPlayFabSocket.InFlightQueue ___m_inFlightQueue, bool ___m_isClient)
        {
            PlayFabAckDiagnostics.AckProcessed(
                __instance, msgId, ___m_inFlightQueue, ___m_isClient);
        }
    }

}
