# Chazman Mods for Valheim

Public source for the Runic mods, maintained by Charles Sammons (Chazman).

## Downloads and source

Download the current packaged mods from [GitHub Releases](https://github.com/ChazmanMods/ValheimMods/releases).
The table below records the latest local release packages synchronized on September 14, 2026.
Thunderstore availability and moderation status are independent of GitHub downloads.
Each mod is independently versioned. Suite ZIPs install dependencies through Thunderstore;
manual installations also need the dependencies listed in each manifest.

The source column describes this checkout. Where it is newer than the package column,
that code is development work and is not the code shipped in that listed package.
A source sync does not constitute in-game acceptance of development versions.

| Mod | Packaged version | Source version |
| --- | --- | --- |
| [RunicAgriculture](./RunicAgriculture/) | 1.0.3 | 1.0.3 |
| [RunicAwareness](./RunicAwareness/) | 1.0.2 | 1.0.2 |
| [RunicBuildCamera](./RunicBuildCamera/) | 1.0.3 | 1.0.3 |
| [RunicCharacterVault](./RunicCharacterVault/) | 1.0.2 | 1.0.2 |
| [RunicClock](./RunicClock/) | 1.0.1 | 1.0.1 |
| [RunicCrafting](./RunicCrafting/) | 1.1.0 | 1.1.0 |
| [RunicDisplayStands](./RunicDisplayStands/) | 1.3.8 | 1.3.8 |
| [RunicExploration](./RunicExploration/) | 1.0.2 | 1.0.2 |
| [RunicInteraction](./RunicInteraction/) | 1.0.4 | 1.0.4 |
| [RunicInventory](./RunicInventory/) | 1.1.5 | 1.1.5 |
| [RunicModClientSuite](./RunicModClientSuite/) | 1.0.24 | 1.0.24 |
| [RunicModServerSuite](./RunicModServerSuite/) | 1.0.16 | 1.0.16 |
| [RunicModSuite](./RunicModSuite/) | 1.2.34 | 1.2.34 |
| [RunicPortals](./RunicPortals/) | 1.2.4 | 1.2.4 |
| [RunicPrecisionBuildTool](./RunicPrecisionBuildTool/) | 2.0.4 | 2.0.4 |
| [RunicProduction](./RunicProduction/) | 1.0.6 | 1.0.7 |
| [RunicSafety](./RunicSafety/) | 1.0.5 | 1.0.5 |
| [RunicSentinel](./RunicSentinel/) | 1.4.0 | 1.4.2 |
| [RunicSentinelClient](./RunicSentinelClient/) | 1.0.1 | 1.0.1 |
| [RunicSentinelServer](./RunicSentinelServer/) | 1.1.0 | 1.1.2 |
| [RunicSigns](./RunicSigns/) | 1.0.3 | 1.0.3 |
| [RunicStorage](./RunicStorage/) | 1.2.4 | 1.2.4 |
| [RunicVelocity](./RunicVelocity/) | 1.0.2 | 1.0.2 |
| [RunicWorldEngine](./RunicWorldEngine/) | 1.2.0 | 1.2.0 |

Package filenames and SHA-256 hashes are recorded in [the release catalog](./docs/RELEASE-CATALOG.json).
RunicStorage 1.2.4 includes the in-game-confirmed Quick Stack fix. Its changelog explains the cause.

## Building and contributing

See [BUILDING.md](./BUILDING.md) for prerequisites, build commands, and testing guidance.
Per-mod READMEs and changelogs describe configuration and behavior. Preserve third-party notices
when modifying or distributing code. Report bugs through [GitHub Issues](https://github.com/ChazmanMods/ValheimMods/issues)
with the mod version, game version, reproduction steps, and a log with private information removed.

Other source directories include independent experiments and historical Foundation components.
Their presence here does not mean they are required by current Runic mods or recommended for installation.
Current package manifests are the authority for dependencies.

## Community and support

[Chazman Mods Discord](https://discord.gg/7HKHTCdFqY)

## License

Original Runic source code and accompanying documentation are licensed under the
[MIT License](./LICENSE), copyright (c) 2026 Charles Sammons (Chazman).
You may use, modify and redistribute that code, including commercially, provided
you retain the copyright and license notices.

Third-party code and assets retain their original licenses and copyright notices.
Any component-specific LICENSE or NOTICE files must be preserved; the root license
does not replace them. Valheim, Unity and other third-party game or runtime assets
are not licensed by this repository. Logos and artwork are not covered by the MIT
code license unless explicitly stated otherwise.
