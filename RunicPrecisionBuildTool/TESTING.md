# Testing Runic Precision Build Tool 2.0.1

Use the audited Valheim 0.221.12 assemblies and configured BepInEx profile. From the repository root,
run the plugin build once and then its focused suite once:

```powershell
dotnet build .\RunicPrecisionBuildTool\RunicPrecisionBuildTool.csproj -c Release --no-restore
dotnet run --project .\RunicPrecisionBuildTool.Tests\RunicPrecisionBuildTool.Tests.csproj -c Release
```

The focused suite covers quaternion placement math, world/local translation, independent resets,
matching, snap-side proof, repeat history, input arbitration, zero-allocation core paths, catalog and
localization bounds, target caching, installed Valheim signatures/IL, Harmony cleanup and ordering,
native-owner mutation denial order, dependency metadata, documentation, and the published icon.

The root release's single dedicated-server acceptance matrix verifies transformed placement through
Valheim's normal commit, local native-owner F11/F12 behavior, denial for pieces not currently owned
by that peer, ward/range/no-build checks, per-success durability, one-shot undo, collider saturation,
and coexistence with Build Camera, Crafting, and Agriculture. No separate durable mutation harness
is used.

Useful manual checks before that final matrix are:

- Leave precision mode off and confirm ordinary rotate/place/remove/repair behavior stays vanilla.
- Press P outside build mode and with non-Hammer tools; it must do nothing. Toggle P across Hammer
  piece changes and Hammer exit; no state may leak into another session.
- Exercise every normal/fine rotation and movement chord plus all independent/full resets.
- Match Keypad0-Keypad9, repeat a two-piece pattern, and confirm native invalid placements still fail.
- Exercise search, favorites, and recents against changing unlock state. With an empty search query,
  F6 must cycle all currently unlocked pieces without an error message.
- Press F11 twice after one eligible placement; only the first action may remove it.
- Damage many locally owned pieces and use F12; nearest bounded successes each consume one durability
  charge, while a saturated 256-collider query changes nothing.
- On a dedicated client, repeat F11/F12 against a locally owned piece and one owned elsewhere; only
  the native local-owner path may mutate.

Final focused-test totals and multiplayer evidence are recorded by the root release workflow.
