# Ghostbuster

Fixes stale player copies left at portal entrances after the real player has teleported away.

**Server-side only. No configuration required.** Install on your dedicated server or hosting player's game; joining players do not need Ghostbuster.

## Installation

Install on the **dedicated server**, or on the **hosting player's game** for a hosted world. Other players do not need the mod. Installing it only on a non-host client does not fix the server.

Requires [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Install Ghostbuster into the host's profile using your mod manager, import the release ZIP for local testing, or copy `BepInEx/plugins/Ghostbuster/Ghostbuster.dll` from the ZIP into the same path under your server/game installation. Restart the server or host after installation.

## Scope

Valheim checks whether observers should forget a player during a sector change, before storing the player's new position. A long-distance teleport can therefore miss the entrance observer's removal notification.

Ghostbuster repeats that existing check **after** the new position has been stored, for the standard `Player` prefab on the server only, when the player changes sectors. Valheim's normal ownership and visibility checks decide which observers need notification. Ordinary player sector crossings also pass through this correction; there is no teleport-specific network flag to rely on.

One Harmony patch. No settings, RPCs, timers, extra dependencies beyond BepInEx, save data, or changes to portal permissions, inventory restrictions, destinations, animation, cooldowns, or movement. Non-player objects are untouched. Non-host clients perform no correction.

This prevents future missed notifications. It does not scan for ghosts already present before installation. Restarting for installation clears those client sessions.

## Compatibility and verification

Built against the locally installed Valheim **1.0.16**, Steam build **25527674**. Automated checks cover the installed game IL and the production patch's behavior against a focused networking model. Live multiplayer validation is still required; other game versions and mods that replace player prefabs or sector replication are not verified.

To verify in game: leave one player beside an entrance, teleport another to a distant exit, then have the traveler remain within the destination's current 64-meter sector for at least a minute. The entrance observer should no longer retain the traveler. Repeat in both directions. Verify a nearby observer at the destination still sees the traveler and can watch them move normally.

Uninstall by removing `Ghostbuster.dll` and restarting. No save migration is needed.

## Build

Run `dotnet build Ghostbuster.csproj -c Release`. Override `VALHEIM_INSTALL` and `BEPINEX_PROFILE` with MSBuild properties if your installations differ. Run `dotnet run --project Tests/Behavior/Behavior.csproj -c Release` and `dotnet run --project Tests/Contracts/Contracts.csproj -c Release` for validation. Run `./package.ps1` after placing the generated `icon.png` alongside this README.

Original ghost-removal artwork. This independent game mod is not affiliated with or endorsed by the Ghostbusters franchise.
