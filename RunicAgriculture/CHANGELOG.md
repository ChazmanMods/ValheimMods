# Changelog

## 1.0.3 - 2026-09-10

- Fixed nearby seed chests failing synchronization checks because their contents were read using the old inventory format.
- Fixed false chest-content mismatches caused by Valheim's normal durability rounding during loading.
- Corrected the shared chest-busy check before consuming seeds.

## 1.0.2 - 2026-09-09

- Updated the native placement call and inventory-change notification to Valheim 1.0's exact method signatures.
- Re-audited the installed Valheim 1.0.7 client assemblies and updated the BepInEx dependency to 5.4.2350.

## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0

- Kept bounded area harvest available for every ready, permitted Valheim Pickable, including wild
  Pickables without crop mappings. Only the optional replant offer now requires the replant setting,
  an exact grown Plant mapping, and planting authorization; unmapped harvests no longer show a
  misleading missing-plant warning. The cultivator is not required during harvest.
- Made rows/columns and the generated shape the requested planting footprint through the fixed
  1,600-cell safety boundary; removed the live `MaximumPreview` control that could silently cut a
  valid 40×40 Grid down to a smaller saved limit.
- Added configurable, bounded planting-resource sourcing from personal inventory first and then
  eligible nearby chests. Chest mutation is exact-endpoint, native-owner-local, access/ward checked,
  synchronized, deterministic, rollback-protected, and never claims ownership or sends a custom RPC.
- Changed limited-resource fill order to centre-out for shaped patterns and right-to-left for Grid
  and Row. Red now means resource shortage only; invalid terrain/spacing/access ghosts are muted
  amber and the former blue shortage state is gone.
- Made the rectangular Grid the obvious/default layout and added a one-time reset from a saved
  legacy shape; rows and columns now appear as separate decrement/increment controls in Valheim's
  compact scaled bottom build-hint HUD.
- Separated ground validity from transient stamina/cultivator durability so valid ghosts are no
  longer recolored by the immediate action budget. The HUD lists up to three amber blocked causes.
- Changed a confirmed left-click batch to consume one stamina/cultivator action while retaining
  the normal per-cell resource cost, allowing all selected valid available resources to plant in one batch.
- Validate the complete configured footprint before applying the combined resource limit. Invalid
  early cells now backfill from later valid cells without changing their blocked reason.
- Kept the existing bounded planting patterns, live preview controls, controller editor, contextual
  control bar, exact-Pickable area harvest, and confirmed replant offers.
- Removed Runic Core, Persistence, and Transactions project, plugin, package, registry, capability,
  protocol, and exact-version dependencies.
- Removed the durable plant/replant composite operation, client inventory reservations, journals,
  reconciliation, quarantine, server parity dispatch, owner-command ledger, and world-object
  transaction provider.
- Routed planting and replanting through the local owning player's native `Player.PlacePiece` path
  with immediate per-position validation and explicit batch costs.
- Routed area harvest through current ward checks and the exact loaded Pickable's native interaction
  path; no Agriculture-owned networking or persistent request state remains.
- Replaced the suite-wide mutation lease with a small Agriculture-owned in-memory batch scope that
  always releases on return, exception, or plugin shutdown.
- Reduced the Thunderstore manifest to BepInEx only and added focused independence/native-path tests.
- Changed cultivator pattern planting to ordinary left-click and reserved 1×1 rows/columns for a
  single plant; migrated the former Alt+P binding to Mouse0.
- Made bare wheel rotate the whole ground-plane pattern, while Alt/Shift wheel continue to edit
  rows/columns and Alt+Shift wheel edits spacing.
- Clamped spacing to the selected crop's native minimum and kept planting attempts bounded by the
  exact combined personal-and-nearby resource budget.
- Replaced the separate black IMGUI control bar with Valheim's native bottom build-hint rows and
  restored the original hints when the cultivator preview closes.
- Added dominant blocked-preview explanations, rounded crop spacing upward to a collider-safe tenth,
  and stopped successful commit checks from replacing the real planting stop reason with a raw
  `agriculture.valid` code.

## 0.1.1

- Added controller-accessible confirm, pattern-cycle, and area-harvest controls through Valheim's
  input layer, with contextual input suppression and live binding validation.
- Added status guidance, deterministic area-harvest targeting, preview-pool recovery, and explicit
  feedback for invalid previews, costs, and changed selections.

## 0.1.0

- Added bounded row, grid, and circle planting previews with per-position validation.
- Added explicit planting confirmation, modest same-crop mature area harvest, and confirmed replant
  previews with normal Valheim costs.
