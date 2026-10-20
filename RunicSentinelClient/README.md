# Runic Sentinel Client 1.0.5

The lightweight companion for players joining a Runic Sentinel server. It reports the installed mod profile when the server requests it and receives administrator-authorized character actions.

## Installation

Install through r2modman or Thunderstore Mod Manager. Players use this package; administrators use full **RunicSentinel** for the F3 dashboard. Keep your existing configuration when updating. The lightweight client remains inactive on a dedicated server or listen host.

## Host-controlled building and terrain limits

Players need only this **RunicSentinelClient 1.0.5** package to use the building-height and digging-depth feature. Full Sentinel is not required on ordinary players' computers.

The host runs the matching **RunicSentinel 1.6.1** and enables custom limits in **F3 → World → Building and terrain**, saves the settings and restarts the host. This client receives and acknowledges those settings before world admission and applies the same terrain and structural-support patches. No client setting or administrator permission is needed. Joining a vanilla-limit host uses vanilla limits; disconnect and world changes reset received settings.

If both full Sentinel and Client are installed, Client defers these patches to an active full Sentinel implementation, avoiding duplicate support scaling. The lightweight package adds no server administration or host configuration controls.

## Player actions

Your server administrator can select your online character to raise or reset skills, heal, clear food or status effects, set adrenaline, apply status effects, and enable or disable God, Ghost, Fly or No-cost. Cheat actions require your own `confirmcheats` acknowledgment in F5. Skill changes are saved with your character.

## Language files

The mod follows Valheim's selected language. Translation files are in `Translations/RunicSentinelClient` beside the DLL. Missing translations use English. See `TRANSLATING.md` for instructions.

Questions and logs: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)

## Character mode and status controls

Use RunicSentinel 1.6.1 on the administrator client, RunicSentinel 1.6.1 on the dedicated server, and RunicSentinelClient 1.0.5 or full RunicSentinel 1.6.1 on each player receiving character actions.

In **Players**, select an online character and choose **On** or **Off** beside God, Ghost, Fly or No-cost. **Refresh character mode status** reports that character's current modes. Grants last for the current connection and character; previous mode settings are restored on disconnect or character change. The affected player must run `confirmcheats` in their own F5 console before receiving cheat actions.

Choose a status effect and enter **Duration (seconds)**. Leave duration blank for the effect's default, or enter `0` for indefinite duration. `3600` is one hour. Shields offer damage absorption; Burning and Poison offer total effect damage. Leave intensity blank for the effect's default. When intensity is entered without duration, the selected effect's default duration is used.

The full administrator client takes ownership of character actions when both Sentinel client packages are installed.


Administrator actions no longer show Sentinel recipient popups or synchronous action-generated HUD notices. Normal gameplay notices remain active, and success/failure replies still reach the administrator. Host building settings are available in the full Sentinel **F3 → World → Building and terrain** section.
