# RunicCrafting 3.0 development workspace

**Not a completed mod or a release package.** This isolated project preserves the
requested 3.0 specification, the release protocol, the current 2.1.2 source
baseline, installed-game binding audits, original diagnostic source, and genuine
mixed-installation test evidence. It now also contains an original, independently
buildable core library and deterministic checks. The existing releases remain in place.

- `SPECIFICATION.md`: exact supplied specification.
- `RELEASE_VALIDATION_PROTOCOL.md`: workspace protocol copy.
- `FEATURE-TRACEABILITY.md`: current feature gaps and delivery status.
- `VALIDATION-PLAN.md`: 18 bounded check groups, declared before tests.
- `AUTHORITY-AND-OUTCOME-CONTRACT.md`: candidate native chest path and unresolved
  remote manual-input acceptance contract.
- `VALIDATION-REPORT.md`: actual evidence, limits and release verdict.
- `CORE-DEVELOPMENT.md`: core reproduction, declared test scope and adapter obligations.
- `src/RunicCrafting.Core/`: original planner, spatial index, exact custody,
  action/session/batch coordinators, operation journal and adapter contracts.
- `tests/RunicCrafting.Core.Checks/`: 12 deterministic check groups; references the
  preserved planner only to retain the source-limit reproduction.
- `baseline/`: 132 untouched 2.1.2 Crafting/Shared files; hash comparison recorded.
- `tools/NativeAccessProbe/`: original BepInEx test helper, version 0.0.1.
- `audit/`: locally decompiled installed game methods and analysis; not shipping
  source and not a redistribution package.
- `fixtures/`: isolated game roots, test saves and unabridged logs.

Build the diagnostic helper with `tools/Build-Probes.ps1` using the installed .NET
SDK. Game/BepInEx paths are explicit in its project. The helper must only run in
its dedicated fixtures. It creates test objects, manipulates test stock, invokes
native handlers and exits test games. Never install it in a playing profile.

Build and verify the game-independent core with:

```powershell
& '.\tools\Build-Core.ps1'
```

Run from this project directory with .NET SDK 9 installed. This builds a
netstandard2.1 library and a net9.0 console checker without third-party packages.
For a targeted repeat, pass `-Group '09'` (the two-digit group prefix).
Timestamped build logs, check reports, source hashes and the DLL hash go to
`evidence/`. The core DLL has no BepInEx entry point and cannot be installed as
the client or server plugin. No gameplay hook has been enabled by this checkpoint.

`Start-NativeAccessProbe.ps1 -ManualOnly` runs only the native cooking outcome
diagnostic; omitting the switch runs native chest/replication diagnostics. These
use simulated peer roles/packets inside one real game process and are labeled as
such. They do not count as joined-client tests.

`Start-MixedFixture.ps1` stages an unmodified dedicated server, an unmodified
interactive client, and a separate instrumented client. It uses a private local
server, separate local save paths, and no loader on the vanilla/server roles.
Steam Cloud is independent of the local save-directory option: use a new test
character and move it to local storage explicitly. `Start-MixedClient.ps1` launches
the selected role; the vanilla role is interactive. The mixed test requires a
real human operating the unmodified client. Do not call its helper a released
RunicCrafting 3.0 client.

No server coordination plugin, 3.0 client package, migration, publication, or
normal-profile deployment has been produced. Unsupported native remote input
must not be disguised as a successful/confirmed transaction.
