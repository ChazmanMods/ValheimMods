# Changelog

## 1.2.4 - 2026-09-14

- Fixed Quick Stack stopping with "The source stack changed before publication" after successfully moving a full stack into a rule-based or remembered chest. Valheim retains the removed item's old stack count; Quick Stack now checks that the item still belongs to the source inventory before trying another destination.
- Partial stacks still continue to other matching chests, and fully moved stacks no longer interrupt the remaining items in the same action.
- Added an early rejection for stale source references before any transfer or rollback. Existing ownership checks, protected slots, exact rollback, chest rules and labels remain intact.

## 1.2.3 - 2026-09-14

- Fixed chest labels appearing behind a chest or inside an adjacent open chest. Placement now uses the chest body and its own lid rather than temporary open-state bounds.
- Top labels stay attached to their matching lids when chests open and close. Saving while a chest is open no longer offsets the closed-chest label.
- Corrected Front, Back, Left and Right placement, and oriented top text to read from the chest's front.
- Preserved existing saved rules, captions, colors, backgrounds and placement settings. Labels rebuild with corrected placement when loaded.

## 1.2.2 - 2026-09-14

- Fixed exterior labels appearing as a tiny dot. Labels now use pixel-sized uGUI text on a world-space canvas, instead of combining a 3D text component's font scale with an additional tiny transform scale. Background and text share one canvas with the backing drawn first.
- Added biome-only filters to automatic captions: selecting Only from biome > Meadows now enables the label and displays Meadows when no custom caption is entered.
- Fixed movement while editing Rules or Search. Valheim's PlayerController.TakeInput path is now blocked as well as Player.TakeInput, and controls sampled before the panel opens are neutralized.
- Fixed typed letters triggering Use, movement bindings or other ZInput keyboard shortcuts behind the editor. Text and pointer input still reach the UI; Escape/controller Cancel remain available.
- Blocked camera wheel zoom, mouse look, stick input and camera action buttons while a Storage editor is open. Camera input is masked only while camera code is running, so UI scrolling remains available. Closing frames are consumed before gameplay resumes.
- Added a shared input-guard source for reuse by other Runic editors, plus executable HarmonyX regressions and installed-game signature checks. This release connects the guard to RunicStorage Rules and Search.

## 1.2.1 - 2026-09-14

- Fixed the Rules launcher covering vanilla Quick Stack by moving it to a separate footer below the chest panel. Vanilla Stack remains available with its original behavior; chest rules apply to Runic Quick Stack.
- Replaced the confusing, unchanging "Never accept wins" message with an exclusion explanation and a priority explanation that changes between Normal and Preferred. Preferred resolves equally specific destinations; it cannot override an exclusion or a more specific matching rule.
- Added hover and keyboard-focus help to every editor button, item/group entry, color/background choice, and text field, including what Clear, Save, Cancel, and custom-group actions preserve or change.
- Replaced raw color-code entry with a supported Unity named-color picker and optional typed names. Familiar unsupported colors approximate by RGB; unknown names approximate by spelling, with the selected name and hex value shown. The mod generates the color tag and prevents captions from injecting formatting.
- Added separate Transparent, White, and Black label backgrounds, with a preview and saved per-chest settings. Existing 1.1.0 and 1.2.0 metadata loads with transparent backgrounds.
- Fixed blank/untranslated item rows by falling back to item IDs when a usable localized name is unavailable.

## 1.2.0 - 2026-09-14

- Expanded Quick Stack groups to equipment, ammunition, consumables, resources, trophies, valuables, crafting ingredients, and all eight requested biomes, including Deep North.
- Added stable group IDs independent of labels, reusable custom group templates, and chest-local group definitions shared with other players.
- Added Always accept item exceptions, Never accept exclusions, an independent Only from biome filter, and Normal/Preferred chest priority.
- Routing now prefers exact items over narrow groups, broad groups, remembered items, and existing contents. Priority and distance resolve equally specific destinations; exclusions, permissions, capacity, and player protection still apply.
- Migrated existing food rules, remembered contents, and exterior label settings to the expanded saved format. Corrupt or unknown rule data remains protected from overwrite.
- Verified curated prefab identifiers against the installed Valheim item manifest and ammunition/potion classification against native item data.

## 1.1.0 - 2026-09-14

- Added per-chest Remember contents so Quick Stack can refill a chest after its last matching item is used, without reserving an unusable item.
- Added a Quick Stack Rules editor to the open chest panel, with searchable exact-item selection, food categories, and editable remembered contents.
- Exact-item rules take priority over category rules; explicit rules override remembered/current contents. Existing range, capacity, ownership, and player-item protection checks remain in use.
- Added automatic exterior text labels with Front, Back, Left, Right, and Top placement, color names or hex values, adjustable size/offsets, and custom captions.
- Stored bounded rule and label data on the chest's native network object. Saving rechecks chest access and rejects stale edits.

