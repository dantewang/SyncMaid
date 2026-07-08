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

### 6. Destructive UI actions have no confirmation
Task delete and destination delete are single-click with no undo. Add a small confirm
dialog (the `DialogHost` pattern makes this cheap), or an inline two-step button.
Related: deleting a task/destination leaves its entries in `status.json` forever —
prune statuses for unknown destination ids on save (one-line cleanup in
`MainWindowViewModel.OnStatusesUpdated` / `Persist`).

## 🟡 Polish / hygiene

### 7. Duplicated network-path detection
`DestinationEditorViewModel.LooksLikeNetworkPath` re-implements
`SyncMaid.Core.IO.NetworkPath.IsNetwork` (they drifted already: same logic, two homes).
Delete the private copy, call Core.

### 8. Scheduled tasks don't show the next run
`CronSchedule.NextOccurrenceUtc` exists and is tested; the card badge shows the raw cron
string only. Show "next run in 2 h" (tooltip: absolute local time) on the trigger badge —
cheap and makes Scheduled feel trustworthy. (Requires a lightweight refresh, e.g.
recompute when the card renders or on a 1-minute UI timer.)

### 9. Sync error text lacks the failing file
`SyncEngine.ExecuteDestination` catches per-destination exceptions and stores
`exception.Message` — but not *which file/operation* failed. Wrap the per-operation
`TransientRetry.Execute` call so the caught exception is annotated with
`operation.RelativePath` before becoming the status error.

### 10. Modal dialogs ignore the keyboard
No Esc-to-cancel / Enter-to-default in the in-window modals. Handle `KeyDown` in the
overlay (Esc → Cancel command of the current dialog VM; optionally Enter → OK when valid).
Small a11y/UX win.

### 11. Sidebar selection barely does anything
Selecting a task in the sidebar expands its card but doesn't scroll it into view.
`ScrollViewer.BringIntoView` on the selected card (or `ItemsControl` container lookup)
makes the sidebar an actual navigator.

## ⚪ Noted, fine to defer

- **Tray icon + close-to-tray** — ✅ done (`c4cf50e`). Tray icon with a "Show main window" /
  "Exit" menu and left-click to restore, plus a "Close to the system tray instead of exiting"
  setting so watch/scheduled tasks keep running when the window is hidden. Introduced the first
  persisted app *setting* (`AppSettings` + `JsonSettingsStore` → `settings.json`, fronted by
  `IAppSettingsService`); the hide-vs-exit decision lives in a testable `TrayController` behind
  an `IShellController` seam. Design: [guide-tray-icon.md](guide-tray-icon.md).
- **OS-specific features the .NET-idiomatic way** (structural) — autostart is a single
  Windows-only service with inline `OperatingSystem.IsWindows()` guards; move to the standard
  pattern (neutral interface + `[SupportedOSPlatform]` per-OS impls + a DI selector + no-op
  fallback) so macOS/Linux are drop-ins later. Full design in
  [guide-os-specific-services.md](guide-os-specific-services.md).
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
- **Configurable config/data location (portable mode)** — let the user keep everything in
  the default `%APPDATA%\SyncMaid` or beside the executable for portable installs. Full
  design in [guide-config-location.md](guide-config-location.md).

## Suggested order

1. #1 atomic config writes (safety, tiny)
2. #2 error surfacing + minimal log (unlocks debugging everything else)
3. #3+#4 cancel + progress (one PR, same wiring)
4. Title bar guide → Settings guide (feature track)
5. #5 mirror-guard confirmation flow
6. #6, #7, #8, #9, #10, #11 as filler between larger items
7. Filter composition guide (feature track, independent)
