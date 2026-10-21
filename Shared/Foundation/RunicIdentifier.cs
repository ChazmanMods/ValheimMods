using System;

namespace Runic.Foundation.Core
{
    /// <summary>
    /// Validates stable Runic identifiers. Identifiers are lowercase dot-separated tokens and
    /// may contain ASCII letters, digits, and internal hyphens.
    /// </summary>
    public static class RunicIdentifier
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128)
                return false;

            bool segmentHasCharacter = false;
            bool previousWasHyphen = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool alphaNumeric =
                    character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9';

                if (alphaNumeric)
                {
                    segmentHasCharacter = true;
                    previousWasHyphen = false;
                    continue;
                }

                if (character == '-')
                {
                    if (!segmentHasCharacter || previousWasHyphen)
                        return false;
                    previousWasHyphen = true;
                    continue;
                }

                if (character == '.')
                {
                    if (!segmentHasCharacter || previousWasHyphen)
                        return false;
                    segmentHasCharacter = false;
                    previousWasHyphen = false;
                    continue;
                }

                return false;
            }

            return segmentHasCharacter && !previousWasHyphen;
        }

        /// <summary>Returns a valid identifier or throws with the canonical validation message.</summary>
        public static string Require(string value, string parameterName)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException(
                    "Runic identifiers must be lowercase dot-separated tokens containing only " +
                    "ASCII letters, digits, and internal hyphens.",
                    parameterName);
            }

            return value;
        }
    }
}
