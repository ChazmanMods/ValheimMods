# Runic Storage

**Version 1.2.4**

A large Valheim base eventually contains enough identical chests that storing loot becomes its own memory game. You know the item exists somewhere; finding it, distributing a new haul, and rebuilding your carried supplies are the tedious parts.

**Runic Storage turns a room full of boxes into storage you can actually use.** See what a chest contains, move matching items where they belong, restock what you carry, and search across eligible nearby containers.

## Major features

- Preview useful chest contents before opening the container.
- Quick-stack carried items into nearby matching storage.
- Remember chest contents and route Quick Stack using exact items, groups, biome filters and exclusions.
- Add named, colored labels to chest faces and lids, with optional white or black backgrounds.
- Store all into the opened chest and restock configured supplies.
- Search nearby storage and highlight every matching chest.
- Sort containers and consolidate compatible partial stacks.

## How it feels in-game

Returning from an expedition becomes unload, restock, and go. You spend less time opening every chest, dragging repeated stacks, or trying to remember where the iron ended up, while the containers in your base remain real Valheim storage.

## Safety and compatibility

Storage acts only on loaded, accessible, synchronized containers. For mutations it uses Valheim's
native ownership claim only after both the local state and synchronized ZDO state prove the chest is
unused; a chest another player is actively editing is never claimed or changed. Read-only hover and
Search can still display its synchronized persisted contents. Wards and ordinary chest access remain
authoritative, protected or locked items are respected when optional integrations are present, and
ambiguous state aborts the enhanced action. The mod is independently installable with BepInEx.

## Features

- **Chest contents on hover** adds a deterministic, localized, bounded summary to eligible closed
  containers.
- **Quick Stack** (`Left Alt + Q`) moves eligible carried items into nearby containers using saved
  chest rules, remembered item types, or ordinary matching contents.
- **Restock** (`Left Alt + R`) pulls configured items up to their target quantities, whether a chest
  is currently open or the player is standing near closed chests.
- **Search** (`Left Alt + F`) opens a filterable list of item kinds found in eligible nearby chests.
  Selecting an item gives every matching chest a bright animated yellow ring and light for 15
  seconds. The picker takes a renewable cursor lease, opens with keyboard focus in the filter, and
  owns its left-mouse clicks so selecting, filtering, or closing it cannot also punch, swing, or use
  the equipped tool. It yields control back to Valheim after the captured click is fully released.
  The complete window's font size (`Search.MenuFontSize`, 10–32) and named color
  (`Search.MenuFontColor`) are live Configuration Manager settings. Color is a dropdown containing
  Light Gray, White, Gold, Yellow, Orange, Red, Pink, Purple, Blue, Cyan, Turquoise, Green, and
  Lime; no color code is required. Larger sizes also enlarge the window, fields, action buttons,
  item rows, and title clearance. The picker is a native Unity Canvas built with Valheim's live
  inventory/dialog panel Sprite, button Sprite and states, scrollbar art, text-input art, TMP font,
  and Canvas scaling. Those assets remain native Sprite references; they are not copied out of the
  packed UI atlas. A dark scene scrim and opaque inset keep the list readable in full daylight and
  dark interiors. If a UI replacement mod removes one of those sources, an opaque brown Valheim-
  toned native `Image` fallback keeps the same layout and behavior. A persisted named font choice
  remains authoritative when the native view is rebuilt.
- **Sort opened container** (`Left Alt + S`) applies the configured ordering while preserving locked
  slots.
- **Store All** (`Left Alt + A`) moves eligible carried items into the exact currently opened chest.
- **Consolidate carried stacks** (`Left Alt + C`) merges serialization-compatible stacks while
  respecting protected cells and equipped or quest items.

Controller shortcuts use Valheim's named `ZInput` actions and remain configurable. Hold
`JoyAltKeys` and use D-pad Down for Quick Stack, Up for Restock, Right for Search, Left for
Consolidate, or `JoyRStick` to sort the opened container. Invalid or ambiguous bindings fail closed.

## Multiplayer behavior

Storage uses Valheim's native ownership model. A mutation is allowed only when the active player is
the local native owner and every container being changed is locally owned, accessible, synchronized,
not loading, and not otherwise in use. This same rule applies in solo, listen-server, and dedicated-
server client sessions. Storage claims accessible idle containers through Valheim's native ownership
mechanism before changing them. An open or unsynchronized chest is skipped; other eligible chests
can still be used. Native network ownership is not the same as the player who built a chest.

