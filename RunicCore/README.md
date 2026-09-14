# Runic Core

Runic Core is the small, non-gameplay foundation for the Runic Valheim mod suite. It gives
independent plugins one stable place to publish discovery metadata, find typed services, report
input conflicts, and send concise actionable notifications. Version 1.1.0 installs no Harmony
patches and does not alter a world, player, inventory, ZDO, RPC, or save.

## What it provides

- Immutable module metadata: unique module ID, Semantic Versioning 2.0 version, protocol version,
  owned capabilities, and optional extension points.
- A process-wide, thread-safe module/capability/service registry at `RunicRegistry.Shared`.
- Deterministic typed service lookup and automatic service cleanup when a provider unregisters.
- Capability-change and module-change events with immutable before/after snapshots.
- Canonical capability IDs shared by the suite.
- Provider-neutral immutable contracts for a local-owner inventory crash journal, reconciliation
  lock, exact local-primary readback proof, and external tombstone-custody evidence. Core stores no
  journal and mutates no inventory; a gameplay provider implements the service.
- An engine-independent exact-chord keybinding conflict registry.
- A notification bus that requires both a denial/failure reason and a player-facing remedy.
- Per-source/code/context notification rate limiting with bounded memory use.
- Explicit `ConfigAuthority` values for local preferences, server rules, and world state.

## Minimal dependent-plugin API

Register the module during `Awake`, after Runic Core has loaded:

```csharp
using Runic.Foundation.Core;

private ModuleRegistration _module;
private ServiceRegistration _service;

private void Awake()
{
    _module = RunicRegistry.Shared.RegisterModule(new ModuleDescriptor(
        "runic.example",
        "Runic Example",
        "0.1.0",
        "1.0",
        new[] { RunicCapabilityIds.ContainerQuery }));

    _service = RunicRegistry.Shared.RegisterService<IContainerQuery>(
        RunicCapabilityIds.ContainerQuery,
        "runic.example",
        new ContainerQuery());
}

private void OnDestroy()
{
    _service?.Dispose();
    _module?.Dispose();
    // Equivalent explicit form when a lease was not retained:
    // RunicRegistry.Shared.UnregisterModule("runic.example");
}
```

Consumers use a typed, fail-open lookup:

```csharp
if (RunicRegistry.Shared.TryGetService<IContainerQuery>(
        RunicCapabilityIds.ContainerQuery,
        out IContainerQuery query))
{
    query.TryQuery(...);
}
else
{
    // Keep the caller's bounded vanilla-safe fallback or hide this integration.
}
```

`RegisterService<T>` requires the provider module to be registered and to have declared the
capability. Multiple providers are allowed. Lookup chooses the highest priority, then the
lexicographically smallest provider module ID, then the earliest registration. The overload with
an `int priority` is available when a capability contract defines a preferred provider policy.

Registration leases contain generation tokens. Disposing an old lease cannot accidentally remove
a later replacement that reused the same ID.

## Discovery and protocol metadata

Runic identifiers are lowercase dot-separated tokens containing ASCII letters, digits, and
internal hyphens. IDs are compared with ordinal, case-sensitive comparison; invalid or duplicate
IDs fail immediately with an actionable exception.

`SemanticVersion` accepts and orders SemVer 2.0.0 versions, including prerelease and build
metadata. `ProtocolVersion` uses `major.minor`; peers with the same protocol major are compatible,
and minor versions represent additive revisions. Runic Core 1.1.0 publishes protocol `1.0` through
`RunicCoreMetadata` and its own `ModuleDescriptor`.

Capability availability begins when at least one registered module declares the ID; a service is
not required. Subscribe to `RunicRegistry.Shared.CapabilityChanged` when an optional integration
must appear, disappear, or refresh after plugins load or a provider changes. Callbacks run after
the registry lock is released, and one failing subscriber is isolated from later subscribers.

## Canonical capability IDs

