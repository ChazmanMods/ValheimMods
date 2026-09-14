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

The focused suite contains 34 checks covering typed editor validation and prefill, Standard Pair tag
limits, portal-panel contracts, command compatibility, data keys and schemas, group choice transport
and catalog compatibility, world-file round trips, native ownership gates, overwrite confirmation,
destination selection, map models, arrival placement, Public/Personal/Group policy defaults,
independent ward stop codes, consistent unloaded-remote destination handling, optional Group API
behavior, dependency metadata, static architecture constraints, and peer-bound Group RPC
authentication.

Dedicated-server gameplay scenarios and final suite coexistence are recorded separately by the root
release acceptance workflow after every changed mod has passed its own build and focused tests.
