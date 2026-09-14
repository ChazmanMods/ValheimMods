# Runic Storage verification

## Quick Stack regression - 1.2.4

- The supplied 2026-09-14 game log contains eight Storage exceptions with "The source stack changed before publication", including one immediately after a successful Wood x50 transfer.
- Inspected the installed Valheim Inventory.RemoveItem(ItemData, int) IL: full removal delegates to RemoveItem(ItemData), retaining the detached item's count. Corrected the test stub to match this contract.
- Added an executable regression for partial transfer, full removal with a nonzero detached count, source-membership routing checks, rejection after cell reuse without inventory callbacks, and successful transfer of subsequent items.
- Automated tests: 54/54 passed. User confirmed in-game on 2026-09-14: "That seems to have fixed it." This records user-reported acceptance of the Quick Stack fix, not independent execution of every checklist below.
- Final Thunderstore preparation retains version 1.2.4 and the tested DLL, SHA-256 `EC1F76F312EF136C32334D45FD880D4ADB27AC60A211D14751A8729094AF979F`. The package includes the full changelog history. No runtime changes were made after confirmation.
- In-game check: carry unprotected Wood plus a second resource; configure one nearby chest to Always accept Wood and another with remembered Wood. Press Alt-Q once. All eligible items should move without the exception. Repeat with the first chest nearly full to verify the remaining Wood reaches the next eligible chest. Check that equipped/protected items stay put.

## Release confirmation - 1.2.3

- User confirmed in-game: "RunicStorage 1.2.3 works in-game. Button it up for Thunderstore."
- Recorded 2026-09-14. This is user-reported in-game acceptance, not a claim that every historical checklist below was independently executed.
- Release build previously passed with zero warnings/errors; automated suite passed 53/53. The shared input guard also passed the executable HarmonyX regression harness.
- Release preparation changes documentation and package metadata only. The confirmed DLL is retained: SHA-256 `1A1F146E6E1A132094E2AED12A599E5F3A5C3BD6F7592CC6C121C232D41E8231`.
- Version remains 1.2.3. The Thunderstore ZIP includes the full changelog history, including 1.1.0. Uploading/publishing is separate from local package preparation.

Historical verification notes and repeatable checklists follow.

Run the focused deterministic suite from the repository root:

```powershell
dotnet run --project RunicStorage\Tests\RunicStorage.Tests.csproj -c Release
```

The focused tests cover nearest eligible planning, protected-source filtering, partial capacity,
bounded query coverage and spatial cells, action/UI routing, local native ownership requirements,
controller sessions, diagnostics, hover disclosure evidence, optional Inventory absence, and static
standalone-architecture checks.

Manual checks for this repair:

- With a chest open, use Alt+A and Alt+R; with it closed, use Alt+C and Alt+R. Valid carried items
  must not fail merely because Inventory returned a fresh native list object.
- Use Alt+F near several chests, immediately type in the focused filter, click an item, and verify
  every matching chest receives the animated yellow highlight while nonmatching chests do not;
  clicking the filter, an item, Clear, or Close must not punch, swing, or use the equipped tool.
- While Alt+F is open, change `Search.MenuFontSize` and choose several named values from the
  `Search.MenuFontColor` dropdown in Configuration Manager. Verify the title and every label, text
  field, action button, item row, and empty-result message update; at size 32, controls and rows
  must grow vertically without clipping their text. Close and reopen with `MenuFontColor = Blue`;
  the text must remain blue—native-theme reconstruction must not overwrite the persisted choice.
- Compare the Alt+F picker with Valheim's inventory and split-stack dialog. It must use the actual
  live panel Sprite, button states, borders, TMP font, text-input art, scrollbar art, and UI scale.
  It must never show a solid/neon-yellow atlas region around the window. Repeat in bright midday
  sunlight and a dark interior; the scene scrim, opaque panel, and inset list must keep the configured
  text readable. Hover and press Clear, Close, and item rows to verify native button states.
