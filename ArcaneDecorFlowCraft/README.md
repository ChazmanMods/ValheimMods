# ArcaneDecor: FlowCraft 0.3.1

Build copper plumbing with gravity flow, Arcane powered pumps, animated outlet streams and impact splashes.

FlowCraft is standalone. It requires only BepInExPack Valheim 5.4.2350 and Jotunn 2.30.1 or later. ArcaneNaturalWaters and WaterGardens are not required.

## Building

Find all **18 pieces in Hammer > ArcaneDecor: FlowCraft**, with the complete 8 cm set followed by the complete 4 cm set. Each size has exactly nine pieces:

1. 90-degree elbow
2. Long 90-degree elbow (twice the bend radius)
3. Intake
4. 2 m copper pipe
5. 4 m copper pipe
6. 1 m powered pipe
7. 2 m powered pipe
8. 4 m powered pipe
9. Dragon head fountain, sized to match

Each copper piece costs one Copper bar, except the 4 m copper pipe which costs two.

Learning Copper and Surtling Cores reveals the powered build pieces in both sets. Placing them still consumes the matching crafted powered pipe.

The 4 cm set has half the diameter of the existing set, with unchanged pipe lengths, intake height and elbow connection positions. The 8 cm/4 cm names are nominal connection sizes; the existing stylized models are retained, not resized to literal centimetres. Match diameters when connecting pieces.

Craft powered pipes before placing them, using copper pipe items of the matching diameter:

- One 2 m copper pipe and one Surtling Core yield two 1 m powered pipes.
- One 4 m copper pipe and two Surtling Cores yield four 1 m powered pipes, two 2 m powered pipes, or one 4 m powered pipe.

Each powered pipe adds **5 gallons per second**, for every length. Outlet speed scales with supplied flow: independent one- and two-pump lines launch at 4 and 8 metres/sec when gravity does not add flow. Outlets on the same connected network share its available flow. Immerse the intake in water. Gravity feeds downhill lines; powered pipes lift water uphill. The last exposed end pours, with animated water and splashes on solid surfaces. Water is sampled through Valheim's native water system, including compatible water volumes from other mods.

Pipe ends snap together with aligned openings and hollow copper sleeves that seat the flange joints. Page Up tilts placement by 45Â°, Page Down rolls it by 90Â°, and the mouse wheel turns it. Pipes use native metal structural support and can be buried with the hoe. Hammer icons depict the supplied models.

## Dragon fountains and running indicators

The 4 cm dragon is approximately 1 metre tall; the 8 cm dragon is twice as large. Small heads cost 1 Copper bar and large heads cost 2. Mount the plate on a stone, marble or metal building piece using the Hammer. The visible head snaps to the centre of that block face. Wood and terrain are not fountain mounting surfaces.

Bring a matching pipe to the **back of that same supporting piece**, directly behind the mouth. The preview snaps within roughly half a metre of the connection. A perpendicular pipe ends flush at the rear face. A pipe parallel to the wall snaps to the correct clearance and receives an automatic copper 90° fitting into the wall. No extra elbow ingredient is charged. Normal manually placed elbows also work when their end faces the wall. Connections follow the actual supporting collider through walls up to 8 metres thick, including rotated walls; no hidden pipe is added to inventory.

A pipe ending against or slightly inside stone stays blocked while no fountain is installed. Adding the matching fountain connects the feed through its supporting piece. The rear connection consumes the pipe's outlet, so only the dragon mouth pours. Removing and rebuilding supply sections or the head reconnects the line automatically when the sockets match. Removing the wall, disconnecting the pipe or losing the water source stops the fountain. Fittings and connections are reconstructed from the placed pieces when the area loads. Native structural support still applies.

Powered pipes illuminate a low-gloss red orb with a bright emissive core, a soft coloured halo and matching nearby light only when their connected system has flowing outlets. The halo remains visible with bloom disabled, and scales with pipe diameter. A dry or fully blocked system stays unlit.

## Installing the split

Install `ArcaneDecorFlowCraft.dll` on the server and every client. If retaining natural rivers and hot springs, also update to **ArcaneNaturalWaters 0.9.9 or later**. Remove the old combined `ArcaneWaterWorks.dll` and replace any ArcaneNaturalWaters 0.9.8 or earlier DLL. Do not keep duplicate DLLs in other plugin folders.

Plumbing uses new FlowCraft prefab and item IDs. Existing test pipes are not migrated; rebuild them with FlowCraft. Natural rivers and hot springs remain in ArcaneNaturalWaters. The two mods do not depend on one another.

Synchronized timed water spouts remain a future addition. Dragon fountains run continuously while supplied.

Build with `Build-Package.ps1`. See `PLUMBING.md` for the plumbing guide.

## Fountain size and splash

Both dragon fountain stream diameters are doubled from 0.2.4. At equal supplied flow, an 8 cm fountain still has twice the stream diameter of a 4 cm fountain. This visual change does not alter flow rate or throw distance. Its impact spray uses twice-sized droplets, twice the emission radius and four times the particle emission; longer splash flight produces a larger footprint. Pipe diameter controls this visual scale, while supplied flow controls launch speed. Each powered piece still contributes 5 gallons/sec in either size.


Copper Pulse Fountain (0.3.1): a ground-mounted copper catch dish with an aimable nozzle, built for 2 Copper in the existing FlowCraft category. A FlowCraft jet hitting the dish triggers a fixed burst; aim it at another dish to build cascading water features. E turns the nozzle, Shift+E changes tilt. See PLUMBING.md for timing and setup.
