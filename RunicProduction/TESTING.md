# Testing Runic Production

## 1.0.7 ownership handoff candidate - 2026-09-14 (private, not packaged)

- Fixes the code-level split-owner stall and removes live linking-player lookups from pending/legacy
  replenishment activation. Existing link principals and current ward/personal-chest checks remain.
- The station owner requests a chest; only its current owner grants. The grant puts a fresh receipt
  into the native ZDO and yields ownership. The requester waits for the snapshot with that receipt,
  then uses the existing synchronized inventory and exact synchronous commit/rollback paths.
  The receipt is not an item transaction, and cannot replay a transfer after reload.
- Read the installed Valheim ZDOMan serializer: each transmitted ZDO includes owner/data revisions,
  owner, position, and the full serialized ZDO. ForceSendZDO works through the server on clients.
- Bounded request retries, owner dwell time, and temporary UID-priority contention handling cover
  shared/chained chests without forcing claims, holding items, or locking unrelated player actions.
- Automated checks: 78/78 focused tests, including adapter signatures and authorization wiring;
  27/27 source-linked handoff simulations, including independent peer state, duplicate/delayed
  delivery, owner-before-data ordering, disconnect-style station change, reciprocal acquisition,
  denied access, missing peers/mods, open chests, retries, and bounded contention.
- Run: `dotnet run --project tools/RunicProduction.HandoffRegression/RunicProduction.HandoffRegression.csproj -c Release`.
- These simulations use controlled native/Unity stand-ins. They do not establish actual Steam,
  crossplay, Linux Unity, or in-game item conservation. Do not describe them as live acceptance.
- In-game test: install 1.0.7 on the server/host AND both test clients, preserving world/config/links.
  Have A configure kiln/smelter fuel/input/output and cooking/fermenter/replenishment chains. Keep B
  at the base while A moves away, logs out, then rejoins. Repeat with the host staying online while
  a joining setup player departs. Verify ingredient decrements and outputs exactly once. Repeat
  with shared relay chests and recipes requiring ingredients across two oppositely owned chests.
- Open/close one participating chest; other stations must continue and its own service resume.
  Verify revoked ward and personal-chest access denies, restored access resumes, and empty/full,
  out-of-range, moved, destroyed/replaced chests never bypass identity or reserve constraints.
- Test save/reload, empty replenishment chest gaining an exemplar after A leaves, native ownership
  reassignment while a request is in flight, and disabling one peer's Production (must defer safely).
- Candidate destination: `E:\Valheim Mods\PreProduction\Chazman-RunicProduction-1.0.7.zip`.
  Package audit: six intended root entries match source hashes, assembly/manifest 1.0.7,
  BepInEx-only dependency and 256x256 icon; no private testing document in the ZIP.
  ZIP SHA-256: `4875D4E1D312D519FA246A23B90FC5DD85582883DF859DE3E6E41A8A06775D71`.
  DLL SHA-256: `4A1D827FC34744480FA4FB91118A653D6BB30BB5D356B50C6F8759C4091796DA`.
  No live installation, server restart, Thunderstore upload, Latest folder promotion, or suite change.

## Release preparation - 2026-09-14 (1.0.6 history, private, not packaged)

- User acceptance: the user reported "It works" and requested Thunderstore release preparation.
  This is user-reported gameplay acceptance, not a newly observed test matrix.
- Release candidate: 1.0.6. The inspected Valheim 1.0 profile still contains 1.0.5, so the general
  acceptance report is not treated as proof of an identified live 1.0.6 session.
- Release work updates documentation and packages the existing candidate without gameplay changes.
- Fresh build/test output and package hashes are recorded under `artifacts/Thunderstore/RunicProduction/1.0.6`.
- Fresh result: release build passed with zero warnings/errors; 77/77 focused tests passed from the repository root.
- Package audit passed: six intended root files, matching manifest/plugin/assembly version 1.0.6,
  BepInEx-only dependency, 256x256 PNG icon, and exact entry-to-source SHA-256 verification.
- Upload ZIP: `E:\Valheim Mods\Latest Runic Mods\Chazman-RunicProduction-1.0.6.zip`.
  Nothing was uploaded or installed during this release preparation.

## 1.0.6 input capture investigation - 2026-09-13 (historical)

- The player reported immediate kiln-link cancellation in 1.0.5. Their exact incident is not reproduced
  in game; the concrete cross-frame capture gap is fixed independently of that confirmation.
- ProductionLinkInput.CanReadGesture now rejects a captured physical press as well as a processed frame.
  Release sampling requires both held=false and down=false. Failed reads retain capture. No timers,
  permissions, ownership, automation, saved link formats, or server state were changed.
