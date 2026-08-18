# Changelog

## 1.0.2

- Restore pitch, roll, movement, fine-step, reset, and guide controls by reading input through Valheim's `ZInput` layer.
- Allow configured fine and modifier shortcuts to work as part of multi-key build-control chords.
- Make the movement modifier configurable and default it to `Right Alt`, avoiding Infinity Hammer's `Left Alt + Arrow/Page` placement bindings.

## 1.0.1

- Prevent placement input polling before the local player exists.
- Prevent repeated `NullReferenceException` log spam while connecting, spawning, disconnecting, or returning to the main menu.

## 1.0.0

- Initial release.
