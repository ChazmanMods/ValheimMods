# Runic Sentinel 1.6.1

Manage your Valheim server from the F3 dashboard. Choose approved mods, manage administrators and bans, run commands, view player reports, and adjust building and terrain limits.

Self-reported plugin profiles provide compatibility evidence, not unforgeable proof of an unmodified client. Admission rules and administrator permissions remain server-managed.

## Installation and setup

1. Install **RunicSentinel 1.6.1** on the host (dedicated or listen server) and administrator clients. Ordinary players need only **RunicSentinelClient 1.0.5**.
2. Install the dependencies listed by your mod manager, including Runic Safety for full Sentinel. Do not install RunicSentinelServer alongside full RunicSentinel.
3. Start with **Remote Admission → Policy = Optional** in the host configuration.
4. Add your account ID to the host's `adminlist.txt`, join the game and press **F3**.
5. Select **Set Up Sentinel**, choose your mod policy, and select **Apply & Sign Policy**.
6. When your players' mod profiles are ready, set admission to **Required** and restart the host.

Keep your existing configuration and the host's private key in `BepInEx/config/RunicSentinel/server-private`. When replacing RunicSentinelServer, preserve its configuration and policy files. Before first launch, copy `chazman.RunicSentinelServer.cfg` to `chazman.RunicSentinel.cfg` if the latter does not already exist. Remove the Server package. RunicSentinelServer 1.3.0 does not provide the new building and terrain controls. Existing signed mod profiles may need updating for the new plugin versions.

## Taller buildings and deeper terrain

Open **F3 → World → Building and terrain**:

1. Check **Enable custom building and terrain limits**.
2. Set the vertical support multiplier, maximum terrain raise and maximum digging depth.
3. Select **Save building limits for next restart**, then restart the host and rejoin.

The panel shows running limits and restart status. The World tab replaces Server Cap and retains its player-cap controls below. Changes require host administrator authorization; stale edits are rejected.

The support multiplier ranges from **1–10**. A value of 2 halves vertical support loss; it does not guarantee twice the building height because materials, connections and horizontal spans still matter. Terrain limits range from **1–64 metres**, measured above or below the original ground, including already excavated terrain. Defaults are disabled, with support 1, raise 8 and dig 8. Water level and the inability to dig tunnels are unchanged.

Ordinary players only need SentinelClient. The host sends its limits before world admission; player configuration cannot override them. Custom-limit hosts require compatible acknowledgements even with Optional admission. This is a compatibility check, not anti-cheat authentication. Connections to an unmodified host retain vanilla limits.

Back up your world before enabling custom limits. Lowering limits, disabling the feature or removing the mod can clamp extended terrain and cause tall structures to collapse. Retain your limits after save/reload. Manual configuration is also available under `[Building and Terrain]` in `BepInEx/config/chazman.RunicSentinel.cfg`.

## F3 dashboard

- **Mod Policy:** search mods by name, choose Required, Allowed, Greylisted or Blocked, then select **Apply & Sign Policy**. Advanced rules let you specify versions and file restrictions.
- **People:** filter all people, online accounts, administrators or banned accounts. Manage administrator rights and bans, kick connected players, review recent activity and create player reports.
- **Commands:** enter a command and press Enter or **Run**. Browse the command guide, read command output, and toggle God, Ghost, Fly, Freefly, debug mode or no-cost building with live On/Off indicators. Sentinel authorizes each request; no separate Server Devcommands mod is required.
- **Catalog:** filter by mod or type and select an item or piece for its icon, internal ID, details and recipe. Build its arguments using quantity, applicable quality and qualifier controls, then append them to the selected console command. Review the console line and press Run.
- **Players:** select an online character to view observations and last-known position, use character commands, teleport, bring them to you or kick them. Administrator names carry an Administrator suffix. Account roles, bans and reports are managed in People.
- **World:** adjust building support, terrain raising and digging limits. With Runic World Engine on the host, also choose 2–64 players. Saved changes apply after restarting the host.

## Administrator access

Server administrators have Sentinel access by default. Add your account ID to the server's `adminlist.txt`; for Steam, use `V_<SteamID64>`. Close and reopen F3 after changing the list.

To use only Sentinel roles, set `UseServerAdminList = false` under **Administrator Access** in the server configuration. Remove separately assigned Sentinel roles in the People tab when revoking access. Bans take precedence over administrator roles.

## Commands and reports

- `runic_sentinel status` shows the current policy and admission status.
- `runic_sentinel report` saves a support report in `BepInEx/config/RunicSentinel/reports`.
- `runic_sentinel networks` saves a snapshot of loaded portal and production networks from the host console.

## F3 player and command dashboard

The Players tab refreshes server observations every ten seconds. Select a name for sampled movement, last-known coordinates and gameplay actions. People contains activity and report controls. Object lists use current creator metadata. JSON, CSV and TXT exports stay under the server's `BepInEx/config/RunicSentinel/PlayerReports`. A local preview contains only the bounded records displayed in F3.

