# Runic Sentinel 1.0.0

Runic Sentinel is a standalone BepInEx mod for bounded local plugin snapshots and signed policy
review. It has no Runic Foundation runtime dependency. It hashes loaded plugin files on a background
worker, canonicalizes the observations, and evaluates them against a strictly canonical
`RUNIC-SENTINEL/2` policy.

Policy verification uses RSA-3072 with SHA-256 and PKCS#1 v1.5. Sentinel loads only a verification
public key:

```text
RUNIC-RSA-PUBLIC/1
modulus=<canonical Base64 for exactly 384 bytes>
exponent=AQAB
```

The file ends in exactly one LF and its exact-byte SHA-256 must match
`Policy.TrustedPublicKeySha256`. The detached signature is the canonical Base64 encoding of exactly
384 signature bytes followed by one LF. The private signer belongs in external Server Forge or an
offline provisioning tool. Sentinel performs public verification only: no private key or shared
signing secret is configured, read, retained, or distributed by this mod.

The signed policy has a fixed line order and signs the exact bytes, including the final LF:

```text
RUNIC-SENTINEL/2
profile=2026.08.22
sequence=42
issued=1787356800
expires=0
unknown=Quarantined
rule=Required|example.mod|1.0.0|<64 lowercase hex or *>
```

Rules are ordered by plugin ID. A lower sequence, or a different payload at an already accepted
sequence, is rejected for the process lifetime. Invalid, absent, expired, oversized, changed,
unpinned, or incorrectly signed files leave Sentinel monitor-only.

## Compatibility exchange

When enabled, Sentinel owns two namespaced Valheim routed RPCs. A client sends its aggregate snapshot
digest, capture time, policy digest/sequence/profile, local disposition, exact Sentinel version, and a
short-lived request ID. The server acts only after rebinding the routed sender ID to the exact current
ready `ZNetPeer`. Messages are schema-bound, size-limited, exact-consumption decoded, and kept only in
bounded memory. There is no durable journal, global lock, recovery state, or shared RPC framework.

This exchange occurs after Valheim has authenticated and connected the peer. It is honest
compatibility evidence, not cryptographic proof that a full-trust client is clean. Only the server's
signed policy is authenticated.

`Remote Admission.Policy` is sampled at startup:

- `Optional` records bounded mismatch evidence without disconnecting the peer. This is the safe
  first-run default.
- `Required` disconnects the exact authenticated peer when the client version, snapshot, signed
  policy, timestamp, or allow disposition does not match.
- `Disabled` creates no RPC handlers or remote-evidence lease.

Use the same signed policy and key pin on the server and clients before selecting `Required`.

## Bounds and privacy

- At most 512 plugin descriptors, 512 MiB per unique DLL, and 4 GiB across unique DLL paths.
- Policy is capped at 1 MiB; public-key and signature files are capped at 1 KiB.
- Worker generations cancel superseded work, and stable reads reject concurrent file changes.
- Evidence has at most 32 local providers with eight entries each (256 total). One provider cannot
  evict another provider's allocation.
- Compatibility duplicate results are capped at 256 and expire after one minute. A client has one
  pending request, three attempts, and a six-second timeout.
- Evidence is process-memory only. Sentinel writes no world data and sends no telemetry.
- `Enabled = false` is startup-inert: no worker or network handlers are created.

Install Runic Sentinel and BepInEx on the machines where you want its policy behavior. No Runic Core,
Persistence, Permissions, or Transactions DLL is required.
