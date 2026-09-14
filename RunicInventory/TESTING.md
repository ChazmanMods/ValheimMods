# Runic Inventory testing

## 1.1.5 release packaging - 2026-09-14

- Release build: zero warnings and zero errors. All 167 main-suite checks pass.
- Separate net48 HarmonyX resize/ejection, slot-result, and cleanup-retention regression passes.
- Refreshed the stale dedicated-server fingerprint fixture after the installed server's exact
  version accessor, inventory, ownership, and server-role contract checks passed. Current server
  SHA256: F4EC6D8FC07054058F5E98040B3C1C65BDF0B061FD2ED27087EF48F29586B737.
  This is the same server binary audited during the RunicDisplayStands 1.3.7 release.
- Gameplay source is unchanged by release preparation. The notes below record earlier candidate
  results; their fingerprint failure is resolved by this fixture update.
- No new live Unity visual, save/reload, or grave-recovery acceptance was performed in this pass.
  Automated results do not establish those outcomes. The in-game checklist below remains useful.
- Thunderstore ZIP is built by tools/Package-RunicInventoryRelease.ps1; its entry hashes, DLL and
  manifest versions, dependency, and 256x256 icon are checked. No upload or live install performed.

## 1.1.5 compact quiver panel (private, not packaged)

- Display-only change: RectTransform anchored positions and panel size; never calls Inventory.SetHeight,
  changes item coordinates, or rewrites quiver/keybinding configs. The 1.1.4 HarmonyX guards are unchanged.
- Added seven checks for equipped/unequipped layout, offset handling, native pocket sizes, UI scale,
  invalid inputs, renderer mutation boundaries, and Valheim's index-based click mapping.
- Standard inventory only; Auga layout is left untouched. Custom quiver positions are not rewritten;
  no compaction is applied when their placement leaves no removable gap above the role row.
- In-game checks: toggle CompactQuiverLayout on/off; open/close inventory and containers; equip/unequip
  quiver; verify label/hover alignment, dragging, right-click equip, slot locks, gamepad selection,
  altered UI scale, native pocket upgrades, and retained items after save/reload. Use the test character.
- Candidate for PreProduction only. Do not claim Unity visual acceptance from automated geometry tests.
- Release build and 166/167 main-suite checks pass (unchanged dedicated-server fingerprint failure).
  All seven new checks and the separate net48 HarmonyX regression pass. ZIP content hashes verified.

## 1.1.4 HarmonyX resize correction (private, not packaged)

- 1.1.3 is rejected: player's supplied log confirms adapter startup, then native-pocket-size,
  then "Dropping 5 invalid positioned items." Do not distribute that candidate.
- Root cause: installed BepInEx HarmonyX executes every prefix and ANDs return values; returning
  false in Runic's high-priority prefix does not suppress BA's later resize or slot-result writes.
- Guard BA's three conflicting prefix bodies directly, not just the shared original methods.
  Non-Runic inventories still use BA's original behavior. No other mods' patches are removed.
- Independent net48 harness using the installed 0Harmony.dll reproduces the old late-prefix behavior,
  verifies the guarded resize, and verifies ref-result isolation for FindEmptySlot and HaveEmptySlot.
  It also exercises the production cleanup policy. Command:
  dotnet run --project tools/RunicInventory.HarmonyXRegression/RunicInventory.HarmonyXRegression.csproj -c Release
- This is a managed-runtime reproduction, NOT an in-game Unity test. The normal net8 test host cannot
  initialize this older HarmonyX runtime; the separate net48 host is required.
- The player-supplied source identifies BA 2.0.0, unlike the logged/audited 1.9.9 binary. Do not widen
  the binary allowlist or claim 2.0.0 compatibility on source similarity alone.
- Before promotion: use a disposable character or backed-up copy with BA quiver enabled. Load with
  five equipped armor items and quick-slot stacks; verify no dropped items, both row sets visible,
  all item quantities/metadata retained, then save/reload, sort/pickup full inventory, and grave recovery.
  Verify the log says "HarmonyX resize/slot guards" and contains no automatic ejection.
- No live client/server deployment, server restart, or Thunderstore upload is part of this change.
- Results: 159/160 main-suite checks pass; sole failure remains the pre-existing dedicated-server
  fingerprint fixture. Separate net48 HarmonyX regression passes. Package 1.1.4 generated and
  ZIP contents hash-verified; rejected 1.1.3 moved to ReleaseArchive/Rejected-RunicInventory-1.1.3.

## 1.1.3 Better Archery adapter (private, not packaged)

- Candidate only: no client/server deployment or Thunderstore upload. Validate in game before promotion.
- Audited Thunderstore BetterArchery 1.9.99 / plugin 1.9.9 SHA256:
  549B3B6AC69C512E4B3BA1EEECD34687ACB72712A3B068BD1D2783B06BE1F823.
  Different binaries do not enable the adapter. Obtain the affected player's DLL hash if it does not activate.
- Automated coverage: all native pocket sizes, resize pairs, existing role/lock migration,
  full-inventory disable refusal, quiver-disabled migration, invalid quiver contents, binary contracts.
- Release build completed; 157/158 Inventory tests passed, including all 10 new compatibility checks.
  The sole failure is the unchanged dedicated-server binary fingerprint fixture noted below.
