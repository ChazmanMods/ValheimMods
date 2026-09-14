namespace RunicCharacterVault
{
    internal static class SaveStatusMessages
    {
        internal const string Saving = "Saving character...";
        internal const string Accepted = "Saving character......";
        internal const string Saved = "Character saved";
        internal const string Failed = "Failed";
    }

    internal sealed class SaveStatusLifecycle
    {
        private string _requestId = string.Empty;
        private bool _waitingForCommit;

        internal int Version { get; private set; }

        internal int Begin(string requestId, bool waitingForCommit)
        {
            Version++;
            _requestId = requestId;
            _waitingForCommit = waitingForCommit;
            return Version;
        }

        internal bool CanCommit(string requestId)
        {
            return _requestId == requestId && _waitingForCommit;
        }

        internal bool CanFail(string requestId, int version)
        {
            return Version == version && _requestId == requestId && _waitingForCommit;
        }

        internal bool IsCurrent(int version)
        {
            return Version == version;
        }

        internal void Clear()
        {
            Version++;
            _requestId = string.Empty;
            _waitingForCommit = false;
        }
    }
}
