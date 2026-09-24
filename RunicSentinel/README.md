# Runic Sentinel 1.5.0

Manage your Valheim server from the F3 dashboard. Choose the approved mods, manage administrators and bans, browse game content, run commands and view player reports.

## Installation and setup

1. Install **RunicSentinelServer** on the dedicated server and **RunicSentinel** on the administrator's client. A listen host uses full RunicSentinel. Use only one of these two packages in each profile.
2. Install the dependencies listed by your mod manager, including Runic Safety. Ordinary players use **RunicSentinelClient**.
3. Start with **Remote Admission → Policy = Optional** in the server configuration.
4. Add your account ID to the server's `adminlist.txt`, join the game and press **F3**.
5. Select **Set Up Sentinel**, choose your mod policy, and select **Apply & Sign Policy**.
6. When your players' mod profiles are ready, set admission to **Required** and restart the server.

Keep the server's private key in `BepInEx/config/RunicSentinel/server-private` on the server. Keep your existing Sentinel configuration when updating.

## F3 dashboard

- **Mod Policy:** search mods by name, choose Required, Allowed, Greylisted or Blocked, then select **Apply & Sign Policy**. Advanced rules let you specify versions and file restrictions.
- **People:** filter all people, online accounts, administrators or banned accounts. Manage administrator rights and bans, kick connected players, review recent activity and create player reports.
- **Commands:** enter a command and press Enter or **Run**. Browse the command guide, read command output, and toggle debug mode or no-cost building. Sentinel authorizes each request; no separate Server Devcommands mod is required.
- **Catalog:** filter by mod or type and select an item or piece for its icon, internal ID, details and recipe. Build its arguments using quantity, applicable quality and qualifier controls, then append them to the selected console command. Review the console line and press Run.
- **Players:** select an online character to view observations and last-known position, use character commands, teleport, bring them to you or kick them. Administrator names carry an Administrator suffix. Account roles, bans and reports are managed in People.
- **Server Cap:** with Runic World Engine installed on the host, choose 2–64 players and select **Save for Next Restart**. Restart the host to apply the saved limit.

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

Administrators use the Players dossier in RunicSentinel 1.5.0 with RunicSentinelServer 1.2.0. Inline tools target the selected online character: teleport to them, bring them, move them to coordinates/another player, raise or lower/reset a skill, heal, clear food/status, set adrenaline, and apply a registered status effect. Skill changes follow native 0–100 clamping and character-save behavior.

The affected player needs either full RunicSentinel 1.5.0 or lightweight RunicSentinelClient 1.0.3 for character actions. Native teleport does not require the new receiver. Client-side cheat actions require the affected player's own prior confirmcheats acknowledgment; the administrator cannot silently acknowledge it for them. The UI disables unavailable receivers and waits for the exact target client's result. A timeout or disconnect is explicitly unconfirmed, not success. No arbitrary remote console execution is provided. Requests and replies are recorded in player activity.

## Integrated command engine

The Server Devcommands 1.109 command/feature baseline is integrated into RunicSentinel 1.5.0 and RunicSentinelServer 1.2.0. Do not install the separate Server Devcommands plugin alongside these packages. Native Valheim commands remain available.

Examples:
- `addstatus Rested 3600` applies one hour of Rested; `addstatus Rested 6000` applies 100 minutes.
- `addstatus Burning 20 100` supplies duration and supported effect intensity.
- `alias rest addstatus Rested 3600` creates a shortcut; run `rest` from F3 or F5.
- `bind F7 addstatus Rested 3600` stores a binding.
- `fly; wait 1000; fly` runs a chain with a delay in milliseconds. Each executable command receives fresh Sentinel authorization.

The command guide includes registered parameters and completion values. `dev_config` exposes the integrated gameplay/configuration options; `permissions` manages command and feature restrictions on the server. Automatic devcommands defaults off. Valheim's own explicit cheat confirmation remains required for cheat handlers.

Command configuration uses RunicSentinel.alias*.yaml, RunicSentinel.binds*.yaml and RunicSentinel.commands.permissions.yaml in BepInEx/config. Previous third-party configuration is not silently imported. The dedicated-server binary excludes client GUI/input modules. YAML parsing is embedded; there is no separate parser or command-mod installation step.