- With a UI replacement mod that removes or swaps an Inventory panel/button sprite, reopen Alt+F.
  The picker must rebuild for the changed live source or use its opaque brown native-control fallback;
  filtering, clicking, scrolling, cursor restoration, and attack suppression must remain unchanged.
- Select a nearby item and verify both yellow rings animate around every matching chest for the
  full highlight lifetime without `StorageSearchHighlight.UpdateVisuals` exceptions. Destroying or
  unloading a highlighted chest must remove its marker without repeated Unity log errors.
- Close the picker with Escape and verify movement/input and the cursor return to their prior state.

Build the plugin with:

```powershell
dotnet build RunicStorage\RunicStorage.csproj -c Release
```

Before release packaging, the suite-level acceptance run also verifies a dedicated-server client
path where owned player/container mutations succeed and non-owner mutations fail closed. Historical
artifacts under `Tests/LiveEvidence` are retained as reference material; they are not a substitute for
the current standalone acceptance run.

The architecture checks require:

- only BepInEx in the package manifest;
- no runtime project reference or BepInEx dependency on another Runic mod;
- no Storage transfer RPC, durable saga, WAL/journal, quarantine, or suite-global mutation gate;
- native player and container ownership at every mutation entry point; and
- Inventory item protection remaining optional.

## Chest surfaces and lid attachment - 1.2.3

- Clean Release build and 53/53 automated tests passed. Existing modal input code is unchanged.
- Inspected the installed `piece_chest_wood.prefab` with UnityPy: the inactive snow cap, body mesh/collider, and separate `m_open`/`m_closed` lid objects are distinct. The two lid objects share a mesh; the open variant translates -0.392 on chest-local Z. The body and both lids have 180-degree local Y rotation and 0.9 scale. Regression fixtures use their extracted bounds.
- New tests cover outward-facing face axes, top readability from the front, actual wooden-chest body/lid positions, open-lid displacement and rotated placements. Installed-game contract checks verify the native open/closed GameObject fields.
- User subsequently confirmed 1.2.3 works in-game; see the release confirmation above. Repeatable adjacent-chest check: load existing Wood and Meadows labels, view closed chests from their fronts, and open/close each separately. Labels must stay on their own selected faces; top text must remain with its lid. Reopen Rules and Save while the chest is open; placement after closing must be identical.
- Repeat with all five faces, a rotated chest, nonzero offsets, white/black backgrounds, and another chest model. For modded lid variants with unrelated meshes, only the recognized closed-lid attachment is shown rather than guessing an open-lid placement.

## Label scale and modal input - 1.2.2

- Clean Release build; 51/51 automated tests pass, including biome-only automatic labels and checks against installed PlayerController, Player.SetControls, GameCamera and ZInput signatures.
- `Tests/InputHarness/InputHarness.csproj` runs the production shared input patches with the installed BepInEx HarmonyX runtime against managed stand-ins. PASS: keyboard/controller movement gates, local-only Player gate, already-sampled control neutralization, Use/movement/attack actions, typed-key shortcut suppression, preserved cancel controls and UI wheel/pointer reads, blocked camera zoom/look/sticks, nested camera exception cleanup, and close recovery.
- This is not an in-game rendering test. User verification is still needed: open Rules, select Only from biome > Meadows, clear custom text, choose Top and Save; confirm a readable Meadows label instead of a dot. Repeat with an explicit caption, Front placement and contrasting background.
- With an editor open, type all movement-bound keys (including a rebound E), scroll, move the mouse and use controller sticks. The character and camera must not respond; text and list scrolling must work. Escape should close a dropdown first, then the editor; ordinary movement/zoom should work again afterward.
- Test RunicStorage Rules and Search independently, including Search with the inventory closed. The shared source is currently wired to these Storage editors; other standalone mod editors require their own integration/build.

## Chest editor clarity and backgrounds - 1.2.1

