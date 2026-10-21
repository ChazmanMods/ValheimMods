# Testing Runic Storage

Dated acceptance logs for past releases live in
[docs/release-notes/RunicStorage-verification-history.md](../docs/release-notes/RunicStorage-verification-history.md).
This file is the repeatable checklist.

## Build and automated checks

Run from the repository root:

```powershell
dotnet build RunicStorage\RunicStorage.csproj -c Release
dotnet run --project RunicStorage\Tests\RunicStorage.Tests.csproj -c Release
pwsh tools\Sync-RunicSuiteVersion.ps1 -Check
```

The focused suite covers nearest-eligible planning, protected-source filtering, partial capacity,
bounded query coverage and spatial cells, action/UI routing, local native ownership requirements,
controller sessions, diagnostics, hover disclosure evidence, optional Inventory absence, and the
static standalone-architecture checks (BepInEx-only manifest, no reference to another Runic mod,
native ownership at every mutation entry point, Inventory item protection optional).

## Manual checks before a release

- Storage, Production and Crafting are installed at the same version on the server and every client;
  each startup line shows the same `core` hash. With one module from an older release installed, the
  log must show the single "built from different embedded cores" error and every module must stay on
  vanilla inventory handling.
- With a chest open, use Alt+A and Alt+R; with it closed, use Alt+C and Alt+R. Valid carried items
  must move; protected, equipped, hotbar and quest items must stay.
- Alt+Q near several chests: eligible items reach the nearest accepting chest; when the first chest is
  nearly full, the remainder reaches the next eligible chest without exceptions.
- Alt+S sorts the open chest; Shift+Ctrl+P sorts the carried inventory and preserves empty locked cells.
- Alt+O twice within 30 seconds applies a SmartSort preview; Escape cancels; a chest changed between
  preview and confirmation is reported and nothing moves.
- Alt+F near several chests: type immediately in the filter, click an item, and verify every matching
  chest receives the animated highlight; clicking the filter, an item, Clear or Close must not punch,
  swing or use the equipped tool. Change `Search.MenuFontSize` and `Search.MenuFontColor` while open
  and verify every label and row updates; reopen and verify the color persists. Escape restores
  movement and the cursor.
- Shift+Alt+right-click selects carried stacks for the trash; the confirmation names the count and
  nothing is deleted when the selection changed or another inventory action is active.
- On a dedicated server with two clients: owned player/container mutations succeed, non-owner
  mutations fail closed, and total item counts match before and after each run.
