# Building Runic mods

Each runtime mod has its own C# project. Build only the mods you need; suite
directories contain dependency manifests rather than runtime assemblies.

## Prerequisites

- A .NET SDK supporting .NET 8 tests and netstandard2.1 runtime projects.
- A local Valheim installation matching the version you intend to support.
- BepInExPack Valheim and its `BepInEx/core` assemblies. Current release manifests
  specify BepInExPack 5.4.2350.
- Some integration harnesses require Windows/.NET Framework 4.8 or a dedicated
  server installation. See the individual project and test documentation.

Game, Unity, and BepInEx binaries are external build dependencies. They are not
included in this repository. Projects retain author-machine defaults, which can
be overridden with MSBuild properties. For example, in PowerShell:

```powershell
dotnet build RunicStorage/RunicStorage.csproj -c Release `
  '-p:VALHEIM_INSTALL=C:\Games\Valheim' `
  '-p:BEPINEX_PROFILE=C:\Games\Valheim\BepInEx'
```

Most projects derive `GAME_MANAGED` from `VALHEIM_INSTALL`; inspect the project's
PropertyGroup for additional paths. Output normally goes to `bin/Release` under
the module. DisplayStands source is now in `RunicDisplayStands/`.

## Verification

For the Storage regression suite:

```powershell
$env:VALHEIM_INSTALL = 'C:\Games\Valheim'
dotnet run --project RunicStorage/Tests/RunicStorage.Tests.csproj -c Release `
  '-p:BEPINEX_PROFILE=C:\Games\Valheim\BepInEx'
```

Other test projects live in sibling `*.Tests` directories or a module's `Tests`
directory. Some are console regression runners rather than `dotnet test` projects.
Consult their README, TESTING documentation, and project files. Tests that inspect
native game methods need matching installed client/server assemblies.

A successful build is not a substitute for in-game verification. The root README
distinguishes packaged versions from newer development source versions.

## Packaging

Use the module's packaging script where provided, such as
`tools/Package-RunicStorageRelease.ps1` or
`RunicDisplayStands/Package-Release.ps1`, after a Release build. Pass an explicit
`-OutputDirectory` to choose the destination. Do not bundle game/runtime reference
assemblies. Preserve MIT and third-party license notices when redistributing.

Some historical deployment tools retain machine-specific paths and are included
for reference; inspect them before use. Do not run deployment scripts merely to
build a mod. Never commit server credentials, signing keys, player/world saves,
or unredacted operational logs.
