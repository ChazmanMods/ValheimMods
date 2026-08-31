# Runic Safety

Runic Safety adds context-bound confirmations, protected-item checks, vanilla tombstone audits,
bounded diagnostics, and verified migration backups for Valheim 0.221.12.

## Installation and independence

Install `RunicSafety.dll` in `BepInEx/plugins`. BepInEx is the only package dependency. Runic Core,
Persistence, Permissions, Transactions, Inventory, and every other gameplay mod are optional.

Safety has no suite registry, protocol handshake, durable transaction bridge, recovery journal,
grave-custody lock, join-time recovery, or account quarantine. It does not create, destroy, or write
world objects. All patches can only allow or decline the original Valheim callback.

If Runic Inventory is installed, Safety discovers its public item-protection seam by reflection at
the exact destructive action. Locked items are denied, unlocked items continue through Safety's
own rules, and an unavailable or malformed installed adapter fails closed. Without Inventory,
Safety loads normally and applies its equipped, quest-item, and configured-rare rules.

## Confirmations

The same action must be repeated against the same object and state within the configured window for:

- hammer removal of an occupied container;
- hammer removal of a ship or cart;
- replacing a non-empty portal tag;
- configured rare-item sacrifice where the original boundary supports confirmation.

A changed target or state starts a new confirmation. Empty containers, initial or unchanged portal
tags, and ordinary unprotected inputs remain single-action Valheim behavior. Pending confirmation
memory is capped at 128 contexts and is cleared on configuration changes and shutdown.

## Protected destinations

Safety checks explicit item candidates at the incinerator, smelter input/fuel, cooking station
input/fuel, fermenter, and item stand boundaries. Equipped, quest, Inventory-locked, and configured
rare items are evaluated before the original callback. Equipped, quest, and locked items are denied;
rare items require confirmation by default.

Incineration is also checked on the current owner in `RPC_RequestIncinerate`. Remote routed sender
IDs do not qualify for administrator bypass. The optional administrator bypass is limited to the
authoritative local host/admin process and is disabled by default.

## Death and recovery planning

Before `Player.CreateTombStone`, Safety serializes the live native inventory and verifies its current
capacity. The original Valheim call remains in control and creates the ordinary tombstone; Safety
does not suppress death, move items, create another recovery object, or lock the grave afterward.

An optional topology provider may register through `SafetyIntegrationApi.Recovery`. Without one,
the audit covers the vanilla/native inventory dimensions. An unresolved plan is reported to the
player and the bounded diagnostics buffer, but it never starts a recovery state machine.

## Migration backups

`SafetyIntegrationApi.Backups` exposes bounded backup operations for migration tools. A backup:

1. acquires a non-blocking per-root single-flight scope;
2. validates bounded source count, size, names, and reparse-point rules;
3. stable-reads and SHA-256 hashes every source;
4. writes a restore manifest and completion marker;
5. commits with an atomic same-root directory rename;
6. applies retention only to exact Safety-marked backup directories.

`ExecuteAfterBackup` reopens and SHA-256-validates the committed files immediately before invoking
the supplied mutation. A failed or cancelled backup never invokes that callback. Source files are
opened read-only, committed backups are preserved if the later callback fails, and Safety does not
auto-restore multi-file saves.

## Optional API

`RunicSafety.Api.SafetyIntegrationApi` exposes the live confirmation, protection, recovery-planning,
backup, local compatibility, diagnostic, and status services. Independently installed callers
should resolve the type dynamically and tolerate a null service. No caller must install Safety in
order to load.

## Configuration defaults

- `General.Enabled = true`
- `Confirmations.RareItemSacrifice = true`
- `Confirmations.OccupiedContainerDestruction = true`
- `Confirmations.ShipOrCartDestruction = true`
- `Confirmations.PortalOverwrite = true`
- `Confirmations.RepeatWindowSeconds = 4`
- `Protected Items.Enabled = true`
- `Protected Items.AdministratorBypass = false`
- `Protected Items.RarePrefabNames` accepts a maximum 256 exact prefab IDs
- `Migration Backups.RootDirectory = BepInEx/config/RunicSafety/backups`
- `Migration Backups.RetentionCount = 5`
- `Migration Backups.MaximumFiles = 32`
- `Migration Backups.MaximumTotalMiB = 2048`

There is no `Update`, `FixedUpdate`, scene scan, or per-frame polling. Diagnostics retain at most
256 bounded events and do not include item lists, portal text, save contents, or source paths.
