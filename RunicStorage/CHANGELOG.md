# Changelog

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
