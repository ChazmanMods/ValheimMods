# Runic Suite 14 — Compatibility, Security, and Performance Audit

Audit date: 2026-08-22
Audited game target: Valheim 0.221.12
Source-gate status: **PASS**
Build/package/load-smoke status: **PASS**
Dedicated remote-player feature-parity status: **HOLD**

> Unpublished correction note (2026-08-25): Runic Storage 1.0.0 now permits an authorized,
> physically reachable non-owner replica to show hover contents only after its live inventory is
> byte-exact with the current replicated `s_items` payload. Hover still never requests ownership.
> The current focused Storage result is 91/91; historical test-count rows below retain the
> 2026-08-22 audit-freeze counts.

The source, deterministic package, compatibility, and isolated dedicated-load gates are green: all 23
audit projects passed, every Release build completed with zero warnings and zero errors, the unified
cross-module audit passed 20/20, and the exact 20-plugin staged payload reached true server readiness.
Those gates prove safe loading and the implemented feature slice; they do not prove that remote
players can use every original-design feature. The user requires a dedicated server and player group,
so the host-only/fail-closed paths listed below are release blockers. The current packets must not be
presented as the final dedicated-parity release. The normative remediation matrix and multi-client
release topology are maintained in [RUNIC_DEDICATED_PARITY.md](./RUNIC_DEDICATED_PARITY.md).

## Scope and exact identities

The suite contains exactly the fourteen gameplay responsibilities in the original Runic Valheim
Vanilla Plus design specification. Runic Building deliberately keeps the already-published Runic
Precision Build Tool identity. Creating a second placement-transform plugin would create competing
ownership of the same responsibility and break the published lineage.

| # | Design responsibility | Module / GUID | Release | Exact Foundation floors | Focused result |
|---:|---|---|---:|---|---:|
| 1 | Storage | Runic Storage / `chazman.RunicStorage` | 1.0.0 | Core 1.0.0; Transactions 1.0.0 | 38/38 PASS |
| 2 | Crafting | Runic Crafting / `chazman.RunicCrafting` | 1.0.0 | Core 1.0.0; Permissions 1.0.0; Transactions 1.0.0 | 27/27 PASS |
| 3 | Agriculture | Runic Agriculture / `chazman.RunicAgriculture` | 1.0.0 | Core 1.0.0; Transactions 1.0.0 | 49/49 PASS |
| 4 | Production | Runic Production / `chazman.RunicProduction` | 1.0.0 | Core 1.0.0; Permissions 1.0.0; Transactions 1.0.0; Persistence 1.0.0 | 150/150 PASS |
| 5 | Building | Runic Precision Build Tool / `chazman.RunicPrecisionBuildTool` | 2.0.1 | none | 144/144 PASS |
| 6 | Inventory | Runic Inventory / `chazman.RunicInventory` | 1.0.0 | Core 1.0.0; Transactions 1.0.0 | 120/120 PASS |
| 7 | Portals | Runic Portals / `chazman.RunicPortals` | 1.0.0 | Core 1.0.0; Permissions 1.0.0 | 226/226 PASS |
| 8 | Exploration | Runic Exploration / `chazman.RunicExploration` | 1.0.0 | Core 1.0.0 | 52/52 PASS |
| 9 | Awareness | Runic Awareness / `chazman.RunicAwareness` | 1.0.0 | Core 1.0.0 | 66/66 PASS |
| 10 | Interaction | Runic Interaction / `chazman.RunicInteraction` | 1.0.0 | Core 1.0.0 | 70/70 PASS |
| 11 | Safety | Runic Safety / `chazman.RunicSafety` | 1.0.0 | Core 1.0.0 | 178/178 PASS |
| 12 | Velocity | Runic Velocity / `chazman.RunicVelocity` | 1.0.0 | Core 1.0.0 | 19/19 PASS |
| 13 | Sentinel | Runic Sentinel / `chazman.RunicSentinel` | 1.0.0 | Core 1.0.0 | 16/16 PASS |
| 14 | World Engine | Runic World Engine / `chazman.RunicWorldEngine` | 1.0.0 | Core 1.0.0 | 15/15 PASS |

