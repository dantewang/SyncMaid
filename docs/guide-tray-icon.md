# Guide: tray icon + close-to-tray

**Status:** implementation guide — not yet implemented
**Depends on:** the Settings dialog (guide-settings-autostart.md) for the toggle; introduces
the first persisted app *setting* (see "Settings storage" below).

## Goal

Run SyncMaid in the background from the system tray:
- A **tray icon** (the app icon) with a tooltip.
- A **"Close to tray"** setting: when on, closing/minimizing the main window **hides** it to
  the tray instead of exiting; triggers (watch/scheduled) keep running.
- The tray icon's **right-click menu** has **"Show main window"** and **"Exit"**.
- Left-click the tray icon → show/activate the main window.

This is the missing half of the background-app story: watch/scheduled tasks already run
without the window focused, and the mirror mass-delete flow was deliberately built to flag a
blocked run on the row (a `NeedsConfirmation` state + an *independent* confirmation window)
rather than pop the main window — precisely so it behaves well once the app lives in the tray.

## Avalonia support (verify against 12.0.5 before building)

Avalonia has first-party `TrayIcon` + `NativeMenu` (cross-platform: Windows/macOS/Linux, with
the usual platform caveats), so **no per-OS code is needed** for the tray itself. API surface
to confirm against the installed package:
- `Avalonia.Controls.TrayIcon` — `Icon` (`WindowIcon`), `ToolTipText`, `IsVisible`, `Menu`
  (`NativeMenu`), `Command`; `Clicked` event.
- Attached to the `Application` via `TrayIcon.Icons` (a `TrayIcons` collection) — declaratively
  in `App.axaml`, or programmatically with `TrayIcon.SetIcons(app, icons)`.
- `NativeMenu` / `NativeMenuItem` (`Header`, `Command`/`Click`) for the context menu.

Recommend **programmatic** creation in `App.OnFrameworkInitializationCompleted` (it already
owns the DI graph and the desktop lifetime), reusing `Assets/syncmaid.ico`:
`new WindowIcon(AssetLoader.Open(new Uri("avares://SyncMaid/Assets/syncmaid.ico")))`.

## Design

### Lifetime / close-to-tray mechanics

- Set `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown` so hiding the last window does
  not quit the app.
- Handle `MainWindow.Closing`: if **close-to-tray is on**, `e.Cancel = true` and `Hide()` the
  window; otherwise call `desktop.Shutdown()` (preserves the normal "close = exit" behavior
  when the setting is off).
- Tray menu: **"Show main window"** → `Show()` + `Activate()` (and restore if minimized);
  **"Exit"** → `desktop.Shutdown()`. Left-click `Clicked` → same as "Show main window".
- Dispose the `TrayIcon` on shutdown.
- Keep the tray-icon wiring behind a small seam (e.g. `ITrayController`) so `App` stays thin
  and the hide-vs-exit decision is unit-testable (the tray/native menu itself is manual-test
  only).

### Settings storage (new)

"Close to tray" is a genuine app preference and needs to persist. Autostart deliberately uses
the registry as its own source of truth, so there is **no settings file yet**. Introduce a
`JsonSettingsStore` following the existing store pattern (`JsonTaskStore`/`JsonStatusStore` +
`AtomicFile.Write`), writing `settings.json` next to `tasks.json` in the config dir. Start it
with one field:

```csharp
public sealed record AppSettings(bool CloseToTray = false);
```

Register `ISettingsStore` in DI; `SettingsViewModel` reads/writes it. (This is also where the
future config-location choice and "start minimized" would live — see guide-config-location.md
and the autostart guide.)

### Settings dialog

Add a "Window" section to the Settings dialog with a **"Close to tray instead of exiting"**
checkbox, applied immediately (same pattern as the autostart toggle). Note in the dialog that
scheduled/watch tasks keep running while hidden.

### Interactions / follow-ons (note, don't necessarily build)

- **Start minimized:** with autostart on + close-to-tray, ideally start hidden to the tray.
  Implement by passing `--minimized` in the Run-key value (the autostart guide flagged this)
  and honoring it at startup (don't `Show()` the main window; just create the tray icon).
- **Single instance:** launching a second copy (e.g. autostart + manual) should focus the
  existing one rather than start a second tray icon. Out of scope here; note as a follow-up
  (a named-mutex / single-instance check at startup).

## Test plan

- `ISettingsStore` round-trips `CloseToTray` via the in-memory filesystem (mirror the
  `JsonTaskStore` tests; reuses `AtomicFile`).
- The hide-vs-exit decision as a pure/seam-tested unit: close event + `CloseToTray=true` →
  cancels + hides; `false` → shuts down.
- `SettingsViewModel`: the checkbox reflects and updates the stored value.
- Tray icon, native menu, and actual show/hide are **manual** on Windows (and later per-OS).
- AOT publish stays warning-free (`TrayIcon`/`NativeMenu` are standard Avalonia; the settings
  store is System.Text.Json source-gen like the others).
