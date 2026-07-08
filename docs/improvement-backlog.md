# Improvement backlog (code review findings)

**Status:** review findings + recommendations, ranked. Companion guides:
guide-custom-titlebar.md, guide-filter-composition.md, guide-settings-autostart.md.

**Overall state:** solid. 137 tests, warning-free native AOT, hardened sync engine
(atomic verified copy/move, mirror guardrail, retry, run lock), provider seam for future
backends, clean MVVM with DI. The items below are the gaps that remain.

## 🔴 High value / low effort

### 1. Config writes are not atomic (ironic, given the engine) — ✅ done (`b841ae0`)
`JsonTaskStore.Save` and `JsonStatusStore.Save` overwrite `tasks.json` / `status.json`
in place via `WriteAllBytes`. A crash or power cut mid-write corrupts the file — for
`tasks.json` that means **losing every task definition**, the exact failure mode the sync
engine was hardened against. `IFileSystem` already has the primitives
(`CreateWriteThrough` + `Replace`): write to a sibling temp, then atomic-rename, same as
`SafeFileTransfer`. Also consider keeping a `tasks.json.bak` of the previous version on
each save (one extra rename) and falling back to it on parse failure at load.
*Effort: small. Tests: fault-injection via the in-memory fs (fail during write → old file
intact).*

### 2. Errors are swallowed silently — ✅ done (`b31211d`, `0ace4c8`)
Done: both `TaskNodeViewModel` `catch {}` blocks now log via `ILogger`, and a file log
(`Microsoft.Extensions.Logging` + a custom `FileLoggerProvider`) writes to
`AppData/SyncMaid/logs/syncmaid.log` with single-backup size rollover; the config stores'
save paths are guarded and app-level unhandled/unobserved exceptions are logged.
Part a done (`0ace4c8`): a trigger that fails to start now sets a `TriggerError` on the
task node and shows an amber "Trigger error" badge on the card (tooltip = the reason), so a
bad watch path / cron is visible, not just logged.

## 🟠 Real functional gaps

### 3. No cancellation UI — ✅ done (`186999d`)
Done: the per-task Run button turns into a Stop button while running; a
`CancellationTokenSource` owned by `TaskNodeViewModel` is passed through
`ISyncEngine.ExecuteAsync`, and a cancelled run reverts each destination to its prior
status (a neutral outcome, not a failure).

### 4. Progress is computed but never shown — ✅ done (`186999d`)
Done: the engine's `IProgress<SyncProgress>` is marshaled via `IUiDispatcher` onto each
destination row as a live `Copying photos/2024/img_0042.jpg (3/120)` line
(`DisplayStatus` shows the progress while running, the status otherwise).

### 5. Mirror guard has no user-facing resolution path — ✅ done (`da1a08b`)
Done: a blocked mass-delete now shows a `NeedsConfirmation` state (amber), and a Review
action opens an **independent top-level window** (not an in-window overlay, so it works
when the main window is hidden — background/tray-friendly) listing the count + a sample of
the would-be deletions with Keep / Delete. Approving re-runs that destination with a
one-shot, non-persisted override; the empty-source guard stays a non-overridable hard
failure. The mass-delete threshold is now editable per destination (percentage + off
toggle) in the Mirror section.

### 6. Destructive UI actions have no confirmation — ✅ done (`3e86f47`)
Done: task-delete and destination-delete now open a modal confirmation (a reusable
`ConfirmViewModel`/`ConfirmView` shown via the in-window `DialogHost` — correct here since
deletes only fire from the visible main window) with a red destructive button; both delete
commands `await` it. Also done: `Persist` prunes statuses whose destination no longer exists,
so `status.json` no longer accumulates orphans (rewritten only when something was removed).

## 🟡 Polish / hygiene

### 7. Duplicated network-path detection — ✅ done (`0c33dbb`)
Done: `DestinationEditorViewModel` dropped its private `LooksLikeNetworkPath` and calls the
shared `SyncMaid.Core.IO.NetworkPath.IsNetwork`.

