using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RunicCharacterVault.Shared
{
    public static class ConnectionFailureMessages
    {
        private const string StateKey = "RunicCharacterVault.Shared.ConnectionFailureMessages.v1";
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RunicCharacterVault.ConnectionFailureMessages");

        public static void Push(string source, string message)
        {
            Push(source, message, (string)null);
        }

        public static void Push(string source, string userMessage, string systemMessage)
        {
            Push(source, userMessage, systemMessage, null);
        }

        public static void Push(string source, string userMessage, Exception exception)
        {
            Push(source, userMessage, exception?.Message, exception);
        }

        public static void Push(string source, string userMessage, string systemMessage,
            Exception exception)
        {
            string displayMessage = DisplayMessage(
                userMessage, systemMessage ?? exception?.Message);
            if (string.IsNullOrWhiteSpace(source) || displayMessage == null) return;
            bool added = false;
            lock (AppDomain.CurrentDomain)
            {
                Stack<Tuple<string, string, string>> messages = GetMessages();
                var entry = Tuple.Create(source, displayMessage, systemMessage);
                if (!messages.Contains(entry))
                {
                    messages.Push(entry);
                    added = true;
                }
            }
            if (added)
                LogQueued(source, displayMessage, systemMessage, exception);
        }

        public static void Clear(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            lock (AppDomain.CurrentDomain)
            {
                Stack<Tuple<string, string, string>> messages = GetMessages();
                var retained = new Stack<Tuple<string, string, string>>();
                while (messages.Count > 0)
                {
                    Tuple<string, string, string> entry = messages.Pop();
                    if (entry.Item1 != source) retained.Push(entry);
                }
                while (retained.Count > 0) messages.Push(retained.Pop());
            }
        }

        internal static bool TryPopAll(out string message)
        {
            lock (AppDomain.CurrentDomain)
            {
                Stack<Tuple<string, string, string>> messages = GetMessages();
                if (messages.Count > 0)
                {
                    var pending = new List<string>();
                    while (messages.Count > 0) pending.Add(messages.Pop().Item2);
                    message = string.Join("\n", pending);
                    return true;
                }
            }
            message = null;
            return false;
        }

        private static Stack<Tuple<string, string, string>> GetMessages()
        {
            var messages = AppDomain.CurrentDomain.GetData(StateKey)
                as Stack<Tuple<string, string, string>>;
            if (messages != null) return messages;
            messages = new Stack<Tuple<string, string, string>>();
            AppDomain.CurrentDomain.SetData(StateKey, messages);
            return messages;
        }

        private static string DisplayMessage(string userMessage, string systemMessage)
        {
            if (!string.IsNullOrWhiteSpace(userMessage)) return userMessage;
            return string.IsNullOrWhiteSpace(systemMessage) ? null : systemMessage;
        }

        private static void LogQueued(string source, string userMessage, string systemMessage,
            Exception exception)
        {
            string diagnostic = exception?.ToString() ?? Environment.StackTrace;
            if (string.IsNullOrWhiteSpace(systemMessage) || systemMessage == userMessage)
            {
                Log.LogWarning(
                    $"Queued connection failure message from {source}: {userMessage}\n" +
                    $"Diagnostic stack:\n{diagnostic}");
                return;
            }
            Log.LogWarning(
                $"Queued connection failure message from {source}: " +
                $"userMessage={userMessage}; systemMessage={systemMessage}\n" +
                $"Diagnostic stack:\n{diagnostic}");
        }
    }

    [HarmonyPatch]
    internal static class ConnectionFailureMenuPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        private static void ShowConnectError(TMP_Text ___m_connectionFailedError)
        {
            if (ConnectionFailureMessages.TryPopAll(out string message))
                ___m_connectionFailedError.text = message;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static void Start(GameObject ___m_connectionFailedPanel,
            TMP_Text ___m_connectionFailedError)
        {
            ShowNext(___m_connectionFailedPanel, ___m_connectionFailedError);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnConnectionFailedOk))]
        private static void OnConnectionFailedOk(GameObject ___m_connectionFailedPanel,
            TMP_Text ___m_connectionFailedError)
        {
            ShowNext(___m_connectionFailedPanel, ___m_connectionFailedError);
        }

        private static void ShowNext(GameObject panel, TMP_Text text)
        {
            if (!ConnectionFailureMessages.TryPopAll(out string message)) return;
            text.text = message;
            panel.SetActive(true);
        }
    }
}
