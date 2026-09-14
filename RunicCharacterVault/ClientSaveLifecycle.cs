namespace RunicCharacterVault
{
    internal sealed class ClientSaveLifecycle
    {
        private bool _active;
        private bool _enrolling;
        private bool _spawned;

        internal bool IsActive => _active;
        internal bool CanUpload => _active && _spawned;
        internal bool IsEnrolling => _enrolling;
        internal bool HasSpawned => _spawned;

        internal void ActivateExisting()
        {
            _active = true;
        }

        internal void BeginEnrollment()
        {
            _active = true;
            _enrolling = true;
        }

        internal bool RecordSpawn(bool isLocalPlayer)
        {
            if (!isLocalPlayer)
            {
                return false;
            }

            _spawned = true;
            bool saveEnrollment = _enrolling;
            _enrolling = false;
            return saveEnrollment;
        }

        internal void Reset()
        {
            _active = false;
            _enrolling = false;
            _spawned = false;
        }
    }
}
