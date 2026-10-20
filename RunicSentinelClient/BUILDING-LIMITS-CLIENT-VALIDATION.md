## FIX

RunicSentinelClient 1.0.5 now includes the host-settings receiver and shared terrain/support patches. Ordinary players no longer need full Sentinel for building limits. Host remains the full RunicSentinel 1.6.1 test candidate. Matching refreshed packages are provided; no deployment/public release performed.

## ROOT CAUSE

Pre-change source/project inspection confirmed the lightweight client omitted BuildingLimits and SentinelBuildingLimits. It could not receive/acknowledge the custom-limit handshake or alter native terrain/support calculations. No pre-change live client reproduction was performed. The shared implementation is now linked into the client assembly with server configuration, server handshake sender and server admission guard compiled out. The client has no binary dependency on full Sentinel.

## RISK LEVEL

3 — High: multiplayer settings, terrain/support patches and persistent-world behavior. Likely regressions: missed settings, duplicate support scaling with both plugins installed, stale settings across connections. Native serialization and terrain algorithms are unchanged from the preceding candidate.

## TEST BUDGET

13 meaningful checks planned; 10 executed: two builds and eight automated checks. Three live scenarios remain pending. Reused prior host terrain-path investigation and neighboring-IL evidence. Build retries/harness corrections are not counted as extra checks.

## TESTS RUN

- Full Sentinel Release build: passed, zero warnings/errors.
- Lightweight Client plus harness Release build: passed, zero warnings/errors.
- Client-compiled defaults/independent raise and dig limits: passed.
- Finite/range validation: passed.
- Wire round trip and malformed payload rejection: passed.
- Production transpiler executed against installed game IL for four terrain paths, preserving neighboring instructions: passed.
- Dig threshold patch and incompatible IL rejection: passed.
- Support calculation/material contract and static RunicIntegrity integration: passed.
- Compiled client transport/reset checks, absence of host control methods and absence of full Sentinel binary dependency: passed.
- Every building patch's ownership guard, full-plugin soft dependency/load order, availability-property lookup and isolated client installation decision: passed static/unit checks.

Command: `dotnet RunicLocalization/RunicSentinelClient.Tests/bin/Release/net8.0/RunicSentinelClient.Tests.dll --building-limits`

## REGRESSION CHECK

Checked vanilla defaults, wire bounds, independent terrain limits, unmodified neighboring terrain instructions, single vertical-loss output, connection reset hooks and compiled server-authority separation. Added shared Prepare guards and a full-plugin availability property so compatible full and client packages do not both install building patches. Existing character-action/admission implementations were not changed. Coexistence was inspected, not exercised in a running game.

## INVARIANTS

Bounded settings, wire validation and retained terrain instructions verified. Host-only configuration and absence of client server-enforcement code verified in compiled assembly. Live multiplayer agreement, resource conservation and save/reload survival remain unverified.

## ISSUES FOUND

Static harness initially missed compiler-nested receiver methods and optimized reference comparisons, and needed the BepInEx assembly resolver for attributes. Corrected the harness after inspecting actual IL; all eight checks now pass. No gameplay regression was observed because no live gameplay session was executed.

## UNTESTED RISK

Three required live scenarios remain: (1) host and genuinely joined Client-only participant build/dig/raise, with independent observation and concurrent/ownership-transfer cases; (2) real disconnect/reconnect, vanilla-host reset, incompatible-client rejection and full-plus-client coexistence; (3) actual extended terrain and tall structures survive save/stop/reload, with relevant RunicIntegrity behavior and runtime logs checked. No separate-process joined client or disposable modified-world save/reload was established. Static/unit checks are not proof of any of these.

Adversarial questions: most likely accidental regression is duplicate scaling (guard inspected); alternate original failure is Client-only admission without patches (client IL inspected); desynchronization/world loss remains a live risk; host and client may behave differently (separate builds only); reconnect/reload may fail (reset hooks inspected, real cycle pending). Reducing/removing custom limits can still collapse structures or clamp terrain; back up the world first.

## VERDICT

**INCOMPLETE - Important validation could not be performed**