## 1.0.6 - 2026-09-10

- Fixed opened-chest storage actions being rejected because the chest-busy flag was read incorrectly.
- Fixed chest hover contents disappearing because of Valheim's normal durability rounding during loading.

## 1.0.5 - 2026-09-10

- Fixed worn tools or other durability-bearing items blocking Quick Stack and other storage actions because Valheim 1.0 rounds durability during save/load.
- Transfer previews and rollback now use exact in-memory item copies, retaining full durability precision, custom data, and original equipped-item references.
- Synchronized chest validation accepts only the exact durability conversion made by one native load/save; changes to quantities, item identities, slots, and metadata still fail validation.
- Quick Stack distinguishes no capacity, ownership changes, unavailable chests, and snapshot failures. Temporarily unavailable public chests get a bounded discovery retry.
- Personal chests and denied ward/native access remain protected. Public chests need no separate authorization list. Concurrent writes to an actively used chest remain blocked to prevent item loss.

## 1.0.4 - 2026-09-10

- Fixed nearby storage actions failing when another player owned the surrounding zone by safely
  claiming only accessible containers that Valheim reports as unused.
- Busy containers are never claimed or mutated, so an actively edited chest cannot be overwritten.
- Chest hover and Search can read synchronized persisted contents while another player has the
  chest open, without taking ownership or changing the container.

## 1.0.3 - 2026-09-09

- Updated split-dialog, item insertion, inventory notification, and exact metadata comparison paths for Valheim 1.0.
- Re-audited the installed Valheim 1.0.7 client and dedicated-server assemblies and updated the BepInEx dependency to 5.4.2350.

## 1.0.2 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.1.

## 1.0.1

- Fixed the first Alt+Q after world entry falsely reporting that no authorized chest was nearby
  when Valheim had not populated the loaded-container index yet. Quick Stack now performs a bounded,
  automatic 0.2-second discovery retry (up to eight times) and executes as soon as nearby chest
  identities finish synchronizing. Real range and authorization failures are still reported without
  being mistaken for this startup race.

## 1.0.0

- Fixed first-use Quick Stack, Restock, and Search incorrectly reporting that no authorized nearby
  chests existed until a chest had been opened. Each explicit action now reconciles Valheim's
  currently loaded containers before querying the bounded spatial index.

- Made Runic Storage independently installable with BepInEx as its only package dependency.
- Removed runtime coupling to Runic Core, Persistence, Permissions, Transactions, and Inventory.
- Replaced the dedicated transfer/composite protocol with Valheim's native local player and
  container ownership path.
- Removed remote transfer RPCs, capability registration, durable operation roots, client/server
  sagas, journals, global mutation gates, quarantine records, grave custody, and recovery polling.
- Added a Storage-local in-memory mutation lease and exact same-process preflight, save, and rollback
  checks.
- Kept Runic Inventory item protection as a reflection-only optional integration; Storage remains
  fully usable when Inventory is absent.
- Preserved the bounded spatial index, strict hover privacy and synchronization proof, action
  diagnostics, keyboard/controller routing, sorting, search, Store All, and consolidation behavior.
- Replaced Alt+F's fixed Wood lookup with a filterable catalog of item kinds in nearby eligible
  chests; selecting one highlights every matching chest with an animated yellow ring and light.
- Kept the Alt+F picker above Valheim's camera cursor capture, brought it to the front, and focused
  the filter so both mouse selection and immediate typing work.
- Prevented left-clicks owned by the Alt+F picker from also triggering Valheim's primary attack,
  including the release frame when selecting an item closes the picker.
- Added Configuration Manager-visible Alt+F menu font size and named-color dropdown settings, with
  bounded sizing, legacy hex-to-nearest-name migration, and larger control/item heights to prevent
  clipped text.
- Replaced the Alt+F picker's IMGUI/rasterized-atlas presentation with a native uGUI Canvas that
  directly references Valheim's live dialog panel, button states, text input, scrollbar, TMP font,
  and Canvas scale. This prevents a packed atlas region from rendering as a neon-yellow frame while
  preserving configured named font colors, font sizing, filtering, scrolling, focus, and attack
  suppression; missing native art receives an opaque Valheim-toned native-control fallback.
- Gave each search-highlight ring its own Unity child object and added fail-closed visual lifetime
  guards, preventing a missing ring or unloaded chest from producing an exception every frame.
- Allowed Restock to use the exact locally owned currently opened chest as well as eligible closed
  nearby chests, and allowed both Search and Restock to route while inventory/chest UI is open.
- Corrected open-chest mutation ownership selection and carried-item protection proof so Alt+A,
  Alt+C, and Alt+R do not fail solely because Valheim returned a fresh list wrapper.
- Replaced obsolete protocol tests with focused standalone, ownership, planner, routing, hover,
  and architecture regressions.