- Seven new executable cases cover all three buttons held across frames, stale edges after quick release,
  station-to-chest rearming, two hooks in one frame, intentional second-press cancellation, session reset,
  and held-press intent changes. These call the production capture methods without Unity input polling.
- Build and focused tests: 77/77 passed. Thunderstore-format 1.0.6 candidate staged in PreProduction only.
- In-game acceptance: test kiln Input (Alt+Left) and Output (Alt+Right), holding the first click while
  moving aim, then release/reclick on the chest. Repeat intentional cancellation at the kiln, Shift unlink,
  rapid clicks, low/high framerates, Alt release while holding mouse, and relevant input-mod combinations.
  Confirm held linking gestures do not attack/block/remove pieces. Check solo, host, and joining client.
- No installs, server restarts, uploads, or mod-suite dependency changes were performed.

Use the audited Valheim 1.0.7 assemblies and configured BepInEx profile. From the repository root,
run the plugin build once and then its focused suite once:

```powershell
dotnet build .\RunicProduction\RunicProduction.csproj -c Release --no-restore
dotnet run --project .\RunicProduction\Tests\RunicProduction.Tests.csproj -c Release
```

The focused suite covers standalone metadata and assembly references, retired architecture absence,
native-owner behavior, owner-approved handoff without remote item operations, stable token and storage
format compatibility, exact player-facing role selection including fire/lamp Input, bounded link
state, reserves, composite nearby sourcing, producer
policies, catalog bounds, fail-closed planned inputs, immediate rollback, nearby indexing, catalog
publication, vanilla smelter-output fallback after proven no-mutation/rollback, station-local safety
pause when a persisted mutation is indeterminate, exact Alt mouse link and Shift+Alt unlink routing, action
suppression through button release (including FixedUpdate/SetControls-before-Update timing), and
station-level role selection plus green/yellow/turquoise linked-chest ring coverage. The ring tests
also enforce one child LineRenderer per ring and fail-closed handling of an incomplete visual graph.

The root release's one dedicated-server acceptance matrix verifies:

- smelter Input/Fuel/Output with complete-batch vanilla fallback;
- cooking and fermenter throughput coexisting with below-reserve Replenishment behavior;
- direct recipe output from composite linked/nearby inputs and a physical exemplar;
- refillable fire Wood and lamp Resin from their exact designated Input chests;
- optional nearby sourcing with per-chest reserves and bounded-query rejection;
- link, refresh, and exact unlink controls with current reach, range, ward, access, creator, and
  native-owner checks;
- resume behavior after station/chest ownership splits, and deferral while an endpoint is unloaded,
  busy, unauthorized, or its saved target proof is stale;
- valid existing links/catalogs and singleton migration without creating operation records;
- no unrelated inventory or tool lock after a denied or failed Production action.

Useful manual checks before the final matrix are:

- Link each supported role, cancel one pending selection, let one selection expire, and unlink only
  the exact selected relation.
- While pointing anywhere at each station family, verify Alt+Left Mouse arms Input,
  Alt+Middle Mouse arms Replenishment, and Alt+Right Mouse arms Output. Repeat the same gesture on
  the chest within 30 seconds; use Shift+Alt at both steps only to unlink. Confirm no attack, block, build
  menu, secondary attack, or remove action leaks through an accepted or rejected Production chord.
- Confirm the just-linked chest shows a vivid green Input ring, yellow Output ring, or turquoise
  Replenishment ring for 12 seconds. Aim at the station again and confirm every currently linked
  chest is marked, including every Replenishment destination. Leave the rings active for their full
  lease and confirm the log has no LineRenderer-addition errors or UpdateVisuals exceptions.
- Fill a smelter Output chest, confirm vanilla output still appears, then free a complete batch and
  confirm routing resumes.
- Give one oven and one fermenter both Output and Replenishment links; below-reserve work must prefer
  the matching exemplar chest while already-completed output always finishes routing.
- Add/remove an exemplar or change a producer definition; polling must refresh the exact signed targets.
- Split station and chest ownership between peers and open a chest during service; owner-approved
  handoff must resume after closing, without duplicate output, persistent work items, or unrelated locks.
- Inject one callback/publication failure that proves the original chest state and confirm the next
  eligible destination may be tried. Inject an indeterminate publication and confirm no other chest
  or vanilla spawn is attempted, only that station pauses, and reloading resumes from saved state.
- Enable nearby ingredient discovery, split a recipe across several shared accessible chests, exceed
  the configured source limit, and confirm no partial donor set
  is used.
- Link a food and a mead-base exemplar to an allowed recipe station, then link mead-base Input and
  multiple matching mead exemplar chests to a fermenter; verify only the exact requested outputs move.

Final focused-test totals, dedicated-server evidence, and coexistence evidence are recorded by the
root release workflow.