- Release build: zero warnings/errors. Automated suite: 49/49 passed.
- Added palette round trips, case/alias handling, spelling and RGB approximation, formatting injection rejection, exact v2 metadata migration, all background values and invalid-background rejection. Existing v1 migration, routing, exclusion and transfer/protection tests also pass.
- In-game visual checks remain pending. This build was not installed into a running profile by the packaging task.
- Open wooden, reinforced and larger/modded chests at normal and high UI scales. Confirm vanilla Take All and Stack are unobstructed and usable; the Rules footer below the panel must remain onscreen.
- Hover every button, field and dynamic list entry; verify help is readable, stays onscreen, disappears on leaving/closing, and never intercepts clicks. Verify keyboard focus help too.
- Toggle Normal / Preferred and read the changing explanation. With two equally specific matching chests, Preferred is tried first; an excluded item still never enters that chest.
- Open text-color choices; choose White, Black, Orange, and Cyan. Type `ornage`, `gold`, and a near-red hex color; verify the resulting supported name, feedback and preview. Missing item translations should show prefab IDs rather than blank rows.
- Preview and save Transparent / White / Black backgrounds on all five faces of rotated chests. Verify depth, text contrast, size, offsets, reopen persistence and a second updated client's view. Check Cancel leaves chest settings unchanged.
- Escape should first dismiss an open color/background menu, then close the editor. No click should also attack/use an item.
- Reopen old 1.1.0/1.2.0 chests and verify rules, custom groups, memory and original label settings survive migration. Use 1.2.1 on participating clients after saving the new format.

## Chest rules and exterior labels - 1.1.0

Release build: zero warnings/errors. All 41 automated tests pass, including memory persistence after
emptying, edited/disabled memory, exact-item distinctions, food-stat boundaries, destination priority,
all five label-setting round trips, malformed metadata, bounded memory, and a guarded transfer into
an empty remembered chest. Existing transfer rollback, capacity, ownership, and protection tests pass.
The installed Container.Save hook is checked against the game assembly.

No live Unity UI/visual or multi-peer playtest was performed during this implementation pass.
In-game acceptance still needs:
- Open a wooden/reinforced/black-metal chest. Verify the Rules button and editor fit at the chosen UI scale.
- Enable Remember contents with Wood inside, save, empty the chest, then Quick Stack Wood; repeat after world reload.
- Add Finewood as an exact rule. Verify Wood does not enter even if already present. Clear rules to restore normal matching.
- Exercise Health/Stamina/Balanced/Eitr Food categories, full destinations, ward denial, and locked player items.
- Edit/remove remembered items, disable learning, and verify Cancel never changes persisted rules.
- Select all five label faces on rotated chests; adjust offsets and size; enter named and hex colors.
- Verify another modded client sees saved labels/rules, stale editors cannot overwrite newer edits, and labels disappear with the chest.
- Type inventory/chat shortcut characters into fields; close with Escape/Cancel/Save and verify no attack or unintended item transfer occurs.

The ZIP is prepared locally; no live profile, world, or Thunderstore listing was changed.

## Expanded groups - 1.2.0

- Clean Release build, zero warnings/errors; all 46 automated checks passed.
- New coverage: Never/Always precedence, independent biome filtering, group union, exact/narrow/broad
  ordering, Preferred tie breaking, remembered/current fallback, modded type-based classification,
  all eight biome designations, overlapping biome membership, custom group snapshot independence,
  label/rule separation, and lossless schema-1 food/memory/label migration to schema 2.
- Every curated prefab identifier is checked against the installed SoftRef manifest. Native prefab
  extraction confirmed arrow/bolt ammo tags, mead consumable/effect properties, and AncientCoin value.
- No live multiplayer or Unity UI test was performed in this pass. In-game checks still required:
  edit/reuse/delete a custom template across two chests; verify existing snapshots remain unchanged;
  change chest priority and compare exact, narrow and broad destinations at different distances;
  combine Ores/Metal Bars with Only from biome; verify Never accept and Always accept exceptions;
  reopen existing 1.1.0 chests and verify memory, labels, and colors remain intact.
- No live deployment or publication performed.
