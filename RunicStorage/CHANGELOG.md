# Changelog

## 2.4.0

- Storage, Production and Crafting now verify at startup that they were built from the same release. Mixed versions log one error and keep every module on vanilla inventory handling instead of running a version mix. The startup line shows the shared core hash.
- All three modules now release together at one version.
- Documentation: the install instructions describe the matching-version requirement and the client/server fallback; verification history moved to `docs/release-notes`.

## 2.3.5

- Restored Alt+S to sorting the open chest.
- Moved personal inventory sorting to Shift+Ctrl+P.
- Personal sorting preserves player slot locks, including empty locked cells, alongside equipped, hotbar and dedicated-slot protection.


## 2.3.3

- Fixed quickstack checking an uninitialized or stale chest inventory before the native contents had loaded. Matching and capacity checks now use the current candidate inventory on the same press.
- Alt+S now sorts your carried inventory, including while a chest is open. The controller chest-sort action remains available.
- Carried sorting preserves protected items and empty reserved slots without changing item counts or metadata.

## 2.3.2

- Keep background automation at its intended two-second retry interval while awaiting remote chest state.
- Reduce unnecessary background request traffic.
- Preserve prompt retries for manual player actions.
