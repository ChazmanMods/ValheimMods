# Validation and release limits

## Automated checks

`dotnet run --project RunicDeathPenalty.Tests -c Release`

184 assertions cover every built-in progression boundary, missing/out-of-order boss kills, manual mode and admin caps, policy serialization and invalid inputs, ordinary versus multiplied loss, per-skill recovery budgets, reconnect serialization, fixed window expiry, catalog exceptions, and the installed native patch/field contracts. Run with a Managed folder as its argument to validate another installed game assembly. Both the installed client and dedicated-server assemblies are checked during this build.

## Isolated runtime checks

`RunicDeathPenalty.Smoke` is a test-only BepInEx plugin and is NOT included in the release. It runs in a separate copied dedicated-server installation under `.runic-death-smoke`, with a new test world/save directory, `DontSaveAnything`, no public server listing, and no access to production characters/worlds. It quits after the checks. `Invoke-Smoke.ps1` rebuilds/copies only the test DLLs and launches that prepared directory hidden.

The smoke loads the real game and validates the Harmony patches, loaded recipes, item-tier outcomes, actual player Skills.LowerAllSkills behavior, partial-progress reset, character custom recovery state, HardDeath protection decisions, suppression and restoration of Corpse Run, tombstone marking and blocked/native boost behavior, and equipment rejection. It also exercises the native ZRpc serialization/dispatch using in-memory sockets for matching/missing/mismatched version admission, server policy sync and rejection of peer-supplied policy.

A second run includes the locally built RunicCrafting and RunicProduction plugins. Direct crafting/output preparation denials and unchanged inventory are checked, in addition to the normal runtime checks. Deliberately rejected handshake cases log expected "Connection rejected" errors; those are test assertions, not startup failures. Look for the final `RDP_SMOKE_PASS` marker. A standalone run and a run with both optional integrations have passed.

The actual loaded catalog maps more than 1,300 item/piece prefabs. Remaining names include NPC attacks, unused/developer items and some items needing an explicit administrator mapping; default unknown-item behavior and strict mode are described in README.md. Smoke reports export the complete mapped/unmapped lists and recipe/boss evidence beside the isolated test config.

## Still required before production rollout

These are not claimed as completed by the isolated checks:

1. Connect two real clients to a dedicated test server; check policy updates, missing-mod rejection and reconnect behavior over the actual Steam/PlayFab transport.
2. Perform actual player deaths in Swamp/Mountains before and after Bonemass, including ordinary and marked grave recovery after saving/restarting the world.
3. Test a dungeon entrance/reconnect and coastal border; verify restrictions on mining, tree drops, pickups, fishing, native chest withdrawals and merchant purchases.
4. Exercise the full deployed mod pack, including custom items, remote storage, auto-production and any other death/skill mods. Assign tiers for the server's unmapped custom content.
5. Confirm the chosen multiplier and optional recovery budget with the administrator before deploying to an existing group world.

The release build does not install itself into a live profile or deploy to production.
