# Runic Storage

This release improves how Runic mods share chest supplies. No extra Runic mod is required. Update the server/host and participating clients together for the multiplayer improvements.

**Version 1.3.6**

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
cooperative ownership handoff after the current owner confirms access and that the chest is
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
  The complete window's font size (`Search.MenuFontSize`, 10â€“32) and named color
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

Storage only moves items through loaded, accessible chests whose contents are ready to use. A chest another player is using is skipped. When multiplayer ownership changes, Storage waits for the chest to synchronize before moving items.

Crafting, Storage and Production coordinate their transfers when installed together. If an action fails, recovery avoids overwriting unrelated inventory changes. If the result cannot be confirmed, the affected storage is blocked for that session. Inspect the items and share the log when reporting the problem; restarting alone does not repair it.

Search and hover never move items or open chests. Personal-chest and ward restrictions still apply. Search markers disappear safely if their chest is removed.

## Optional Runic Inventory integration

When Runic Inventory is installed, Storage honors its locked and protected items. Without it, Storage still protects equipped items, quest items and the hotbar according to your settings. If item protection cannot be checked, the affected action is stopped.

Configuration Manager is optional. Settings are available in the normal BepInEx configuration file.

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

## Label emojis

Click **Emojis** beside the caption/label field, choose Food, Materials, Equipment or Places, then choose one of 64 icons. The icon replaces selected text or inserts at the cursor. Close or Escape dismisses the picker without discarding the editor draft; save the editor to apply changes. Supported emojis can also be pasted. The preview and world label use the same bundled artwork, retaining its colors when text color changes.

Limits remain 256 UTF-16 units for signs and 96 for chest labels; each bundled emoji uses two units. Flags, skin tones, joined sequences and emojis outside the picker are not included in this release. Players need the updated mod to display the bundled artwork; captions remain ordinary Unicode text in existing saves.

Emoji artwork: Twemoji, copyright Twitter, Inc. and other contributors, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), from [jdecked/twemoji](https://github.com/jdecked/twemoji). Images are arranged in a padded atlas without changing the artwork. The graphics license is embedded in the DLL.

## Modded containers and ItemDrawers

Makail's [ItemDrawers 0.5.8](https://thunderstore.io/c/valheim/p/makail/ItemDrawers/) uses the internal prefab ID `piece_drawer`. Its custom save format requires an adapter; the optional adapter is included and enabled by default.

When RunicStorage is installed, edit `BepInEx/config/chazman.RunicStorage.cfg`:

```ini
[Modded Containers]
QuickStackPrefabIds = piece_drawer
PullPrefabIds = piece_drawer
```

`QuickStackPrefabIds` enables deposits through QuickStack. Storage's `PullPrefabIds` enables Restock. RunicCrafting 1.1.3 and later use their own `PullPrefabIds` configuration for crafting, building, cooking and refueling, independently of Storage. Older Storage setting descriptions may still mention shared Crafting pulls; that applies only to Crafting 1.1.2. RunicInventory provides carried-slot protections.

Use exact internal prefab names separated by commas or semicolons. Blank lists disable the corresponding custom adapters; wildcards are not supported. Ordinary modded containers using Valheim's normal inventory/save format already work automatically. Adding a name cannot supply an adapter for an unknown custom save format. This adapter targets Makail's original mod; KGvalheim and other drawer implementations are not claimed compatible.

Assign an item to a drawer using ItemDrawers first. QuickStack accepts matching assigned drawers, including an assigned drawer at zero quantity, up to the drawer's capacity. A completely unassigned drawer is not automatically assigned by QuickStack. Spawned items can be deposited, matching ItemDrawers' own deposit action: its type/count-only save format does not retain picked-up or spawned flags. Items with non-default quality, durability, custom data, crafter attribution or other incompatible attributes stay in the backpack and receive a specific explanation. Withdrawn items use normal backpack stack limits.

Existing range, access, ward, ownership and protected-slot rules still apply. Both updated RunicStorage and RunicCrafting DLLs are needed for all features. This release passed dedicated-server testing.

## Bend chest labels

Open a chest, choose **Quick Stack Rules**, then **Bend…**. Both **Up / down** and **Forward / backward** have **− / +** buttons, a numeric field, and a shared **Reset bends** button. Each click changes the displayed value by 1; this is equivalent to only 0.01 in the old RunicSigns bend scale. Start at 1 or −1. Type decimal values for smaller changes. Positive values bend the center up/forward; negative values bend down/backward; 0 is flat. Both axes combine, including emoji captions.

For a barrel, turn **Wrap around container: ON** in the Bend window. The label center stays anchored while the ends turn around the rounded sides. **Wrap adjustment = 0** fits the container bounds; minus makes it flatter and plus makes it tighter. Each button changes the curvature by one percent of the initial fit; decimals allow smaller steps. Up/down bending still combines with wrapping. Wrapping is available on Front, Back, Left and Right; Top uses the standard bow. **Reset bends** switches wrapping off and clears both axes.

Choose **Done**, then **Save** in the chest editor to persist the bends. **Cancel** discards the draft. Existing version-1/2/3 labels remain flat, and version-4 bends keep their previous shapes; other settings and item rules are preserved. Version-5 chest-rule records add the wrap toggle, so update all participating clients before saving labels. Existing label styles and saved rules are preserved.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicStorage` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
