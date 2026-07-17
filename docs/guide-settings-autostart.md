# Guide: Settings dialog + start with Windows

**Status:** implementation guide — not yet implemented
**Depends on:** the title-bar Settings button (guide-custom-titlebar.md) as the entry
point, though a toolbar button works as an interim trigger.

## Scope

A Settings dialog (in-window modal, same pattern as the editors) containing, for now, a
single option: **"Start SyncMaid when Windows starts"**. No other settings yet.

## Autostart mechanism — decision

For an **unpackaged** Win32 desktop app (which SyncMaid is — a native-AOT exe), the
standard, Microsoft-documented, least-AV-suspicious mechanism is the **per-user Run key**:

```
Key:   HKCU\Software\Microsoft\Windows\CurrentVersion\Run
Name:  SyncMaid
Value: "C:\path\to\SyncMaid.exe"        (quoted)
```

Why this one:
- Per-user, **no elevation** needed (HKLM would require admin and affect all users).
- Fully visible and user-controllable in **Task Manager → Startup apps** and
  Settings → Apps → Startup — this transparency is exactly why it's the least likely to
  be flagged; AV heuristics target *covert* persistence (services, HKLM under
  non-admin-looking installers, scheduled tasks with hidden flags, obscure hives).
- Startup-folder `.lnk` is equally legitimate but shortcut creation needs COM
  (`IShellLink`) — more code, no benefit.
- Task Scheduler is *more* commonly flagged and is overkill.
- MSIX `StartupTask` is Microsoft's most-recommended path **for packaged apps only** —
  note it as the future answer if the app is ever MSIX-packaged; not applicable now.

`Microsoft.Win32.Registry` is in-box, Windows-only (fine — app is Windows-only), and
AOT-compatible (no reflection). No new packages.

## Design

### Service

```csharp
public interface IAutoStartService
{
    AutoStartState GetState();          // Enabled | Disabled | DisabledByWindows
    void Enable();                      // writes quoted Environment.ProcessPath
    void Disable();                     // deletes the value (no-op if absent)
}
```

Implementation `WindowsAutoStartService` in the app project (not Core — it's a Windows/UI
concern), registered in DI; the dialog VM takes the interface so tests use a fake.

Details that matter:
- **Value = quoted `Environment.ProcessPath`.** On each `GetState()`, if the value exists
  but points to a different path (user moved the exe), treat as Enabled and rewrite on
  next `Enable()` — or simpler: `Enable()` always rewrites the current path, and the
  checkbox toggling handles drift naturally.
- **The registry Run value is the single source of truth.** Do **not** introduce a
  `settings.json` for this feature — there is nothing else to persist yet. (When future
  settings need a file, add a `JsonSettingsStore` following the existing store patterns.)
- **Windows' own kill-switch:** Task Manager "disable" doesn't delete the Run value; it
  writes a disabled flag under
  `HKCU\...\CurrentVersion\Explorer\StartupApproved\Run` (binary blob, first byte odd =
  disabled). Read it in `GetState()` and report `DisabledByWindows` so the UI can show
  *"Turned off in Windows Task Manager — enable it there"* instead of a checkbox that
  silently does nothing. Do **not** write to StartupApproved (that would be exactly the
  kind of override AV dislikes); deleting + re-creating the Run value on re-enable is the
  clean reset and is acceptable.

### Dialog

- `SettingsViewModel : DialogViewModel<bool>` (result unused; `Close` on the single
  "Close"/"Done" button) + `SettingsView` UserControl on `Border.dialogCard`, registered
  in `App.axaml` data templates like the two editors — the `DialogHost` machinery needs
  zero changes.
- Content: a "Startup" section label, a `CheckBox` "Start SyncMaid when Windows starts",
  applied **immediately on toggle** (not on OK — settings dialogs shouldn't need a save
  step for a registry toggle), plus the `DisabledByWindows` info row (reuse the
  `filterRow` + warning-icon pattern from the network-verify caution).
- Entry point: title-bar gear (preferred) or a temporary toolbar icon button; command on
  `MainWindowViewModel`: `await _dialogHost.ShowAsync(new SettingsViewModel(_autoStart));`
  via `IDialogService` for consistency.

### Behavior note (worth a line in the dialog)

Starting with Windows opens the main window by default. ✅ Superseded by "Start minimized
to the system tray" (issue #13): a persisted `AppSettings.StartMinimized` setting —
deliberately independent of autostart, applying to every launch — rather than the
`--minimized` Run-value argument originally sketched here. See guide-tray-icon.md.

## Test plan

- Fake `IAutoStartService` → VM tests: checkbox reflects state; toggle calls
  Enable/Disable exactly once; `DisabledByWindows` shows the notice and disables the box.
- Real service: manual verification only (registry writes in unit tests are hostile to CI)
  — enable, check Task Manager Startup list shows "SyncMaid", reboot or re-login once,
  disable, confirm value gone. Optionally an integration test behind a
  `[Trait("Category","Manual")]`.
- Headless UI test: settings dialog renders in the overlay (mirror the existing
  `Showing_a_dialog_renders_it_in_the_main_window_overlay` test).
- AOT publish stays warning-free (registry APIs are AOT-safe; verify once).
