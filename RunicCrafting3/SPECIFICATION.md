# RunicCrafting Gameplay and Implementation Specification

Version 3.0 | October 2, 2026 | Binding implementation contract for Codex

## 1. Product goal

RunicCrafting makes nearby permitted supplies feel like a natural extension of the player's inventory. Install it on the playing client, walk into a workshop, and craft, upgrade, build, cook, refuel, and repair through familiar controls. Players should spend time creating and playing rather than carrying stacks between chests or troubleshooting shared storage.

Client-side operation is the default. An optional dedicated-server component improves shared-resource coordination where supported and measured. Both modes provide the same player controls and intended gameplay features. Server support must not require every player to install the client plugin. Players without RunicCrafting retain ordinary game controls and chest access; server support does not give them the client interface features.

This specification defines a product to implement and validate. It does not establish that proposed native access or server coordination mechanisms already work. Codex must prove authorization, freshness, writer exclusion, and effect confirmation in the installed game before declaring support.

### Binding priorities

1. Correct materials and exactly one intended effect.
2. Responsive, enjoyable crafting and building in ordinary shared bases.
3. Client-only usability, including mixed installations.
4. Quiet automatic recovery from temporary contention and changing stock.
5. Familiar controls, readable information, and predictable cancellation.
6. Optional server support that measurably improves demanding shared use.
7. Compatibility and simple maintenance that preserve these benefits.

Neither sophisticated architecture nor passing simulated tests establishes excellent gameplay. Measure actual player-facing results. Safety failures and routinely frustrating interruptions are both release blockers.

## 2. Scope and independence

Included: nearby crafting and upgrading; batch crafting; material previews; one-click building with live aim; full-backpack ingredient use; manual cooking and machine input; manual refueling; equipment repair; optional area repair; protected ingredients; source preferences; optional workshop permissions; localization; compatibility adapters; optional dedicated-server coordination.

RunicStorage owns SmartSort, chest organization, deposits, quick stacking, restocking, chest rules, and storage search. RunicCrafting respects applicable storage policies through an integration contract. It must not implement its own sorting, automatic deposit, item grouping, or storage-management interface.

RunicProduction owns unattended production and automated feeding. RunicCrafting handles actions initiated by the player. Holding an interaction key may produce bounded manual actions while the player remains present; it must not become background automation.

RunicCrafting works independently of RunicStorage and RunicProduction. Integrations are optional and must not introduce dependency cycles. The optional server package must run without loading client UI classes.

Use only minimal verified mod-loader and patching dependencies. Any additional dependency needs a concrete capability justification. Do not introduce a server dependency into the client-only native-storage path.

## 3. Installation and capability contract

| Installation | Required experience |
| --- | --- |
| Single player with client plugin | All supported native actions and nearby supplies |
| Listen server with client plugin | Same controls; tested local and joining-player behavior |
| Unmodded dedicated server, only crafter modded | Supported native storage access; others continue normally |
| Several modded clients without server support | Safe competition through proven native or cooperating-owner paths |
| Dedicated server with add-on | Automatic use of advertised compatible coordination capabilities |
| Server add-on with vanilla players | Normal native access; mediated native writes tested |
| Add-on absent or incompatible | Proven client baseline remains available before spending |
| Storage with separate inventory authority | Verified adapter to that authority; never debit a display mirror |

Recommend server support based on observed contention and measured workload, not a fixed player count. A few active players can contend heavily; a larger dispersed group may not. Do not imply server support necessarily increases FPS or prevents conflicts from arbitrary inventory writers.

Auto-detect support through an authenticated, versioned capability exchange. Do not guess from assembly names. Report only capabilities actually available. Select a compatible path before mutation without holding the crafting menu hostage.

## 4. Required player experience

### Crafting and upgrading

Use the native menu, recipes, station requirements, discovery, animation, quality, variants, and output rules. Counts include permitted nearby materials. Carried eligible ingredients are consumed first by default, then preferred nearby sources in a stable order.

One click means one action. Prepare materials during the native animation where possible. Refresh and find alternate supplies if someone takes previewed stock. If supplies really run out, explain the exact deficit. Never require a separate fetch click.

