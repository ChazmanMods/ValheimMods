# Changelog

## 2.4.0

- Storage, Production and Crafting now verify at startup that they were built from the same release. Mixed versions log one error and keep every module on vanilla inventory handling instead of running a version mix. The startup line shows the shared core hash.
- All three modules now release together at one version.
- Documentation: fixed the mangled separator in the README, moved the sap extractor and wisp fountain section into the body, and set the package link to the Runic Mods Discord like the other modules.

## 2.3.4

- Updated ownership status text and package documentation.

## 2.3.3

- Added Output chest links for sap extractors and wisp fountains.
- Preserved native root/extraction and fountain spawning behavior and actual modded/scaled drops.
- Paused additional automatic collection while a collector has output waiting for storage.

## 2.3.2

- Keep background automation at its intended two-second retry interval while awaiting remote chest state.
- Reduce unnecessary background request traffic.
- Preserve prompt retries for manual player actions.
