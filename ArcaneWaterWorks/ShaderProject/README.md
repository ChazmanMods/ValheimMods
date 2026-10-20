# River surface shader

Build with Unity 6000.0.75f1, matching Valheim's built-in render pipeline:
`-batchmode -nographics -quit -executeMethod BuildRiver.Build`, with this directory
as `-projectPath`. Output `../Assets/riverflow` is embedded in ArcaneNaturalWaters.dll.

Only the custom shader is bundled. Normal and foam textures are borrowed from the
installed game's ocean at runtime. The river renders at queue 3050, after native water.
A named GrabPass captures the scene, including receiving water, for refraction.
Opaque camera depth clips banks and objects, softens intersections, and rejects
refraction offsets that would pull foreground objects into the water. The river
crossfades at sea level only over a submerged bed. Explicit opaque-depth testing
allows the transition to blend over the native ocean without competing wave depths
cutting it into triangles. Reflection probes and sunlight supply reflection.
The surface pass also uses the native water stencil comparison (not equal to 2),
honoring the boat interior masks drawn by Valheim's Custom/WaterMask shader.
There are no foam sprite overlays, additional colliders, or CPU fluid simulation.

UV2 stores world X/Z downstream velocity. UV3 stores shoreline distance and outlet
blend. UV4 stores depth and distance to a pond. Vertex color green stores whitewater
strength, and blue stores wave amplitude. Two overlapping six-second advection phases
move normal textures and displaced waves downstream. RiverWaveMath.cs mirrors the
wave formula for buoyancy using the synchronized game clock. Shore and receiving-water
fades suppress displacement where water meets terrain or another water body.
OceanJoin also tapers the native ocean contribution at dry/bank vertices; CPU liquid
height queries use the same bank weight.

The release bundle targets Windows 64-bit/D3D. Unsupported renderers retain the
native water material and log an error. Headless servers do not load the shader.
