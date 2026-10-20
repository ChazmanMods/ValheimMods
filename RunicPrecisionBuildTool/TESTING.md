# Runic Precision Build Tool 2.0.9 verification

Based on the 2.0.8 release source, whose existing compiled DLL SHA256 matched the upload package: 0E04958F9E3E5A7AFBDA7D24F95598206D293001133488A4456210B4F675054E.

The horizontal panel update preserves the standalone plugin's StartupAccelerator lifecycle, language files, and native placement behavior. Existing bounded undo and area repair continue to require the local peer to be the current owner, including on a dedicated client. Vanilla building remains unchanged when precision mode is off.

Build: dotnet build RunicPrecisionBuildTool -c Release.
Focused suite: dotnet run --project RunicPrecisionBuildTool.Tests -c Release.
Startup lifecycle: dotnet run --project tools/PrecisionStartupRegression -c Release.

Manual layout checks: show/hide target and axis readouts, change font size, resize the window, inspect long coordinates, and leave Hammer placement. Verify side-by-side columns, screen margins, and restoration of the native panel dimensions.

The installed game is 1.0.16; four existing focused test fixtures still require 1.0.15 version/hash values. Live in-game layout verification has not been performed.