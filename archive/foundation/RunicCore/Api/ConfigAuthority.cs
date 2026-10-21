namespace Runic.Foundation.Core
{
    /// <summary>Declares which peer owns the effective value of a configuration setting.</summary>
    public enum ConfigAuthority
    {
        /// <summary>A preference stored and applied only by the local client.</summary>
        ClientLocal = 0,

        /// <summary>A rule supplied and enforced by the active server or local host.</summary>
        ServerAuthoritative = 1,

        /// <summary>A rule persisted with and enforced for the current world.</summary>
        WorldAuthoritative = 2
    }
}
