# Runic Awareness

Runic Awareness 1.0.0 is a restrained, display-only explanation layer for Valheim 0.221.12. Its
only runtime requirement is BepInExPack Valheim 5.4.2333. It does not change food, effects,
equipment, comfort, structures, creatures, inventories, production, input, network ownership, or
world state.

## What it observes and shows

- **Food** — the local player's active food names and upward-rounded remaining timers (three visible
  slots; hard scan ceiling eight).
- **Effects** — locally active status effects that already carry a HUD icon, with remaining time or
  `active` for untimed effects (configurable 1–8 rows; hard scan ceiling 32).
- **Comfort** — current comfort, shelter state, remaining Rested time, and bounded category winners
  captured after vanilla finishes its own comfort calculation (hard ceiling 64 comfort pieces).
- **Item-comparison capture** — the item already receiving Valheim's mouse/controller inventory
  tooltip can still be compared with the directly equipped slot in a bounded local cache. The
  overlay now yields completely to inventory, so this compatibility capture is not rendered as an
  Awareness panel while the inventory is open. No DPS, secret modifier, or future-performance
  prediction is invented.
- **Production context** — bounded text already exposed by the currently hovered cooking station,
  fermenter, or production switch. Awareness does not query another mod for hidden station state.
- **Agriculture and tameable context** — bounded text already returned by the currently hovered
  plant, beehive, or tameable.
- **Building context** — the current hovered/selected piece name, known health, transform, crafting
  station level, and a support-color legend, but only while that piece is within the physical
  avatar's bounded interaction reach and every loaded hostile-ward check explicitly allows disclosure.

Each panel has its own toggle. The non-interactive overlay defaults to the left-middle safe-area
anchor, supports the four original corner anchors, UI scale from 0.75–2.0, and an additional
1.0–1.5 controller readability multiplier. Awareness adds no keybinding, so it cannot collide with
other Runic controls. Controller item selection follows Valheim's own tooltip-selection path.

The overlay yields completely while another interactive UI is open: inventory/container/crafting,
the large map, build-piece selection, chat, the pause menu, console, text entry/viewer, trader,
barber, feedback/connect panel, unified popup, or platform virtual keyboard. It also yields to the
Runic Portals setup guide when that optional mod is live and configured to show the guide; both its
normal-map directory and walk-in picker are covered by the large-map gate. Consequently,
item-comparison capture never draws over the open inventory; Valheim's own tooltip remains the
inventory UI.

## Source, privacy, and refresh boundaries

The display sampler runs every 0.2–2 seconds (0.35 seconds by default). It hashes bounded local
state and rebuilds panel text only when a timer bucket or source value changes. Drawing uses only a
cached composition; it performs no inventory enumeration, comfort world scan, or scene scan.
Language changes clear all localized captures and signatures before rebuilding.

- Food and effect rows come from the local player's existing `GetFoods()` and `SEMan` lists.
- Comfort details reuse the completed vanilla `SE_Rested.CalculateComfortLevel(Player)` pass. The
  scratch list is never read while unsheltered because Valheim leaves it stale in that branch.
  Captures expire after five seconds and must match the current player, comfort, and shelter state.
- Item data is retained for at most 0.8 seconds from the exact vanilla tooltip builder used by mouse
  and controller inventory navigation and must match the current local-player instance. Awareness
  never scans the inventory.
- Context text is accepted only from a current vanilla `GetHoverText()` result whose component
  hierarchy matches the local player's current hover object. It expires after 0.8 seconds; input
  above 8,192 characters is rejected before retention, and rendered context is capped at 768
  characters and 1–6 lines. Capture postfixes use Harmony's low-priority final-postfix ordering so
  compatible normal-priority Production/Agriculture hover additions are present before capture.
- Production, agriculture, and tameable context comes only from the bounded native hover text
  already returned for the current subject. Awareness publishes and consumes no cross-mod status
  service, capability, registry, or protocol for those panels.
- Building detail uses the same physical-avatar reach cap and strict, no-flash loaded-ward proof.
  The reach is hard-capped at 10 m, and Awareness never claims ownership. A Build Camera selection
  cannot extend this disclosure boundary. If the selected piece is remote, ward-denied, ward
  topology exceeds the 4,096 ceiling, or proof faults, Awareness shows only the generic
  `Building details unavailable` status and reads no health, transform, or station level.
- Localization accepts only a single safe key of at most 128 characters and uses Valheim's direct
  translation lookup. It bypasses the expanding/LRU-caching text parser, inspects only a bounded
  prefix of returned third-party text, and retains at most a 96-character sanitized label.

No hidden enemy, radar, future weather, undiscovered object, or remote-management panel exists.
Turning the module or any panel off changes no gameplay state. On a dedicated/batch process the
runtime is intentionally inert and installs no Harmony hooks.

## Authority, multiplayer, and server boundary

Runic Awareness is a local client-observation and display-only layer, not server authority. It sends
no multiplayer RPC, requests no ownership, performs no world mutation, and cannot ask a server or
another player to disclose or change state. Every detailed panel requires evidence already available
to the local client plus its own bounded privacy gates; unavailable proof produces only a generic or
hidden panel. A dedicated server has no display role, so the module remains inert there.

## Intentional MVP limits

- Crop, beehive, tameable hunger/pregnancy, and population details appear only when vanilla already
  includes them in the current visible hover text. Awareness does not probe private creature or
  world-manager state to fill missing fields.
- Comfort lists vanilla's counted category winners and duplicate/zero alternatives from that exact
  completed pass. It does not search for undiscovered furniture or predict future Rested state.
- Building support is explained as Valheim's color legend; this release does not infer a hidden
  structural graph or add transform controls.
- Unsupported modded item types remain raw safe values. If an installed signature or the optional
  runtime-discovered Runic Portals configuration surface is absent or incompatible, that display
  enhancement hides cleanly and the vanilla UI remains unchanged.

## Configuration

`General.Enabled` controls the module. `Panels` contains separate toggles for food, effects,
comfort, the compatibility item-comparison capture, production, agriculture, building, and
tameable animals. Inventory suppression takes precedence over item comparison. `Display`
contains timer precision, refresh interval, row/line bounds, scale, controller multiplier, and
anchor. `MiddleLeft` is the default anchor. UI suppression is unconditional and has no keybinding or
configuration switch. `Diagnostics.VerboseLogging` logs configuration refreshes only; it never logs
hidden world state.

On the first run containing the left-middle change, a one-time marker migrates only the exact legacy
`Anchor = TopRight` value to `MiddleLeft`; existing `TopLeft`, `BottomRight`, and `BottomLeft` choices
are preserved. After that marker is recorded, it never rewrites the anchor again, so you may choose
any anchor—including `TopRight`—and that later choice remains in effect. The `Migrations` marker is
internal bookkeeping and should normally be left unchanged.