Upgrades identify the exact selected item instance. Moving, replacing, or changing that item invalidates the pending upgrade safely. A same-name item must never be substituted accidentally. Preserve native durability and metadata behavior.

### Batch crafting

Provide a compact quantity selector, plus/minus, Max, and a modifier for larger steps. Initial batch cap is 100 crafts, configurable within tested bounds. Distinguish number of crafts from resulting quantity when a recipe produces multiple items.

Show the achievable amount considering usable ingredients, output capacity, and native conditions. Run individual native actions with fresh validation between them. Do not prepay the whole batch or hold chests throughout it. Display progress and offer immediate stop.

Completed crafts remain completed; cancellation stops uncommitted work. Stop on a genuine deficit, invalid station, death, menu close, or output capacity failure. Changing recipes stops the old batch. Prevent duplicate activation without hiding an invisible click queue or freezing unrelated controls.

### Building

One click prepares materials and places exactly one current valid preview. Aim, rotation, and snapping stay live during the brief wait. Ghost movement is not cancellation. Piece, tool, and intent identity stay fixed; final placement uses the current valid pose.

Cancel on explicit cancellation, piece/tool change, leaving build mode, death, teleport, world switch, or loss of required conditions. Never place later after cancellation. Recreated preview objects during normal updates must not invalidate a valid intent.

Full-backpack building works without visibly staging ingredients into slots. Preserve placement validity, required station, stamina, durability, and costs. Optional stationless exceptions require an explicit allowlist and default to disabled.

### Cooking, refueling, and manual input

Preserve normal controls and native accepted-item, fire, slot, capacity, process, and reach rules. Consume only the quantity actually accepted. Explicit rejection consumes nothing; timeout is not proof of rejection and cannot authorize speculative refund.

Preserve native item selection when available. Otherwise use readable player preferences and stable ordering. Do not silently consume valuable wood, food, equipment, or protected items when ordinary suitable input is available. Define action-specific fuel/input allowlists.

Hold-to-input starts the next bounded action only after the previous one resolves and the player remains at a valid target holding the key. Stop on release or full target. No unlimited delayed queue. Keep input and output collection distinct.

### Repair

Repair All repairs eligible equipment at the currently valid station using native rules. Optional Area Repair is a separate intentional action, disabled by default, restricted to permitted pieces and supported native behavior. Process larger areas within a frame budget. Preserve station requirements, access, and native costs. Play success feedback only after confirmed repair.

### Shared bases

Unmodded players open chests and move materials normally. Do not force visible chest panels, repeatedly close another player's interface, take over active access, or require installation by other players.

An idle native chest with another owner is not automatically excluded. Use verified native access respecting permissions. For busy sources, find a complete alternative plan first, then briefly wait where safe. Simultaneous viewing and consumption require an installed authority that mediates relevant writes; otherwise native exclusion may require a short defer.

## 5. Material information and protection

Show carried, usable nearby, total, and missing quantities. Keep the default display compact; expanded detail is optional. Do not expose denied private contents. Previews estimate availability; they do not reserve or authorize spend.

Busy stock cannot masquerade as immediately usable stock. Label separately if displayed. If search limits prevented a complete search, do not claim a definitive deficit.

Preserve native quality, variant, world-level, and alternative-ingredient matching. Plan restrictive exact costs before permissive alternatives so general costs do not consume the only item meeting an exact requirement. Use checked arithmetic and native output quantities.

Default protection excludes equipped items, explicit favorites/locks, and nonfungible equipment or custom-data-bearing items from ordinary ingredient matching. Recipes intentionally requiring such items need verified action-specific selection. Protect individual instances when supported. Handle unavailable protection integrations conservatively and disclose the precise limitation.

Recipe interchangeability differs from transport/return identity. Retain prefab, stack, quality, variant, durability, crafter information, world level, custom data, and supported extension fields. Display names and slot coordinates are not sufficient identity.

## 6. Source selection and discovery

Carried supplies come first unless explicitly configured otherwise. Rank nearby sources by player preference, availability, distance, and stable identity. Respect exclusion rules from RunicStorage through its supported integration. Ordinary native chests do not require enrollment.

