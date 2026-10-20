# RunicInteraction 1.0.9 verification

- Main suite: 65 checks, including the installed Door.UpdateState Harmony target and native
  access, ownership, and close contracts.
- Door runtime harness: 14 checks running production DoorAutoCloseRuntime against simulated
  game objects. Covers remote opening without local Interact, repeated updates, close/reopen,
  obstruction, ownership handoff, wards, restricted doors, disabled/absent player, changed
  identity, animation delay, and synchronous native close callbacks with multiple timers.
  Also covers world-generated doors and doors without a Piece through both observation and
  local interaction, player-built local openings, and construction eligibility revalidation.
- Commands: `dotnet run --project RunicInteraction.Tests/RunicInteraction.Tests.csproj -c Release`
  and `dotnet run --project RunicInteraction.Tests/DoorHarness/DoorHarness.csproj -c Release`.
- Live multiplayer acceptance remains: enable AutoCloseDoors, have a visitor open an accessible
  player-built door without you interacting, step out of its path, and verify closure after the configured
  delay. Repeat while obstructed, after closing/reopening, with changing network ownership,
  and with ward access denied. Visitors need no mod if an enabled observing client is present.
  Open generated doors in burial chambers, frost caves, and surface ruins and verify they stay
  open beyond the delay. Repeat with a remote player opening them and with already-open doors.
- This is client-side observation of loaded doors, not server-only offline automation. No live
  installation, live gameplay test, or upload was performed for this change.
