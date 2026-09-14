# Changelog

## 1.1.0 - 2026-08-31

- Added typed contracts for signed Sentinel administrator roles, server-side automatic enforcement,
  and runtime-integrity status.
- Added the canonical `security.roles`, `security.enforcement`, and
  `security.runtime-integrity` capability IDs.

## 1.0.0 - 2026-08-25

- Normalized the coordinated Thunderstore release identity and dependency floor to 1.0.0.
- Preserved the accepted Core contracts and runtime behavior without feature changes.

## 0.1.1 - 2026-08-22

- Added Core-owned typed contracts for canonical `security.attest`, `security.evidence`,
  `security.admission`, `zdo.observe`, and `zdo.ownership` integration boundaries.
- Added the canonical `inventory.item-locks` single-item protection query and the bounded,
  metadata-free `safety.confirmation` high-impact confirmation contract.
- Added canonical `inventory.durable-operations` plus provider-neutral immutable intent, phase,
  local-primary preflight/readback, reconciliation-lock, and tombstone-custody contracts. The
  contract exposes no Valheim or live item types and does not claim profile-save acknowledgement.
- Added exact active-operation manifest recovery as a bounded cloned read. Foreign owners,
  mismatched operations, terminal evidence, and journal/mirror conflicts fail closed.
- Defined item-query domain semantics: `false` is not-applicable (never implicitly unlocked), while
  `true` plus `Unknown` is governed uncertainty that destructive consumers must fail closed on.
- Made world-data declarations immutable, unique-output bounded, and independently inspected-input
  bounded so duplicate enumerables cannot consume unbounded work.

## 0.1.0

- Add the `chazman.RunicCore` BepInEx bootstrap with no Harmony patches or gameplay changes.
- Add immutable `ModuleDescriptor`, Semantic Versioning 2.0 metadata, and major-generation
  protocol compatibility metadata.
- Add the process-wide, thread-safe `RunicRegistry.Shared` module/capability/service registry.
- Add deterministic typed service selection, explicit unregistration, disposable registration
  leases, automatic provider-service cleanup, and capability-change snapshots.
- Publish canonical suite capability IDs for permissions, containers, material reservations,
  persistence, networking, notifications, keybindings, startup work, security, and ZDO access.
- Add a dependency-free input chord model and exact-chord conflict registry for Runic and known
  vanilla bindings.
- Add an actionable notification bus requiring a reason and remedy, with bounded per-context rate
  limiting and isolated subscribers.
- Add explicit client-local, server-authoritative, and world-authoritative configuration semantics.
- Add a fully commented example configuration and deterministic console test suite.
