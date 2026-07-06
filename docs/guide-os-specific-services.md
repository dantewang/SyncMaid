# Guide: OS-specific features the .NET-idiomatic way

**Status:** structural-improvement guide — not yet implemented
**Motivation:** autostart ("Start with Windows") is currently a single Windows-only service
with `OperatingSystem.IsWindows()` guards sprinkled inside it. There's no plan to ship macOS
or Linux soon, but OS-specific behavior should follow the standard .NET pattern so the seam is
right and adding a platform later is a drop-in, not a rewrite.

## The .NET-standard pattern

.NET's first-party tools for this are the **platform-compatibility attributes**
(`[SupportedOSPlatform("windows")]` / `[UnsupportedOSPlatform(...)]`), the **`OperatingSystem`
guards** (`OperatingSystem.IsWindows()/IsMacOS()/IsLinux()/IsWindowsVersionAtLeast(...)`), and
the **CA1416 analyzer** that ties them together. The idiomatic shape for a cross-platform app
with some OS-specific behavior:

1. A **platform-neutral interface** in shared code (no platform attributes).
2. **One implementation per OS**, each annotated `[SupportedOSPlatform("…")]`, containing the
   platform APIs (no internal `if (OperatingSystem.IsWindows())` guards — the class is only
   ever constructed on its platform).
3. A **DI selector** at the composition root that picks the implementation using
   `OperatingSystem.Is*()` — which the analyzer recognizes, so calling a
   `[SupportedOSPlatform("windows")]` constructor inside an `if (OperatingSystem.IsWindows())`
   is CA1416-clean.
4. A **no-op fallback** for unsupported platforms so callers never branch.

```csharp
services.AddSingleton<IAutoStartService>(_ =>
    OperatingSystem.IsWindows() ? new WindowsAutoStartService()
    : OperatingSystem.IsMacOS() ? new MacAutoStartService()
    : OperatingSystem.IsLinux() ? new LinuxAutoStartService()
    : new NoOpAutoStartService());
```

DI factory lambdas keep this AOT-safe (no reflection), consistent with the existing composition
root. Avoid multi-targeting / `#if` / separate RID assemblies — unnecessary here and heavier.

## Refactor autostart to fit

- Keep `IAutoStartService` (already platform-neutral).
- `WindowsAutoStartService`: restore `[SupportedOSPlatform("windows")]` on the class and
  **remove the internal `OperatingSystem.IsWindows()` early-returns** (they only existed to
  satisfy CA1416 from a neutral call site — the selector guard now does that).
- Add `NoOpAutoStartService` (returns `Disabled`, no-ops) for the fallback.
- Move the platform choice into `App.ConfigureServices` per the selector above.
- Future impls when/if needed:
  - **macOS:** a LaunchAgent plist at `~/Library/LaunchAgents/com.syncmaid.autostart.plist`.
  - **Linux:** an XDG autostart entry at `~/.config/autostart/syncmaid.desktop`.

## Also apply the pattern to

- **Recycle Bin** (`PhysicalFileSystem.Recycle`, currently `shell32!SHFileOperation` P/Invoke,
  Windows-only): factor the platform-specific delete behind the same interface/selector shape
  if cross-platform ever matters (macOS: `NSFileManager trashItem`; Linux: XDG trash spec).
  Lower priority than autostart.
- **What does NOT need this:** the tray icon / close-to-tray (guide-tray-icon.md) is fully
  cross-platform through Avalonia's `TrayIcon`/`NativeMenu`, and the config-location resolver
  (guide-config-location.md) is plain `System.IO`. Only genuinely OS-native integrations
  (registry, shell trash, launch agents) need the selector pattern.

## Test plan

- `NoOpAutoStartService`: `GetState()` is `Disabled`; `Enable()`/`Disable()` are no-ops.
- Selector: to make it testable rather than reading the ambient OS, consider a tiny
  `IPlatformInfo` seam (or just cover the concrete impls per-OS manually). Existing autostart
  VM tests already use a fake service, so the dialog is unaffected by this refactor.
- Windows impl behavior is unchanged (Run key) — the existing manual verification still holds.
- AOT publish stays warning-free.
