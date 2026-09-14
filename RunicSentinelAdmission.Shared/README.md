# Runic Sentinel admission v2 shared source

These files are an internal, Valheim-free protocol boundary. They are intentionally source-linked
into `RunicSentinelClient` and should be source-linked into the authoritative `RunicSentinel`
project. They are not a separately shipped assembly.

Server integration uses the following seams:

1. Register `AdmissionProtocolV2.DirectRpcName` on each exact peer's direct `ZRpc`.
2. Create and retain a per-peer `AdmissionChallenge` with
   `AdmissionProtocolV2.CreateChallenge(receivedUnixSeconds, lifetimeSeconds)`.
3. Send `AdmissionProtocolV2.EncodeChallenge(challenge)` inside a `ZPackage`.
4. Decode an incoming report with `AdmissionProtocolV2.TryDecodeReport`.
5. Validate it against the retained challenge and server receipt time with
   `AdmissionProtocolV2.TryValidateReport`. The returned `AdmissionClientProfile` is canonical and
   safe to translate into the server's policy-evaluation snapshot.
6. Return an `AdmissionDecisionMessage` encoded by `AdmissionProtocolV2.EncodeDecision`.

The v2 profile digest uses `RUNIC-SENTINEL-CLIENT-PROFILE/1` and exists to bind the report to the
challenge. Before calling Sentinel's existing `AdmissionPolicy`, translate every returned entry to
an `AttestedPlugin` with empty dependency/capability lists and compute a fresh existing
`RUNIC-ATTESTATION/1` snapshot digest. Do not pass the v2 digest into that older snapshot contract.

Only a Required-mode decision that the authoritative server has approved may set
`ResumeHandshake = true`. The client then invokes the native direct `ServerHandshake` method once.
The server's handshake prefix must consume a one-shot approved-resume state before allowing the
native method through. Optional-mode decisions use `ResumeHandshake = false`.

In Required mode the first `RPC_ServerHandshake` call creates/sends the challenge and withholds the
native method. After a valid report is authoritatively allowed, latch a one-shot approved-resume for
that exact `ZRpc`, send the allow+resume decision, and let the client's second `ServerHandshake`
invocation consume the latch and execute vanilla. In Optional mode, never withhold vanilla and
never set the resume flag.

Bind all challenge and replay state to the actual `ZRpc`/`ZNetPeer` object. Never accept a peer ID,
identity, role, policy result, or admission disposition from the report. Profiles contain only
plugin GUID, version, and SHA-256 compatibility evidence.
