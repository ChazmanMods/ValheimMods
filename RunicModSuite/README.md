# Runic Mod Suite

## Release 1.2.43 - September 20, 2026

- Updated RunicProduction to 1.0.12 to fix fermenter content storage on current Valheim.
- Updated RunicInteraction to 1.0.9: auto-close only affects player-built doors, excluding generated dungeon, cave, and ruin doors.
- Existing stranded fermenter batches may need recovery; the update does not automatically recover them.
- All other dependency versions and the client/server module split are retained.

Update participating clients and servers together and restart.

**Build smarter. Store faster. Automate the chores. Keep Valheim feeling like Valheim.**

Runic Mod Suite is a collection of 19 independently installable Valheim mods focused on building,
storage, crafting, farming, production, exploration, server administration, and quality-of-life
improvements. Install the entire suite with one click, or use only the Runic mods you want.

The philosophy behind Runic is simple: remove unnecessary friction without removing the game.
Your workbenches still matter. Materials still cost materials. Crops still have to be planted.
Production stations still produce items. Wards and permissions still matter. Runic gives you better
tools for doing those things.

This is a dependency-only modpack. It contains no duplicate plugin DLLs and does not overwrite
player or server configuration files. Thunderstore or r2modman downloads each listed Runic package,
Configuration Manager, and their required dependencies.

This is the complete, combined edition. For a lean multiplayer deployment, use **Runic Mod Client
Suite** on player profiles and **Runic Mod Server Suite** on the authoritative server instead.

## Included mods

| Mod | What it adds |
|---|---|
| **Runic Agriculture** | Plant organized crop fields with configurable patterns and live spacing previews. Harvest an area, then quickly replant without changing Valheim's crop rules. |
| **Runic Awareness** | Understand food, status effects, comfort, equipment, and your current situation using information your character already knows. |
| **Runic Build Camera** | Detach the camera while building so you can place, repair, or remove pieces from the angle you need, with optional nearby pickup and Wisplight support. |
| **Runic Character Vault** | Keep authoritative player characters on your server with validated transfers, protected saves, and rolling backups. Install it on both server and clients. |
| **Runic Clock** | Show game time, world day, sun/moon status, and optional local time in a configurable client-only HUD. Toggle with Left Alt+C. |
| **Runic Crafting** | Craft and build using materials in nearby containers, with clearer requirements, configurable workshop access, stationless build rules, Repair All, and optional hotkey area repair for structures. |
| **Runic Display Stands** | Store items directly in item and armor stands and use armor stands as quick loadout stations. |
| **Runic Exploration** | Search and filter known map pins and navigate through explored territory without revealing anything you have not discovered. |
| **Runic Interaction** | Smooth out common annoyances with hold-to-repeat actions, safer transfers and doors, equipment restore, menu memory, text improvements, and pickup filters. |
| **Runic Inventory** | Add dedicated equipment and quick-access slots to Valheim's familiar inventory, with automatic handling, locks, protected items, safe sorting, and pickup controls. |
| **Runic Portals** | Turn portal pairs into named, permission-aware networks while ordinary unconfigured portals continue to work normally. |
| **Runic Precision Build Tool** | Get full six-axis placement control, fine adjustments, transform matching, undo, area repair, and advanced building tools. |
| **Runic Production** | Link chests to existing stations for automatic ingredients, fuel, output collection, stock replenishment, and complete production workflows. |
| **Runic Safety** | Protect valuable inventory and world state with contextual confirmations, protected-item rules, recovery safeguards, verified backups, and compatibility checks. |
| **Runic Sentinel** | Control a modded server with approved mod profiles, authenticated administrators, bans, admission enforcement, backups, reports, and a secure F3 panel with server-cap administration. |
| **Runic Storage** | Preview chest contents, quick-stack nearby storage, store all, restock, search, sort, and consolidate partial stacks. |
| **Runic Signs** | Design signs live with selected-text styling, emojis, precise colors, text effects, curves, offsets, and scaled placement previews. |
| **Runic Velocity** | Measure modded startup milestones and maintain a validated plugin cache so changed files and loading costs are easier to diagnose. |
| **Runic World Engine** | Configure a validated host player cap, monitor peers, latency, traffic, ownership transfers and backlog warnings, and smooth asynchronous world saves. |

## Raven's Gate security

