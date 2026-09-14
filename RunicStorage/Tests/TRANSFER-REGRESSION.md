# Storage 1.0.5 transfer regression checks

Run `dotnet run --project RunicStorage/Tests/RunicStorage.Tests.csproj -c Release` with BEPINEX_PROFILE set to the installed BepInEx directory.

The regression tests compile the actual production transfer, snapshot, payload comparison, and player-lease implementations against managed substitutes for Unity-bound objects. The substitutes model the inspected Valheim 1.0.7 inventory operations and item serialization used by the fixtures. Installed-assembly tests separately check the runtime method contracts. These tests are not a live multiplayer gameplay test.

Coverage: worn tools reproducing native durability quantization; successful wood transfer with a worn equipped tool; exact float/custom-data preservation; independent shadow dictionaries; partial/full capacity; ownership rejection; ward/native access checks; missing player lease; publication failure restoring both inventories and original item references; snapshot identity; chest payload validation accepting exactly one native durability conversion while rejecting changed counts, custom data, durability tampering, and truncation.

Live acceptance still required: use Quick Stack with worn equipment and matching materials near public chests with room; verify both inventories, relog and verify persistence. Repeat in solo and a dedicated-server client session, including two nearby players, a ward denying access, a personal chest, and an actively opened chest. Busy chest writes must remain blocked without preventing other eligible chests from working. Do not claim simultaneous multi-writer chest editing is supported.