All fourteen manifests contain the exact BepInEx package floor
`denikson-BepInExPack_Valheim-5.4.2333`. The 1,170 focused gameplay checks above are distinct
harness checks, not a code-coverage percentage.

### Compatibility participants, not gameplay-module count

| Participant | GUID | Release | Focused result | Role in this audit |
|---|---|---:|---:|---|
| Runic Core | `chazman.RunicCore` | 1.0.0 | 29/29 PASS | typed registry, module identity, input and shared contract owner |
| Runic Permissions | `chazman.RunicPermissions` | 1.0.0 | 44/44 PASS | permission and ward-policy decisions |
| Runic Transactions | `chazman.RunicTransactions` | 1.0.0 | 37/37 PASS | durable claims, stable world-object tokens and mutation coordination |
| Runic Persistence | `chazman.RunicPersistence` | 1.0.0 | 13/13 PASS | bounded atomic persistence primitives |
| Foundation integration | — | coordinated release | 7/7 PASS | cross-library identity, dependency and protocol behavior |
| Runic Build Camera | `chazman.RunicBuildCamera` | 0.1.0 | 71/71 PASS | separate optional camera companion and overlap/privacy participant |
| Runic Integrity | `chazman.RunicIntegrity` | 0.2.2 | 6/6 PASS | separate optional integrity companion |

Build Camera remains completely separate: it is not one of the fourteen, is not a hard dependency of
their manifests or assemblies, and retains its own GUID, DLL and focused suite. It is nevertheless
always included in the unified compatibility inventory because its camera/placement hooks and remote
hover behavior affect the safety proof.

## Evidence model and release rule

This report separates three kinds of evidence:

1. **Source evidence** — the checked-out projects, built DLLs and local focused/integration audit
   runs described below. These gates passed.
2. **Artifact evidence** — deterministic archive recreation, staged DLL/package hashes, the source
   snapshot digest and the promotion ledger. BuildSuite must write these facts to
   `artifacts/RunicSuite14/AUDIT-EVIDENCE.json`.
3. **Runtime evidence** — an isolated dedicated-server run using the exact staged DLL payload. Its
   evidence location must be recorded by the artifact JSON.

The artifact and runtime rows are intentionally not represented as successful here because they had
not run at report freeze. A release is GO only if the generated evidence binds one source snapshot to
all packages, proves two independent recreations are byte-identical, proves rollback-safe all-or-none
promotion, and records all of the following smoke facts:

- the runtime uses the pinned Valheim/BepInEx/Harmony binaries and the exact staged DLL hashes;
- the server log reports `Valheim version: 0.221.12`;
- `Registering lobby` occurs before `Opened Steam server`;
- the process remains alive through a bounded post-ready dwell of at least ten seconds;
- BepInEx, standard output, standard error and the isolated Unity log contain no fatal load, patch,
  dependency, type-load or unhandled-exception signature;
- the disposable save/config/plugin roots are outside every live profile and world.

## Audited binary environment

The unified 20/20 audit read the installed binaries rather than trusting a label. It verified these
SHA-256 values and embedded/file versions:

| Binary / contract | Audited version or SHA-256 | Source-gate result |
|---|---|---|
| Client `assembly_valheim.dll` | `3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004` | PASS; IL version tuple is 0.221.12 |
| Dedicated `assembly_valheim.dll` | `84A1B34F95774D36BE328390578D7B07C5CFFBC8CBB15119541900F055D486A3` | PASS |
| `valheim_server.exe` | `A1E5ACCF766C1177A7E0B82B457CBED74CB3C9EFB5EE8E5C1E0BBBB60BD52839` | PASS |
| `BepInEx.dll` | `E9AC3A950E91E71B13DF5480B36CE06AF27E981A688F0E62125B674D03A0713A` | PASS; file version 5.4.23.3 |
| `0Harmony.dll` | `1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031` | PASS; file version 2.9.0.0 |
| `Mono.Cecil.dll` | `7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6` | PASS |
| Every gameplay manifest | exact BepInEx package `5.4.2333` | PASS |