Actions use a small process-local Storage lease so two Storage actions cannot interleave. Each move
preflights exact shadow inventories, captures exact backups, saves both endpoints, and attempts exact
rollback on an ordinary synchronous failure. Storage does not provide cross-process or crash-atomic
transactions, RPC transfer protocols, journals, global suite locks, quarantine records, or recovery
sagas.

Search and hover do not mutate container contents. They require vanilla access and verified inventory
data. Hover accepts Valheim's single load/save durability rounding, but not changed item quantities
or unrelated payload differences.
Hover remains bounded by physical player reach, strict ward access, snapshot size, stack count,
label length, output length, retry, and cache ceilings. It never opens a container or takes ownership.

Each animated search ring uses its own Unity child object. If a chest or another mod invalidates a
ring, light, or visual root during its lifetime, the visual-only marker disables and removes itself
once instead of throwing on every frame.

Nearby discovery uses an event-maintained 10 m spatial-cell index with a hard 50 m radius and
256-candidate ceiling. Personal, denied, busy, invalid, and unverifiable containers are excluded.

Store All and Consolidate accept freshly returned native inventory item lists as long as every item
reference still matches exactly. With Runic Inventory installed, a belt/equipment transition that
temporarily invalidates the special row recovers after the next valid snapshot instead of leaving
Storage permanently unable to prove carried-item protection.

## Optional Runic Inventory integration

If Runic Inventory is installed, Storage discovers its public item-protection API by reflection and
honors locked or protected carried items. If Inventory is absent, Storage continues with its own
equipped-item, quest-item, and hotbar protections. A present but incompatible or indeterminate
Inventory provider fails closed for the affected action. Neither plugin loads the other at runtime.

Configuration Manager is optional. All settings are regular BepInEx configuration entries.

The search picker uses native uGUI controls (`Image`, `Button`, `TMP_InputField`, `ScrollRect`, and
`Scrollbar`) on its own scaled Canvas. The original Valheim Sprites render through their authored
atlas rectangles and sliced borders, preventing the wrong atlas region from becoming the window
background. A small raw-pointer observer remains in `OnGUI` only to maintain the proven attack-
suppression latch; it does not draw the menu.

## Quick Stack Rules and chest labels

Open a placed chest and click **Quick Stack Rules** in the separate footer below its inventory panel. The vanilla **Stack** button remains available in the chest header and retains vanilla behavior; saved chest rules are used by Runic Quick Stack (`Left Alt + Q`). Hover over any editor control for help; keyboard focus also shows help.

While Rules or Search is open, movement, gameplay shortcuts and camera zoom/look are blocked. Typing, UI scrolling, buttons and Escape/controller Cancel continue to work. Controls resume after the closing input is consumed. A biome-only filter also supplies an automatic label: **Only from biome > Meadows**, with the caption blank, displays **Meadows**.

- Turn on **Remember contents** to learn the item types currently in the chest. It also learns newly stored types as the chest saves. You can take or consume the last item; Quick Stack will still recognize the empty chest later. No item is reserved or made unusable.
- Search **Always accept** for exact types such as Wood or Finewood, or use **Groups** for built-in and reusable custom groups. Multiple selected groups accept any matching member. Positive selection automatically enables the exterior label.
- Health Foods means health greater than stamina; Stamina Foods means stamina greater than health. Equal values go under Balanced Foods. Eitr Foods accepts food with positive eitr; Food accepts all food. These categories can overlap.
- The **Accepted** tab lets you remove selected rules. **Remembered** lets you remove individual learned item types. **Clear rules** removes explicit rules; **Clear remembered items** clears learned types. Save applies edits; Cancel discards them. To stop relearning current contents, turn Remember contents off.
- Explicit rules replace the normal contents/remembered-items match for Quick Stack. **Never accept** rejects an item even if a positive rule or current contents match. **Only from biome** filters group and remembered/current-content matches; **Always accept** is an exact-item exception to that biome filter. Full or inaccessible destinations are skipped, and protected player items remain protected.
- Choose **Front**, **Back**, **Left**, **Right**, or **Top** for the text on the chest body. Adjust size and horizontal/vertical offsets if needed for a particular chest model. Labels follow the chest's rotation and disappear with it; no separate sign piece or materials are required.
- Labels automatically show up to three selected or remembered designations. Enter custom text to override that caption. Choose a named text color from **Text**, or type a name in the adjacent field. Supported names are Red, Cyan, Blue, Darkblue, Lightblue, Purple, Yellow, Lime, Fuchsia, White, Silver, Grey, Black, Orange, Brown, Maroon, Green, Olive, Navy, Teal, Aqua, and Magenta. The mod writes the hex color tag automatically. Familiar unsupported names and hex values use the nearest palette RGB; unknown words use the nearest spelling. The preview and status show the selected approximation. New selections are opaque; untouched legacy labels retain their existing color until edited and saved.
- **Background** selects Transparent, White, or Black behind the text. It is independent of text color. The preview shows both; choose contrasting text and background for readability.
- **Never accept** prevents Runic Quick Stack delivery to this chest regardless of matching rules or priority. It does not remove existing items or prevent manual placement. **Preferred** only chooses this chest before Normal chests with equally specific matches; distance decides remaining ties. The explanation changes when you toggle priority.
- Rules and labels are saved with that chest in the world and synchronize through its native network state. Use RunicStorage 1.2.4 on participating installations for the corrected labels and input handling. Install it on all processes that add/remove chest items, including an automation host, for consistent automatic learning.

