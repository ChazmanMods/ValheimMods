# ArcaneHunter validation

## 0.2.0 pickup validation

The production pickup prefix is exercised against a small mutable drop registry and inventory adapter: range boundaries, swap-removal during collection, toggle/master disabling, remote-player exclusion, placed/tar/disabled drops, unique quest items, inventory slots, per-item weight checks, ownership retry, death, batching and throttling. Installed game assemblies are separately checked for the pickup patch, injected static toggle and reflected drop registry.

Live acceptance remains pending: test loose drops at 2/100/500 meters within loaded areas, full inventory, maximum carry weight, V-toggle off/on, a remote-owned drop with two clients, dense drops, item displays, tar, death/logout and pickup during ArcaneSniper flight. Confirm disabling restores vanilla range. Update server and all clients to 0.2.0 because the mod requires matching versions.

## Automated

Run `dotnet run --project Tests/ArcaneHunter.Tests.csproj -c Release` after building the mod. The executable throws and exits nonzero on failure. It compiles the actual spawn prefix against small spawn-data adapters; production game/Jötunn/Unity calls and Harmony patch targets are independently resolved against the installed assemblies using Mono.Cecil. This validates signatures, not live Harmony execution or Unity behavior.

Coverage includes exact vanilla defaults, non-compounding spawn changes, index preservation, zero-chance disable, weather-list isolation, raid exclusion, bounded rates, custom rule errors, journal persistence, duplicate kill receipts, one-time rewards, daily rotation, disabled commissions, permanent seals, and all patch entrypoints.

## In-game acceptance checklist — pending

1. Install in a separate test profile with BepInEx and Jötunn. Enter a world; verify the log reports ArcaneHunter 0.1.0 without Harmony errors and the config has 39 creature sections.
2. Press F6; inspect all five tabs at 1080p and a smaller resolution. Test scrolling, search, mouse buttons, closing via Escape, and resuming normal movement/camera control. Confirm chat/inventory/menu input does not trigger tracking keys.
3. Press F7 near wild deer. Verify stamina cost, cooldown, species observations, clue aging/expiry, focus filtering, and changing bearings as you move. Move the animal and confirm its clue remains at the observed position. Verify empty searches also consume stamina and cooldown.
4. Make a natural full-health/unalerted kill and a 40m+ bow kill. Verify records, commission completion and seals. Killing tamed animals, bosses or cheat-flagged creatures should not award hunting records.
5. Save/logout/rejoin; confirm journal, focus, earned seals and current daily commission progress survive. Move to a new biome on the same day and verify the board stays fixed. Check next-day rotation.
6. Compare serpent baseline with 3x chance / 2x frequency; verify valid ocean/weather/time restrictions still apply. Test chance 0 and restore to 1; existing animals must not be deleted. Confirm raid spawns remain unaffected. Use adequate observation samples for probabilistic spawning.
7. Dedicated server plus two clients: verify host settings synchronize, mismatched/missing mod versions are rejected, and only the final hitter receives a record when the other client owns the animal. Repeat after moving between ownership areas and after reconnecting. Confirm unrelated creature deaths do not grant credit.
8. Test with the intended modpack, particularly other spawn, creature level, input, inventory and UI mods. Review logs for exceptions.

## Known scope limits

- No live game or multiplayer session has been exercised by the automated harness.
- Tracking sees only loaded characters; it can sense through terrain.
- Wind hints do not alter Valheim AI detection.
- Cosmetic journal receipts are not a server-authoritative anti-cheat system.
- Hit bonuses require matching local hit metadata; ownership handoffs or damage-over-time can lose that bonus.
- Only ambient spawning is configurable; some creatures use location spawners instead.
- A zeroed-out species can still be chosen for a daily commission.
- Unload via a plugin hot-reloader is not supported; restart the game after removing the DLL.
