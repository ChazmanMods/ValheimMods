# Private patch probe

`CapacityPatchProbe.cs` exercises the compiled plugin against the real game's managed assembly
without starting a world or invoking networking. It checks full patch installation, admission-prefix
coexistence, late transpiler conflicts, sticky faults, rollback, clean reinitialization, and missing patches.

This is NOT an in-engine or multiplayer acceptance test. Standalone Unity Mono lacks the engine's
internal calls. On this machine the eight assertions completed against the Windows dedicated
1.0.12 assembly, but Mono emitted unresolved Unity internal-call warnings and exited 1. That result
is not a clean runtime smoke pass. The normal .NET test runner separately passes its 29 checks.

The probe is excluded from the normal test project and from the Thunderstore ZIP. Compile it with
Unity's Mono C# compiler, referencing BepInEx.dll and 0Harmony.dll. Arguments, in order:

1. Game Managed directory.
2. Matching BepInEx core directory.
3. Compiled RunicWorldEngine.dll.
4. A private, disposable config path (never a real game/server config).

Keep BepInEx core first in MONO_PATH, followed by the matching Windows Mono framework and
facades, then game Managed. Mixing Unity 2022 and Unity 6000 framework libraries is invalid.

For actual acceptance, load the package in an isolated Valheim 1.0.12 host and verify both Steam
and PlayFab joins, cap boundary/rejection, actual per-peer RTT/rates, warnings and recovery, and
ordinary saves. Do not use a production world to create artificial backlog or packet loss.
