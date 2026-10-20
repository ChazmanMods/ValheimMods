# Classic continuation maintenance build

This directory deliberately uses the archived ArcaneWaters 0.6.1 DLL as its baseline. Do not build a release by copying the main project's entire DLL: that would restore the rejected later water rendering.

`Build.ps1` compiles the current source as a method donor, then transplants only `RiverHoe.Extend`, `Route.AppendFrom`, and the new `Route.NeedsContinuation` helper into the original DLL. It updates the version to 0.6.7. The patcher verifies that the other 358 method bodies and all 16 embedded resources remain unchanged. This preserves the 0.6.1 shader, geometry, boats, textures, and audio.

The changes are maintained in `../ArcaneWaterWorks/RiverHoe.cs` and `../ArcaneWaterWorks/Route.cs`. The archive is under `../Rollback/ArcaneWaters-0.6.1_WaterGardens-1.1.3`.

`Tests/Classic.Tests.csproj` executes the resulting DLL's route model directly, including 3,204 metres of connected sections. `Runtime/Smoke.csproj` uses real in-game section objects, ZDO saves, ownership and selection with a deterministic flat-ground fixture in a disposable world. `Run-Runtime.ps1 -Baseline` reproduces the old failures; without that switch it tests the fixed DLL. Never launch either while the user's Valheim is running. The script uses only the existing isolated runtime.

Ship only the whitelisted `package` contents. Runtime logs and these build notes are not release documents.
