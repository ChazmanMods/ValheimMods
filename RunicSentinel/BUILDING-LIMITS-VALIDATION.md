Historical first-candidate report. Client support is now available in RunicSentinelClient 1.0.5; see BUILDING-LIMITS-CLIENT-VALIDATION.md for the follow-up validation and current installation requirements.

## FIX

Added opt-in host settings for vertical support loss, terrain raising and digging. Built as RunicSentinel 1.6.1 test candidate from the local 1.6.0 source in RunicLocalization. Only the full RunicSentinel package implements the feature; it must be installed on every participant. No deployment or public release performed.

## ROOT CAUSE

This is a new feature request. Before editing, inspection of the installed game's decompiled code found fixed +/-8-metre bounds in TerrainComp.LevelTerrain, RaiseTerrain and ApplyToHeightmap, plus Heightmap.LevelTerrain and a separate 7.95-metre AtMaxWorldLevelDepth check. There were no corresponding Sentinel controls. The original numerical clamp is reproduced in the focused harness; an in-game pre-change reproduction was not performed.

TerrainOp/hoe/pickaxe operations enter these editing paths, while heightmap rebuilding also applies the limits when loading persisted terrain. WearNTear.UpdateSupport and support-color calculations call GetMaterialProperties; vertical loss is divided by the configured multiplier without altering maximum/minimum material support or horizontal loss. Terrain arrays, owner-driven native mutation, resource costs, ZDO serialization and save formats remain native. The new RPC exchanges configuration only. Its acknowledgement proves compatibility, not authentication or cheat resistance.

## RISK LEVEL

3 — High. Persistent terrain geometry, structural stability, Harmony patches and multiplayer configuration/admission are affected. Most likely regressions: extended terrain clamping on rebuild, unchanged dig-stop threshold, collapse or desynchronization after ownership transfer, and stale settings after reconnect. Reducing/removing custom limits on an existing world remains hazardous and is documented.

## TEST BUDGET

12 meaningful checks planned; 8 executed (one build plus seven focused automated checks). Four live gameplay/network/persistence checks remain outstanding. Build retries and reruns after changes are not separate checks. No generic full suite was run.

## TESTS RUN

1. Final Release plugin and focused harness build: passed, zero warnings/errors.
2. Vanilla defaults and independent positive/negative terrain bounds: passed, numerical checks.
3. Finite/range validation and invalid settings: passed.
4. Exact settings wire round trip; malformed size/schema/NaN rejection: passed.
5. Executed the production transpiler against IL read from the installed game for all four terrain edit/rebuild methods: passed. Both limits replaced; other instructions preserved in order.
6. Actual dig-stop IL replacement and rejection of incompatible IL: passed.
7. Vertical loss scaling, installed material-property signature, single-output postfix, and RunicIntegrity native solver/color call sites: passed. This is calculation/contract/static integration evidence, not a gameplay integration test.
8. Compiled host admission guard and disconnect/world-reset hooks: passed static inspection. This does not prove network ordering or live synchronization.

Command: `dotnet RunicLocalization/RunicSentinel.Tests/bin/Release/net8.0/RunicSentinel.Tests.dll --building-limits`

## REGRESSION CHECK

Checked preservation of neighboring terrain IL (including clamp/write operations), vanilla values, independent raise/dig controls, material strengths/horizontal loss interface, malformed configuration and wire data. Inspected RunicIntegrity's use of the native solver. Preserved the existing 1.6.0 administration/command implementation. Did not claim runtime compatibility with other terrain/support mods.

## INVARIANTS

Verified bounded finite settings, exact wire shape, default values and unchanged neighboring terrain IL. Authority guards and session reset hooks were inspected. Native save schema is unchanged. Multiplayer agreement, actual persistent-state survival, owner-only mutations and resource conservation remain unverified in-game.

## ISSUES FOUND

Located the separate dig-stop threshold during blast-radius inspection and included it. Found older source in ChazmanModsRepo and moved the final changes to the current 1.6.0 baseline in RunicLocalization. The first harness build hit the game's Console class naming collision; fixed with System.Console. The bundled Mono-oriented Harmony detour reader did not run under .NET 8; tests now read actual game IL with Cecil and invoke the real production transpiler without native detours. Final focused checks pass.

## UNTESTED RISK

No Valheim gameplay processes were running. Two independently joined participant sessions and a disposable saved world were not established for this task. The four required live checks remain:

1. Host digs/raises past 8 metres and stops at custom bounds; builds taller wood/stone/metal structures; neighboring leveling, smoothing, mining drops and RunicIntegrity suggestions remain correct.
2. A genuinely joined separate-process client performs the same operations; a second participant observes correct terrain and support, including ownership transfer/concurrent edits.
3. Start from a backed-up 1.6.0 world, modify terrain/structures under this candidate, save, stop and reload; verify actual geometry and structural survival. Unchanged schema alone is not persistence proof.
4. Real client reconnect, then join a vanilla-limit host; confirm settings reset. Verify missing/incompatible clients are refused by custom-limit hosts, and inspect logs for patch/RPC/runtime errors.

The adversarial questions map directly to these checks: likely regressions are rebuilding terrain and support collapse; alternate failure paths are leveling and dig-stop; persistent mutations can affect resources/world geometry; host/client behavior can differ; reconnect/reload can expose stale settings. Automated/static evidence does not close these live gaps. No startup-only, simulated peer or handler-routing evidence is represented as live multiplayer validation.

## VERDICT

**INCOMPLETE - Important validation could not be performed**

