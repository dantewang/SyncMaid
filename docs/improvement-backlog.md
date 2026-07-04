# Improvement backlog (code review findings)

**Status:** review findings + recommendations, ranked. Companion guides:
guide-custom-titlebar.md, guide-filter-composition.md, guide-settings-autostart.md.

**Overall state:** solid. 137 tests, warning-free native AOT, hardened sync engine
(atomic verified copy/move, mirror guardrail, retry, run lock), provider seam for future
backends, clean MVVM with DI. The items below are the gaps that remain.

## 🔴 High value / low effort

### 1. Config writes are not atomic (ironic, given the engine)
`JsonTaskStore.Save` and `JsonStatusStore.Save` overwrite `tasks.json` / `status.json`
in place via `WriteAllBytes`. A crash or power cut mid-write corrupts the file — for
`tasks.json` that means **losing every task definition**, the exact failure mode the sync
engine was hardened against. `IFileSystem` already has the primitives
(`CreateWriteThrough` + `Replace`): write to a sibling temp, then atomic-rename, same as
`SafeFileTransfer`. Also consider keeping a `tasks.json.bak` of the previous version on
each save (one extra rename) and falling back to it on parse failure at load.
*Effort: small. Tests: fault-injection via the in-memory fs (fail during write → old file
intact).*

### 2. Errors are swallowed silently
- `TaskNodeViewModel.StartTrigger`: `catch { /* TODO: surface trigger-start failures */ }`
  — a bad watch path means the task silently never auto-runs.
- `TaskNodeViewModel.RunAsync`: `catch { /* TODO: surface sync errors */ }` — engine-level
  failures (not per-destination ones) vanish.
- No logging anywhere in the app.
Recommendation: (a) route unexpected exceptions into the destination/task status text so
the UI shows them; (b) add a minimal rolling log file (`AppData/SyncMaid/logs/`) behind a
tiny `ILog` interface — plain `StreamWriter`, no framework needed, AOT-friendly. Being a
*sync* tool, users will eventually ask "what did it do at 03:00?" — that's the log.

## 🟠 Real functional gaps

### 3. No cancellation UI
`ISyncEngine.ExecuteAsync` takes a `CancellationToken`, but the UI never passes one — a
large or stuck sync (network destination!) cannot be stopped. Make the per-task Run button
turn into a Stop button while running (`CancellationTokenSource` owned by
`TaskNodeViewModel`; cancelled outcome shown as a neutral status, not a failure).

### 4. Progress is computed but never shown
The engine reports `IProgress<SyncProgress>` (current file, N of M) and the UI ignores it.
While running, show on the destination row: `Copying photos/2024/img_0042.jpg (3/120)`
(marshal via the existing `IUiDispatcher`). Pairs naturally with #3.

### 5. Mirror guard has no user-facing resolution path
When `MirrorGuard` aborts a mass delete, the destination just shows *Failed* with the
guard message. There is no way to say "yes, I really deleted half my source, proceed".
Recommendation: a per-run confirmation flow — status gains a "needs confirmation" state;
clicking it opens a dialog listing the would-be deletions (or a count + sample) with
"Delete N files" / "Keep them" actions; proceeding re-runs that destination with the
guard bypassed **once** (an explicit `overrideMassDelete` flag on `ExecuteAsync`, never
persisted).

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

- **Tray icon + minimize-to-tray** — natural companion to autostart (see
  guide-settings-autostart.md); Avalonia has `TrayIcon` support. Watch/scheduled tasks
  make SyncMaid a background app; today closing the window kills the triggers.
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

## Suggested order

1. #1 atomic config writes (safety, tiny)
2. #2 error surfacing + minimal log (unlocks debugging everything else)
3. #3+#4 cancel + progress (one PR, same wiring)
4. Title bar guide → Settings guide (feature track)
5. #5 mirror-guard confirmation flow
6. #6, #7, #8, #9, #10, #11 as filler between larger items
7. Filter composition guide (feature track, independent)
