## 1.2.7

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.2.6 - 2026-09-19

- Added minus/plus buttons beside both bend fields in Effects.
- Rescaled bend controls by 100: entering 1 now gives the old 0.01 bend, and each button click changes by that amount. Decimal input allows finer adjustments; zero stays flat.
- Preserve existing saved sign shapes and style data. Old coefficients are displayed in the new units without rewriting their values; e.g. an existing 0.01 bend now reads 1.

## 1.2.5 — 2026-09-18

- Includes selected-text styling, emojis, text effects, curves, caption offsets, placement previews and existing-sign editing.

- No selection edits caption defaults; selected words retain their independent formatting.
- RESET TEXT restores uniform default text formatting without erasing the caption or moving the sign.
- Neutral private font face tint and enabled rich-text colors prevent inherited sign tint from darkening chosen colors.
- Text Color... label; opacity controls reflect effective alpha, including older saved settings.

## 1.2.4 — Caption depth test (2026-09-18)

- Added Depth input and Back/Forward buttons for independent caption translation along its facing direction; positive moves forward, negative backward.
- Depth uses the selected offset units and fine/coarse step. Center text resets all three axes; backgrounds follow the caption.
- Version-4 style data persists depth, with zero depth for previous designs. Curvature remains independent.
- Test release only; RunicStorage and public packages unchanged.

## 1.2.3 — Text-sized background test (2026-09-18)

- Backgrounds now follow the actual rendered text/emoji bounds with padding, including selected text sizes, multiple lines and curved geometry.
- Backgrounds follow caption alignment/offsets and hide for empty captions. Whole-sign scaling still scales text and background together.
- Includes the free-text wrapping fix and combined effects from 1.2.2. Test release only.

## 1.2.2 — Free text and combined effects test (2026-09-18)

- Free text size now disables automatic word wrapping so enlarged portal captions extend horizontally; explicit newlines remain supported. Fit mode restores the original wrapping behavior.
- Replaced the exclusive effect cycle with an Effects panel: independent outline/shadow toggles and signed up/down and forward/backward curve strengths. All four controls can combine.
- Curvature transforms freshly generated text and emoji geometry; cancelling or resetting restores the original appearance.
- Version-3 style data saves both curves and combined effects; existing version-1/2 designs remain readable.
- Isolated test build only. No public release promotion or RunicStorage changes.

## 1.2.1 — Named-color and selection test (2026-09-18)

- Added a named-color picker with all 22 supported names and visible swatches; formatting is generated automatically.
- Color, bold, italic, size, opacity and Clear formatting now apply only to selected caption text. Select all explicitly formats the whole caption.
- Preserve the selected range while using formatting controls and the color picker; choosing the same color for another selection works.
- No selection means no text formatting changes, including a caret within an emoji.
- Isolated test build only; not promoted to latest. RunicStorage is unchanged.

## 1.2.0 — Designer test (2026-09-18)

- Add F8 live placement designer and local live preview on existing signs.
- Add selected-text styling, precise colors and alpha, opacity, italic, free sizing, outline/shadow and symbol insertion.
- Add numeric offsets up to ±2000 text units or ±20 sign fractions with fine/coarse controls.
- Apply the draft caption/style on native placement; preserve bracket resizing.
- Keep captions plain for native filtering; save version-2 style metadata separately from caption text.

- Isolated testing only; no latest-release promotion.

## 1.1.0 - 2026-09-16

- Preserved bracket-key hammer preview sizing and saved placement scale from 1.0.4.
- Add a 64-emoji picker with Food, Materials, Equipment and Places categories.
- Render bundled full-color emojis in the editor and world labels without extra dependencies or runtime downloads.
- Preserve Unicode in the existing save format, guard insertion limits, and avoid broken surrogate pairs.

## 1.0.4 - 2026-09-14

- Added enlarged hammer previews: use ] / [ to adjust sign size before placement, or set Placement.SignScale. Shortcuts are configurable and respect text input and modal editors.
- Applied the selected scale before native placement checks, and saved it through the native IPlaced callback after creator assignment. Board and colliders match the preview without changing the shared prefab.
- New signs retain their selected size across multiplayer synchronization and save/reload. Existing sign settings are preserved.

## 1.0.3

- Fix text-position arrows moving the caption off the sign on the first increment. Offsets now account for the text object's own scale and rotation.
- Preserve the original text position and backing-panel position; offsets do not accumulate between edits.
- Keep the same saved settings format. Existing offsets are interpreted correctly after updating; Center still resets both offsets.

## 1.0.2

- Includes the matching wood-and-iron icon with cyan runes, installation instructions and sign-size/character-limit guidance.

- Fix the reproduced persistent input lock when RunicStorage and RunicSigns are installed together. HarmonyX shares `__state` by declaring type name even across assemblies; the duplicate camera hook name let one mod overwrite the other's scope state.
- Give all RunicSigns modal patches a unique namespace. RunicStorage does not need to be changed.

## 1.0.1

- Remove a possible unbounded held-action lock after closing or saving; this did not resolve the cross-mod conflict subsequently fixed in 1.0.2.
- Limit protection against held attack/use inputs to those actions, with a one-second stale-input timeout.
- Build the editor while inactive and activate input blocking only after it successfully opens.
- Release modal state before cleanup; handle partial UI construction, callback failures, disabled components and missing canvases safely.

- Use the matching wood-and-iron RunicSigns icon with cyan runes.

## 1.0.0

- Fresh, focused editor for vanilla wooden signs.
- RunicStorage named-color palette, independent text sizing, backgrounds, alignment and offsets.
- Proportional physical sign scaling from 0.25× to 4×.
- Native caption/author preservation, synchronized appearance and save conflict handling.
- Bounded save leases, timeout/cancel handling and modal gameplay input protection.
- No BetterSigns assets or code; no storage functionality.

Versions 1.0.0 and 1.0.1 were development builds; use 1.0.2 or later.
