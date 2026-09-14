# Runic Mod Server Suite

**The Runic services that do useful work on the authoritative Valheim process.**

Runic Mod Server Suite is the one-click package for a dedicated server or the authoritative side of
a listen server. It installs the eight Runic modules that provide host synchronization,
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
| **Runic Velocity** | Records local server-startup milestones and maintains a validated plugin cache. |
| **Runic World Engine** | Configures a validated host player cap, monitors peers, latency, traffic, ownership transfers and backlog warnings, and smooths asynchronous world saves. |

Configuration Manager is omitted because a headless server has no in-game configuration window.
Edit the generated BepInEx configuration files on the server instead.

## Deployment matrix

| Profile | Install |
|---|---|
| Dedicated server | **Runic Mod Server Suite** |
| Ordinary remote player | **Runic Mod Client Suite** |
| Listen host who also plays | **Both suites** in the same profile |
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
**Runic Sentinel 1.4.0** on their client can open **F3 > Server Cap**, enable the override, set the
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

## Release 1.0.16

This release includes RunicProduction 1.0.6 for reliable production-link clicks and
RunicDisplayStands 1.3.7 for visible, named armor slots and protected-row equipment swaps.
Update the corresponding client and server suites together, then restart the game and server.
Existing configuration files are preserved.
