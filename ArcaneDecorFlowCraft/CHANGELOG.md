# 0.3.1

- Fix the missing Copper Pulse Fountain placement preview. Add a small base collider supported by the Hammer's positioning algorithm, while retaining the detailed dish collider for water impacts.
- Test selection through the actual Hammer table, preview positioning on stone flooring, and native piece placement, in addition to burst and cascade behavior.

# 0.3.0

- Add the supplied copper dish and movable nozzle as Copper Pulse Fountain in the existing FlowCraft Hammer category. Costs 2 copper bars and uses native metal structural integrity.
- FlowCraft water hitting its dish triggers a fixed 0.75-second burst at 10 gallons/sec, with a 2-second minimum interval between triggers. Continuous incoming water produces repeated pulses; no accumulation is required.
- Use turns the nozzle 15 degrees through a full 360 degrees. Shift+Use adjusts elevation from 15 to 90 degrees. Aiming and burst state are networked.
- Bursts travel through the air, splash on impact, and can trigger another dish after their travel time. Solid obstacles block triggering. Detection works independently of visual distance culling.
- Retain both nine-piece pipe sets, existing flow mechanics, package branding, and the BepInEx/Jotunn-only dependencies.

# 0.2.7

- Detect water across the intake strainer, including shallow immersion and exposed openings when its bottom is partly buried. Fully buried and dry intakes remain blocked.
- Connect direct rear feeds offset up to 55 cm from the hidden fountain inlet when they enter the same supporting block. Preserve diameter matching and one feed per fountain.
- Preserve 5 gallons/sec per powered segment, gravity, fountain widths, splash sizes, and the single build category.

# 0.2.6

- Block bare pipe outlets against or slightly inside solid geometry. A wall feed cannot emit through stone while its fountain head is absent; adding the matching fountain routes water to its mouth.
- Invalidate connections when pieces are removed or enabled and refresh occupied sockets before snapping a replacement.
- Apply stopped/restored outlet visuals immediately when the network is recomputed, clearing old stream geometry and splash particles on disable.
- Preserve gravity rules, 5 gallons/sec per powered segment, jet speed, doubled fountain widths and the unified eighteen-piece catalogue.

# 0.2.5

- Double the visible stream diameter from both dragon fountain sizes. The 8 cm fountain remains twice as wide as the 4 cm fountain at equal flow.
- Preserve flow rates, pump contributions, jet speed, throw distance, splash settings and ordinary pipe outlet widths.

# 0.2.4

- Restore one shared Hammer category: ArcaneDecor: FlowCraft. Show the complete 8 cm set together, followed by the complete 4 cm set.
- Complete both nine-piece sets: short 90-degree elbow, long 90-degree elbow, intake, 2 m and 4 m copper pipes, 1 m / 2 m / 4 m powered pipes, and the matching dragon fountain.
- Long elbows have twice the bend radius without increasing pipe diameter. Added matching model-rendered Hammer icons and curved collision shapes.
- A matching 4 m copper pipe and two Surtling Cores craft one 4 m powered pipe. It contributes 5 gallons/sec like every other powered segment, and retains the glowing power indicator.

# 0.2.3

- Restore clearly luminous running indicators with a stronger emissive core, a soft unlit coloured halo, and matching nearby light.
- Apply the same glow treatment to both lengths and both diameters. The core, halo and light share one colour setting, supporting red, blue or green segment colours; current Surtling pieces remain red.
- Keep the halo visible with bloom disabled and depth-tested against solid geometry. Dry, stopped and distant indicators turn off.

# 0.2.2

- Split the Hammer catalogue into complete 8 cm and 4 cm categories, each containing all seven pieces. Powered pieces become discoverable after learning Copper and Surtling Cores; construction still requires the matching crafted pipe.
- Align facing socket directions when snapping and seat connected openings with hollow copper sleeves to close visible flange gaps.
- Give Surtling orbs a separate low-gloss red material and restrained red emission so their active colour remains recognizable.
- Centre the visible dragon fountain on the mounting block face, including rotated blocks.
- Make outlet speed proportional to supplied flow: separate 5 and 10 gallon/sec lines now have 4 and 8 m/sec launch speeds, instead of nearly identical throws.
- Scale stream diameter with the connection size: at equal flow the 8 cm stream is twice as wide as the 4 cm stream. Larger outlets also produce twice-sized splash droplets, a wider spray footprint and four times the spray count.
- Retain the approved oval package icon.

# 0.2.1

- Updated the package icon to the approved vertical oval bronze-and-teal medallion, matching the ArcaneDecor mod family.

# 0.2.0

- Added six half-diameter 4 cm variants alongside the nominal 8 cm set, preserving all lengths and elbow socket positions. Each size has matching crafting items and recipes.
- Added large and small dragon fountains from the supplied AD_Dragon_Spout_001 model, with model-rendered hammer icons.
- Added stone/marble/metal wall mounting, flush rear pipe snapping and automatic 90° fittings for parallel runs. Matching sizes connect through the actual supporting piece and emit water only from the mouth.
- Powered orb emission and red lighting now follow running flow, turning off when dry or sealed.
- Preserved 5 gallons/sec per powered pipe, native structural integrity, animated water and collision splashes.

# 0.1.0

- Extracted all six plumbing pieces, four craftable items, five recipes, models, icons, placement controls, flow simulation and outlet effects from ArcaneNaturalWaters.
- Added an independent plugin, config, DLL and Hammer category: ArcaneDecor: FlowCraft.
- Replaced direct natural-water implementation calls with native water queries and standalone terrain sampling.
- Assigned FlowCraft prefab and item IDs. Existing test builds are not migrated.
- Preserved crafting yields, 5 gallons/sec per powered piece, gravity flow, snapping, native structural integrity and animated collision splashes.