| Constant | ID | Contract boundary |
|---|---|---|
| `PermissionsEvaluate` | `permissions.evaluate` | Evaluate a canonical permission decision |
| `ContainerQuery` | `container.query` | Discover or query eligible containers without mutation |
| `ContainerTransfer` | `container.transfer` | Perform an authorized item transfer |
| `MaterialsReserve` | `materials.reserve` | Reserve a complete material cost |
| `MaterialsConsume` | `materials.consume` | Commit an authorized reserved cost |
| `PersistenceMigrate` | `persistence.migrate` | Version and migrate persisted records |
| `NetworkProtocol` | `network.protocol` | Advertise or negotiate a network contract |
| `NotificationPublish` | `notification.publish` | Publish actionable suite notifications |
| `KeybindingsRegistry` | `keybindings.registry` | Register bindings and inspect exact conflicts |
| `StartupMeasure` | `startup.measure` | Publish startup timing measurements |
| `StartupCache` | `startup.cache` | Provide validated startup-cache data |
| `SecurityAttest` | `security.attest` | Publish a bounded local plugin snapshot and non-auth nonce binding |
| `SecurityEvidence` | `security.evidence` | Register exact-lease providers and submit bounded review evidence |
| `SecurityAdmission` | `security.admission` | Publish non-authoritative local admission diagnostics |
| `SecurityRoles` | `security.roles` | Resolve signed administrator identities |
| `SecurityEnforcement` | `security.enforcement` | Report a server-observed violation for bounded enforcement |
| `SecurityRuntimeIntegrity` | `security.runtime-integrity` | Read Sentinel's current runtime integrity state |
| `InventoryItemLocks` | `inventory.item-locks` | Query one native item's bounded protection state |
| `InventoryDurableOperations` | `inventory.durable-operations` | Journal and reconcile one bounded local-owner inventory operation |
| `SafetyConfirmation` | `safety.confirmation` | Request one-shot confirmation for a high-impact operation |
| `ZdoObserve` | `zdo.observe` | Observe synchronized world-object state |
| `ZdoOwnership` | `zdo.ownership` | Register bounded world-data declarations using active module leases |

Runic Core itself also owns `foundation.modules` and `foundation.services`. Feature contracts may
add more specific dot-separated suffixes without changing a canonical root's meaning.

`IItemProtectionQuery` separates domain membership from protection state. `false` means the native
object is outside that provider's governed domain, so the consumer applies its own independent
policy and never infers that the item is unlocked. `true` with `Unknown` means the provider governs
the item but cannot prove its state and destructive consumers fail closed; `true` with `Unlocked`
or `Locked` is an exact decision. Providers never retain the native object after the synchronous call.

`IInventoryDurableOperationService` is deliberately engine-independent. Intents contain only a
UUID, bounded module/purpose/tag identifiers, one opaque copied manifest, and canonical SHA-256
evidence. Snapshots expose immutable phase/readback state and optional tombstone custody; numeric
world/player IDs in custody are correlation hints, not identity proof. Consumers must call
`TryCheckCurrentPrimarySupport` before they journal, tag, or dispatch a remote mutation. A successful
`TryBegin` proves the provider's independent journal was admitted; it never treats Valheim's profile
save call as a durable acknowledgement. Recovery consumers can call `TryReadExactManifest` only for
their exact active owner/operation pair; the provider returns a bounded defensive copy and denies
terminal, corrupt, or mirror-conflicting evidence.

Core itself grants no authority. Its attestation interface exposes `ProvidesClientAuthenticityProof`,
and Sentinel reports false because client DLL claims are self-reported compatibility evidence.
Sentinel's enforcement contract accepts reports only from active registered modules and acts only
on the authoritative server; its result says whether a disconnect actually ran. Provider,
enforcement, confirmation, and world-ownership APIs authenticate cooperative callers with an active
`ModuleRegistration` from the exact target registry instance and its private lease token.

## Keybinding conflict registry

`InputChord` represents a device, a primary control, and order-independent modifiers without a
UnityEngine dependency. Whitespace, hyphens, underscores, and casing are normalized for exact
comparison, so `Left Shift` and `left_shift` describe the same control. A module registers each
active binding with `RunicCoreApi.Keybindings.Register(...)`; known vanilla bindings use the
reserved provider ID `RunicModuleIds.Valheim`.