The pack installs combined Runic Sentinel and its Runic Safety backup dependency. Runic Sentinel
does not require Runic Core or Runic Persistence. A new server
starts with admission Optional so an administrator cannot accidentally lock everyone out. After the
first launch, add your account to the server's `adminlist.txt`, join, press **F3**, and click
**Set Up Sentinel** once. A local listen host is recognized automatically. Existing policies and
keys are preserved. The
server-authenticated panel manages the exact profile, roles, bans, automatic enforcement, backups,
reports, and network maps. The dependency-only suite contains no key material; a private signing key
created at runtime remains on the authoritative server. The separate Forge remains optional for
offline-key setups.

Sentinel blocks requests before escalation and disconnects only on conclusive or repeated
high-confidence protocol evidence. Ordinary permission mistakes are denied without being labeled
as cheating. Client DLL claims are compatibility evidence; consequential Runic actions remain
server-validated because a hostile client controls its own process.

## Configuration Manager

The pack also installs **shudnal's Configuration Manager 1.1.17** so players and administrators can
inspect and edit the Runic mods' exposed settings through its in-game interface. Configuration
Manager is included as a convenience; the Runic plugins themselves remain independently installable.

## Installation

1. Create or select a Valheim profile in Thunderstore Mod Manager or r2modman.
2. Install **Runic Mod Suite**.
3. Let the manager install every dependency.
4. Launch the game with **Start modded**.

For a dedicated multiplayer deployment, install **Runic Mod Server Suite** on the server and
**Runic Mod Client Suite** on ordinary player profiles. A listen host should use this complete suite, which includes full Sentinel and its F3 panel.

The individual Runic mods remain independently configurable in the included Configuration Manager.
Because this pack does not ship configuration files, existing settings are preserved.

For a server using Runic Character Vault, every connecting player needs the Client Suite. Select
your existing character: version 1.0.2 allows existing-character enrollment by default. The server
administrator can disable imports. Wait for the first save confirmation before leaving. Do not
combine the vault with another server-character mod.

## Updating

Update this modpack through the mod manager. A new suite version may update one or more exact
dependency versions while leaving the remaining components unchanged.

## Installation compatibility

Use **RunicModServerSuite** only on the dedicated server. Ordinary players use **RunicModClientSuite**; administrators add full **RunicSentinel 1.4.2** to that client profile for F3. Listen hosts use **RunicModSuite**.

**Never combine full Sentinel and SentinelServer in one profile.** When switching packs, remove the previous pack and its unwanted Sentinel variant; mod managers may retain old dependencies. Fully restart Valheim after changing plugins. SentinelClient may coexist with full Sentinel.

Signs must be installed at the same version on every player and the server. Disable BetterSigns because it replaces the same editor. The server pack includes Storage so chest learning covers server-owned Production automation.

RunicDeathPenalty remains planned for a future suite release and is not included.

## Release 1.2.35

Updated package versions:

- Chazman-RunicAgriculture-1.0.5
- Chazman-RunicCrafting-1.1.7
- Chazman-RunicInteraction-1.0.10
- Chazman-RunicInventory-1.1.10
- Chazman-RunicPortals-1.2.10
- Chazman-RunicProduction-1.0.15
- Chazman-RunicSentinel-1.5.0
- Chazman-RunicSigns-1.2.7
- Chazman-RunicStorage-1.3.7

Update client and server packages together for Portals, Production, Signs, and Storage. Inventory 1.1.6 and Storage 1.2.5 provide Quick Stack-only slot exclusions. Existing configuration is preserved.

## Release 1.2.37

Includes RunicStorage 1.3.0 and RunicSigns 1.1.0 with full-color emoji pickers, retained Quick Stack protection, and sign placement sizing. Includes RunicPrecisionBuildTool 2.0.6 with the F4 Local/World rotation toggle. These mod versions were confirmed working in-game by the author. Existing configuration and all other dependency versions are retained.

## Release 1.2.38

Includes the Valheim 1.0.14 compatibility fixes, retaining the current emoji and precision-building releases.

## Release 1.2.39

Includes RunicBuildCamera 1.0.4 with activation focus recovery and visible toggle feedback, confirmed working in-game by the author. All other dependency versions are unchanged.

## Release 1.2.40

Includes RunicWorldEngine 1.2.1, fixing the optional player-cap override on Valheim 1.0.14 while retaining existing patch-integrity checks.

## September 18, 2026 update

Compatible game updates are accepted through API checks in the updated modules; incompatible APIs still disable safely.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
