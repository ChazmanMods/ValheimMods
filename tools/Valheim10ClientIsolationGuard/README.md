# Valheim 1.0 smoke isolation guard

This is a harness-only BepInEx plugin. It is not a Runic mod and must never be
included in a Thunderstore package or counted as a Runic payload identity.
Its reserved `000.runic...` GUID and `000 Runic...` display name place it first
in BepInEx's dependency sort; every consuming harness must prove that it is the
first `Loading [...]` record and reaches READY before any payload plugin loads.

At `BaseUnityPlugin.Awake`, before `PlatformManager` is initialized, it:

1. validates a fresh absolute `RUNIC_SMOKE_SAVEDIR` that does not overlap the
   live `RUNIC_SMOKE_LIVE_VALHEIM_DATA` root;
2. calls `Utils.SetSaveDataPath` and verifies Valheim retained the exact value in
   `Utils.m_saveDataOverride` (the public getter cannot be called this early
   because it dereferences the not-yet-created platform provider);
3. sets and verifies `SaveSystemSessionFlags.DontSaveAnything`;
4. Harmony-prefixes `Splatform.Steam.SteamPlatform.SaveDataProvider` so it
   returns `null`, then verifies ownership of the installed prefix; and
5. emits one `RUNIC_SMOKE_ISOLATION_READY` marker.

Any missing value, overlap, late load, missing API, or unverified patch causes
`Environment.FailFast`; the game is not allowed to continue without all three
save protections.

The mechanism is process-agnostic and supports both the graphical Valheim
client and the dedicated server **only when BepInEx plugin Awake runs before
platform initialization**. The client harness proves this ordering at runtime.
Any dedicated-server harness reusing it must independently prove the same
ordering and immutable pre/post live-path fingerprints.
