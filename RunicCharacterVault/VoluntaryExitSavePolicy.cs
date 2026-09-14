namespace RunicCharacterVault
{
    internal interface IVoluntaryExitSaveRequest
    {
        bool Request();
    }

    internal enum VoluntaryExitSaveAction
    {
        PassThrough,
        WaitForPendingSave,
        WaitForNewSave
    }

    internal static class VoluntaryExitSavePolicy
    {
        internal static VoluntaryExitSaveAction Start(bool playerEnteredWorld,
            bool savePending, IVoluntaryExitSaveRequest request)
        {
            if (!playerEnteredWorld)
            {
                return VoluntaryExitSaveAction.PassThrough;
            }
            if (savePending)
            {
                return VoluntaryExitSaveAction.WaitForPendingSave;
            }
            return request.Request() ? VoluntaryExitSaveAction.WaitForNewSave
                : VoluntaryExitSaveAction.PassThrough;
        }
    }
}
