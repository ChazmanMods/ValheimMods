namespace RunicCharacterVault
{
    internal interface IKickSaveRequest
    {
        KickSaveRequestResult Request();
    }

    internal sealed class KickSaveRequestResult
    {
        internal KickSaveRequestResult(bool started, string requestId = "")
        {
            Started = started;
            RequestId = requestId;
        }

        internal string RequestId { get; }
        internal bool Started { get; }
    }

    internal enum KickSaveEligibility
    {
        Unmanaged,
        Rejected,
        SaveRequired
    }

    internal enum KickAction
    {
        Allow,
        AllowWithoutSave,
        WaitForPendingSave,
        RequestSave
    }

    internal static class KickSavePolicy
    {
        internal static KickAction Decide(bool validServerPeer, bool saveAuthorized,
            bool savePending, KickSaveEligibility eligibility)
        {
            if (!validServerPeer || saveAuthorized)
            {
                return KickAction.Allow;
            }
            if (eligibility == KickSaveEligibility.Rejected)
            {
                return KickAction.AllowWithoutSave;
            }
            if (savePending)
            {
                return KickAction.WaitForPendingSave;
            }
            return eligibility == KickSaveEligibility.SaveRequired
                ? KickAction.RequestSave : KickAction.AllowWithoutSave;
        }
    }

    internal static class KickSaveRequestExecutor
    {
        internal static KickSaveRequestResult Execute(KickAction action, IKickSaveRequest request)
        {
            return action == KickAction.RequestSave
                ? request.Request() : new KickSaveRequestResult(false);
        }
    }
}
