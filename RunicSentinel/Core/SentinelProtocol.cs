namespace RunicSentinel.Core
{
    internal static class SentinelVersion
    {
#if RUNIC_SENTINEL_SERVER_ONLY
        internal const string Current = "1.2.0";
#else
        internal const string Current = "1.5.0";
#endif
    }
}
