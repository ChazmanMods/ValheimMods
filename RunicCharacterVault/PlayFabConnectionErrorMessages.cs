using PlayFab;

namespace RunicCharacterVault
{
    internal static class PlayFabConnectionErrorMessages
    {
        internal static string ForApi(PlayFabError error)
        {
            string code = error?.Error.ToString() ?? HttpCode(error);
            if (IsRateLimited(error))
            {
                return "Too many attempts. Please wait and try again. (Code 429)";
            }
            if (error?.Error == PlayFabErrorCode.LobbyNotJoinable)
            {
                return $"The server is not accepting connections. (Code {code})";
            }
            return $"A PlayFab connection error occurred. Please try again. (Code {code})";
        }

        internal static string ForMatchmaking(ZPLayFabMatchmakingFailReason reason)
        {
            switch (reason)
            {
                case ZPLayFabMatchmakingFailReason.InvalidServerData:
                    return "The server information is invalid. (Code InvalidServerData)";
                case ZPLayFabMatchmakingFailReason.ServerFull:
                    return "The server is full. (Code ServerFull)";
                case ZPLayFabMatchmakingFailReason.NotLoggedIn:
                    return "PlayFab sign-in failed. Please try again. (Code NotLoggedIn)";
                case ZPLayFabMatchmakingFailReason.APIRequestLimitExceeded:
                    return "Too many attempts. Please wait and try again. (Code 429)";
                case ZPLayFabMatchmakingFailReason.EndPointNotOnInternet:
                    return "The server is not reachable. (Code EndPointNotOnInternet)";
                case ZPLayFabMatchmakingFailReason.InvalidParameter:
                    return "The connection request is invalid. (Code InvalidParameter)";
                default:
                    return $"A PlayFab connection error occurred. Please try again. (Code {reason})";
            }
        }

        internal static string ForParty(int code)
        {
            switch (code)
            {
                case 11:
                    return "PlayFab is not ready. Please try again. (Code 11)";
                case 4098:
                    return "The PlayFab connection expired. Please try again. (Code 4098)";
                default:
                    return $"A PlayFab connection error occurred. Please try again. (Code {code})";
            }
        }

        private static bool IsRateLimited(PlayFabError error)
        {
            return error?.HttpCode == 429 ||
                error?.Error == PlayFabErrorCode.APIRequestLimitExceeded ||
                error?.Error == PlayFabErrorCode.APIClientRequestRateLimitExceeded ||
                error?.Error == PlayFabErrorCode.LobbyPlayerMaxLobbyLimitExceeded;
        }

        private static string HttpCode(PlayFabError error)
        {
            return error == null || error.HttpCode <= 0 ? "Unknown" : error.HttpCode.ToString();
        }
    }
}
