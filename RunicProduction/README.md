# Runic Production 1.0.7

Automate your base without replacing Valheim's machines. Link chests to smelters, ovens,
fermenters, production stations, fires, and lamps for automatic ingredients, fuel, output
collection, and stock replenishment. Chain stations together to create complete production
workflows using the containers and machines already in your base.

Your stations still do the work and consume the normal materials. Runic Production simply moves
eligible resources through explicit links you create, letting an ore chest feed a smelter, a fuel
chest keep it running, and an output chest supply the next stage of your production line.

## Highlights

- Link multiple Input, Fuel Input, Output, or Replenishment chests to supported stations.
- Build connected workflows such as a mead ketill feeding a chest that supplies fermenters.
- Keep fires and lamps fueled from linked containers using their normal fuel items.
- Stock chosen food, mead-base, cooking, or fermentation outputs by placing physical examples in a
  Replenishment chest.
- Let supported cooking and recipe stations draw ingredients from eligible nearby storage.

## Safety and compatibility

Runic Production keeps Valheim's normal station state and container inventories as the gameplay
owners of that data. The mod is independently installable; its only runtime requirement is
BepInExPack Valheim 5.4.2350, with no shared Runic library required.

For multiplayer, install the same version on the server/host and every player's client. Existing
links remain in place; the player who configured them does not need to remain nearby or online.
Keep Production enabled on the machines participating in the session.

## Supported links

| Station | Input | Fuel Input | Output | Replenishment |
|---|---:|---:|---:|---:|
| Smelter-family station | Yes | Yes | Yes | No |
| Cooking station / oven | Yes | When fuelled | Yes | Yes |
| Allowed recipe station | Yes | No | Yes | Yes |
| Fermenter | Yes | No | Yes | Yes |
| Refillable fire or lamp | No | Yes | No | No |

Input supplies exact accepted ingredients while preserving configured reserves. Fuel Input is a
separate role selected at the station's native fuel control and supplies only the station's native
fuel prefab while keeping `Fuel.ProtectedReserve` in the chest. Existing Input links continue to
supply fuel until that station receives its first explicit Fuel Input link. Output is a throughput destination. Replenishment keeps
approved output types stocked from physical exemplars in its linked chests.

Fireplace-family objects use their own native fuel definition. A wood fire therefore draws Wood
from its Fuel Input chest, while a resin lamp draws Resin, without maintaining a separate
Runic fuel list.

Every role accepts multiple chests. Cooking and fermenting prefer an eligible below-reserve
Replenishment destination and may also retain ordinary Output links. Recipe stations can use linked
Input chests or eligible nearby ingredient chests. Smelter output interception
suppresses Valheim's normal spawn only after the complete output batch is deposited successfully;
otherwise the ordinary vanilla output path runs.

A physical chest may be the Output of one station and the Input of another, including a mead
ketill-to-chest-to-fermenter line. Roles are scoped independently to each station, and a chest may
also hold more than one explicit role for the same station. Exact conversion tables and configured
reserves still decide which item types a station is allowed to consume; the relay relation itself
does not copy, rename, or guess an item.

## Linking controls

Start a link while pointing anywhere at the station. Hold Alt and click the role's mouse button,
release the mouse button, then point at the desired chest and repeat that exact Alt+mouse gesture within 30 seconds. Either
Alt key works; Ctrl is deliberately excluded. The accepted click is consumed so it cannot also
attack, block, open the build menu, remove a piece, or perform a secondary attack.

To remove a link, perform the same two steps with Shift also held at both the station and the linked
chest. Link and unlink modes cannot be mixed during the 30-second selection.

| Station gesture | Selected role |
|---|---|
| Alt+Left Mouse on an ingredient control or station body | Input |
| Alt+Left Mouse on the native fuel control | Fuel Input |
| Alt+Right Mouse | Output |
| Alt+Middle Mouse | Replenishment |

The station hover always lists `Input`, `Fuel Input`, `Output`, and `Replenishment`, showing `empty`, `1 chest`, or
the exact linked-chest count. Output selection is station-level; it no longer requires aiming at a
front bay, food slot, tap, or spout. Successful commits report `Linked as input/output/replenishment
for <station name>`. Successful removals report the corresponding `Link removed` message.

Loaded linked chests are visually identified whenever the player points at their station: Input
(including migrated legacy Fuel links) has bright green rings, Output has bright yellow rings, and every Replenishment destination has
turquoise rings. A successfully linked chest keeps its ring for 12 seconds immediately after the
commit; contextual station aiming renews the rings while the station remains pointed. Fuel Input
uses the same green input ring. Every role
is additive and shows every linked destination, up to the configured bounded limit. Each of
the three animated rings has its own child renderer; if any visual object becomes invalid, that
chest's marker tears itself down once instead of throwing repeatedly or affecting automation. A
later display lease can then create a fresh marker.

A Replenishment chest may be linked while empty. It remains inert until a physical exemplar is
placed inside; scheduler polling then derives the exact uniquely supported target and producer
signature. Removing every exemplar disables that target without guessing. A positive exemplar
count below `Replenishment Stock.DefaultReserve` triggers production; exact-prefab overrides are
available in `PrefabReserves`. The single default soft limit is eight chests per station role and
the hard limit is sixteen, with at most thirty-two authorized target types.