Default material range is 20 meters from the player. Target reach and station coverage remain independent checks. Optional workshop policies may tighten range or define a different supply anchor; explain that setting. Never silently bypass progression or interaction reach.

Cache spatial discovery and counts. Invalidate on meaningful movement, source lifecycle, inventory/access changes, spend/return, station changes, and configuration changes. No full inventory scans or decoding each UI frame. Initial tuning: discovery refresh about 350 ms; preview refresh about 150 ms. Commit validation is always fresh.

Start with a nearest-first working set of 128 sources, expanding within measured frame budgets when necessary. Candidate and per-action limits must not silently turn available supplies into false missing-item claims.

Custom containers, large-count storage, mobile storage, and separate inventory authorities need explicit adapters. Exclude graves and character-carried storage by default; opt-in support requires access and identity tests. Readable does not mean writable.

## 7. Architecture

| Component | Responsibility |
| --- | --- |
| ClientBootstrap | Client lifecycle, owned patches, settings, localization |
| GameBindings | Audited native methods, serialization, supported-build checks |
| ActionCoordinator | Intent, animation, batches, cancellation, one effect |
| SourceIndex | Spatial discovery, cached previews, eligibility |
| MaterialPlanner | Exact costs, protected items, deterministic fresh plans |
| AccessBroker | Native, cooperating-owner, server, and adapter path selection |
| NativeAccess | Proven authorization, exclusion, freshness, release |
| SourceAdapters | Explicit actual-authority access/mutation contracts |
| MaterialCustody | Exact slices and confirmed unused returns |
| OutcomeResolver | Correlated status, deduplication, uncertainty |
| PolicyEvaluator | Native access plus optional tighter workshop rules |
| ServerCoordinator | Optional request ordering and authority integration |
| PlayerFeedback | Localized messages, progress, cancellation |
| Diagnostics | Bounded evidence, profiling, compatibility results |

Keep planning and transitions testable outside Unity. Mutations execute on the required game thread. Workers operate on immutable copies only. Use narrow owned patches; never unpatch unrelated plugins. Broad native replacements require a demonstrated necessity.

Pending intent lives in the coordinator, not a temporary thread-local scope across asynchronous waits. Virtual ingredient authorization exists only inside one audited native execution window. Suppress redundant deductions while preserving all other native checks. Guard recursive callbacks and clean scopes in finally blocks.

## 8. Client-side access proof

Before broad implementation, audit current native request/grant, ownership, in-use, load/save, replication, disconnect, destruction, and action-effect methods. Record exact build versions and observed sequences.

Before each debit establish permission, actual writer exclusion, and fresh authoritative inventory. A local owner flag, fingerprint, or client mutex alone is insufficient. Do not force ownership because a chest appears idle.

Use permission-respecting native authorization and short exclusive mutation windows where proven. Temporary native ownership transfer is allowed when needed and verified. Release logical access promptly; never forcibly restore an obsolete owner or acquire every chest while browsing.

Cooperating peers may optimize access but cannot replace exclusion of native writers. Absence of a custom handler falls back to the proven native path before mutation. Test with actual unmodified clients.

If a safe path cannot be established, leave the source class explicitly unsupported. Preserve carried ingredients and independent sources. Failure to meet ordinary mixed-installation native support remains an unresolved release blocker, not permission to change the promise silently or ship unsafe writes.

## 9. Optional dedicated-server support

Supply a separate installable package. Default mode is Auto: use verified compatible server coordination when available and beneficial, otherwise the proven client baseline. Players should not need to select protocols. An advanced diagnostic override may exist.

### Required behavior

- Authenticate requester session, player association, world, operation identity, and payload.
- Revalidate permissions, range, source/item identity, and quantity at the actual authority.
- Order competing mutations fairly with short holds and no starvation.
- Reduce repeated negotiation where measurements demonstrate benefit.
- Deduplicate retries and provide status for uncertain operations.
- Coordinate native open/close, move, stack, take-all, destruction, and supported integrations.
- Return condition-specific results and advertise actual supported source capabilities.

### Authority decision

