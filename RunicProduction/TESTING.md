# Testing Runic Production

Dated acceptance logs for past releases live in
[docs/release-notes/RunicProduction-verification-history.md](../docs/release-notes/RunicProduction-verification-history.md).
This file is the repeatable checklist.

## Build and automated checks

Run from the repository root:

```powershell
dotnet build RunicProduction\RunicProduction.csproj -c Release
dotnet run --project RunicProduction\Tests\RunicProduction.Tests.csproj -c Release
pwsh tools\Sync-RunicSuiteVersion.ps1 -Check
```

The focused suite covers conversion validation for live and registered fermenters (declared batch
sizes, non-positive and oversized batches, exact counts, ambiguous recipes), replenishment reverse
recipes, the role matrix, persistent link and catalog round-trips, the version match against
`Shared/RunicSuite.props`, and the static standalone-architecture checks.

## Manual checks before a release

- Storage, Production and Crafting are installed at the same version on the server and every client;
  each startup line shows the same `core` hash.
- Link a smelter's input, fuel and output chests with the Alt gestures; unlink with Shift held at both
  steps. The link ring renders and the station keeps its normal manual controls.
- Complete a batch on a recipe producing 4 or 10 items; the exact quantity is deposited once. With
  insufficient output capacity the batch stays in the station until the whole batch fits.
- Fermenter: a changed positive duration, a mixed valid/invalid recipe table, and two inputs sharing
  one output all work for ordinary throughput; replenishment refuses the ambiguous reverse recipe.
- Replenishment: an exemplar item in the replenishment chest sets the stock target; reserves are
  respected; a completed batch advances fairness across several targets.
- Beehive, sap extractor and wisp fountain output links collect after the native spawn and pause
  while output is waiting for storage. Manual collection still works.
- Two clients on a dedicated server: link chains created by different players, open and drag items
  in a shared chest, then close it. Transfers pause while open and resume afterward. Repeat with the
  prior owner disconnecting and with ward access revoked. Item totals must match before and after.
