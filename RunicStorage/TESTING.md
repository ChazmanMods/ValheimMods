# Runic Storage 1.0.0 verification

Run the focused deterministic suite from the repository root:

```powershell
dotnet run --project RunicStorage\Tests\RunicStorage.Tests.csproj -c Release
```

The focused tests cover nearest eligible planning, protected-source filtering, partial capacity,
bounded query coverage and spatial cells, action/UI routing, local native ownership requirements,
controller sessions, diagnostics, hover disclosure evidence, optional Inventory absence, and static
standalone-architecture checks.

Manual checks for this repair:

- With a chest open, use Alt+A and Alt+R; with it closed, use Alt+C and Alt+R. Valid carried items
  must not fail merely because Inventory returned a fresh native list object.
- Use Alt+F near several chests, immediately type in the focused filter, click an item, and verify
  every matching chest receives the animated yellow highlight while nonmatching chests do not;
  clicking the filter, an item, Clear, or Close must not punch, swing, or use the equipped tool.
- While Alt+F is open, change `Search.MenuFontSize` and choose several named values from the
  `Search.MenuFontColor` dropdown in Configuration Manager. Verify the title and every label, text
  field, action button, item row, and empty-result message update; at size 32, controls and rows
  must grow vertically without clipping their text. Close and reopen with `MenuFontColor = Blue`;
  the text must remain blue—native-theme reconstruction must not overwrite the persisted choice.
- Compare the Alt+F picker with Valheim's inventory and split-stack dialog. It must use the actual
  live panel Sprite, button states, borders, TMP font, text-input art, scrollbar art, and UI scale.
  It must never show a solid/neon-yellow atlas region around the window. Repeat in bright midday
  sunlight and a dark interior; the scene scrim, opaque panel, and inset list must keep the configured
  text readable. Hover and press Clear, Close, and item rows to verify native button states.
- With a UI replacement mod that removes or swaps an Inventory panel/button sprite, reopen Alt+F.
  The picker must rebuild for the changed live source or use its opaque brown native-control fallback;
  filtering, clicking, scrolling, cursor restoration, and attack suppression must remain unchanged.
- Select a nearby item and verify both yellow rings animate around every matching chest for the
  full highlight lifetime without `StorageSearchHighlight.UpdateVisuals` exceptions. Destroying or
  unloading a highlighted chest must remove its marker without repeated Unity log errors.
- Close the picker with Escape and verify movement/input and the cursor return to their prior state.

Build the plugin with:

```powershell
dotnet build RunicStorage\RunicStorage.csproj -c Release
```

Before release packaging, the suite-level acceptance run also verifies a dedicated-server client
path where owned player/container mutations succeed and non-owner mutations fail closed. Historical
artifacts under `Tests/LiveEvidence` are retained as reference material; they are not a substitute for
the current standalone acceptance run.

The architecture checks require:

- only BepInEx in the package manifest;
- no runtime project reference or BepInEx dependency on another Runic mod;
- no Storage transfer RPC, durable saga, WAL/journal, quarantine, or suite-global mutation gate;
- native player and container ownership at every mutation entry point; and
- Inventory item protection remaining optional.
