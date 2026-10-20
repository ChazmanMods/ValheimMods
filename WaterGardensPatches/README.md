# WaterGardens incremental source

The base source is in `../RunicLilyPads/` (1.2.1). Later releases were produced with Mono.Cecil patch projects and method donors. These directories preserve the original source rather than flattening successive binary edits into a fabricated full source tree.

| Source directory | Change |
| --- | --- |
| WaterGardensHangingIvy | Hanging ivy, 1.3.0 |
| WaterGardensIvyLights | Ivy lights, 1.3.1 |
| WaterGardensIvyChain | Ivy chains, 1.3.2 |
| WaterGardensPondMotion | Pond and fish motion |
| WaterGardensGentle | Gentle fish motion, WaterGardens 1.3.4 and koi 0.2.2 |
| WaterGardensTreeIvy | Tree ivy, 1.3.5 |
| WaterGardensArcaneLilies | Arcane lilies, 1.4.0 |
| WaterGardensThunderstore | Package identity patch |
| WaterGardensKoiRelease | Koi Fish 1.0.0 identity patch |

Patch programs declare their expected input assembly/version and donor assembly. Read each `Build/Program.cs`, `KoiBuild/Program.cs`, or `WaterBuild/Program.cs` before running it. Supply the matching prior build, required donor, and Mono.Cecil reference from your own dependencies. Original local paths remain in historical build/test scripts. Game/runtime binaries and prior release DLLs are external dependencies.

Some donors contain copied neighboring source used for compilation; the patch programs identify the exact methods actually transplanted. A source-only checkout does not reconstruct every prior binary automatically.