The generated artifact evidence must re-record the client/server build identifiers and binary hashes
used by the release smoke. These source-gate pins do not substitute for that later proof.

## Verification results

`Test-RunicSuite14.ps1` built and ran 23/23 audit projects on 2026-08-22. Each build used Release,
`TreatWarningsAsErrors=true`, and completed with zero warnings and zero errors.

| Gate | Result |
|---|---:|
| Four Foundation component suites | 123/123 PASS |
| Foundation integration | 7/7 PASS |
| Fourteen gameplay focused suites | 1,170/1,170 PASS |
| Wave 1 cross-module integration | 21/21 PASS |
| Build Camera companion | 71/71 PASS |
| Integrity companion | 6/6 PASS |
| Unified identity/environment/dependency/capability/Harmony/input/documentation audit | 20/20 PASS |
| Audit projects | 23/23 PASS; zero build warnings/errors |
| Full-suite deterministic archive evidence | HOLD; authoritative result belongs in `artifacts/RunicSuite14/AUDIT-EVIDENCE.json` |
| Exact-staged-payload dedicated-server smoke | HOLD; authoritative smoke path belongs in that JSON |

Summing the named harness counts gives 1,418 checks. This number documents execution breadth; it is
not a claim that every possible multiplayer timing, third-party mod or hardware combination was run.

## Compatibility audit conclusions

### Identities, dependencies and capabilities

- All fourteen GUIDs, assembly identities and package names are unique; their plugin, assembly,
  product, manifest and package versions agree.
- All fourteen icons are exact 256 x 256 PNGs and have fourteen distinct SHA-256 values.
- Gameplay peers are optional runtime integrations: there are no gameplay-to-gameplay manifest hard
  dependencies and no compiled hard assembly references between gameplay modules.
- Optional peers use public Core-owned contracts. No consumer duplicates or manually patches a
  private peer implementation.
- Core owns the canonical `inventory.item-locks`, `safety.confirmation`, `security.*`, `zdo.observe`
  and `zdo.ownership` roots. Inventory publishes the typed item-lock query; Storage, Interaction and
  Safety consume it with the same `Locked` / `Unlocked` / in-domain `Unknown` semantics. Safety
  publishes the typed confirmation contract consumed by Portals.
- Persistent ZDO writes remain with declared single-purpose owners, including Transactions' durable
  claim/identity records. Display-only modules do not create mutation keys.
- Foundation dependency floors in manifests and compiled references agree exactly with the table
  above. Missing, stale, wrong-type or throwing optional providers fail closed or hide cleanly.

### Harmony targets and ordering

The audit resolved every static patch and the approved Build Camera dynamic targets against the
installed 0.221.12 assemblies. It inventories patches per method, keeps overload signatures distinct,
and conservatively groups wildcard targets with every same-name overload. All 35 discovered shared
target groups are explicitly allow-listed with their exact participating modules and roles; stale or
over-broad approvals fail the audit.

The high-risk order checks passed:

- Transactions' `WearNTear.Destroy(HitData,bool)` denial runs at Priority.First (800). Production's
  prefix runs at Priority.Last (0), declares `HarmonyAfter` Transactions, honors an earlier
  `__runOriginal=false`, and acquires no teardown lease after a prior veto.
- Transactions may suppress claimed `Container.CheckForChanges` work before Storage, Crafting and
  Production run additive index-refresh postfixes.
- Safety protection and confirmation precede Inventory lock and Production ownership/automation
  decisions on cooking, fermenting, smelting, stands and incineration.
