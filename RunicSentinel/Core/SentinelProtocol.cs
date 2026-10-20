namespace RunicSentinel.Core
{
    internal static class SentinelVersion
    {
#if RUNIC_SENTINEL_SERVER_ONLY
        internal const string Current = "1.3.0";
#else
        internal const string Current = "1.6.1";
#endif
    }
}