The registry intentionally reports an exact chord even when two bindings declare different
contexts because Valheim UI and gameplay contexts can overlap. It never guesses, disables, or
remaps a player's configuration. `GetConflicts()` and `GetConflictsFor(moduleId)` return immutable
snapshots for logs or configuration UI.

## Actionable notifications

Publish through `RunicCoreApi.Notifications`:

```csharp
NotificationPublishResult result = RunicCoreApi.Notifications.Publish(
    new NotificationRequest(
        "runic.example",
        "container.denied",
        NotificationSeverity.Warning,
        "That container does not permit linked material use.",
        "Ask its owner to allow linked consumption.",
        deduplicationKey: containerStableId));
```

The default key is source module + reason code + deduplication context. Repeated messages inside
the configured interval are suppressed and return an exact `RetryAfter`; different objects or
reason codes remain independent. Publishers can request a longer interval per message but cannot
shorten the configured floor.
Subscriber exceptions are isolated, and the internal key table evicts its oldest entry at its
fixed bound. The BepInEx plugin subscribes as a logger; future HUD consumers can subscribe without
changing publishers.

## Configuration authority

- `ClientLocal`: local presentation or input preference; never treated as a server rule.
- `ServerAuthoritative`: the active server or local host owns and enforces the effective value.
- `WorldAuthoritative`: the value is persisted with and enforced for the current world.

The enum declares semantics only. Runic Core does not synchronize or persist another module's
configuration.

## Configuration

First launch creates:

`BepInEx/config/chazman.RunicCore.cfg`

The package includes `RunicCore.cfg.example` with all settings:

| Section | Key | Default | Purpose |
|---|---|---:|---|
| Notifications | MinimumIntervalSeconds | `10` | Default per-source/code/context cooldown, 0-300 seconds |
| Notifications | LogPublishedNotifications | `true` | Log notifications published through the bus |
| Diagnostics | VerboseLogging | `false` | Log registry state transitions; never per-frame |

## Installation

### Mod manager

Install through Thunderstore Mod Manager or r2modman. BepInExPack Valheim is the only package
dependency.

### Manual

1. Install BepInExPack Valheim.
2. Place `RunicCore.dll` in `BepInEx/plugins/RunicCore/`.
3. Start Valheim.

## Compatibility and deliberate gaps

- The registry is process-local discovery. Version exchange at server join and RPC negotiation
  belong to a provider registered under `network.protocol`.
- Capability constants define boundaries; Runic Core does not implement permissions, container
  mutation, reservations, migrations, security checks, or ZDO ownership policy.
- Only bindings explicitly registered by participating mods or a vanilla-binding adapter can be
  compared. Version 1.1.0 does not scrape arbitrary BepInEx configuration files.
- Notifications are logged by Core but have no built-in HUD, localization catalog, persistence,
  or network transport.
- Service contracts remain owned by the capability modules. Core stores typed instances and never
  reflects into private implementation members.

These boundaries keep Core safe to install or remove: it has no world state and no gameplay patch
surface. A dependent feature must hide its integration or use a bounded vanilla-safe fallback when
a peer capability is absent or protocol-incompatible.

## Building and tests

Build the plugin:

```powershell
dotnet build .\RunicCore.csproj -c Release
```

Run the pure deterministic console suite (no game process or BepInEx runtime required):

```powershell
dotnet run --project .\Tests\RunicCore.Tests.csproj -c Release
```

The tests link the exact API sources and cover SemVer ordering, protocol compatibility, immutable
descriptors, concurrent module registration, capability transitions, deterministic typed service
fallback, stale-lease safety, chord normalization/conflicts, and fake-clock notification limits.

## Support and community

Questions, compatibility reports, and feature discussion are welcome in the Chazman Mods Discord:

**https://discord.gg/7HKHTCdFqY**

Created by **Chazman**. Runic Core is an independent mod and is not affiliated with Iron Gate
Studio.