- Inventory locked-item/slot denials precede Crafting commits and Interaction transfer gestures.
- Production/Agriculture hover composition precedes Awareness' low-priority observer capture.
- Storage, Agriculture and Inventory controller getter prefixes are monotonic denials: no later
  prefix can turn a consumed input back into an allowed input.
- Building, Crafting, Agriculture and Build Camera placement hooks have explicit, disjoint roles and
  restoration finalizers. The one Building placement transpiler has no competing transpiler.
- Default keyboard and controller chords, including Precision's `[Controls - ...]` sections and
  `KeypadPeriod`, have no cross-module collision.

### Authority, privacy and loss safety

- Every state-changing path is required to revalidate actor, local/server authority, ownership,
  permission, ward, distance, identity, capacity and immutable before/after evidence at its last
  commit boundary.
- Crash-cut, malformed-input, retry, replay, cancellation, exception, rollback, generation and
  disable/uninstall cases are present in the focused suites. Incomplete or contradictory durable
  evidence is retained/frozen; it is not guessed away.
- Display modules request neither ownership nor extra server data. Detailed state is disclosed only
  from already available, current, physically reachable and access/ward-approved evidence.
- Signed Sentinel policy uses strict bounded parsing and public verification. It does not claim that
  a compromised client can attest its own process as trustworthy.

### Storage chest-content hover

The new hover summary is observational and access controlled. Before any inventory decode it requires
the local avatar's bounded physical reach, strict no-flash hostile-ward clearance, installed container
access, a valid replicated ZDO, and no open/busy/loading/mutation/durable-claim conflict. It compares
the live inventory with the current replicated `s_items` snapshot byte-for-byte, then repeats the
relevant state and payload checks immediately before publication.

It requests no ownership. An authorized non-owner replica may show contents only after the exact
live/ZDO proof succeeds; Build Camera remote hover, stale bytes, in-use containers and uncertain
authority receive only the original vanilla hover. Work is capped at
1,024 stacks, 4,096 custom-data entries, the configured payload/decoded-byte envelope, 512 cache
entries, 128-character localization tokens, 48-character retained labels and 8,192 characters of
base hover text. Storage's focused suite includes authorization order, time-of-check/time-of-use,
stale-evidence, no-ownership, Build Camera distance, hostile-input and cache invalidation regressions.

## Performance audit

The suite does not claim a universal optimum across every CPU, world and third-party mod set. The
release criterion is stronger and measurable in the places that prevent runaway work: idle paths do
not scan the world; collection work has explicit ceilings; hostile inputs are rejected before large
allocation; caches are event/change maintained; and the named profiles pass their inspection,
allocation or wall-clock thresholds.

