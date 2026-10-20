# RunicSigns 1.2.6 release acceptance — 2026-09-19

The author confirmed all updates in this task tested in-game and authorized Thunderstore packaging. Release packaging preserves the tested DLL from the final candidate; changes at promotion are documentation and configuration-example corrections only. Historical test notes below remain as implementation records, not outstanding release blockers.

# RunicSigns 1.1.0 release acceptance

The author confirmed successful in-game testing on September 17, 2026.

35 tests, including emoji saves and placement sizing; cross-mod input recovery in both load orders; installed-game contracts and release-package checks.

Storage 1.3.0 and Signs 1.1.0 retain emoji support and their existing version numbers while incorporating the previously published protection and placement behavior. Automated regression results are recorded separately from the author’s in-game confirmation.


## Fine bend controls: manual acceptance

- On each axis, click + once from zero: the field should read 1 and the bend should equal the old RunicSigns 0.01 effect. Click − to return to flat; repeat with negative values. Type 0.1 for a tenth of that step.
- Combine both axes, include emojis, reset, save, reopen and reconnect. Confirm the values and shapes persist. Cancel a draft and verify the saved label/sign does not change.
- Check existing Signs curves retain their shape while their displayed number is 100 times the previous number. Check existing chest labels load flat and preserve routes, colors, backgrounds and positions.
- For chest labels, check front/back/left/right/top, lid opening, and opaque backgrounds. A backward bend must stay visible in front of its backing.
- Check both panels at typical display sizes: each axis has separate −/+ buttons without overlap, and all fields are reachable.

## 1.2.8 vanilla compatibility — live acceptance checks still required

Automated tests exercise production save/render logic with simulated peers; they do not render real TMP geometry or connect two game clients.

1. Run 1.2.8 on the authoring client, with a completely vanilla second client and server. Enable vanilla viewer mode and save a portal label with text-unit vertical offset 40, positive and negative horizontal offsets, color/opacity, size, selected bold/italic and multiple lines.
2. Compare both views after the native refresh interval. Verify the author preview matches the vanilla client, including alignment, original material tint and normal board size. Repeat each alignment and sign-fraction offsets; native indentation is not a transform translation.
3. Place a new compatible sign, travel away/back, reconnect both clients, and restart the server. Confirm caption formatting persists and no custom RPC timeout occurs when a vanilla client owns the sign.
4. Reopen/save twice in RunicSigns; verify the caption is plain editable text, literal angle brackets survive, and formatting does not compound. Cancel a changed draft and verify the saved display returns.
5. Test a ward-denied edit, revoked access, muted author, and filtered user content. No preview or metadata may expose hidden caption text.
6. Verify full-feature signs still render normally; toggle viewer mode and cancel, then toggle and save. Unsupported features must be absent in compatible mode, including emoji artwork and depth/curve effects.
7. Try a maximum-length caption containing many literal '<' characters and 128 style runs. Oversized exports must show an error and write nothing. Vanilla editing is subject to its normal small character limit and may damage the formatted caption; use RunicSigns for editing.

## 1.2.9 multiline-offset verification

- 70 production regression tests and installed-game API contracts passed.
- Unity batch geometry check used the installed Valheim Unity.TextMeshPro.dll with an Arial test font and native wrapping. It reproduced the old bug at +12: first row moved 12, second row moved 0.
- Corrected formatting moved every visible glyph by the same amount for wrapped text, explicit two-line captions, and three-line captions at -12, +12 and +40. Native centered alignment can change the magnitude of negative offsets, but line spacing remains constant.
- Geometry report: Research/RunicSigns-VanillaCompatibility/geometry-result.txt (workspace root). This does not replace an in-game test with the actual sign font and a vanilla peer.
- Existing saved signs need to be reopened and saved in 1.2.9 to export the corrected tags for vanilla viewers. No automatic world rewrite occurs.
