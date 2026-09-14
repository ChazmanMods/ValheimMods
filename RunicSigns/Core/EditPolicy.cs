using System;

namespace RunicSigns.Core;

internal static class EditPolicy
{
    internal const double LeaseSeconds = 12;
    internal static bool LeaseActive(string token, double expiry, double now) =>
        !string.IsNullOrEmpty(token) && !double.IsNaN(expiry) && !double.IsInfinity(expiry) &&
        expiry > now && expiry <= now + LeaseSeconds + 2;

    internal static string Reject(string expectedStyle, string actualStyle, string expectedText,
        string actualText, bool hasAccess, bool busy)
    {
        if (!hasAccess) return "Sign access is unavailable. Move closer and check ward permissions.";
        if (busy) return "Another save is in progress. Wait a moment and try again.";
        if (!string.Equals(expectedStyle, actualStyle, StringComparison.Ordinal) ||
            !string.Equals(expectedText, actualText, StringComparison.Ordinal))
            return "This sign changed while you were editing. Copy your caption, then reopen the editor.";
        return "";
    }
}