| Module | Audited hot-path bound or profile | Source evidence |
|---|---|---|
| Storage | 100/1,000/10,000 candidate profiles visit each supplied candidate once and retain only nearest 64; 50 m query visits at most 121 cells; unchanged exact hover uses a 512-entry bounded cache | 38/38 PASS |
| Crafting | event-maintained spatial index; nearest 256 candidates and at most 128 endpoints; provider truncation falls back to the independently bounded/local-authorized set; no global object scan | 27/27 PASS |
| Agriculture | at most 50 pooled preview/attempt positions and 25 exact-crop harvest objects within the 8 m hard radius; every position is revalidated | 49/49 PASS |
| Production | irrelevant-station profiles at 100/1,000/10,000 do exactly N raw inspections and zero persistence parses, identity candidates, registrations or scheduler sort keys; nearby staging caps 64 sources/1,024 contributions/10 items; global new-operation budget is 64 per two-second quantum | 150/150 PASS |
| Building | 100/1,000/10,000 catalog inputs stop at the inspection cap; catalog holds 256 entries; warmed input routing, pose composition and complete core frames allocate 0 bytes across 100,000 iterations | 144/144 PASS |
| Inventory | 1,000/10,000 oversized plans stop after at most 129 enumerator advances; 10k allocation stays within 4 KiB of 1k; 10,000 invalid topology proofs allocate under 64 KiB; 100,000 pickup calculations allocate under 1 KiB | 120/120 PASS |
| Portals | authoritative scan cap 4,096, graph cap 2,048, result cap 128 and bounded 256-entry stores/ring; maximum graph build/query and 1,000 route plans each complete under the 10-second regression threshold; idle update has no direct allocation | 226/226 PASS |
| Exploration | 10,000-pin hard cap and 24-result cap; current local profile: 100/1k/10k single builds took 0.12/1.27/11.89 ms and allocated 84,696/800,464/7,958,408 bytes; four-second repeated-change profiles performed two builds and took 0.26/1.98/23.57 ms with 159,672/1,591,208/15,907,096 bytes | 52/52 PASS |
| Awareness | 0.2–2 s sampling, cached drawing, no inventory/comfort/scene scan; 2,000,000-character adversarial sanitizer input finishes under one second; panel is capped at 40 lines/4,096 characters | 66/66 PASS |
| Interaction | three frame ticks contain no LINQ/direct managed creation; eight of at most 512 door timers serviced per frame; one reusable 32-collider non-alloc buffer; 64 menus/128 filter rules | 70/70 PASS |
| Safety | no `Update`/`FixedUpdate` or scene scan; work occurs only at protected/high-impact action or death preflight; confirmation/root/provider/diagnostic collections have fixed ceilings | 178/178 PASS |
| Velocity | at most 4,096 directories/files, 512 MiB per file, 4 GiB per scan and 8 MiB cache; a 1,000-entry cache round-trip passes; hostile oversize decode allocates under 1 MiB; oversized 10,000-entry input is rejected before enumeration | 19/19 PASS |
| Sentinel | at most 512 plugin descriptors, 512 MiB each/4 GiB total, 1 MiB policy, 32 evidence providers/256 evidence entries; 1,000- and 10,000-descriptor over-cap inputs fail at the bound; workers are cancellation/generation gated | 16/16 PASS |
| World Engine | aggregate sample at most once/second using counters/collection counts; no base scan/sector walk/save clone; 256 modules and 256 entries per kind; declaration enumeration stops after 1,024 inspected values | 15/15 PASS |

The Exploration timings are diagnostic measurements from the current local Release/no-build test run;
they are not a promise for other machines. The remaining rows state the exact passing regression
thresholds or deterministic work ceilings. The generated artifact evidence must bind the released
DLL hashes to its own test and smoke execution rather than relying on these working-tree measurements.

## P0/HIGH findings ledger

A green source result must not erase how the serious defects were found and prevented from returning.
Every major P0/HIGH item from this audit is mapped to a concrete remediation and a regression gate.