The Commands tab uses the installed command descriptions, a left console, a right guide and the content catalog below. Load arguments into the console and press Run or Enter; Up/Down recalls recent commands. Sentinel implements its own authenticated command routing; no separate Server Devcommands mod is required. World commands execute on the server and player-local commands execute on the authorized administrator client. Valheim's cheat confirmation still applies. Console output distinguishes handler completion from pending asynchronous actions.

## Language files

The mod follows Valheim's selected language. Add translations in `Translations/RunicSentinel` beside the mod file. Missing translations use English. See `TRANSLATING.md` for instructions.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)

## Dashboard update

Drag the lower-right corner to resize F3, or select Fit screen. Run, Clear Output and Insert into console remain outside the command-list scroll area. Use Arguments to browse parameters and installed completion options; hover a parameter for its explanation. Dynamic values depend on the installed command's completion provider.

People combines connected/session players with accounts from the native server access lists and signed policy. Administrator and Banned changes save native server files with backups; revoking a signed role also requires the server signing key. Native grants require UseServerAdminList=true. Self-removal is blocked to prevent lockout. Kick targets the authenticated connection. Offline access-list entries have no invented character location.

Admin Tools creates distinct health and network text reports, retains server copies and downloads complete copies to BepInEx/config/RunicSentinel/Reports on the administrator client. The network scan is a bounded sample of configured Runic links, not a full-world topology reconstruction. Player report previews remain explicitly bounded. Enforcement displays recent server evidence and explains threshold tradeoffs. The policy label names the signed rules, not an r2modman profile.

## Selected-player actions

Administrators use the Players dossier in RunicSentinel 1.6.1 with full Sentinel on the host. Inline tools target the selected online character: teleport to them, bring them, move them to coordinates/another player, raise or lower/reset a skill, heal, clear food/status, set adrenaline, and apply a registered status effect. Skill changes follow native 0–100 clamping and character-save behavior.

The affected player needs either full RunicSentinel 1.6.1 or lightweight RunicSentinelClient 1.0.5 for character actions. Native teleport does not require the new receiver. Client-side cheat actions require the affected player's own prior confirmcheats acknowledgment; the administrator cannot silently acknowledge it for them. The UI disables unavailable receivers and waits for the exact target client's result. A timeout or disconnect is explicitly unconfirmed, not success. No arbitrary remote console execution is provided. Requests and replies are recorded in player activity.

## Integrated command engine

The Server Devcommands 1.109 command/feature baseline is integrated into RunicSentinel 1.6.1 and RunicSentinelServer 1.3.0. Do not install the separate Server Devcommands plugin alongside these packages. Native Valheim commands remain available.

Examples:
- `addstatus Rested 3600` applies one hour of Rested; `addstatus Rested 6000` applies 100 minutes.
- `addstatus Burning 20 100` supplies duration and supported effect intensity.
- `alias rest addstatus Rested 3600` creates a shortcut; run `rest` from F3 or F5.
- `bind F7 addstatus Rested 3600` stores a binding.
- `fly; wait 1000; fly` runs a chain with a delay in milliseconds. Each executable command receives fresh Sentinel authorization.

The command guide includes registered parameters and completion values. `dev_config` exposes the integrated gameplay/configuration options; `permissions` manages command and feature restrictions on the server. Automatic devcommands defaults off. Valheim's own explicit cheat confirmation remains required for cheat handlers.

Command configuration uses RunicSentinel.alias*.yaml, RunicSentinel.binds*.yaml and RunicSentinel.commands.permissions.yaml in BepInEx/config. Previous third-party configuration is not silently imported. YAML parsing is embedded; there is no separate parser or command-mod installation step.

## Character mode and status controls

Use RunicSentinel 1.6.1 on the administrator client, RunicSentinel 1.6.1 on the dedicated server, and RunicSentinelClient 1.0.5 or full RunicSentinel 1.6.1 on each player receiving character actions.

In **Players**, select an online character and choose **On** or **Off** beside God, Ghost, Fly or No-cost. **Refresh character mode status** reports that character's current modes. Grants last for the current connection and character; previous mode settings are restored on disconnect or character change. The affected player must run `confirmcheats` in their own F5 console before receiving cheat actions.

Choose a status effect and enter **Duration (seconds)**. Leave duration blank for the effect's default, or enter `0` for indefinite duration. `3600` is one hour. Shields offer damage absorption; Burning and Poison offer total effect damage. Leave intensity blank for the effect's default. When intensity is entered without duration, the selected effect's default duration is used.

The full administrator client takes ownership of character actions when both Sentinel client packages are installed.



Administrator actions no longer show Sentinel recipient popups or synchronous action-generated HUD notices. Normal gameplay notices remain active, and success/failure replies still reach the administrator.

## Adjustable Commands layout

In **F3 → Commands**, use **A- / A+** to change text size from 80% to 150%. Drag the vertical divider between Console and Command guide to change their widths, or the horizontal divider above Loaded content to change the upper-section and Catalog heights. Each neighboring section adjusts with it. Minimum sizes grow with the text; scroll the tab when your window is too small. **Reset layout** restores the default text and section proportions. These preferences last for the current game session.
