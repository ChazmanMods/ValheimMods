using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RunicCharacterVault.Shared;

namespace RunicCharacterVault
{
    internal sealed class CharacterVaultSettings
    {
        private bool serverInitialized;
        private readonly ConfigEntry<bool> allowMultipleCharacters;
        private readonly ConfigEntry<bool> allowExistingCharacters;
        private readonly ConfigEntry<string> startingItems;

        internal CharacterVaultSettings(ConfigFile config)
        {
            StartingItems = new List<StartingItem>();
            allowExistingCharacters = config.Bind(
                "Server", "AllowExistingCharacters", true,
                "Import a previously played character when no vault copy exists. The first upload is trusted; existing vault copies are never replaced by enrollment. Disable for fresh-character-only servers.");
            allowMultipleCharacters = config.Bind(
                "Server", "AllowMultipleCharacters", false,
                "Allow one platform account to enroll more than one character name.");
            startingItems = config.Bind(
                "Server", "StartingItems", string.Empty,
                "Optional comma-separated Valheim prefab:quantity pairs for newly enrolled characters.");
        }

        internal bool AllowMultipleCharacters { get; private set; }
        internal bool AllowExistingCharacters { get; private set; }
        internal IReadOnlyList<StartingItem> StartingItems { get; private set; }

        internal void InitializeServer()
        {
            if (serverInitialized || !ServerRole.IsServer) return;
            string[] arguments = Environment.GetCommandLineArgs();
            AllowExistingCharacters = allowExistingCharacters.Value;
            AllowMultipleCharacters = CharacterVaultArgumentPolicy.TryResolveAllowMultiple(
                arguments, out bool commandLineAllowMultiple)
                ? commandLineAllowMultiple
                : allowMultipleCharacters.Value;
            string configuredItems = CharacterVaultArgumentPolicy.TryResolveStartingItems(
                arguments, out string commandLineItems)
                ? commandLineItems
                : startingItems.Value;
            StartingItems = ParseItems(configuredItems);
            serverInitialized = true;
            CharacterVaultPlugin.Log.LogInfo(
                $"Server allowExistingCharacters={AllowExistingCharacters}, allowMultipleCharacters={AllowMultipleCharacters}, " +
                $"startingItemCount={StartingItems.Count}.");
        }

        private static IReadOnlyList<StartingItem> ParseItems(string value)
        {
            List<StartingItem> items = new List<StartingItem>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return items;
            }

            foreach (string entry in value.Split(','))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
                    !int.TryParse(parts[1].Trim(), out int quantity) || quantity <= 0)
                {
                    throw new InvalidOperationException($"Invalid CharacterVault starting item '{entry}'.");
                }

                items.Add(new StartingItem(parts[0].Trim(), quantity));
            }

            return items;
        }
    }

}
