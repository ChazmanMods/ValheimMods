using System;

namespace RunicCharacterVault
{
    internal static class CharacterVaultArgumentPolicy
    {
        private const string MultipleArgument =
            "--charactervault-allow-multiple-characters";
        private const string ItemsArgument = "--charactervault-starting-items";

        internal static bool TryResolveAllowMultiple(string[] arguments, out bool result)
        {
            result = false;
            if (!TryReadValue(arguments, MultipleArgument, out string value)) return false;
            if (!bool.TryParse(value, out bool parsed))
            {
                throw new InvalidOperationException(
                    $"Command-line switch {MultipleArgument} requires true or false.");
            }
            result = parsed;
            return true;
        }

        internal static bool TryResolveStartingItems(string[] arguments, out string result)
        {
            return TryReadValue(arguments, ItemsArgument, out result);
        }

        private static bool TryReadValue(
            string[] arguments, string name, out string value)
        {
            value = "";
            bool found = false;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (found || index + 1 >= arguments.Length)
                {
                    throw new InvalidOperationException(
                        $"Command-line switch {name} is missing or duplicated.");
                }
                value = arguments[++index];
                found = true;
            }
            return found;
        }
    }
}