### 8. Scheduled tasks don't show the next run — ✅ done (`0c33dbb`)
Done: a scheduled task's card shows a live "next run in 2 h" badge (tooltip: absolute local
time) computed from `CronSchedule.NextOccurrenceUtc`, refreshed by a one-minute view timer
(`MainWindowViewModel.RefreshSchedules()` → each node's `RefreshNextRun()`).

### 9. Sync error text lacks the failing file — ✅ done (`acdb431`)
Done: the per-operation `TransientRetry.Execute` call is wrapped so any surviving failure
becomes a `SyncOperationException` that prefixes the relative path and verb
(e.g. `Failed to copy 'photos/img.jpg': <reason>`); that message is what the destination
status stores. Cancellation still propagates untouched.

### 10. Modal dialogs ignore the keyboard — ✅ done (`0c33dbb`)
Done: a non-generic `IDialogViewModel` exposes `RequestCancel`/`RequestAccept`; the window
routes Esc → cancel and Enter → the dialog's default action. Editors save on Enter when valid;
the delete confirm ignores Enter on purpose (can't be accepted accidentally).

### 11. Sidebar selection barely does anything — ✅ done (`0c33dbb`)
Done: selecting a task in the sidebar scrolls its card into view
(`ItemsControl.ContainerFromItem` + `BringIntoView`), so the sidebar is an actual navigator.

## ⚪ Noted, fine to defer

- **Tray icon + close-to-tray** — ✅ done (`c4cf50e`). Tray icon with a "Show main window" /
  "Exit" menu and left-click to restore, plus a "Close to the system tray instead of exiting"
  setting so watch/scheduled tasks keep running when the window is hidden. Introduced the first
  persisted app *setting* (`AppSettings` + `JsonSettingsStore` → `settings.json`, fronted by
  `IAppSettingsService`); the hide-vs-exit decision lives in a testable `TrayController` behind
  an `IShellController` seam. Design: [guide-tray-icon.md](guide-tray-icon.md).
- **OS-specific features the .NET-idiomatic way** (structural) — ✅ done (`0b70c7f`).
  `WindowsAutoStartService` now carries `[SupportedOSPlatform("windows")]` (inline guards
  removed) and the composition root selects it via `OperatingSystem.IsWindows() ? … :
  new NoOpAutoStartService()` — the guarded ternary the CA1416 analyzer recognizes, so it's
  warning-free including under AOT. macOS (LaunchAgent) / Linux (XDG) drop in as extra
  branches. (The Recycle Bin P/Invoke is the other candidate if cross-platform ever matters.)
  Full design in [guide-os-specific-services.md](guide-os-specific-services.md).
- **Move + Watch interplay** — a Move task with a Watch trigger re-fires after its own
  source deletions (debounced; next run plans zero ops). Harmless but wasteful; could
  suppress the watcher during an active run of the same task.
- **`RunAll` fire-and-forget** — executes nodes without awaiting; per-task locking makes
  this safe, but exceptions from the async void path rely on each node's own catch.
  Covered by #2's error surfacing.
- **Status history** — `status.json` keeps only the last outcome per destination. A small
  ring buffer (last N runs) would enable a history flyout; goes well with the log in #2.
- **Editor validation polish** — task editor accepts any path string; a gentle
  "folder does not exist" hint (non-blocking) would catch typos before first run.
- **Configurable config/data location (portable mode)** — ✅ done (`1ad72ed`). A Storage
  section in Settings switches between the default `%APPDATA%\SyncMaid` and a `Data` folder
  beside the executable; the mode is decided by a marker file next to the exe. Switching
  migrates `tasks.json`/`status.json`/`settings.json` (+ `.bak`s) copy→verify→delete (marker
  flipped last), refuses an unwritable target, and relaunches so the startup-wired paths take
  effect. Core `ConfigLocationService` does the work behind `IConfigLocationService`; the app
  adds `IAppRestartService`. Design: [guide-config-location.md](guide-config-location.md).

## Suggested order

1. #1 atomic config writes (safety, tiny)
2. #2 error surfacing + minimal log (unlocks debugging everything else)
3. #3+#4 cancel + progress (one PR, same wiring)
4. Title bar guide → Settings guide (feature track)
5. #5 mirror-guard confirmation flow
6. #6, #7, #8, #9, #10, #11 as filler between larger items
7. Filter composition guide (feature track, independent)
