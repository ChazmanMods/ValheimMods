# Testing Runic Production 1.0.0

Use the audited Valheim 0.221.12 assemblies and configured BepInEx profile. From the repository root,
run the plugin build once and then its focused suite once:

```powershell
dotnet build .\RunicProduction\RunicProduction.csproj -c Release --no-restore
dotnet run --project .\RunicProduction\Tests\RunicProduction.Tests.csproj -c Release
```

The focused suite covers standalone metadata and assembly references, retired architecture absence,
native-owner behavior, lack of custom RPC and persistent operation records, stable token and storage
format compatibility, exact player-facing role selection including fire/lamp Input, bounded link
state, reserves, composite nearby sourcing, producer
policies, catalog bounds, fail-closed planned inputs, immediate rollback, nearby indexing, catalog
publication, vanilla smelter-output fallback after proven no-mutation/rollback, station-local safety
pause when a persisted mutation is indeterminate, exact Alt mouse link and Shift+Alt unlink routing, action
suppression through button release (including FixedUpdate/SetControls-before-Update timing), and
station-level role selection plus green/yellow/turquoise linked-chest ring coverage. The ring tests
also enforce one child LineRenderer per ring and fail-closed handling of an incomplete visual graph.

The root release's one dedicated-server acceptance matrix verifies:

- smelter Input/Fuel/Output with complete-batch vanilla fallback;
- cooking and fermenter throughput coexisting with below-reserve Replenishment behavior;
- direct recipe output from composite linked/nearby inputs and a physical exemplar;
- refillable fire Wood and lamp Resin from their exact designated Input chests;
- optional nearby sourcing with per-chest reserves and bounded-query rejection;
- link, refresh, and exact unlink controls with current reach, range, ward, access, creator, and
  native-owner checks;
- pause behavior when station/chest ownership is split, an endpoint is unloaded or busy, or a saved
  target proof is stale;
- valid existing links/catalogs and singleton migration without creating operation records;
- no unrelated inventory or tool lock after a denied or failed Production action.

Useful manual checks before the final matrix are:

- Link each supported role, cancel one pending selection, let one selection expire, and unlink only
  the exact selected relation.
- While pointing anywhere at each station family, verify Alt+Left Mouse arms Input,
  Alt+Middle Mouse arms Replenishment, and Alt+Right Mouse arms Output. Repeat the same gesture on
  the chest within 30 seconds; use Shift+Alt at both steps only to unlink. Confirm no attack, block, build
  menu, secondary attack, or remove action leaks through an accepted or rejected Production chord.
- Confirm the just-linked chest shows a vivid green Input ring, yellow Output ring, or turquoise
  Replenishment ring for 12 seconds. Aim at the station again and confirm every currently linked
  chest is marked, including every Replenishment destination. Leave the rings active for their full
  lease and confirm the log has no LineRenderer-addition errors or UpdateVisuals exceptions.
- Fill a smelter Output chest, confirm vanilla output still appears, then free a complete batch and
  confirm routing resumes.
- Give one oven and one fermenter both Output and Replenishment links; below-reserve work must prefer
  the matching exemplar chest while already-completed output always finishes routing.
- Add/remove an exemplar or change a producer definition; polling must refresh the exact signed targets.
- Split station and chest ownership between peers and open a chest during service; no ownership
  transfer, duplicate output, persistent work item, or unrelated lock may appear.
- Inject one callback/publication failure that proves the original chest state and confirm the next
  eligible destination may be tried. Inject an indeterminate publication and confirm no other chest
  or vanilla spawn is attempted, only that station pauses, and reloading resumes from saved state.
- Enable nearby ingredient discovery, split a recipe across several shared accessible chests, exceed
  the configured source limit, and confirm no partial donor set
  is used.
- Link a food and a mead-base exemplar to an allowed recipe station, then link mead-base Input and
  multiple matching mead exemplar chests to a fermenter; verify only the exact requested outputs move.

Final focused-test totals, dedicated-server evidence, and coexistence evidence are recorded by the
root release workflow.
