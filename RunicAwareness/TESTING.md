# Runic Awareness 1.0.2 maintainer verification record

This is an internal release record, not a player installation checklist.

## Automated contract

The focused suite verifies:

- identity/version/manifest alignment and a standalone BepInEx-only runtime dependency;
- no Runic assembly or project references and no Production service/capability discovery;
- Valheim 1.0.7 plus every exact installed method/field used by food, effects, timers, comfort,
  inventory selection, hover contexts, building state, and the private translation dictionary;
- postfix-only, installed-verified `Priority.Last` Harmony hooks that observe normal-priority composed
  hover results and cannot rewrite original tooltip/hover results;
- no calls to food/effect/equipment/inventory/ZDO/RPC/ownership/health/structure gameplay mutators;
- no inventory enumeration, comfort world scan, scene scan, or state scan in the draw path;
- timer rounding/finite ceilings, sanitization, bidi/rich-text removal, deterministic item values,
  raw unsupported types, and hard panel line/character bounds;
- bounded localization tokens/output, context text/age, item age, comfort age/player/state, and
  building-detail avatar reach/ward disclosure without ownership requests;
- the unsheltered comfort branch never reads Valheim's deliberately stale `s_tempPieces` list;
- missing or incompatible optional Portals metadata hides cleanly.
- left-middle safe-area placement and interactive UI suppression cover inventory, the large map,
  build selection, chat, menu, console, text entry/viewer, trader, barber, feedback/connect,
  unified popups, virtual keyboard, and the runtime-discovered Runic Portals setup guide without a
  Portals assembly reference.
- one-time anchor migration converts only an unmarked legacy `TopRight`, commits its marker after
  the anchor write, preserves every other existing anchor, and leaves later user choices alone.

## Normal-play compatibility observations

Check food expiration, a timed and untimed HUD effect, entering/leaving shelter, Rested refresh,
mouse and controller inventory selection, a building piece at several support colors, cooking and
fermenting stations, a crop, beehive, and tameable. Repeat with Runic Production absent and present,
with a remote Build Camera hover, on ultrawide/small safe areas, and after live configuration and
language changes. Awareness must show only already-visible native hover context for production and
must keep building detail absent outside avatar reach or current ward access.

Confirm that the overlay sits at left-middle with the default configuration. Open each supported
interactive panel and verify that Awareness disappears without clearing its cached state, then
returns after the panel closes. With Runic Portals present, repeat while its setup guide is visible,
while its normal-map portal directory is open, and during the walk-in destination picker. Disable
`Display.ShowSetupPanel` in Runic Portals and verify that merely aiming at a portal no longer hides
Awareness.

For migration compatibility, start once with an old Awareness config containing `Anchor = TopRight`
and no migration marker; verify that it becomes `MiddleLeft` and the marker becomes true. Then choose
`TopRight` again and restart to verify it stays selected. Repeat from an unmarked custom corner and
verify that the custom corner is preserved while only the marker is added.

Automated tests cannot reproduce a full Unity render/input frame, third-party language packs, every
modded item calculation, or multiplayer ownership migration. Those are bounded normal-play
compatibility observations, not prerequisites delegated to players before release.
