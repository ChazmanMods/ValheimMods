# Testing Runic Crafting

Dated acceptance logs for past releases live in
[docs/release-notes/RunicCrafting-verification-history.md](../docs/release-notes/RunicCrafting-verification-history.md).
This file is the repeatable checklist.

## Build and automated checks

Run from the repository root:

```powershell
dotnet build RunicCrafting\RunicCrafting.csproj -c Release
dotnet run --project RunicCrafting\Tests\RunicCrafting.Tests.csproj -c Release
pwsh tools\Sync-RunicSuiteVersion.ps1 -Check
```

The focused suite covers deterministic carried-first allocation, concurrent last-resource contention,
exact rollback, independent Workshop Access policies, DLC gates, requirement displays, placement
finalization, smelter and stationless-piece costs, spatial-index stability, and the static
standalone-architecture checks (no Foundation, registry, durable-saga, journal or global
mutation-gate dependency; local-player and native container ownership before any inventory is
exposed to the material engine).

## Manual checks before a release

- Storage, Production and Crafting are installed at the same version on the server and every client;
  each startup line shows the same `core` hash.
- Craft and upgrade at a station using only carried materials (vanilla fallback), then with an owned
  nearby chest supplying part of the cost; the recipe rows show combined totals with C/N/T/M hover.
- Place a hammer piece from nearby chest materials; cancel a pending build and confirm the reserved
  materials are released immediately.
- A chest in use by another player, or denied by a ward, is not used as a source.
- Refuel a fire and add food to a cooking station from nearby materials; the native interaction
  remains available when nearby materials are disabled.
- Repair All and the semicolon area-repair shortcut repair pieces in range and play the station effect.
- `runiccrafting_access show` at an owned station; `station <policy>` and `materials <policy>` change
  access for a second player; a non-creator is refused.
