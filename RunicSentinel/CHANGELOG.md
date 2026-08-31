# Changelog

## 1.0.0 - 2026-08-22

- Added direct pre-PeerInfo Sentinel claims through Runic Persistence with explicit Disabled,
  Optional-default, and Required admission outcomes. Required enforces the server's verified signed
  policy digest/sequence/profile; all client snapshot/hash/disposition values remain explicitly
  self-reported compatibility evidence.
- Added current-session challenge/timestamp freshness, bounded transport-identity evidence,
  replay/equivocation/rollback detection, and per-identity rate isolation without any remote-admin,
  quarantine, or ban claim.

- Replaced the forgeable same-process HMAC design with strict RSA-3072/SHA-256 PKCS#1 v1.5
  verification of exact `RUNIC-SENTINEL/2` bytes. Sentinel loads only a public key whose exact
  canonical-file SHA-256 is pinned in configuration.
- Added strict public-key and signature-file canonicalization, policy sequence/issue/expiry fields,
  and in-process rollback/equivocation rejection.
- Moved attestation, admission, and evidence interfaces to Runic Core and corrected the canonical
  capability from `security.attestation` to `security.attest`.
- Renamed the public nonce digest to an unauthenticated nonce binding and explicitly reports that it
  is neither client-authenticity proof nor an authoritative transport.
- Added exact-Core-lease evidence-provider registration, per-provider fair queues, immutable reads,
  requested/effective action, policy sequence, and saturating accepted/drop counters.
- Made worker publication generation-safe, cancellation-gated, platform-path-correct, and deduplicated
  so multiple plugin descriptors sharing one path hash that file only once.
- Made `Enabled = false` startup-inert: no worker, Core module, or service is created.

## 0.1.0

- Hardened plugin and signed-policy input reads against size-check/read races: hashing consumes the
  exact admitted length through one reusable bounded buffer, and policy/signature/key streams must
  remain byte-exact and metadata-stable through EOF before verification.

- Added bounded signed-policy parsing and HMAC-SHA256 verification with fail-closed monitor-only fallback.
- Added deterministic loaded-plugin attestation, fresh-nonce response, admission policy, and a 256-entry evidence ledger.
- Published `security.attestation`, `security.admission`, and `security.evidence` protocol 1.0 services.
- Deliberately deferred connection enforcement until an authenticated server/client transport exists.
