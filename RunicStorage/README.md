# Runic Storage

Runic Storage 1.0.0 adds secure chest hover details and player-directed storage actions to Valheim.
It is an independently installable BepInEx plugin. Runic Core, Persistence, Permissions,
Transactions, and Inventory are not runtime dependencies.

## Features

- **Chest contents on hover** adds a deterministic, localized, bounded summary to eligible closed
  containers.
- **Quick Stack** (`Left Alt + Q`) moves eligible carried items into nearby containers that already
  contain that item.
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

## Standalone multiplayer boundary

Storage uses Valheim's native ownership model. A mutation is allowed only when the active player is
the local native owner and every container being changed is locally owned, accessible, synchronized,
not loading, and not otherwise in use. This same rule applies in solo, listen-server, and dedicated-
server client sessions. Storage never requests or steals container ownership; an unavailable owner
path produces a specific no-op result.

Actions use a small process-local Storage lease so two Storage actions cannot interleave. Each move
preflights exact shadow inventories, captures exact backups, saves both endpoints, and attempts exact
rollback on an ordinary synchronous failure. Storage does not provide cross-process or crash-atomic
transactions, RPC transfer protocols, journals, global suite locks, quarantine records, or recovery
sagas.

Search and hover do not mutate container contents. They require vanilla access and an exact match between the live
inventory serialization and the current replicated `s_items` payload before exposing contents.
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
