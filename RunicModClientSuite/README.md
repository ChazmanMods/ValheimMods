# Runic Mod Client Suite

## Release 1.0.33 - September 20, 2026

- Updated RunicProduction to 1.0.12 to fix fermenter content storage on current Valheim.
- Updated RunicInteraction to 1.0.9: auto-close only affects player-built doors, excluding generated dungeon, cave, and ruin doors.
- Existing stranded fermenter batches may need recovery; the update does not automatically recover them.
- All other dependency versions and the client/server module split are retained.

Update participating clients and servers together and restart.

**The complete player-facing Runic experience, without server administration bundled into every
client.**

Runic Mod Client Suite is the one-click package for an ordinary player or remote client.
It installs all 19 player-facing Runic mods plus Configuration Manager. The lightweight Runic
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
| **Runic Signs** | Design signs live with selected-text styling, emojis, precise colors, text effects, curves, offsets, and scaled placement previews. |
| **Runic Velocity** | Measure local startup milestones and maintain a validated plugin cache. |
| **Runic World Engine** | Observe world/network health; authoritative hosts can also configure a validated player cap and smooth asynchronous saves. |

## Which suite belongs where?

| Profile | Install |
|---|---|
| Ordinary remote player | **Runic Mod Client Suite** |
| Solo player | **Runic Mod Client Suite** |
| Dedicated server | **Runic Mod Server Suite** |
| Listen host who also plays | **Runic Mod Suite** (full Sentinel with the F3 panel) |
| Remote Sentinel administrator | **Client Suite**, then add **Runic Sentinel** separately |

Install the split suites in their respective client and dedicated-server profiles. Keep their exact
versions aligned between the server and participating clients.

## Sentinel client/server boundary

Runic Sentinel Client only hashes the local BepInEx plugin inventory and answers a direct admission
challenge from a Sentinel server. The server reconstructs that report and evaluates it against its
own signed policy. The client package contains no private key, signer, policy editor, backup code,
server enforcement, operator console, reports, or F3 administrator panel.

An administrator using the F3 panel installs full **Runic Sentinel** separately. Use **1.4.2** with **Sentinel Server 1.1.2** for the current administrator connection fixes.
The **Server Cap** tab is available when the host also runs World Engine 1.2.0.
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

## Installation compatibility

Use **RunicModServerSuite** only on the dedicated server. Ordinary players use **RunicModClientSuite**; administrators add full **RunicSentinel 1.4.2** to that client profile for F3. Listen hosts use **RunicModSuite**.

**Never combine full Sentinel and SentinelServer in one profile.** When switching packs, remove the previous pack and its unwanted Sentinel variant; mod managers may retain old dependencies. Fully restart Valheim after changing plugins. SentinelClient may coexist with full Sentinel.

Signs must be installed at the same version on every player and the server. Disable BetterSigns because it replaces the same editor. The server pack includes Storage so chest learning covers server-owned Production automation.

RunicDeathPenalty remains planned for a future suite release and is not included.

## Release 1.0.25

Updated package versions:

- Chazman-RunicAgriculture-1.0.5
- Chazman-RunicCrafting-1.1.7
- Chazman-RunicInteraction-1.0.10
- Chazman-RunicInventory-1.1.10
- Chazman-RunicPortals-1.2.10
- Chazman-RunicProduction-1.0.15
- Chazman-RunicSigns-1.2.7
- Chazman-RunicStorage-1.3.7

Update client and server packages together for Portals, Production, Signs, and Storage. Inventory 1.1.6 and Storage 1.2.5 provide Quick Stack-only slot exclusions. Existing configuration is preserved.

## Release 1.0.27

Includes RunicStorage 1.3.0 and RunicSigns 1.1.0 with full-color emoji pickers, retained Quick Stack protection, and sign placement sizing. Includes RunicPrecisionBuildTool 2.0.6 with the F4 Local/World rotation toggle. These mod versions were confirmed working in-game by the author. Existing configuration and all other dependency versions are retained.

## Release 1.0.28

Includes the Valheim 1.0.14 compatibility fixes, retaining the current emoji and precision-building releases.

## Release 1.0.29

Includes RunicBuildCamera 1.0.4 with activation focus recovery and visible toggle feedback, confirmed working in-game by the author. All other dependency versions are unchanged.

## Release 1.0.30

Includes RunicWorldEngine 1.2.1, fixing the optional player-cap override on Valheim 1.0.14 while retaining existing patch-integrity checks.

## September 18, 2026 update

Compatible game updates are accepted through API checks in the updated modules; incompatible APIs still disable safely.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
