# Runic Permissions

Runic Permissions is the canonical authorization vocabulary and evaluator for the Runic mod suite. It gives Storage, Crafting, Production, Portals, wards, and future modules the same meanings for identity, ownership, action scopes, and access policies.

Version 1.0.0 evaluates already-resolved server context and now publishes the suite's built-in,
server-owned Group membership provider. Group creation and membership changes travel over Runic
Persistence's direct, transport-bound connection and commit to a world-scoped, checksummed primary
catalog before success is returned. It still does not discover wards or mutate a consumer's world
object; participating modules resolve their current object and ward evidence immediately before an
authoritative action.

## What it provides

- Stable `authority:subject` player identities. Display names are snapshots only.
- Owner and builder records with schema version, revision, and trust state.
- Independent action scopes for discovery, use, linked-material consumption, container open/deposit/withdraw, configuration, linking, portal departure/arrival, deconstruction, and ownership transfer.
- Everyone, Approved, Owner, Nobody, Ward, Ward + exceptions, and optional Group policies with stable persistence names.
- Immutable per-action profiles and explicit allow/deny lists.
- A deterministic, fail-closed evaluator with stable reason codes.
- A typed `IPermissionEvaluationService` published as the Runic Core capability `permissions.evaluate`.
- A typed `IGroupMembershipService` published as `permissions.groups`, backed by the exact current
  world catalog rather than a client cache.
- A typed `IActiveGroupSelectionService` published as `permissions.groups.active`. The server
  persists one selected Group per world and stable player identity; clients receive a bounded
  convenience cache, while authoritative consumers resolve membership again on the server.
- Administrative bypass that cannot become an allow until its audit entry succeeds.

## Groups

Group identity is an immutable lowercase UUID (`Guid:N`). The display name can change without
changing Portal or other Runic-policy identity. Membership is tied to the world-stable
`valheim.player:<PlayerID>` identity. For a remote player, the server creates that key only after
Runic Persistence's `AccountBoundPlayer` resolver proves the current direct backend session, its
transport-owned Player ZDO, and the reverse-unique persisted account-to-player binding. The backend
account remains transport/audit identity; a payload player ID or display name is never authority.

Connected players manage Groups from Valheim's normal chat panel. Press Enter, type a command that
starts with `/group`, and press Enter again. The command and response remain local to that player's
chat panel; they are not sent as public chat. F5 is not required.

A complete first-time example is:

```text
/group create Viking Crew
/group invite Alice
```

`Alice` must be connected, and the text must match exactly one connected player name. If two
connected players have the same name, the server refuses to guess. Alice then enters:

```text
/group accept Viking Crew
```

Creating or accepting a Group automatically makes it active. A player who belongs to several
Groups chooses which one a Runic feature should use with:

```text
/group list
/group use Viking Crew
```

The active choice is stored by the server for that exact world and stable player identity. A
client receives the exact selected UUID for convenience, but the display name and client cache
never authorize an action. The server resolves the stored UUID against current membership again
before a consumer may use it.

The friendly chat commands are:

```text
/group help
/group create <group name>
/group list
/group use <group name>
/group active
/group invite <connected player name>
/group accept <group name>
/group members
/group leave
/group rename <new group name>
/group cancel <connected player name>
/group role <connected player name> <member|officer>
/group remove <connected player name>
/group transfer <connected player name>
/group delete
```

Commands without an explicit Group name act on the active Group. `invite`, `cancel`, `role`,
`remove`, and `transfer` accept a connected player name and require one exact, unambiguous match.
This lets the server map the current authenticated connection to its durable
`valheim.player:<PlayerID>` identity without asking players to copy IDs.

Mutations are authorized and committed by the server. Before each remote mutation, the server durably issues a world-catalog
epoch/sequence token bound to the exact actor, command hash, catalog revision, and Group revision.
The atomic catalog publication stores the mutation and its receipt together. An exact retry after
a lost prepare response returns the same issued token, and a retry after execution returns the same
receipt; token reuse with different bytes or against a newer revision
fails closed instead of overwriting later membership changes:

The durable prepare/execute exchange is part of the first published Permissions protocol. Earlier
development-only query scaffolding is not retained as a compatible release surface.

The older `/runic_group ...` exact-ID syntax remains available in both chat and F5 for diagnostics
and backward compatibility. Ordinary players do not need it. Its target-bearing commands still
accept only an exact `valheim.player <PlayerID>` pair; backend accounts and arbitrary authority
strings are rejected instead of creating unusable members.
Server administrators can also use `runic_bind list`/`runic_bind peers`. Owners control names,
roles, ownership transfer, and deletion; officers may invite and remove ordinary members. Invitations
are bounded to 30 days. A corrupt primary or surviving temporary/backup evidence disables Group
authorization until an administrator resolves the evidence; it is never silently promoted.

