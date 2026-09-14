# Recipe filtering and inventory snapshot regression

The focused suite executes the production ingredient builder, material source, planner, transaction engine, snapshot, and payload comparison with managed substitutes for Unity-bound game objects. Installed-assembly contract tests independently verify the native fields and methods used by the integration. These checks are not an in-game multiplayer acceptance run.

Run with VALHEIM_INSTALL and BEPINEX_PROFILE set to the installed Valheim/BepInEx directories:

`dotnet run --project RunicCrafting/Tests/RunicCrafting.Tests.csproj -c Release`

The axe fixture has 79 wood, 34 flint, and no upgrader ingredient. An unfiltered plan reproduces the missing hidden ingredient. The normal-workbench plan succeeds and consumes exactly 4 wood and 6 flint. Upgrader fixtures retain their special costs and deny missing ingredients. Additional tests cover batch/upgrade arithmetic, unfiltered piece construction, overflow, matching preview/consumption call sites, full-precision worn equipment, original item references, metadata, rollback, denied eligibility, and strict synchronized payload comparison.

Manual acceptance: use a normal workbench with wood/flint in accessible nearby chests and no upgrader token. Verify the axe list entry, displayed ingredient totals, and Craft button agree; craft once and verify a 4-wood/6-flint reduction and one axe. Test an armor recipe, ordinary equipment upgrade, and batch craft. Keep worn equipment in the carried inventory, then reconnect to verify persistence. Verify denied wards/personal chests and real upgrader ingredient shortages still block use appropriately.