- In-game acceptance remains required on Valheim 1.0.12 with the audited BA binary:
  back up character first; test fresh and existing saves, equip/unequip quiver, all three ammo cells,
  firing/changing arrows, role labels, sort, Quick Stack, pickups, swaps, and distinct/overlapping hotkeys.
- Die and recover BOTH ordinary and quiver graves, including a nearly full backpack; verify item quantities,
  durability, upgrades, custom data, equipment and reload. Repeat with native pocket upgrades.
- Test Runic disabled with and without free ordinary cells. Test BA quiver disabled after restart with
  and without room to relocate its arrows. No missing/duplicated item is acceptable.
- Legacy BA `ba drop` is guarded because it assumes every row >= 5 is invalid; other debug commands unchanged.
- The existing dedicated-server hash fixture mismatch described below remains separate from this client fix.

## 1.1.2 slot retention/use regression (private, not packaged)

- Automated: separate transfer/use API states, unavailable provider behavior, native-use allowlist,
  quick-slot wiring, native ammunition removal contract, actual Storage adapter retaining locks,
  and Safety cooking/fuel decisions including equipped, quest, rare, and unavailable-provider guards.
- In-game checks still required: lock arrows and bolts; select/fire them, switch ammunition, exhaust
  a stack, and pick up more. Quick Stack/Store All must leave the locked stack in place.
- Repeat with locked food/potions using normal bindings and all three quick slots. Verify matching
  pickups replenish partial locked stacks. Verify sorting, dropping, and transfers retain their guards.
- With Safety 1.0.5, cook locked meat and add locked wood/resin/coal through normal interactions;
  verify smelting/fermenting and unchanged quest/rare/disposal safeguards. Test solo and a remote client.
- Current local Inventory suite has an existing dedicated-server SHA-256 fixture mismatch:
  expected 9DF99B0011B4CA0A448E6D935C77368B4E3B98EEE7B0AC8D1B43B34E267471B2,
  observed F4EC6D8FC07054058F5E98040B3C1C65BDF0B061FD2ED27087EF48F29586B737.
  Dedicated contract checks pass. The hash assertion was not weakened or changed for this fix.

Target: Valheim 1.0.7.

1.1.0 dedicated-row acceptance (private release checklist, not packaged):

- Existing 4-row character with every ordinary cell occupied: enabling adds exactly one row;
  equipped armor moves into matching roles, and all other items remain present with unchanged metadata.
- Reload old/new characters, including a character supplied by CharacterVault. Verify no stale
  extra-row marker leaks between loads and no second extra row appears on respawn or reconnect.
- Reject materials in each role via drag, swap, split-stack, controller, chest Take All, and direct
  positioned additions. Permit only matching equipment and consumable/tool/utility quick items.
- Buy native pocket upgrades; confirm native row progression excludes the extra row.
- Death/tombstone retrieval after logout/server restart: count and compare all extra-row items.
- Fill normal inventory with the role row occupied: Enabled must be grayed out with a no-room
  explanation. Attempt a config-file disable; Enabled must restore to true. Free space: control
  becomes available, but the mod must not disable until explicitly requested again.
- Disable with room, then re-enable: same item instances/metadata, no duplicates, no stranded cells.
- Before uninstall, disable successfully and verify all items fit in ordinary inventory.
- Test solo, a listen-server host/guest, and dedicated clients on Windows and Linux.

Automated tests cover the exact topology, persistence codec, lossless position rollback, pickup
policy, controller bindings, optional reflection API, installed client/dedicated contracts, and the
absence of Foundation DLL references.

Manual acceptance should verify:

1. With Foundation DLLs absent, install Inventory by itself and confirm quick slots, equip swaps,
   locks, sort, pickup filtering, and configuration reload. In the open inventory, verify the exact
   bottom cells say Helmet, Chest, Legs, Cape, Utility, and Quick 1-3, each with a yellow border.
   Toggle a general slot with Left Alt + right-click, then repeat to unlock it.
2. Exercise death, tombstone recovery, logout/login, disable/re-enable, and uninstall with every
   special role occupied; require no item loss or duplication.
3. Connect one client to a dedicated server. Confirm only that client's native inventory changes and
   the headless server remains inert.
4. Test each independently installed Interaction, Crafting, or Storage mod, then test them together.
   Confirm they discover the
   optional reflection API only when Inventory is present and keep working when it is absent.
5. Inspect the release DLL references and Thunderstore manifest; Core, Persistence, Permissions, and
   Transactions must be absent.
6. Remove the utility belt from its role, move it through another slot, and equip it again. The
   bottom-row labels/borders and role behavior must recover immediately, and Storage protection
   queries for carried items must again return a proven result.
7. With Safety and Interaction installed, verify ordinary cooking and full-stack transfers in solo,
   listen-host, remote-client, and dedicated-server setups. A healthy authoritative Inventory must
   still deny a locked or special-row item. An existing character whose bottom role row is
   incompatible must fall back to vanilla without causing Safety to block ordinary food. A genuine
   owning-local load/rebind or malformed-evidence state must remain fail closed and emit one bounded
   reason.

The pass criterion is exact native ownership, bounded local work, vanilla persistence, and no item
loss across supported lifecycle paths.
