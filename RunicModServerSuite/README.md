# Runic Mod Server Suite

## Release 1.0.24 - September 20, 2026

- Updated RunicProduction to 1.0.12 to fix fermenter content storage on current Valheim.
- Existing stranded fermenter batches may need recovery; the update does not automatically recover them.
- All other dependency versions and the client/server module split are retained.

Update participating clients and servers together and restart.

**The Runic services that do useful work on the authoritative Valheim process.**

Runic Mod Server Suite is the one-click package for a dedicated server. Listen hosts who also play should use the full Runic Mod Suite. It installs the ten Runic modules that provide host synchronization,
server-routed features, owner-local automation, safety/backup services, security administration,
startup diagnostics, or world-save smoothing. Its Sentinel dependency is the server-only package,
not the combined or client edition.

This is a dependency-only modpack. It contains no plugin DLLs, private keys, policies, or
configuration files. Thunderstore or r2modman downloads each declared package, and existing server
configuration remains in place.

## Included server modules

| Mod | Server role |
|---|---|
| **Runic Character Vault** | Stores each player's authoritative character, validates transfers, and maintains rolling backups. The same package must also be installed on every client. |
| **Runic Display Stands** | Publishes the host's configured eligible stand-prefab list to modded clients. |
| **Runic Portals** | Serves bounded portal-directory data and authorizes Group operations. |
| **Runic Production** | Automates eligible loaded stations when Valheim assigns the required objects to the server process. |
| **Runic Safety** | Supplies server-side safety seams and Sentinel's verified transition-backup dependency. |
| **Runic Sentinel Server** | Gates Required-mode handshakes, evaluates client reports against signed policy, and provides authenticated administration, server-cap configuration, bans, evidence, backups, reports, and dedicated-console commands. |
| **Runic Signs** | Design signs live with selected-text styling, emojis, precise colors, text effects, curves, offsets, and scaled placement previews. |
| **Runic Storage** | Synchronizes chest rules and labels and learns contents changed by server-owned automation. |
| **Runic Velocity** | Records local server-startup milestones and maintains a validated plugin cache. |
| **Runic World Engine** | Configures a validated host player cap, monitors peers, latency, traffic, ownership transfers and backlog warnings, and smooths asynchronous world saves. |

Configuration Manager is omitted because a headless server has no in-game configuration window.
Edit the generated BepInEx configuration files on the server instead.

## Deployment matrix

| Profile | Install |
|---|---|
| Dedicated server | **Runic Mod Server Suite** |
| Ordinary remote player | **Runic Mod Client Suite** |
| Listen host who also plays | **Runic Mod Suite** (full Sentinel with the F3 panel) |
| Remote Sentinel administrator | **Client Suite**, then add combined **Runic Sentinel** separately |

The client and server suites intentionally overlap where a feature has work on both processes. A
remote player does not need to install the Server Suite as a second pack: the Client Suite already
contains every shared gameplay module and deliberately omits Sentinel Server and combined Sentinel.

## Sentinel boundary

Sentinel's authority, managed private key, dedicated commands, and policy state belong on the
server. **Runic Sentinel Server** contains that authoritative functionality without the ordinary
player package. Players receive the lightweight **Runic Sentinel Client** through the Client Suite;
it can report a bounded plugin inventory but cannot administer or enforce anything. Remote F3
administrators install combined Sentinel separately. Never distribute the server-private key.

Add your account ID to the server's `adminlist.txt`, then join with full Sentinel, press **F3**,
and click **Set Up Sentinel** once. On Valheim 1.0, use `V_<SteamID64>` for a Steam account;
Sentinel also recognizes raw and `Steam_` IDs. Existing policies and signing keys are preserved.
First-time setup leaves admission mode unchanged. Server-admin inheritance can be disabled with
`[Administrator Access] UseServerAdminList = false`; separately signed roles remain independent.

## Multiplayer consistency

### Player cap

The player-cap override is optional and disabled by default. An authorized administrator with
**Runic Sentinel 1.4.2** on their client can open **F3 > Server Cap**, enable the override, set the
desired limit, and save it for the next server restart. Players do not need to change their local
cap settings. Updating this suite preserves the server's existing configuration.

### Shared gameplay mods

- Keep Display Stands, Portals, Production, and Safety versions/configuration compatible with the
  clients using those features.
- Production follows Valheim's native object ownership. Installing it on both server and clients
  lets the current eligible owner perform bounded automation.
- Client-only tools remain absent from the server pack because they add no headless authority or
  enforcement.

## Installation

1. Create or select the dedicated-server profile in Thunderstore Mod Manager or r2modman.
2. Install **Runic Mod Server Suite**.
3. Let the manager install its dependencies.
4. Configure the individual server mods before starting the world.
5. Keep Sentinel's private material only on the authoritative server.

Runic Character Vault stores its server data under
`BepInEx/config/RunicCharacterVault/characters`. Back up that directory with the world, require
players to use the Client Suite, and do not install another server-character mod alongside it.
Version 1.0.2 defaults to `AllowExistingCharacters = true`, allowing established players to enroll
with their current characters. First enrollment trusts that account's supplied character; switch
the setting to false after migration if future players must start fresh. Existing vault profiles
remain authoritative. The first enrolled save is retained in `characters/enrollment-backups`.

Questions, bug reports, and test logs: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)

## Installation compatibility

Use **RunicModServerSuite** only on the dedicated server. Ordinary players use **RunicModClientSuite**; administrators add full **RunicSentinel 1.4.2** to that client profile for F3. Listen hosts use **RunicModSuite**.

**Never combine full Sentinel and SentinelServer in one profile.** When switching packs, remove the previous pack and its unwanted Sentinel variant; mod managers may retain old dependencies. Fully restart Valheim after changing plugins. SentinelClient may coexist with full Sentinel.

Signs must be installed at the same version on every player and the server. Disable BetterSigns because it replaces the same editor. The server pack includes Storage so chest learning covers server-owned Production automation.

RunicDeathPenalty remains planned for a future suite release and is not included.

## Release 1.0.17

Updated package versions:

- Chazman-RunicDisplayStands-1.3.9
- Chazman-RunicPortals-1.2.10
- Chazman-RunicProduction-1.0.15
- Chazman-RunicSentinelServer-1.2.0
- Chazman-RunicSigns-1.2.7
- Chazman-RunicStorage-1.3.7

Update client and server packages together for Portals, Production, Signs, and Storage. Player clients use Inventory 1.1.6 with Storage 1.2.5 for Quick Stack-only slot exclusions; Inventory is not included in the server pack. Existing configuration is preserved.

## Release 1.0.19

Includes RunicStorage 1.3.0 and RunicSigns 1.1.0 with full-color emoji pickers, retained Quick Stack protection, and sign placement sizing. These mod versions were confirmed working in-game by the author. Existing configuration and all other dependency versions are retained.

## Release 1.0.20

Includes the Valheim 1.0.14 compatibility fixes, retaining the current emoji and precision-building releases.

## Release 1.0.21

Includes RunicWorldEngine 1.2.1, fixing the optional player-cap override on Valheim 1.0.14 while retaining existing patch-integrity checks.

## September 18, 2026 update

Compatible game updates are accepted through API checks in the updated modules; incompatible APIs still disable safely.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
