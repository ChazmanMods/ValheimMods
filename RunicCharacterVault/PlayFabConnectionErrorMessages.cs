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
                return global::Runic.Localization.RunicText.Get("text_af7dad45d13b");
            }
            if (error?.Error == PlayFabErrorCode.LobbyNotJoinable)
            {
                return global::Runic.Localization.RunicText.Format("text_bd2c40124b83", code);
            }
            return global::Runic.Localization.RunicText.Format("text_c174578e11cc", code);
        }

        internal static string ForMatchmaking(ZPLayFabMatchmakingFailReason reason)
        {
            switch (reason)
            {
                case ZPLayFabMatchmakingFailReason.InvalidServerData:
                    return global::Runic.Localization.RunicText.Get("text_8fce283a2663");
                case ZPLayFabMatchmakingFailReason.ServerFull:
                    return global::Runic.Localization.RunicText.Get("text_4d0dafcf90be");
                case ZPLayFabMatchmakingFailReason.NotLoggedIn:
                    return global::Runic.Localization.RunicText.Get("text_69013a159f3b");
                case ZPLayFabMatchmakingFailReason.APIRequestLimitExceeded:
                    return global::Runic.Localization.RunicText.Get("text_af7dad45d13b");
                case ZPLayFabMatchmakingFailReason.EndPointNotOnInternet:
                    return global::Runic.Localization.RunicText.Get("text_d657442d1a93");
                case ZPLayFabMatchmakingFailReason.InvalidParameter:
                    return global::Runic.Localization.RunicText.Get("text_aae954706453");
                default:
                    return global::Runic.Localization.RunicText.Format("text_c174578e11cc", reason);
            }
        }

        internal static string ForParty(int code)
        {
            switch (code)
            {
                case 11:
                    return global::Runic.Localization.RunicText.Get("text_179e37eda9e9");
                case 4098:
                    return global::Runic.Localization.RunicText.Get("text_519ef9b193e3");
                default:
                    return global::Runic.Localization.RunicText.Format("text_c174578e11cc", code);
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
            return error == null || error.HttpCode <= 0 ? global::Runic.Localization.RunicText.Get("text_b764cdc0eab7") : error.HttpCode.ToString();
        }
    }
}
