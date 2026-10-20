# 0.1.2

- Fixed a severe populated-world startup stall: structure protection now indexes nearby generated ruins once instead of checking every saved object against every location worldwide.
- Structure scanning and ruin cleanup yield between objects so loading can continue rendering and reporting progress.
- Waiting states now appear periodically in the log, including location generation and player-presence waits.
- Existing settlement layouts and terrain caches are retained. No new world is required.
# 0.1.1

- Added large, double-sided, permanently lit direction signs near spawn and at the road entrance.
- Spawn directions include the compass bearing and distance to the start of the paved road.
- Existing prepared layouts receive markers on load without rebuilding terrain. Marker IDs prevent duplication and player removals are respected.

# 0.1.0

Initial standalone settlement preparation test release. Up to five 120 m community clearings, connected paved roads, permanent fuel-free lights, sparse oak/pine perimeter trees, small-ruin replacement, player-build protection, saved world layouts and cached native terrain. Manual installation; incompatible with RunicPaths in the same profile.