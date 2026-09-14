# Changelog

## 1.0.3

- Fix text-position arrows moving the caption off the sign on the first increment. Offsets now account for the text object's own scale and rotation.
- Preserve the original text position and backing-panel position; offsets do not accumulate between edits.
- Keep the same saved settings format. Existing offsets are interpreted correctly after updating; Center still resets both offsets.
- Add rendering regressions for four-way nudges, rotated text, 0.25×–4× boards and Center/reset behavior.

## 1.0.2

- Prepared for Thunderstore release after author-confirmed in-game operation.
- Includes the matching wood-and-iron icon with cyan runes, installation instructions and sign-size/character-limit guidance.

- Fix the reproduced persistent input lock when RunicStorage and RunicSigns are installed together. HarmonyX shares `__state` by declaring type name even across assemblies; the duplicate camera hook name let one mod overwrite the other's scope state.
- Give all RunicSigns modal patches a unique namespace. RunicStorage does not need to be changed.
- Add an actual HarmonyX two-assembly regression covering both patch orders, either editor, nested camera calls and exception cleanup. The original hooks reproduce the reported lock; the isolated hooks recover input.

## 1.0.1

- Remove a possible unbounded held-action lock after closing or saving; this did not resolve the cross-mod conflict subsequently fixed in 1.0.2.
- Limit protection against held attack/use inputs to those actions, with a one-second stale-input timeout.
- Build the editor while inactive and activate input blocking only after it successfully opens.
- Release modal state before cleanup; handle partial UI construction, callback failures, disabled components and missing canvases safely.
- Add regression coverage for permanently held input flags, timeout, re-press and lifecycle reset.
- Use the matching wood-and-iron RunicSigns icon with cyan runes.

## 1.0.0 candidate

- Fresh, focused editor for vanilla wooden signs.
- RunicStorage named-color palette, independent text sizing, backgrounds, alignment and offsets.
- Proportional physical sign scaling from 0.25× to 4×.
- Native caption/author preservation, synchronized appearance and save conflict handling.
- Bounded save leases, timeout/cancel handling and modal gameplay input protection.
- No BetterSigns assets or code; no storage functionality.

Versions 1.0.0 and 1.0.1 were development builds; use 1.0.2 or later.