First prove whether server coordination of existing owners can enforce exclusion across relevant native writes. Scheduling requests alone is not inventory authority. If managed server authority is necessary, prove native interaction mediation, persistence, and ownership behavior before enabling it. Choose the least intrusive proven design meeting gameplay goals.

Never maintain two writable copies of an inventory. Sources controlled by an existing authority use its adapter. Do not silently enroll all chests in a ledger, rewrite inventories, or take ownership of every zone. Any authority migration needs a separate explicit plan.

### Mode changes

Select a path before possible mutation and retain it for that operation. Server loss before debit allows safe baseline replanning. After possible debit, query the original operation instead of replaying locally. Reconnection must not repeat effects.

Ordinary settings changes apply to future intents. Never switch authority beneath unresolved work. Optional server policies are enforceable only where authenticated enforcement exists. Client-only policies are cooperative convenience, not security against malicious clients.

Optional installation does not justify incomplete authority. Release claims must identify which actions, native writes, and sources server support actually covers.

## 10. Action lifecycle

1. Capture action ID, recipe/piece, quantity, exact upgrade item, target, tool, session, and cancellation token. Building pose remains live.
2. Preview costs, carried deficits, and candidate sources without holding inventories.
3. Select the proven path and prepare access during animation or a short pending interval.
4. Acquire only needed sources in stable identity order. Avoid unnecessary holds while waiting.
5. Load fresh state and replan across the full eligible set when original sources no longer suffice.
6. Revalidate costs, permission, output capacity, target, station, exact item, tool, stamina, and cancellation immediately before spend.
7. Debit exact slices once through the actual authority; account for confirmed material custody where needed.
8. Apply the audited native effect once with no redundant resource deduction.
9. Confirm actual effect and accepted quantity; reconcile confirmed unused costs.
10. Release, refresh previews, complete feedback, and retire evidence safely.

Preparation may overlap animation; exclusive chest holds must not unnecessarily span animation or a batch.

| State | Permitted behavior |
| --- | --- |
| Planned / Acquiring | Replan, wait briefly, cancel, or switch safe path before debit |
| Ready | Fresh final validation, debit once, or release |
| Spending | Resolve the same operation; no blind new debit |
| Materials confirmed | Apply effect once or return confirmed unused slices |
| Applying effect | Confirm; no speculative replay/refund |
| Completed | Release and refresh |
| Returning | Resolve the same return once |
| Uncertain | Preserve evidence, isolate obligation, query original authority |
| Cancelled before spend / Confirmed returned | Terminal; unrelated actions remain usable |

## 11. Accounting and recovery invariants

No negative stock, output without required cost, doubled cost, or duplicate effect. Every confirmed slice is consumed, held, or returned exactly once. Partial results are explicit.

Never restore whole inventory snapshots over unrelated changes. Compensate exact confirmed unused slices only when evidence establishes no corresponding effect occurred. Preserve metadata and never overwrite occupied slots. If a confirmed return cannot fit, retain explicit custody and a controlled return path. Do not discard materials or create speculative drops.

Unknown outcome is not successful cancellation. Isolate affected operation/source rather than globally blocking crafting. Process crash, independent save restores, and unsupported writers are separate durability limits. Never claim crash-proof cross-inventory transactions without proven persistence.

Clean access on success, failure, cancellation, timeout, death, teleport, menu close, and unload without closing another player's active access. Retain unresolved evidence needed for later status; cleanup does not authorize refund.

## 12. Requests and adapters

Operation identity contains world, session nonce, action ID, source, phase, and immutable payload identity. Reject changed payload under the same identity. Duplicate replies cannot duplicate effects. Bound queues and payloads. Evidence retention must cover legitimate retries, while retired/session-invalid messages cannot execute.

Authority timing governs leases. Expiry before spend permits clean release; expiry after commit cannot erase replay-prevention evidence. Random request numbers alone are not an idempotency design.

Expose preview, acquisition, freshness evidence, debit, status, confirmed return, and release separately. Results distinguish Busy, Denied, Unsupported, StaleBeforeSpend, Confirmed, ConfirmedPartial, and Unknown. Never guess authority from a native-looking component.