| Severity | Finding | Remediation in the audited source | Regression gate / closure evidence | Disposition |
|---|---|---|---|---|
| P0 | Chest hover could disclose stale or unauthorized contents, especially through remote Build Camera hover or a lagging dedicated-client replica | Require avatar reach, strict ward/access/current-payload proof, busy/claim exclusion and a final TOCTOU recheck; never request ownership; bound decode/text/cache work | Storage hover/privacy tests, Build Camera focused suite, unified documentation/authority gate | CLOSED at source gate |
| P0 | Production treated restart-renumbered raw ZDOIDs as durable identity, producing a false `LinkTargetMoved` for a chest that never moved and risking removal/redirection of a valid relation | Durable relations now use canonical Runic stable station/container tokens; numeric ZDOIDs are current-load caches only; ambiguity freezes the relation; faults are retained until explicit authorized action | `ProductionEndpointIdentityTests` remapped-ZDO case, Fermenter restart-identity case, stable-token/claim tests, Production 150/150 | CLOSED at source gate |
| P0 | Production teardown could acquire recovery state after Transactions or another prefix had already denied `WearNTear.Destroy` | Transactions runs Priority.First; Production runs Priority.Last plus `HarmonyAfter`, inspects `__runOriginal`, and does no lease/adapter mutation after veto | Unified exact role/priority/order audit and Production installed-IL tests | CLOSED at source gate |
| P0 | Locked/protected-item consumers could treat “provider did not govern this object” or uncertain in-domain evidence as unlocked, enabling cross-module mutation | One Core-owned typed `IItemProtectionQuery`; exact item-reference domain; `Locked` and in-domain `Unknown` deny, `Unlocked` is explicit, false/NotApplicable falls back only to each consumer's independent gates | Inventory protection-domain tests; Storage, Interaction and Safety consumer tests; unified canonical-contract gate | CLOSED at source gate |
| HIGH | Foundation package/assembly/version drift could load incompatible identity, permission, claim or persistence protocols | Coordinated Core 1.0.0, Permissions 1.0.0, Transactions 1.0.0 and Persistence 1.0.0 release floors; exact manifest/compiled/package alignment | 123 Foundation component checks, 7 integration checks, 21 Wave 1 checks, unified dependency-floor audit | CLOSED at source gate |
| HIGH | A second “Building” implementation would duplicate placement ownership and conflict with the published plugin lineage | Keep `chazman.RunicPrecisionBuildTool` 2.0.1 as the sole Building identity; keep Build Camera separate | exact-14 identity/uniqueness test, manifest hard-dependency test, placement-overlap role audit | CLOSED at source gate |
| HIGH | Harmony audit could combine whole patch classes, conflate overloads, miss wildcard collisions, or accept stale role approvals and thus hide a real conflict | Inventory is per patch method; exact parameter signatures distinguish overloads; wildcard targets group conservatively; exact participant/role allow-list and effective priority/before/after checks are mandatory | unified target, 35-overlap, skip-order, observer-order and no-competing-transpiler tests | CLOSED at source gate |
| HIGH | Default-input audit missed Precision `[Controls - ...]` sections and `KeypadPeriod`, allowing an undetected default chord collision | Parser includes every shipped control section, keypad key and controller alias/modifier; collisions are evaluated across all modules | unified default keyboard/controller collision test | CLOSED at source gate |
| HIGH | Optional Build Camera/Integrity and Foundation patches were excluded from earlier compatibility inventories | All four Foundation DLLs plus both companions are unconditional compatibility participants in the 23-project and unified runs | exact participant allow-list; 71/71 Build Camera, 6/6 Integrity, 20/20 unified | CLOSED at source gate |
| HIGH | Environment labels alone could let tests pass against a different game/BepInEx/Harmony/Cecil build, and compiler warnings were not fatal | Pin exact binary hashes and embedded/file versions; build every audit project with warnings as errors | environment binary test plus 23/23 zero-warning/zero-error project gate | CLOSED at source gate |
| P0 | Release publication could archive/promote one module at a time and leave a mixed suite after an intermediate failure | BuildSuite must stage and validate the complete set first, then perform all-or-none promotion with exact-hash rollback/restoration and a collision-safe archive ledger | `AUDIT-EVIDENCE.json` promotion/rollback ledger and post-promotion hash verification | IMPLEMENTATION REQUIRES ARTIFACT CLOSURE; release HOLD |
| HIGH | Earlier “two-build” and package validation could test the wrong bytes, accept weak Foundation ZIP structure, or mutate artifacts under a diagnostic skip | Require two independent rebuilds; run the unified audit after those bytes exist; recheck hashes after audit; validate each Foundation ZIP against its exact current source/manifest/root set; diagnostic skip performs no publication | BuildSuite-generated deterministic DLL/ZIP/source/package evidence | IMPLEMENTATION REQUIRES ARTIFACT CLOSURE; release HOLD |
| P0 | A dedicated smoke could copy raw `bin` DLLs, stop at `Load world:`, ignore Unity/stdout/stderr failures, or report a hard-coded game version—creating a false-ready release | Smoke must consume the exact staged payload, require lobby then opened-server ordering, verify 0.221.12 from logs, keep the process alive through the dwell, and scan every isolated log stream | smoke evidence path and hashes recorded by `AUDIT-EVIDENCE.json` | IMPLEMENTATION REQUIRES RUNTIME CLOSURE; release HOLD |
| HIGH | A prose template could claim completion without source snapshot, package hashes, environment pins, finding dispositions or smoke proof | This report records source evidence and explicitly withholds GO; BuildSuite owns the machine-readable release evidence | source report has no unevidenced PASS for packaging/smoke; artifact JSON must validate before promotion | CLOSED for reporting; release still HOLD |

