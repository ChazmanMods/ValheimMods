# Runic Sentinel Forge 1.1.0

Runic Sentinel Forge is the offline administrator tool for Raven's Gate. It creates the RSA-3072
signing key, compiles an editable JSON rules file into a canonical signed passport, and verifies a
passport without launching Valheim.

Keep `RunicSentinel.private.pem` offline and out of every r2modman profile, game server, cloud-sync
folder, support report, and source repository. Only the policy, signature, public key, and the
printed public-key SHA-256 pin belong in the game configuration.

## Commands

```text
RunicSentinel.Forge.exe keygen <offline-key-directory>
RunicSentinel.Forge.exe compile <rules.json> <RunicSentinel.private.pem> <output-directory>
RunicSentinel.Forge.exe verify <policy> <signature> <public-key> <public-key-pin>
RunicSentinel.Forge.exe selftest
```

The tool requires the .NET 8 Desktop Runtime on the administrator's Windows computer. The
Thunderstore Runic Sentinel plugin package deliberately does not contain Forge: BepInEx scans DLLs
under its plugin directory, so keeping the administration tool in a separate archive avoids loading
an unrelated command-line assembly into Valheim.

Use `RunicSentinel.rules.example.json` as a schema example. For the exact installed profile, launch
once with Sentinel admission set to Optional and edit the generated
`BepInEx/config/RunicSentinel.current-profile.json` instead.
