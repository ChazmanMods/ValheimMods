using System;
using BepInEx.Logging;

namespace RunicCharacterVault.Shared
{
    public sealed class ModLog
    {
        private readonly ManualLogSource _logger;
        public ModLog(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void LogFatal(object message) => Write(LogLevel.Fatal, message);
        public void LogError(object message) => Write(LogLevel.Error, message);
        public void LogWarning(object message) => Write(LogLevel.Warning, message);
        public void LogMessage(object message) => Write(LogLevel.Message, message);
        public void LogInfo(object message) => Write(LogLevel.Info, message);
        public void LogDebug(object message) => Write(LogLevel.Debug, message);
        public void Log(LogLevel level, object message) => Write(level, message);

        private void Write(LogLevel level, object message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            _logger.Log(level, $"[{timestamp}] {message}");
        }
    }
}
