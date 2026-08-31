# Changelog

## 1.0.0

- Added bounded local food and HUD-effect timers with configurable upward-rounding precision.
- Added current comfort/shelter/Rested explanation by reusing vanilla's completed comfort pass;
  stale unsheltered scratch data, cross-player captures, and oversized piece sets fail closed.
- Added mouse/controller-selected equipment comparison using known final item and skill-context
  values without inventory scans, DPS prediction, auto-equip, or gameplay mutation.
- Added current-hover production, agriculture, building, and tameable panels with strict age,
  hierarchy, character, line, and scan ceilings.
- Kept production, agriculture, and tameable panels on bounded text already returned by Valheim's
  current hover path; no cross-mod status service, capability, registry, or protocol is used.
- Added bounded direct localization lookup, safe-area anchoring, UI/controller scaling, per-panel
  toggles, low-frequency signature caching, dedicated-process inertness, and fail-closed startup.
- Added focused deterministic, installed-Valheim 0.221.12 signature, Harmony, privacy, display-only,
  performance, dependency, and documentation contract tests.
- Bound building detail to physical-avatar interaction reach and strict no-flash hostile-ward proof;
  remote Build Camera selections now expose only a generic unavailable status.
- Made the package independently installable with BepInExPack Valheim as its only dependency.
- Moved the default overlay anchor to left-middle and made it yield while inventory, large map,
  build selection, chat, menu, console, text-entry/viewer, trader, barber, feedback/connect,
  unified-popup, virtual-keyboard, or optional Runic Portals setup panels are open.
- Inventory suppression now deliberately takes precedence over the legacy bounded item-comparison
  capture, so no Awareness content is drawn over the inventory panel.
- Added a one-time same-version config marker that migrates only the old `TopRight` default to
  `MiddleLeft`; other existing anchors and every later user choice remain untouched.