## Evaluation precedence

One request evaluates one action. Granting `materials.consume-linked`, for example, grants no ability to open, browse, deposit into, or withdraw from the same container.

The evaluator applies this order:

1. An explicit server deny wins over every lower rule.
2. Missing, ambiguous, or stale identity/ownership/profile data denies.
3. An explicit object deny wins over every allow, including admin bypass.
4. Ambiguous, stale, or overlapping-hostile ward data denies.
5. A missing, stale, ambiguous, or mismatched Group provider result denies a Group policy.
6. A server-enabled, deliberate, auditable admin request becomes `AuditRequired`; the public service allows it only after the audit sink accepts the record.
7. An active ward denial blocks object-local public access unless the ward owner explicitly enabled public exceptions.
8. Explicit allow, then the action's base policy, is evaluated.

Ordinary ward denial can be bypassed by deliberate audited administration. Explicit denial and uncertain security inputs cannot.

## Integration

Add references to `RunicCore.dll` and `RunicPermissions.dll`, then resolve the typed service:

```csharp
using Runic.Foundation.Core;
using RunicPermissions.Contracts;

if (RunicRegistry.Shared.TryGetService<IPermissionEvaluationService>(
        RunicCapabilityIds.PermissionsEvaluate,
        out var permissions))
{
    PermissionEvaluation result = permissions.Evaluate(request);
    if (!result.IsAllowed)
    {
        // Convert result.Reason to localized player-facing text.
        return;
    }
}
```

Callers must fail closed if Core, the capability, or a required peer provider is unavailable. Client evaluation may predict UI availability but must never authorize or finalize a state change.

Consumers that need the player's selected Group resolve the separate typed service:

```csharp
if (RunicRegistry.Shared.TryGetService<IActiveGroupSelectionService>(
        GroupCapabilities.ActiveSelection,
        out var activeGroups))
{
    ActiveGroupSelection active = activeGroups.Resolve(stablePlayerIdentity);
    if (!active.IsAvailable) return;

    // active.GroupId is the canonical exact UUID. A client result is presentation/input
    // convenience only; resolve it again on the authoritative server before mutation.
}
```

Runic Permissions requires Runic Core 1.0.0 and Runic Persistence 1.0.0. It registers only after
Core and the direct RPC service finish loading and their protocol majors are compatible. A mismatch
disables both evaluator and Group services with an actionable error.

## Administrative bypass

`AdminBypassEnabled` defaults to `false`. When enabled, a caller must provide all of the following from verified server state:

- confirmed administrator status;
- deliberate admin mode currently active;
- a non-empty admin session ID;
- a non-empty justification;
- an evaluation ID, target resource ID, subject, and action suitable for an audit record.

Every successful bypass is emitted as a warning-level audit entry. If logging fails, the service denies with `AdminAuditUnavailable`.

## Building

Requirements:

- .NET SDK 8 or newer;
- a local Valheim BepInEx profile containing BepInEx 5;
- the sibling `../RunicCore/RunicCore.csproj` and `../RunicPersistence/RunicPersistence.csproj`
  projects.

```powershell
dotnet build .\RunicPermissions.csproj -c Release
dotnet run --project .\Tests\RunicPermissions.Tests.csproj -c Release
```

Override `BEPINEX_PROFILE` on the command line if your profile is elsewhere. The project intentionally stops with a direct dependency message when the sibling Runic Core source project is absent.

## Configuration

Runic Permissions writes `BepInEx/config/chazman.RunicPermissions.cfg`. The only 1.0.0 setting is server-owned `Security.AdminBypassEnabled`; it is disabled by default and requires a restart after a change. See `RunicPermissions.cfg.example`.

## Known 1.0.0 boundaries

- No Harmony patches or gameplay mutations. Group chat commands use Valheim's native non-cheat
  `Terminal.ConsoleCommand` path, which the normal Chat panel already invokes for slash commands.
- Group policy IDs are canonical lowercase UUIDs in `Guid:N` form. No built-in or supported legacy
  Group provider ever shipped, so there is no legacy provider database to import. Development-only
  alias IDs such as `builders` are intentionally malformed and fail closed; the built-in provider
  mints explicit UUIDs rather than silently converting display names into policy identity.
- No ward discovery; the owning gameplay module supplies a current `WardContext`.
- No identity lookup or ownership persistence adapter; those remain with the module that owns the world object.
- The server's Group catalog and bypass setting are authoritative. Client-side evaluation remains
  predictive only.
- Remote Group queries and mutations require a current `AccountBoundPlayer`; an unenrolled,
  conflicting, stale, pre-character, or copied binding cannot read or change membership.
- Group management uses private chat text rather than a custom graphical window. Other consumers
  receive stable reason codes and own their player-facing message.

These boundaries are intentional: the module supplies one conservative contract and one evaluator without taking over another mod's responsibility.