This feature applies to placed chest-like containers, not carts, ships, graves, or display stands.
Each chest supports up to 128 explicit item types and 128 remembered types; the item picker is paged.
Rules affect **Quick Stack**. Manual transfers, Store All, crafting, and production can still put other
items in the chest or use its final item. They do not interpret these rules as inventory locks.
Labels use the chest model's bounds; unusual modded shapes may need position adjustment.
Front is the chest's forward face; Left and Right are from a player facing that front. Top text reads from the front. Body labels use stable body geometry. Top labels attach to the closed lid, with matching open-lid meshes following the same attachment when the chest opens; they are not positioned from a temporary open-state bounding box.

## Rule groups, filters, and priorities

Rules use stable internal IDs, independent of the visible label. Renaming or coloring a chest does not change its sorting rules.

| Picker group | Designations |
|---|---|
| Equipment | Weapons, Shields, Armor, Capes, Tools |
| Ammunition | Arrows, Bolts, All Ammunition |
| Consumables | Food, Health Foods, Stamina Foods, Balanced Foods, Eitr Foods, Potions |
| Resources | Wood Materials, Stone Materials, Ores, Metal Bars, Seeds, Crops, Animal Materials |
| Other | Trophies, Valuables, Crafting Ingredients |
| Biomes | Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands, Deep North |
| Custom | Reusable player-selected groups such as Building Supplies or Expedition Food |

Equipment, ammunition, food, potions, and valuables use item properties. Crafting Ingredients uses the installed recipe ingredient lists. Resources and biomes use curated exact prefab lists, including overlapping biome membership. These lists describe an item's associations, not the location where that particular stack was collected. Unknown/modded biome items require explicit item exceptions or a custom group without a biome filter.

Select **Ores** plus **Metal Bars** to accept either. To restrict them to swamp-associated items, select **Swamp** under **Only from biome**. Selecting Swamp under **Groups** instead adds all curated swamp items as another accepted group. **Any biome** clears the filter.

Routing order:
1. Reject Never accept items, inaccessible chests, and destinations with no capacity.
2. Prefer Always accept exact-item rules, then narrow groups, then broad groups.
3. Within the same specificity, Preferred chests precede Normal chests; distance breaks ties.
4. Fall back to remembered-item and ordinary existing-content matches when no explicit positive rules are set for that chest.

Narrow groups include Wood Materials, Ores, Metal Bars, Shields, Capes, Tools, Arrows, Bolts, specialized food groups, and the other specific resource groups. Weapons, Armor, All Ammunition, Food, Crafting Ingredients, biomes, and custom groups are broad. An exact Finewood chest is tried before Wood Materials, followed by a Building Supplies custom group, even when the broader chest is Preferred.

### Reusable custom groups

Open **Custom groups**, choose **New group**, enter a name, and select its members under **Items**. Click **Save group** to save that reusable template. Then select it under **Groups** and click the chest's **Save** button. Select an existing template under Custom groups to rename it or edit its members; its internal identity stays the same.

The reusable library is stored in this mod profile's configuration. Applying a group copies its definition into the chest's network data, so other players use the same membership without needing your local library. Existing chest definitions are preserved when a library template is changed or deleted. To update another chest, deselect and reselect the group there, then Save. Saving a template is independent of saving or cancelling the chest editor.

Each library/chest supports up to 16 custom groups, with 128 exact members per group, within a total bounded metadata size. Each chest also supports 128 Always accept entries, 128 Never accept entries, and 128 remembered item types. Duplicate/oversized data is rejected without overwriting existing chest rules.

Existing 1.1.0 and 1.2.0 rules, memory, custom groups, and label settings migrate automatically, with Transparent backgrounds. Settings from 1.2.1 and 1.2.2 are retained. Update participating installations to 1.2.4; versions before 1.2.1 do not understand the background-enabled saved format.
