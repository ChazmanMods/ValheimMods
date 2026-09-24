# Runic Character Vault

Runic Character Vault keeps the authoritative copy of each player's character on your dedicated server. When a player joins, the server securely sends that character to the client; while playing and when leaving, validated saves return to the vault.

## Install

Install this package on the **dedicated server and every connecting client**. Do not combine it with ServerCharacters, Landoria CharacterVault, or another server-character mod.

Select your existing character when joining. With `AllowExistingCharacters = true` (the default), a character without a vault copy enrolls using its current inventory, skills, appearance, and progress. No starter items are granted to imported characters. The first save is uploaded after spawning, validated, and stored without rewriting its bytes. Wait for the save confirmation before leaving.

Once the vault has a copy, later joins use that authoritative copy. Enrollment cannot replace it. By default, only one character name is allowed per platform account. Set `AllowExistingCharacters = false` if your server intentionally requires fresh characters.

Server data is stored under:

`BepInEx/config/RunicCharacterVault/characters`

Automatic server backups are stored in the `backups` folder. The first enrolled save is retained separately in `enrollment-backups` and is not removed by rolling retention. A verified local safety copy is made before sending an existing character's enrollment request or replacing the selected profile. A failed local backup stops that operation.

## Configuration

Edit `BepInEx/config/chazman.RunicCharacterVault.cfg` on the server, then restart it.

- `AllowExistingCharacters`: allow first enrollment of previously played characters. Default: `true`. Disable after migration if subsequent new players must start fresh. This never overrides an existing vault character.
- `AllowMultipleCharacters`: permit more than one character name per platform account. Default: `false`.
- `StartingItems`: optional comma-separated prefab and quantity pairs, such as `Hammer:1,Torch:1`, granted only to genuinely new characters.

The legacy command-line switches `--charactervault-allow-multiple-characters` and `--charactervault-starting-items` remain supported and override the configuration file.

Runic Character Vault supports normal saves, world checkpoints, logout, voluntary disconnect, kicks, and graceful dedicated-server shutdown handshakes. The server rejects malformed, oversized, mismatched, or unverified character transfers.

## First-release safety note

Keep normal world and BepInEx backups. Never manually edit vault files while the server is running.

## Attribution

This mod is derived from the MIT-licensed Landoria CharacterVault project. See `NOTICE.md` and `LICENSE`.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicCharacterVault` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Administrator characters

Accounts in the server's `adminlist.txt` can enroll more than one character even when `AllowMultipleCharacters` is false. Normal players retain the configured limit. This exception does not bypass character validation, bans, or existing-character import policy.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