Exact debit binds source, expected item identity/quantity, and required revision or equivalent evidence. Slot alone cannot authorize taking whichever item occupies it later. Large-count sources use checked arithmetic and bounded native-sized slices, preserving assigned identity at zero when required.

## 13. Feedback and settings

Successful recovery is silent. Initially show Getting nearby materials... after 250 ms. Budget approximately 1,200 ms for ordinary predebit recovery and two fresh replans, then tune using measurements. A timeout after possible mutation enters resolution. Actual target protocols may need longer confirmation.

| Verified condition | Message |
| --- | --- |
| Complete fresh search finds deficit | You need {count} more {item}. |
| Necessary source remains busy after alternatives | {chest} is busy. Try again in a moment. |
| Output cannot fit | Make room in your inventory for {item}. |
| Range invalid before spend | Move closer to your supplies. |
| Station invalid | Move into range of the required crafting station. |
| Necessary source denies access | You do not have permission to use {chest}. |
| Supplies changed, confirmed zero spend | Nearby supplies changed. Nothing was used. |
| Possible effect unresolved | Checking whether {action} finished... |
| Automatic confirmation exhausted | Runic could not confirm this action. Other supplies still work. |
| Confirmed unused return cannot fit | Make room to receive your unused materials. |

Use natural localized wording with English fallback. Do not show ownership, revision, or protocol jargon during routine play. Nothing was used requires zero-spend evidence. Keep unresolved details inspectable; avoid repeated screen spam. One outcome notification per intent and duplicate-toast throttling.

Settings expose ranges, sourcing preference, protection, batches, input allowlists, repair, and optional policies. Validate reloads, retain last valid settings, and apply changes to future intents. Recheck tightened permissions before mutation. Server settings govern only capabilities actually controlled by the server.

## 14. Performance targets

Measure on a documented game build, machine, source workload, and network:

- Warm preview/hammer logic adds less than 0.5 ms at p95 with 128 sources and no steady preview allocations per frame.
- Refresh adds less than 3 ms at p95 or is spread across frames without visible stalls.
- Uncontended LAN or about 50 ms RTT: at least 99% of eligible scripted actions complete without another click or failure message; report sample count.
- Additional uncontended material readiness is below 250 ms at p95; report animation overlap separately.
- One hour mixed-base play has no Runic-required relog, permanent busy gate, unintended placement, or accounting error.
- Compare client-only and server-supported busy-base runs for p50/p95 latency, retries, interruptions, negotiation traffic, client/server CPU cost, and native chest responsiveness.

Do not advertise performance improvement without a controlled comparison demonstrating it. Server support must not materially degrade ordinary uncontended play. Separate safe deferrals from successful uninterrupted actions. Safety tests do not excuse poor usability, and speed does not excuse unsafe spending.

## 15. Compatibility and original implementation

Write original code based on audited game behavior and these functional requirements. Do not copy external implementations. Product documentation and player UI need no external author or mod references. Preserve required legal notices for dependencies actually used.

RunicStorage supplies supported authority and policy/protection interfaces. RunicProduction consumption integrates through a verified shared authority. Neither may apply duplicate cost patches. Test expanded inventories, custom recipes, alternate costs, and custom storage through targeted adapters. Disable only demonstrated unsupported capabilities.

Audit identifiers, patch ownership, config semantics, and conflicting DLLs. Avoid global unpatching, per-frame reflection, leaked subscriptions, retained world objects, or compatibility claims based only on assembly presence.

Preserve existing public packages, source snapshots, configurations, characters, worlds, and authority-managed inventories. Work in an isolated branch/project. Local implementation, builds, tests, and packaging are authorized. Publishing and destructive migration require separate instructions.

## 16. Verification matrix

