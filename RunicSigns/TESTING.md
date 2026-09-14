# RunicSigns validation

## In-game confirmation and release status

On 2026-09-14, following installation of 1.0.2 in the author's Valheim 1.0 profile, the author reported: "RunicSigns appears to be working in-game" and requested Thunderstore release preparation. Version 1.0.2 is being packaged for that release with the same DLL that was tested in-game.

This confirms the reported in-game workflow; it does not certify every resolution, controller, ward, physics or dedicated-server scenario listed below. The broader checklist remains for follow-up testing.

## Completed automated coverage

**1.0.3 offset reproduction:** a 1000-unit text rectangle scaled to 0.001 in its parent moved 50 parent units on the first 5% step instead of 0.05. The production renderer failed this fixture before the correction. Rendering tests now cover all four directions, text-local rotation, board scaling and nonaccumulating/Center offsets. Fixtures model transforms; a fresh in-game placement check is still required for 1.0.3.

**1.0.2 cross-mod reproduction:** the actual installed HarmonyX runtime was loaded with two separate assemblies containing the original shared modal hooks. Closing one editor left movement/use/camera blocked in one of the two patch orders; reversing which editor was open reproduced the opposite case. After isolating the RunicSigns hook namespace, the production hooks passed all four combinations and nested exception cleanup. Run `dotnet run --project Tests/Coexistence/Coexistence.csproj -c Release`. This executes hooks against minimal game entry points, not a running game.

Version 1.0.1 adds four regressions covering permanently held actions after close, a finite action-only release timeout, immediate re-press after release, and partial-construction/reset state. Movement and camera blocking never depend on held buttons after the editor closes.

`Tests/RunicSigns.Tests.csproj` compiles the production settings codec and SignRuntime against a deterministic in-memory transport and minimal game stubs. It does not open Valheim or Valheim_Server.

- Culture-independent serialization, defaults, future/corrupt records, numeric bounds and nonfinite values.
- Caption length, newline, surrogate-pair and control-character validation; named color normalization.
- Actual save implementation with both RPC-before-ZDO and ZDO-before-RPC ordering.
- Simultaneous requests, stale captions/styles, owner-side access rejection, local ward/access revocation, unrelated replies, timeout, cancel and expired grants.
- Native caption-write rejection without a style write.
- Appearance refresh without caption changes, noncompounding size changes, simulated reload, unknown-data preservation, hidden-caption preservation and headless physical scaling.

The access booleans and native ownership transport are stubbed in this harness. Installed-contract checks verify the real patched methods and the ward members used through reflection. Neither substitutes for live networking, actual wards, Unity UI or physics tests.

## Broader live validation checklist — not fully completed

Use a disposable test world with the same build installed on two clients and a dedicated server. Back up any world used for testing. These are manual validation instructions, not evidence of completed tests.

1. Place a vanilla sign; open with Use; check all controls at 1280×720, 1920×1080 and ultrawide. Caption entry, multiline text, scrolling, mouse, keyboard and controller cancellation must work without movement, swings or camera changes. Close/save and immediately verify walking and camera control. Held attack/use inputs are suppressed separately until release or a one-second timeout; they must never prevent walking or looking.
2. Check 0.25×, 1× and 4× signs with short and long captions. Confirm the board, collider, targeting and support behavior match the visible size. Check front/back visibility, clipping and white/black backgrounds, including rotated signs.
3. Save color, scale and offset changes from client A; observe B. Repeat while B owns the sign. Verify style-only edits refresh without modifying text.
4. A and B open the same sign; A saves; B's stale save must be refused with its draft retained. Retry while ownership changes and with induced network latency.
5. Protect a sign with a ward. Test creator, permitted player and unpermitted player, including overlapping wards and revoking access after the editor opens.
6. Save, wait for a world save, disconnect/rejoin both clients and restart the server. Caption, author, style, size and collision must persist. Walk out of render range and back.
7. Disconnect or destroy the sign during a pending save. Verify no late draft is written and another player can edit after the lease expires. No client should remain input-blocked.
8. Check native filtering and muted-author restrictions; custom rendering must not expose hidden captions. Test RunicStorage alongside RunicSigns.
9. Remove RunicSigns from the test profiles and reload the test world. Verify original vanilla sign dimensions and retained captions. Reinstall and confirm saved styling returns.

Only mark the release fully validated after recording game versions, participant versions, results and screenshots for these gates.
