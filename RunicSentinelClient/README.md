# Runic Sentinel Client 1.0.3

Runic Sentinel Client is the small player-side counterpart for a server using Runic Sentinel's
admission workflow. It observes the BepInEx plugins loaded in the local process, hashes their DLLs
on a bounded background worker, and reports the resulting ID, version, and SHA-256 profile when the
authoritative server issues a private direct-connection challenge.

Version 1.0.1 targets Valheim 1.0.7 and preserves Valheim's native invite secret when an approved
Required-mode admission resumes the 1.0 server handshake.

## Deliberately not included

This package contains no server policy engine, administrator channel, signing or private-key code,
backup integration, enforcement service, evidence recorder, support report, topology exporter, or
in-game administrator UI. It does not receive the server's policy, signature, public key, private
key, administrator list, or ban list.

The lightweight and full Sentinel packages may coexist in an administrator profile. Each checks
the exact direct-RPC handler before registering, so one active v2 responder owns the endpoint and
the other defers without overwriting it. This also avoids mistaking an installed-but-disabled or
legacy full Sentinel for an active responder. The lightweight runtime remains inert whenever the
local Valheim process is the authoritative server or listen host.

## Connection behavior

- The client registers one bounded v2 method on the connection's direct `ZRpc`; it does not use
  routed world RPCs.
- A server challenge is valid for at most two minutes and is bound to one random request and nonce.
- Reports contain at most 512 plugins and the complete frame is capped at 256 KiB.
- In Required mode, an accepted server decision may explicitly direct the client to resume the
  native `ServerHandshake` exactly once. Optional decisions never request that resume.
- The client never decides that it should be admitted and never supplies an identity or role.

## Installation

Install the package in the player profile through Thunderstore Mod Manager or r2modman. No policy
files are needed on the client. Server operators should leave admission Optional until they have
reviewed and signed an expected client profile.

Questions and logs: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)

## Language files

This version follows Valheim's selected language using files in `Translations/RunicSentinelClient` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)

## Selected-player actions

Administrators use the Players or People dossier in RunicSentinel 1.4.6 with RunicSentinelServer 1.1.6. Inline tools target the selected online character: teleport to them, bring them, move them to coordinates/another player, raise or lower/reset a skill, heal, clear food/status, set adrenaline, and apply a registered status effect. Skill changes follow native 0–100 clamping and character-save behavior.

The affected player needs either full RunicSentinel 1.4.6 or lightweight RunicSentinelClient 1.0.3 for character actions. Native teleport does not require the new receiver. Client-side cheat actions require the affected player's own prior confirmcheats acknowledgment; the administrator cannot silently acknowledge it for them. The UI disables unavailable receivers and waits for the exact target client's result. A timeout or disconnect is explicitly unconfirmed, not success. No arbitrary remote console execution is provided. Requests and replies are recorded in player activity.
