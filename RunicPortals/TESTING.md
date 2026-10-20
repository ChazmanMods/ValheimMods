# Testing Runic Portals

Use the audited Valheim 1.0.7 assemblies and the configured BepInEx profile.

Build the plugin once:

```powershell
dotnet build .\RunicPortals\RunicPortals.csproj -c Release --no-restore
```

Run the focused suite once:

```powershell
dotnet run --project .\RunicPortals.Tests\RunicPortals.Tests.csproj -c Release
```

The focused suite contains 52 checks covering typed editor validation and prefill, Standard Pair tag
limits, portal-panel contracts, command compatibility, data keys and schemas, group choice transport
and catalog compatibility, world-file round trips, native ownership gates, overwrite confirmation,
destination selection, map models, arrival placement, Public/Personal/Group policy defaults,
independent ward stop codes, consistent unloaded-remote destination handling, optional Group API
behavior, dependency metadata, static architecture constraints, and peer-bound Group RPC
authentication.

Dedicated-server gameplay scenarios and final suite coexistence are recorded separately by the root
release acceptance workflow after every changed mod has passed its own build and focused tests.

## Expanded destination picker and damage protection

Automated checks cover cross-network listing and travel, live Group membership and owner checks,
vanilla destination-only routing, duplicate names, search, truncation, more than 128 destinations,
offline/disabled/destroyed/stale exclusions, revisions, travel restrictions and native damage method signatures.

In-game verification still required with 1.2.6 on the host/server and participating clients:

- Enter a Runic portal and verify Public, owned Private, current Group, and Standard Pair destinations,
  including remote portals in unvisited zones. Other players' Private portals and nonmember Groups must stay hidden.
- Select a Standard Pair destination; confirm arrival and then its ordinary vanilla paired departure.
- Verify ward-denied destinations remain blocked and changed tags/modes invalidate stale selections.
- Enter a Runic picker while taking melee, ranged, fire, poison, and environmental damage. Verify no damage
  while the map or directory wait is active. Escape, map closure, source loss, and plugin shutdown must
  restore damage; successful travel must transition to native teleport protection.
- Verify opening the ordinary map grants no protection, and vanilla portal entry remains unchanged.
- Check host and dedicated-server behavior. Automated signature checks do not replace live damage testing.
