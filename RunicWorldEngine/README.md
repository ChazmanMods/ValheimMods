# Runic World Engine 1.2.5

Monitor your Valheim host, configure its player capacity and smooth scheduled saves.

## Quick setup

Install on the dedicated server or the player hosting the world. Start once to create `BepInEx/config/chazman.RunicWorldEngine.cfg`.

To host up to 20 players, set:

```ini
[Player Capacity]
Enabled = true
MaximumPlayers = 20
```

Restart the host to apply the capacity setting. The configurable range is 2–64 players.

## Health reports

Use `runicworld_status` in the local F5 console for the latest report. On a dedicated server, enable periodic summaries:

```ini
[Diagnostics]
LogPeriodicSummary = true
SummaryIntervalSeconds = 30
LogPeerDetails = true
```

Reports show connected players, round-trip latency, traffic, queued data and server health. Sustained health warnings and recovery messages help you see when connections improve. Adjust thresholds under **Server Health**.

## Save settings

**Save Smoothing** is enabled by default. Configure its frame budget and maximum deferral in the host configuration.

## Installation

Requires BepInExPack Valheim. Connecting players do not need World Engine for the host's monitoring or player cap. An optional client installation reports that player's local connection.

## Languages

The mod follows Valheim's selected language, with English fallback. Keep the included Translations folder beside the mod DLL when installing manually.

## Support

[Runic Mods Discord](https://discord.gg/7HKHTCdFqY) · [Support My Work](https://buymeacoffee.com/the_artful_engineer)
