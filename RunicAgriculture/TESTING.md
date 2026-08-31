# Runic Agriculture testing

Run the focused static and policy suite once after changing Agriculture:

```powershell
dotnet run --project .\RunicAgriculture\Tests\RunicAgriculture.Tests.csproj -c Release
```

The suite verifies 1×1 through complete 40×40 Grid footprints, combined-resource selection,
full-footprint validation with later valid-cell backfill, pattern bounds, centre-out shaped and
right-to-left Grid/Row shortfall order, one-time Grid
migration, independent row/column editing, wheel routing,
compact scaled native bottom-hint replacement, detailed invalid-preview explanations, and that
stamina/tool budgets cannot mask ground-valid ghosts, plus determinism,
validation and batch policy, bounded replant
offers, exact Pickable filtering, replant/authorization/matching-plant area-harvest gating,
native `Player.PlacePiece` and `Pickable.Interact` calls,
process-local batch exclusion, unchanged versioning, a BepInEx-only manifest, and an assembly with
no Runic Foundation references.

For manual or dedicated-server acceptance, install the same `RunicAgriculture.dll` on a client and
exercise these behaviors through ordinary gameplay:

1. confirm a multi-position pattern and verify one planting cost is consumed per planted cell from
   personal inventory first and then eligible nearby chests while the one left-click batch consumes
   one stamina/cultivator action; a configured 1×1 Grid plants exactly one;
2. deny one position with a ward or terrain change and verify unrelated inventory and tools remain
   usable immediately;
3. with replant enabled and planting access, area-harvest an exact matching crop without equipping
   the cultivator and verify the aimed object is included; then harvest an unrelated wild Pickable
   and verify it follows vanilla behavior with no Runic harvest/replant message or hover hint;
4. confirm a matching replant offer, then repeat with a changed crop and verify it is rejected;
5. disconnect during a preview or batch and reconnect, verifying no recovery or journal state is
   created and vanilla agriculture remains usable.
6. Use bare wheel to rotate the whole pattern, independently raise/lower Grid rows with Alt+wheel
   and columns with Shift+wheel, and test 1×1, 10×10, 20×20, and 40×40. With 96 total matching
   resources split between personal inventory and eligible nearby chests in a 10×10 Grid, verify
   all configured cells preview, exactly 96 right-to-left cells are green, and four otherwise-valid
   cells are red. Make four early cells invalid and verify they are muted amber while later valid
   cells backfill. Plant from one left click. Exhaust most stamina
   first and verify it produces a single HUD warning rather than turning valid cells red. Then
   lower spacing until it reaches the selected crop's collider-safe minimum. With too few seeds for
   the requested shape, verify the centre fills before outer cells. Verify each amber-cell
   cause appears in Valheim's compact native bottom hints and that no separate black panel appears.
7. Verify an in-use, inaccessible, ward-denied, moving, non-owned, stale, or out-of-range chest is
   never used. Test a piece with multiple resource requirements and confirm the smallest combined
   personal-plus-chest budget controls the red shortage count.

The dedicated server does not need Agriculture for these owner-local enhancements. Installing the
DLL there is harmless but does not introduce an RPC or persistent state service.
