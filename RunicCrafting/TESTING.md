# Runic Crafting 1.0.0 verification

Run from the repository root:

```powershell
dotnet build .\RunicCrafting\RunicCrafting.csproj -c Release
dotnet run --project .\RunicCrafting\Tests\RunicCrafting.Tests.csproj -c Release
```

The focused suite covers deterministic carried-first allocation, concurrent last-resource
contention, exact rollback, independent Workshop Access policies, DLC gates, requirement displays,
placement finalization, smelter and stationless-piece costs, spatial-index stability, and the
standalone dependency/ownership contract.

Static checks confirm that the package has no Foundation, registry, durable-saga, journal, or global
mutation-gate dependency. The runtime adapter must require both local-player ownership and native
container ownership before exposing an inventory to the exact material engine.

The final suite-wide dedicated acceptance is intentionally run once after all gameplay mods have
completed their standalone conversions. Crafting's live checks cover carried-only vanilla fallback,
an owned nearby container craft, an owned nearby build, a denied/in-use container, and immediate
release after a cancelled action.