| Scenario | Required finding |
| --- | --- |
| Sole modded crafter, vanilla owner, unmodded dedicated server | Idle permitted chest safely usable |
| Competing crafters consume last ingredients | Only affordable effects; no duplicate cost or stock |
| Vanilla player opens/moves/takes all during preparation | Normal interaction preserved; safe order or defer |
| Inventory replication follows grant | No stale debit |
| Ownership changes at each spend boundary | Old writes cannot overwrite fresh committed state |
| Disconnect, unload, or chest destruction | Accounted costs; bounded recovery; no speculative refund |
| Original source depleted, alternatives available | Same intent completes from fresh wider plan |
| Full backpack | Ingredients need no visible staging; output capacity respected |
| Quality, variant, custom data, and locked items | Exact matching and protection |
| Upgrade selection changes | No wrong-instance upgrade |
| Batch stop or deficit | Completed crafts retained; no hidden prepaid queue |
| Aim/rotation changes while building waits | Current valid pose once |
| Piece/tool change, cancel, death, or teleport | No delayed effect |
| Target partial acceptance, rejection, or lost reply | Exact accepted cost; no blind retry/refund |
| Duplicate, delayed, reordered, and dropped messages | Idempotent operation and effect |
| Slot item changes | Expected identity checked |
| Add-on absent, incompatible, lost, or reconnected | Safe fallback before spend; original resolution after |
| Server support with vanilla writers | Actual mediation of advertised native writes |
| RunicStorage/RunicProduction present and absent | Independence; no second authority |
| Custom storage and expanded inventory | Correct adapter, capacity, metadata, and returns |
| Permissions tighten mid-action | No newly denied mutation |
| Unload, character switch, relog | No old replay; clean lifecycle |
| Repair and invalid targets | Native rules; truthful success |
| Large base and sustained competition | Stable frames, fair ordering, measured server benefit |
| One-hour ordinary shared play | Pleasant repeated use without relog workaround |

Deterministic planning and failure-boundary tests precede actual separate game clients. Test dedicated and listen servers, including a genuinely unmodified client, latency, disconnects, and message faults. Record pre/post exact inventories, costs, effects, and timing. Simulations and static inspection cannot replace in-game evidence.

## 17. Delivery order and release gates

1. Audit game/repository, document feature baseline, preserve releases.
2. Prove client native access with unmodified owners, freshness, and competing writes.
3. Implement index, planner, protections, state machine, and outcome handling.
4. Deliver crafting/upgrading, truthful previews, and cancellable batches.
5. Deliver live-aim building and full-backpack ingredients.
6. Deliver manual input with observable acceptance, then repair.
7. Prove server authority integration and safe transitions; implement optional support.
8. Add optional integrations, settings, policies, and localization without degrading independence.
9. Complete multiplayer acceptance, performance comparison, and gameplay tuning.
10. Package client and server locally and report readiness. Do not publish automatically.

Required outputs: original source; repeatable builds; separate client/server packages; feature traceability; native access proofs; authority/outcome contracts; compatibility matrix; translations; meaningful tests; actual multiplayer evidence; gameplay performance report; installation instructions; explicit limitations.

Release requires correct accounting, supported mixed native access, responsiveness, no known loss/duplication in tested supported live-session paths, native permission preservation, cancellation correctness, no routine relog, and verified server claims. Unproven promised features remain incomplete.

## 18. Player installation description

Publish these claims only once tested:

> Install RunicCrafting on your client to craft, upgrade, build, cook, and refuel from accessible nearby supplies. Other players can continue using their normal controls without installing it.
>
> Dedicated-server owners can optionally install server support for improved coordination in busy shared bases. RunicCrafting detects compatible support automatically, and your controls stay the same.
>
> When supplies are being used, Runic looks for alternatives or briefly waits. Storage with a separate inventory system needs a supported integration.

## 19. Binding instructions to Codex

Implement a coherent original design prioritizing successful intended actions, responsive menus, live building aim, convenient batches, correct materials, quiet recovery, and clear rare failures.

Client-side operation is the priority. Prove access with unmodified peers rather than assuming custom handlers. Build optional server support that measurably improves shared use and genuinely integrates native writers. A scheduler, owner flag, cached preview, or simulated acknowledgment is not authority proof.

Keep SmartSort/storage management in RunicStorage and unattended automation in RunicProduction. Integrations remain optional. Preserve the same RunicCrafting controls in both modes.

Continue through authorized local implementation, verification, profiling, and packaging. Preserve releases and worlds; do not publish. If essential capability cannot be proven, report its exact mechanism and player impact and keep it incomplete. Never substitute unsafe writes or unsupported promises.
