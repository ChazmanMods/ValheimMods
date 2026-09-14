# Runic Sentinel Client 1.0.1

Runic Sentinel Client is the small player-side counterpart for a server using Runic Sentinel's
admission workflow. It observes the BepInEx plugins loaded in the local process, hashes their DLLs
on a bounded background worker, and reports the resulting ID, version, and SHA-256 profile when the
authoritative server issues a private direct-connection challenge.

Version 1.0.1 targets Valheim 1.0.7 and preserves Valheim's native invite secret when an approved
Required-mode admission resumes the 1.0 server handshake.

The report is compatibility evidence from a client-controlled process, not unforgeable proof. The
server reconstructs the canonical profile and makes every policy, role, ban, and admission decision.

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
