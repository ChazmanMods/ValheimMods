# Archived: Runic Foundation packages

These packages are **no longer used by any released Runic mod** and are kept only for
history. Do not build, package or depend on them.

| Package | What it was | Superseded by |
|---|---|---|
| `RunicCore` | Module discovery, typed services, keybinding conflicts, notifications | Each gameplay mod is standalone; shared helpers live in `Shared/` |
| `RunicTransactions` | Durable multi-endpoint material transactions | `Shared/InventorySafety`, compiled into Storage, Production and Crafting |
| `RunicPersistence` | Direct-session transport and world-object identity | `Shared/InventorySafety` (operation journal, endpoints, network) |
| `RunicPermissions` | Permission policies and player groups | Group sources now in `Shared/Groups/`, embedded by Runic Portals |
| `RunicFoundation.Tests`, `RunicPersistence.Tests` | Their test runners | The standalone-architecture tests in each gameplay mod |

Source that live mods still borrow was moved out before archiving:

- `RunicCore/Api/RunicIdentifier.cs`, `RunicCore/Api/Keybindings.cs` → `Shared/Foundation/`
  (Storage, Agriculture, Inventory)
- `RunicPermissions/Contracts/StableIdentity.cs` and nine `RunicPermissions/Groups/*.cs` →
  `Shared/Groups/` (Portals)

The archived projects reference those files at their new location so they still compile
from here if anyone needs to reproduce an old release.

The release scripts `Build-RunicFoundation*.ps1`, `Deploy-RunicFoundationFreeRelease.ps1` and
`Write-RunicFoundationFree-Acceptance.ps1`, and the `RUNIC_FOUNDATION*.md` notes, moved here
with the packages. `Build-RunicGameplayWave1.ps1` and `Build-RunicSuite14.ps1` at the repository
root are historical release recipes that listed these packages as dependencies; they describe
releases that predate the standalone design and are not runnable against current source.

Each gameplay mod's `StandaloneArchitectureTests` rejects any reference to these packages, so a
dependency on them cannot be reintroduced by accident.
