<div align="center">

<img src="docs/syncmaid.png" alt="SyncMaid" width="128" />

# SyncMaid

**One-way file sync for Windows, done for you.**

SyncMaid watches a source folder and keeps one or more destinations in sync with it — each
destination with its own filters and strategy (mirror, add-only, or move), triggered manually,
on a schedule, or by watching for changes. It lives in the system tray and keeps working while
you don't.

</div>

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download) or later
- Windows (the app is Windows-only)
- For a native AOT publish: the MSVC C++ toolchain (e.g. from a *Visual Studio Developer
  Command Prompt* or the "Desktop development with C++" workload)

## Build & run

Restore, build, and launch the app for development:

```powershell
dotnet run --project SyncMaid
```

A plain Release build (framework-dependent, not AOT):

```powershell
dotnet build -c Release
```

## Tests

Run the whole test suite (engine + UI):

```powershell
dotnet test
```

Run just the engine tests (`SyncMaid.Core`, fast, no UI):

```powershell
dotnet test SyncMaid.Core.Tests/SyncMaid.Core.Tests.csproj
```

Run just the headless UI tests (Avalonia view models and end-to-end flows):

```powershell
dotnet test SyncMaid.UiTests/SyncMaid.UiTests.csproj
```

## Publish a user-ready build

SyncMaid publishes as a self-contained **native AOT** executable — a single folder with no
.NET runtime prerequisite for the end user. Run this from a *Developer Command Prompt* (so the
MSVC toolchain is on the path):

```powershell
dotnet publish SyncMaid -c Release -r win-x64
```

The published app lands in `SyncMaid/bin/Release/net10.0/win-x64/publish/`. Ship that folder's
contents (the `.exe` and its assets) to users.
