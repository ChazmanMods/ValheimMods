# Runic Integrity

Runic Integrity silently searches for useful beam and brace routes between a weak placement ghost and nearby strong structure.

- Green pieces indicate a route to a strong structural anchor.
- Cyan indicates that the route terminates at foundation-level support.
- `Tab` cycles through successful alternate routes.
- The advisor never places pieces, creates network objects, or changes structural-integrity rules.

## Install

Build the project and copy `RunicIntegrity.dll` into `BepInEx/plugins/RunicIntegrity`.

## Current MVP scope

Version 0.2.2 searches real snap geometry for one-to-three-piece horizontal, diagonal, mixed, and offset routes. The search has a strict 12,000-expansion ceiling, retains only 24 frontier states, and never repeats while the placement ghost remains stationary. Suggested pieces are reconstructed from meshes only: they contain no Piece, WearNTear, ZNetView, collider, script, or network-capable prefab instance.