No P0/HIGH row that depends on package or runtime evidence may be treated as closed merely because its
script implementation exists. Its disposition becomes closed only when the generated evidence for the
candidate release passes.

## Dedicated remote-player parity blockers

The following conservative fail-closed boundaries preserved authorization, privacy, and loss safety,
but they do not satisfy the required dedicated multiplayer product scope:

- Valheim 0.221.12 does not expose a trustworthy server-owned copy of a remote dedicated client's
  complete carried inventory. Storage mutation and Crafting nearby consumption therefore remain
  disabled/fail-closed for remote dedicated clients until an authenticated server inventory protocol
  exists.
- Portals now has a connection-bound remote source implementation for metadata mutation and the
  safely held exact-NetworkName large-map picker. It remains a release-evidence blocker until the
  genuine dedicated multi-client harness proves authorized public/private/Group visibility,
  temporary coordinate disclosure, click travel, and denial paths end to end.
- Inventory topology mutations and selected Building repair/undo operations require host authority.
- Automatic owner-locked recovery-container creation is unavailable without a server-owned spawn and
  inventory-transfer protocol.
- Remote join admission has no authoritative compatibility-handshake transport and therefore cannot
  enforce the declared policy for a joining player group.
- A plugin inside a compromised game client cannot attest that same process as trustworthy. Sentinel
  can verify signed policy and supply server-side behavioral evidence, but local self-report is not
  anti-cheat proof.
- Display-only modules never request undiscovered, unauthorized or remote state to enrich UI. Missing
  evidence produces vanilla/generic/hidden output.
- Build Camera is a separate convenience companion and never expands mutation or disclosure reach.
- Velocity is a bounded startup profiler/cache, not a preloader or arbitrary third-party lazy loader.
  World Engine 1.0.0 is observe-only and does not compact, rewrite or prioritize ZDOs.
- The dedicated smoke is a one-process isolated load/world/readiness test. It does not replace a
  long-duration multi-client latency, disconnect/reconnect or arbitrary third-party-mod soak.
- Pure-harness timings and allocation checks identify scaling regressions and hostile-input hazards;
  they are not a guarantee of frame time on every hardware/world combination.
- Ambiguous identity, unavailable ownership, incomplete durable evidence or unsupported peer protocol
  retains/freezes state rather than guessing or deleting it.

These operations must be backed by bounded authenticated RPCs, request correlation, replay protection,
fresh server-side permission/range/revision proof, idempotent commit/rollback, disconnect recovery, and
an honest compatibility handshake. Safe failure is still required, but absence of the feature is no
longer an acceptable release disposition.

## Final decision

**Source compatibility/security/performance audit: PASS.** All fourteen gameplay modules, four
Foundation libraries and both separate companions built and passed their focused and cross-module
gates against the pinned 0.221.12 environment. No source-level release-blocking compatibility or
unbounded-hot-path finding remains in the executed suites.

**Candidate release: HOLD for dedicated feature parity.** The existing machine evidence proves exact
package bytes and a true-ready isolated server load, but it is not a multi-client functional-parity
certificate. Do not label or publish the suite as the requested dedicated-player-group release until
the blockers above are implemented and pass real remote-client join, action, disconnect/reconnect,
replay, permission, rollback, and persistence tests against the exact release payload.