A link requires loaded endpoints, player reach, the configured link range, Valheim chest and ward
access, and a static non-wagon container. During explicit link setup, the mod requests native
ownership when needed and verifies that both endpoints are locally owned before committing the link.
Background automation does not claim ownership; it repeats the access and ownership checks before use.

## Replenishment behavior

Cooking stations and fermenters select only an input that corresponds to an accessible, current
Replenishment target. If the catalog is invalid or no target is currently eligible, the station
does not pull an unrelated ingredient. Completed output is routed only to a destination whose
physical exemplar and signed producer definition still match.

Allowed recipe stations can produce a complete recipe batch from one or more eligible linked Input
or nearby chests directly into a Replenishment destination. The recipe must be enabled and unique for the exemplar,
must not use Valheim's choose-one-ingredient rule, and must still match its recorded output,
requirements, DLC, station, level, roof, fire, and producer signature. The destination must retain
at least one matching exemplar.

`Recipe Nearby Ingredients.Enabled=true` is the default. Recipe, cooking, and fermenter
replenishment may draw from bounded loaded nearby static chests; direct recipes combine split
ingredients into one exact atomic source plan. Setting it false confines ingredients to linked
Input chests. Every donor must be locally owned, accessible, ward-allowed, outside the station's
Output, Replenishment, and legacy Fuel destination sets (unless explicitly linked as Input), and inside the link range. Search is
bounded to 64 candidates and aborts rather than using a truncated result. Reserves are enforced
independently in every donor.

For food or mead-base production, place one desired output exemplar in a Replenishment chest and
link it to the recipe station; the station uses eligible Input and nearby ingredient chests to
make that exact recipe into the exemplar chest. A fermenter pulls a compatible mead base from its
Input and routes finished mead only to Replenishment chests already containing that exact mead.
Repeat Alt+Middle Mouse on the station and then the chest for multiple exemplar
chests; the bounded catalog chooses a
matching destination fairly.

The stock scheduler services each loaded station at most once per quantum and applies the global
operation cap. Unloaded time never creates catch-up recipe batches or fuel service.

## Ownership and failure behavior

Any player with normal chest and ward access can create or remove Production links; being the
builder, an administrator, or the existing network owner is not required. Select the station,
then repeat the matching gesture on a closed chest within range. Completing the link uses native
ownership claims for that selected station and chest, rechecks access, and reloads the chest's
saved contents before publishing the link. Selecting a station alone does not claim it.

Background production follows the station's current network owner. When its linked chests belong
to another peer, Production requests a handoff from that chest's current owner, rechecks the saved
link and current chest/ward access, and waits for the synchronized inventory before moving items.
Shared chests are serviced in turn. Open chests are left alone and service resumes after they close.
Unloaded stations do not run automation; a loaded station may also pause while access is denied or
its chest is unavailable. No new link setup is needed after a normal ownership handoff.

Ordinary transfers have no persistent operation journal, global inventory lock, account state, or
recovery loop. A multi-object mutation uses exact before/after snapshots, publishes each chest's
native persisted inventory, and proves either the full committed state or an exact rollback before
trying anything else. If Valheim cannot prove either result, automation pauses only that station
for the current session and suppresses a duplicate retry or vanilla output; reload to resume from
Valheim's persisted chest state. No unrelated inventory, tool, station, player, or mod is blocked.

For Replenishment, the exact authorized player identity is stored in an inert plan before the role
link becomes authoritative, including when the chest is empty. A failed plan or role publication
does not report a successful link. Signed plan bodies without an exact current Replenishment role
link remain inert and are neither displayed nor consumed by automation.

Actual feature data remains persistent: explicit links, the bounded Replenishment catalog, signed
target definitions, and fairness cursors stay in the established `runic.production.*` world keys.
Existing stable container token keys are preserved. Valid earlier singleton links and plans migrate
to the catalog when their records agree exactly. A tokenless singleton target may be upgraded only
when its one exact legacy chest is loaded and locally owned; old tokenless catalog destinations
require an explicit player refresh. Invalid or ambiguous records pause without guessing or deleting
the relation.

## Multiplayer, configuration, and installation

There is no Runic protocol handshake. Install this DLL on each process expected to automate
objects, and keep gameplay configuration consistent across the server and participating clients.
A peer without Runic Production does not prevent other Runic gameplay mods from loading; it simply
does not run Production logic for objects that peer owns.

Configuration is generated at `BepInEx/config/chazman.RunicProduction.cfg`. The example file
documents link distance, moved-target tolerance, reserves, exact prefab allow/deny lists, optional
nearby discovery, the single per-role chest limit, scheduler bounds, UI hints, and diagnostics. Deny entries override
allow entries. Lowering a bound does not erase existing links.

Install BepInExPack Valheim 5.4.2350, then place `RunicProduction.dll` under
`BepInEx/plugins/RunicProduction/`. Version 1.0.6 supports Valheim 1.0.7 and 1.0.12.

When updating manually, replace the existing DLL instead of keeping multiple copies. Restart
Valheim after updating; existing configuration and saved links are retained.

Community: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)

Runic Production is an independent mod and is not affiliated with Iron Gate Studio.
