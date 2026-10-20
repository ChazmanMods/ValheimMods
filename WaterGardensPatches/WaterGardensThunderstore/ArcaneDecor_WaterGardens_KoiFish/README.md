# ArcaneDecor:WaterGardens:KoiFish 0.2.2

Bring your water garden to life with **Orange Koi, Golden Koi, and Kohaku**. This optional add-on requires **ArcaneDecor:WaterGardens 1.3.4 or later**.

Fish follow smooth wandering routes, spend about a third of their cruising time near the surface, and dive on staggered schedules. Toss food into the pond to gather the school for gentle feeding. Fish ease away from nearby players entering the pond.

## Installation

Install **ArcaneDecor_WaterGardens_KoiFish** with a Thunderstore-compatible mod manager. Allow it to install WaterGardens, Jötunn and BepInEx dependencies. Launch Valheim through the modded profile.

For manual installation, copy the contents of `plugins` into `BepInEx/plugins`. Install the listed dependencies separately.

For multiplayer, install matching versions of WaterGardens and KoiFish on the server and every participating client. This version was tested in a local game; dedicated-server feeding, simultaneous feeding and reconnect behavior have not received new separate-client validation. Treat multiplayer use as beta.

### Updating from the private WaterGardens_Koi build

Disable or remove the old **WaterGardens_Koi** package before enabling this package. Keep only one `WaterGardensKoi.dll` installed. The internal plugin ID, pond settings and food prefab IDs are retained, so existing fish counts and sizes carry forward. The internal name remains `chazman.WaterGardensKoi` for compatibility.

## Add fish to a pond

1. Place a WaterGardens pond.
2. Look at its configuration rock and press **E**.
3. Expand **Fish >**.
4. Set the numbers and size, then select **Save Garden**.

| Control | Range |
| --- | --- |
| Orange Koi | 0–10 |
| Golden Koi | 0–10 |
| Kohaku (white and orange) | 0–10 |
| Fish size | 1×–3× for the pond's school |

Defaults are one of each variety at 1×. Set all counts to zero for no fish. **Cancel** discards menu edits. Settings belong to the individual pond; river configuration stones do not have fish controls.

## Feed the fish

In **Hammer → Water Garden**, place **Koi Food** for **1 Raspberry + 1 Mushroom**. Pick the supply to receive ten food packets. Inventory crafting also produces ten packets for the same ingredients.

Put Koi Food in a toolbar slot and use that slot to toss food into the pond. The school gathers around it, gently feeds for about three seconds, then returns to swimming. A dropped stack feeds in successive rounds, one unit per round.

Food that misses the pond remains recoverable. An empty pond does not consume food.

## Natural movement

- Independent wandering and staggered surface visits.
- Gentle feeding bobs, head tilts and tail movement.
- Nearby players standing or swimming in the pond cause fish to ease aside.
- Players above the water do not trigger avoidance.
- Movement and turning are smoothed; distant schools stop animating.

## Limitations and removal

Fish are decorative, not catchable creatures or inventory items. Their paths are visual and can differ slightly between observers. Avoidance does not push players or change food consumption.

Back up your world before changing its mod setup. Before removing this add-on, remove its placed Koi Food supplies and remaining food items while it is installed. The parent WaterGardens mod can remain installed independently.

## Credits

Created by **Chazman**, who created the koi models and textures. Native Valheim effects are referenced from the installed game. The shared WaterGardens medallion icon is **AI-generated artwork** selected by Chazman. See `THIRD_PARTY.md` for credits.

## Support

[Chazman Mods Discord](https://discord.gg/7HKHTCdFqY) · [Chazman Mods GitHub](https://github.com/ChazmanMods/ValheimMods)

Include your game and mod versions, whether you use a dedicated server, steps to reproduce the problem, and the relevant BepInEx log.
