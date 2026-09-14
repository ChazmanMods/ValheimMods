namespace RunicSentinel.Core
{
    internal static class SentinelVersion
    {
#if RUNIC_SENTINEL_SERVER_ONLY
        internal const string Current = "1.1.2";
#else
        internal const string Current = "1.4.2";
#endif
    }
}
