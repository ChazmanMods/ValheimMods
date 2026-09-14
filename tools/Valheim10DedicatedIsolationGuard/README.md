# Valheim 1.0 dedicated smoke isolation guard

This is a harness-only BepInEx plugin for the copied Valheim dedicated-server
runtime. It is not a Runic mod, must never enter a Thunderstore package, and is
not counted as a payload identity. Its reserved `000.runic...` identity makes it
the first BepInEx plugin; the dedicated harness requires its exact READY marker
before any Runic plugin loads.

The guard accepts only explicit `RUNIC_SMOKE_PROCESS_MODE=dedicated-server` and
fails closed unless all of these conditions are true in `Awake`:

1. the isolated save root is absolute and does not overlap the existing live
   Valheim data root;
2. `Application.dataPath/Managed` exactly equals the harness-provided absolute
   `RUNIC_SMOKE_EXPECTED_MANAGED_ROOT`;
3. neither `PlatformManager.s_instance` nor `DistributionPlatform` exists yet;
4. `Splatform.Steam.dll` is absent from that exact Managed root, no
   `Splatform.Steam` assembly is loaded, and `SteamPlatform` cannot resolve;
5. the dedicated build reports `FileHelpers.CloudStorageSupported == false`;
6. `Utils.SetSaveDataPath` retains the exact isolated root in the expected
   private static field; and
7. `SaveSystemSessionFlags.DontSaveAnything` is set and verified.

The harness independently verifies the dedicated Managed directory with exact
hashes and Mono.Cecil before launch and after exit, including the constant-false
cloud getter and absence of distribution/save-provider implementations. Any
failed invariant is written to stderr when possible and terminates immediately
through `Environment.FailFast`.
