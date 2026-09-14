# Runic Precision Build Tool 2.0.3 — Instructions

## Start and stop

Equip the Hammer, enter its build mode, select a piece, and press P. Precision mode lasts only for
that Hammer placement session. P does nothing in ordinary gameplay or while another tool is active.
Press P again or leave Hammer placement mode to return to ordinary Valheim controls.

## Rotate and move

| Default control | Result |
|---|---|
| Wheel / Alt + Wheel | Yaw / pitch |
| Shift + Wheel or Alt + Shift + Wheel | Roll |
| Add V to a wheel chord | Fine rotation |
| Alt + Left/Right | Sway |
| Alt + Up/Down | Heave |
| Alt + PageUp/PageDown | Surge |
| Add V to a movement chord | Fine movement |
| Hold G | Piece-local axis guides |
| F10 | Reset all Runic transform state |

Defaults are 22.5-degree normal rotation, 1-degree fine rotation, 0.25 m movement, and 0.05 m fine
movement. Yaw, pitch, and roll always follow the pending piece's current local Y, X, and Z axes.
`Movement.ReferenceFrame` separately selects World or Local axes for each new movement step.

## Match, repeat, and catalog

Aim at an existing piece before matching.

| Default control | Result |
|---|---|
| Keypad0 | Exact rotation |
| Keypad1/2/3 | Pitch/roll/yaw only |
| Keypad4/5/6 | World X/Y/Z only |
| Keypad7 | Complete position |
| Keypad8 | Complete position and rotation |
| Keypad9 | Last validated completed snap side |
| KeypadPeriod | Repeat the last two-placement pattern |
| Shift + Keypad1-Keypad6 | Reset one axis |

While the piece menu is open, F6 cycles unlocked search results, F7 toggles a favorite, F8 cycles
unlocked favorites, and F9 cycles unlocked recents. Set `Build Catalog.SearchQuery` to filter F6;
leave it empty to cycle all currently unlocked pieces.

## Undo and area repair

With precision mode active and the piece menu closed:

- F11 attempts a one-shot undo of the most recent eligible placement from this session.
- F12 attempts a bounded area repair around the player.

Both actions use fresh native checks and change only exact loaded pieces whose `ZNetView` Valheim
currently assigns to the local peer. Undo additionally requires the original unchanged, full-health,
inert, structurally independent placement. Area repair charges normal hammer durability once for
each successful repair. Peer-owned or server-owned pieces are skipped; ownership is never claimed.

This applies equally to solo, listen-host, and dedicated-client play. A failed action creates no
pending operation, persistent journal, inventory lock, or recovery task.

## Failure behavior

The readout augments Valheim's selected-piece panel and hides only its own rows when appropriate.
If an installed Valheim method or IL seam differs from the audited 1.0.7 contract, the plugin
disables its hooks and leaves vanilla building available. Version 2.0.3 supports keyboard/mouse and
has no controller bindings.
