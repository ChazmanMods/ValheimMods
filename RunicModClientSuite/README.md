# Runic Mod Client Suite

**The complete player-facing Runic experience, without server administration bundled into every
client.**

Runic Mod Client Suite is the one-click package for a player, remote client, or listen-host player.
It installs all 18 player-facing Runic mods plus Configuration Manager. The lightweight Runic
Sentinel Client is included; the server administration package remains separate.

This is a dependency-only modpack. It contains no plugin DLLs and does not overwrite configuration
files. Thunderstore or r2modman downloads the exact packages listed in the manifest and preserves
the settings already present in the selected profile.

## Included mods

| Mod | What it adds |
|---|---|
| **Runic Agriculture** | Plant organized fields, harvest an area, and replant with bounded previews and validation. |
| **Runic Awareness** | Explain food, status effects, comfort, equipment, and local context without revealing hidden state. |
| **Runic Build Camera** | Build from a detached camera while retaining Valheim's normal placement and ownership rules. |
| **Runic Character Vault** | Exchanges validated character saves with a Runic Character Vault server and keeps a local safety backup before replacement. |
| **Runic Clock** | Show game time, world day, sun/moon status, and optional local time in a configurable client-only HUD. Toggle with Left Alt+C. |
| **Runic Crafting** | Craft and build using eligible nearby containers, with cached material previews and optional hotkey area repair for structures. |
| **Runic Display Stands** | Use item and armor stands as compact storage and quick loadout stations. |
| **Runic Exploration** | Search known pins and navigate only through already explored territory. |
| **Runic Interaction** | Add guarded interaction, transfer, equipment, menu, text, and pickup conveniences. |
| **Runic Inventory** | Add equipment and quick-use roles to Valheim's familiar inventory. |
| **Runic Portals** | Use named, permission-aware portal networks with vanilla fallback. |
| **Runic Precision Build Tool** | Add six-axis placement, matching, repeat transforms, repair, and conservative undo. |
| **Runic Production** | Link eligible chests and production stations for bounded automation. |
| **Runic Safety** | Add contextual confirmations, item protection, recovery safeguards, and verified backups. |
| **Runic Sentinel Client** | Answer a Sentinel server's bounded admission challenge without installing server administration or private-key tooling. |
| **Runic Storage** | Preview, quick-stack, restock, search, sort, store, and consolidate nearby storage. |
| **Runic Velocity** | Measure local startup milestones and maintain a validated plugin cache. |
| **Runic World Engine** | Observe world/network health; authoritative hosts can also configure a validated player cap and smooth asynchronous saves. |

## Which suite belongs where?

| Profile | Install |
|---|---|
| Ordinary remote player | **Runic Mod Client Suite** |
| Solo player | **Runic Mod Client Suite** |
| Dedicated server | **Runic Mod Server Suite** |
| Listen host who also plays | **Both suites** in the same profile |
| Remote Sentinel administrator | **Client Suite**, then add **Runic Sentinel** separately |

Shared packages appearing in both suites are resolved once by the mod manager. Keep their exact
versions aligned between the server and participating clients.

## Sentinel client/server boundary

Runic Sentinel Client only hashes the local BepInEx plugin inventory and answers a direct admission
challenge from a Sentinel server. The server reconstructs that report and evaluates it against its
own signed policy. The client package contains no private key, signer, policy editor, backup code,
server enforcement, operator console, reports, or F3 administrator panel.

An administrator using the F3 panel installs full **Runic Sentinel** separately. Version **1.4.0**
adds the **Server Cap** tab for a host running World Engine 1.2.0 and Sentinel Server 1.1.0.
Ordinary players do not need the administrator package or their own player-cap settings. Both packages can
coexist: their collision-safe direct transport permits one active v2 responder to own the endpoint
without either overwriting another handler. The server's private signing key must never be placed
in this pack or any client profile.

If an ordinary-player profile still contains full Sentinel after changing packs, remove or disable
the complete **Runic Mod Suite** dependency pack first so it does not reinstall combined Sentinel,
then uninstall full Sentinel unless that player is a remote administrator. Keep the server in
Optional mode during this coordinated migration; enable Required only after the new client reports
are observed and the client policy is signed.

## Configuration Manager

The client suite includes **shudnal's Configuration Manager 1.1.17** as an in-game convenience.
It does not make ordinary Runic configuration server-authoritative or automatically synchronize
settings that a Runic mod does not itself synchronize.

## Installation

1. Create or select a Valheim profile in Thunderstore Mod Manager or r2modman.
2. Install **Runic Mod Client Suite**.
3. Let the manager install its dependencies.
4. Launch with **Start modded**.

When joining a Runic Character Vault server, select your existing character. Version 1.0.2 allows
existing-character enrollment by default; the server administrator can disable it. Wait for the
first save confirmation before leaving. Do not install another server-character mod in the same profile.

Questions, bug reports, and test logs: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)

## Release 1.0.24

This release updates RunicStorage to 1.2.3: remembered chest contents, exact-item and group rules,
biome filters including Deep North, reusable custom groups, exclusions and routing priorities.
Chest labels support named colors, optional backgrounds and all five faces, with corrected lid
placement. Rules and Search block background movement and camera input while allowing typing and
UI scrolling. Vanilla chest stacking remains accessible beside the separate Rules control.
Existing Storage settings are preserved; update participating installations to 1.2.3.

This release includes RunicInventory 1.1.5 with Better Archery 1.9.99 compatibility,
inventory-resize safeguards, and the compact quiver layout. Better Archery is optional and
is not installed by this suite; its 2.0.0 build is not covered by the adapter.
RunicProduction 1.0.6 remains included. RunicDisplayStands 1.3.8 keeps your worn cape when the stand has none, equips the stand cape when you are wearing none, and swaps when both have one.
Restart the game after updating. Existing configuration files are preserved.
For multiplayer, keep RunicDisplayStands updated to 1.3.8 on participating clients and the server.
