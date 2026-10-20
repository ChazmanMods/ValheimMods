# Startup lifecycle regression

PluginUnderTest.cs is extracted verbatim from the production Plugin.cs through the end of the Plugin class (before PlayerHandleRadialInputPatch). Regenerate it after changing Plugin.cs. Only external Unity/BepInEx/Harmony/runtime dependencies are replaced by stubs. This simulates immediate and deferred patch application; it is not a live StartupAccelerator/Unity test.

Run `dotnet run --project PrecisionStartupRegression.csproj -c Release`.
