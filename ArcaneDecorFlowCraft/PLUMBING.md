# ArcaneDecor: FlowCraft plumbing

## Build pieces

All **18 pieces** appear in the single **ArcaneDecor: FlowCraft** Hammer category: nine 8 cm pieces, then nine 4 cm pieces. Each set contains a short and long 90-degree elbow, an intake, 2 m and 4 m copper pipes, 1 m / 2 m / 4 m powered pipes, and a matching dragon fountain. Learning Copper and Surtling Cores reveals all powered variants; construction consumes the matching crafted item.

| Piece | Length | Construction cost |
| --- | --- | --- |
| Copper Pipe | 2 m | 1 Copper bar |
| Copper Pipe | 4 m | 2 Copper bars |
| Copper Elbow | 90Â° | 1 Copper bar |
| Copper Long Elbow | 90 degrees, twice the bend radius | 1 Copper bar |
| Copper Intake | Strainer | 1 Copper bar |
| Arcane Powered Pipe | 1 m | 1 crafted 1 m powered pipe |
| Arcane Powered Pipe | 2 m | 1 crafted 2 m powered pipe |
| Arcane Powered Pipe | 4 m | 1 crafted 4 m powered pipe |
| Dragon Head Fountain | Matching size | 1 Copper bar (4 cm); 2 Copper bars (8 cm) |

Both diameter sets use these costs and lengths. The narrow set halves diameter without shortening pipes. The small dragon fountain costs 1 Copper bar; the large dragon costs 2. See README for wall mounting and automatic rear fittings.

## Craft powered pipes

These recipes appear in personal crafting after learning their materials; no extra crafting station is required.

| Ingredients | Result |
| --- | --- |
| 1 Copper bar | 1 Copper Pipe (2 m), a crafting ingredient |
| 2 Copper bars | 1 Copper Pipe (4 m), a crafting ingredient |
| 1 Copper Pipe (2 m) + 1 Surtling Core | 2 powered pipes (1 m each) |
| 1 Copper Pipe (4 m) + 2 Surtling Cores | 4 powered pipes (1 m each) |
| 1 Copper Pipe (4 m) + 2 Surtling Cores | 2 powered pipes (2 m each) |
| 1 Copper Pipe (4 m) + 2 Surtling Cores | 1 powered pipe (4 m) |

Ordinary copper construction uses bars directly. Copper Pipe inventory items are the intermediate ingredient for the powered-pipe recipes. Place crafted powered pipes with the hammer; dismantling returns the placed powered pipe rather than duplicating recipe ingredients.

## Connect a line

Immerse the intake's lower strainer in a lake, ocean, river or hot spring supplied by ArcaneNaturalWaters (when installed). Connect to its top opening. Each connection uses the centre of the visible pipe opening, with facing ends aligned. Pipes crossing along their bodies do not connect.

Use the normal hammer snapping and mouse-wheel rotation. **Page Up** tilts the preview by 45Â°; **Page Down** rolls it by 90Â°, which also lets an elbow turn sideways or upwards. These keys are configurable in FlowCraftâ€™s Plumbing settings. The final free, exposed pipe end pours water automatically. Extending the line moves the outlet to the new last piece.

Pipes may lie on floors, walls, ceilings or terrain. All eighteen pieces use Valheim's native metal structural integrity: ground and supported construction transfer support through physical contacts, with strength lost over distance. Unsupported pipes collapse and return their construction materials. Brace long or elevated runs before removing their supports. Existing floating pipes will be subject to the same rules after updating. Pipes can remain buried when covered with the hoe; terrain provides support. Keep the intake strainer in water and the outlet uncovered: dirt or a solid obstruction blocks an exposed outlet. Pipe-to-pipe connections remain active through buried sections.

## Flow

- Unpowered lines carry a shared base gravity flow of **5 US gallons/sec**, provided the outlet is below the source water level and the route does not rise above that level. This is a development tuning value.
- **Every powered piece adds 5 US gallons/sec**, for the 1 m, 2 m and 4 m lengths, and permits uphill pumping. Two powered pieces provide 10 gallons/sec pumping capacity.
- When gravity can also supply an outlet, its base flow is added to the pumping flow. Pumps do not create water without a wet intake.
- Connected pumps are counted once. Any multiple exposed outlets share the available pumping capacity; the gravity contribution is shared among downhill outlets. Overlapping ports pair one-to-one rather than creating accidental tees.
- Hover over a pipe for water-supply status, connected pump count, pumping capacity and its outlet rate. Powered sections glow red only while the system is discharging water; the stream widens and launches faster as flow increases. Launch speed is 0.8 metres/sec per gallon/sec, capped at 40 metres/sec; independent 5 and 10 gallon/sec lines launch at 4 and 8 metres/sec.
- Breaking the line or drying/removing its intake updates the flow within about half a second. The graph is reconstructed from loaded networked pieces on each peer, so saved transforms are sufficient to reconnect after loading.

The current outlet is a visible water stream with splash effects. It does not yet accumulate water or fill a new pond. Flow simulation covers loaded pieces; a line across unloaded world areas is not an offline fluid simulation.

Streams have moving streaks and ripples. Impact spray follows the struck surface normal, including walls and the ground. The visual trajectory is bounded to four seconds of flight and the spray to 192 particles per visible outlet; distant outlets stop rendering. Pipe sockets stay open, with visible inward-facing copper surfaces around bends.

Only **BepInEx and Jotunn** are required. WaterGardens remains separate.

## Fountain size and splash

Both dragon fountain stream diameters are doubled from 0.2.4. At equal supplied flow, an 8 cm fountain still has twice the stream diameter of a 4 cm fountain. This visual change does not alter flow rate or throw distance. Its impact spray uses twice-sized droplets, twice the emission radius and four times the particle emission; longer splash flight produces a larger footprint. Pipe diameter controls this visual scale, while supplied flow controls launch speed. Each powered piece still contributes 5 gallons/sec in either size.


## Copper Pulse Fountain

Build the copper dish from Hammer > ArcaneDecor: FlowCraft for 2 copper bars. Place it on terrain or supported flooring. Aim a working FlowCraft pipe, dragon fountain, or another pulse fountain into the dish from above. Rain, ordinary splash particles, and standing water do not trigger it.

Each hit triggers a fixed 0.75-second burst at 10 gallons/sec. The minimum interval between burst starts is 2 seconds, so continuous incoming water repeats the effect. This is an arcane trigger effect, not a reservoir that stores the incoming volume. Existing powered pipes still add 5 gallons/sec each.

Look at the dish and press Use (normally E) to turn the nozzle by 15 degrees; it wraps through all 360 degrees. Shift+Use adjusts tilt in 15-degree steps from 15 to 90 degrees. Aim and burst timing are stored in the piece's network state. The dish stays level while the nozzle moves. Chain dishes by aiming one burst into the next; each waits until the water actually arrives. Solid surfaces intercept the water.
