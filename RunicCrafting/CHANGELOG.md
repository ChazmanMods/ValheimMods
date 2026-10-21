# Changelog

## 2.4.0

- Storage, Production and Crafting now verify at startup that they were built from the same release. Mixed versions log one error and keep every module on vanilla inventory handling instead of running a version mix. The startup line shows the shared core hash.
- All three modules now release together at one version.

## 2.3.2

- Keep background automation at its intended two-second retry interval while awaiting remote chest state.
- Reduce unnecessary background request traffic.
- Preserve prompt retries for manual player actions.
