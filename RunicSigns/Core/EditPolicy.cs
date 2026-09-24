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
        if (!hasAccess) return global::Runic.Localization.RunicText.Get("text_2915cba07231");
        if (busy) return global::Runic.Localization.RunicText.Get("text_73bb969fc60c");
        if (!string.Equals(expectedStyle, actualStyle, StringComparison.Ordinal) ||
            !string.Equals(expectedText, actualText, StringComparison.Ordinal))
            return global::Runic.Localization.RunicText.Get("text_998a87d025ab");
        return "";
    }
}
