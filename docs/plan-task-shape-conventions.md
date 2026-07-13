# Implementation plan: task shape conventions

**Status: implemented — all five plans shipped.** Kept as the record of the rationale
and the enforcement design. The rules themselves are in
[AGENT.md](../AGENT.md#task-shape-conventions); they came out of the July 2026
whole-project robustness review and the design discussion that followed it.

| Plan | Landed as |
|------|-----------|
| A — no nesting between source and destinations | `738ebf8` (+ `e18747d`, the shared `RelativePaths.Overlaps` fold) |
| B — Move is exclusive | `0d065e5` (+ `bc7c2d3`, disabled-card styling) |
| C — no same-kind path overlap across tasks | `31a2020` (+ `d5e257a`, hint wrapping) |
| D — trigger notification discipline | `0022b5e` (shared `TriggerNotifier`) |
| E — unplugged vs. empty source | `a5ef09c` |

Implementation notes beyond the plan: E additionally guarded the mirror-deletion
preview (an unplugged source during the confirm flow degrades to no-preview instead of
faulting the command) and pinned first-run-creates-a-missing-destination so the
provider-side tolerance can't regress; D's drain is deliberately non-blocking for
concurrent notifiers (an in-flight delivery must not block watcher callbacks) while
Stop keeps its blocking quiescence barrier.

Plans A–C land the three conventions and follow the same shape: one validation rule,
enforced at two layers — **editor** (friendly: hint + disabled OK/command) and **run
start** (authoritative: the run fails with a clear status and touches no files, covering
hand-edited `tasks.json`) — plus **deletion of the mechanism code the convention makes
dead**. No plan migrates persisted data: an existing task that violates a rule keeps its
config, fails its runs with the explanatory status, and shows the editor hint when
reopened. Plans D–E are the remaining robustness fixes decided alongside the
conventions.

## Plan A — no nesting between source and destinations

Rule: destination ≠ source, destination not inside source, source not inside
destination. All strategies, both directions. Siblings are fine.

### A1. Generalize the engine guard

[SyncEngine.cs](../SyncMaid.Core/Sync/SyncEngine.cs) `ExecuteDestination` already
refuses `AreEquivalent || IsDescendantOf(destination, source)` — **for Move only**.
Change it to run for **every strategy** and add the reverse direction
(`RelativePaths.IsDescendantOf(task.SourcePath, destination.LocalPath)`). One status
message for all three cases, e.g.:
`"Destination must be a separate folder outside the source (and not contain it); no files were changed."`
This check runs before planning, so the Mirror source-inside-destination case can never
reach the orphan scan.

### A2. Generalize the editor guard

[DestinationEditorViewModel.cs](../SyncMaid/ViewModels/DestinationEditorViewModel.cs):
`HasUnsafeMovePath` becomes `HasUnsafeNesting` — drop the
`SelectedStrategy == SyncStrategy.Move` condition, add the reverse `IsDescendantOf`
check, reword `PathHintText` to match the engine message. `CanOk` already consumes it.

### A3. Delete the now-dead nesting-support mechanism

The convention supersedes the "nested destination sees its own output" mitigation:

- `SyncEngine.WithoutNestedDestinationFiles` and both call sites (`ExecuteDestination`,
  `PreviewMirrorDeletionsAsync`).
- `RelativePaths.TryGetPrefixWithin` (keep `AreEquivalent`/`IsDescendantOf` — the
  validations use them) and its `RelativePathsTests` theory.
- The two engine tests that pinned the exclusion behavior
  (`Nested_destination_does_not_recopy_its_own_output`,
  `Nested_mirror_destination_keeps_its_own_content_out_of_planning`) — replaced by
  rejection tests below, which pin the same user-facing safety from the other side.

### A4. Tests

- Engine: for each strategy, destination == source / inside source / containing source →
  destination fails with the message, **zero filesystem mutations** (assert via
  `InMemoryFileSystem.AllPaths` unchanged). The Mirror source-inside-destination case is
  the must-have — it is the data-loss trap this convention closes.
- Editor: nested paths in both directions block OK and show the hint for a non-Move
  strategy (the existing Move theory generalizes); partial-UNC typing stays safe
  (existing test).
- Existing suite: the two deleted exclusion tests are the only expected removals.

## Plan B — Move is exclusive

Rule: a Move destination is its task's only destination.

### B1. Engine guard (authoritative)

In `SyncEngine.Execute`, before the destination loop: if
`task.Destinations.Count > 1 && task.Destinations.Any(d => d.Strategy == SyncStrategy.Move)`,
fail **every** destination with e.g.
`"A Move destination must be the only destination of its task; no files were changed."`
Failing all of them (rather than guessing which one is "extra") keeps the rule visible
and the fix obvious. No ordering logic is needed anywhere — that is the point.

### B2. Editor enforcement

Destination editing flows through `IDialogService.EditDestinationAsync(existing, sourcePath)`
(invoked from [TaskNodeViewModel.cs](../SyncMaid/ViewModels/TaskNodeViewModel.cs)
`AddDestination` / `EditLeaf`). Extend the seam with sibling context — e.g.
`bool hasSiblings, bool siblingIsMove` (or the sibling strategy list) — so:

- `AddDestinationCommand.CanExecute` is false when an existing destination is Move
  (tooltip/hint: "A Move destination must be the only destination").
- Inside the editor, when `hasSiblings` is true the Move option is unavailable
  (disable the strategy choice or fail `CanOk` with the hint — prefer disabling, per
  the declarative-XAML guideline: bind `IsEnabled` to a viewmodel property).
- Editing an existing sole destination to Move stays allowed; editing one of several to
  Move is blocked by the same flag.

Update `FakeDialogService` and the editor construction sites for the new parameters.

### B3. Tests

- Engine: Move+AddOnly and Move+Move tasks → all destinations fail with the message,
  no filesystem mutations; a sole-Move task still runs.
- UiTests: Add-destination command disabled on a task whose destination is Move;
  Move option unavailable when adding a second destination; editing the sole
  destination to Move allowed.

## Plan C — no same-kind path overlap across tasks

Rule: across any two tasks, source↔source and destination↔destination must not be equal
or nested, in either direction. Destination↔source relations across tasks (chaining)
are explicitly allowed and get **no** validation.

Cross-task rules cannot live in the engine (it sees one task at a time), so the
authoritative layer moves to **run start** in the UI composition, where the full task
list lives.

### C1. One overlap checker, owned by the main view model

`MainWindowViewModel` owns the task list, so it owns the rule: a small pure helper
(e.g. `TaskOverlapChecker`) that, given the other tasks' sources and destination paths,
answers whether a candidate source/destination path conflicts, and with which task.
Comparisons use the existing `RelativePaths.AreEquivalent`/`IsDescendantOf` (both
directions), so partial/unresolvable input stays non-throwing. Unit-test it directly:
equal, nested either way, siblings, chaining pairs (dest-of-A == source-of-B → no
conflict), unresolvable paths.

### C2. Editor enforcement

Follow the `directoryExists` delegate pattern already used by the editors:

- **Task editor** (source path): `MainWindowViewModel.NewTask`/`EditTask` pass a
  `sourceConflicts: string → string?` probe (returns the conflicting task's name,
  excluding the task being edited). Non-null → hint
  (`"This folder overlaps task '{name}''s source."`) + OK blocked.
- **Destination editor**: the probe threads through `IDialogService.EditDestinationAsync`
  and `TaskNodeViewModel` (same route as Plan B's sibling context — implement together)
  and checks the candidate path against **other tasks' destinations** only. Same hint +
  blocked OK.

### C3. Run-start enforcement

Hand-edited `tasks.json` can still contain conflicts, and the engine can't see them.
At run start (`TaskNodeViewModel`, before the engine is invoked — the same spot Plan B
could not use because Move-exclusivity is intra-task, engine-visible), consult the
checker via a delegate injected by `MainWindowViewModel`; on conflict, fail every
destination with
`"This task's paths overlap task '{name}'; fix the overlap and run again — no files were changed."`
This is one probe per run against an in-memory list — negligible cost, no new state,
and it naturally covers tasks added/edited later in the session.

### C4. Tests

- Checker unit tests (C1 list above).
- UiTests: creating a task whose source nests another task's source is blocked with the
  hint; adding a destination equal to another task's destination is blocked; a
  chaining pair saves fine; two conflicting tasks loaded from a hand-edited store both
  run into the fail-fast status and touch no files.

## Plan D — trigger-source notification discipline *(assignee: Claude)*

Closes the four open notification races with one shared pattern instead of four spot
fixes: **decide under the lock, deliver outside it, in decided order.**

### D1. A shared, order-preserving notifier

A small internal helper (e.g. `TriggerNotifier` in `SyncMaid.Core/Triggers`) owned by
each source. Under the source's state gate, transitions **enqueue** notifications
(`Error(ex)` / `Recovered` / `Fired`) tagged with the source's current epoch
(the existing `_generation`/`_debounceArm` counters generalize to this); after the gate
is released, the caller **drains** the queue. The notifier guarantees:

- deliveries happen outside the state gate (no subscriber runs under a source lock);
- deliveries are serialized and in enqueue order (a single-drainer flag — whoever
  enqueues while no drain is active does the draining, so Error/Recovered can never
  cross);
- `Stop()`/`Dispose()` bump the epoch under the gate, and the drainer drops entries
  from earlier epochs — preserving the pinned "no notification after Stop returns"
  guarantee without invoking events under the lock.

### D2. Adopt it in all three sources

- `WatchTriggerSource`: `OnDebounceElapsed` (Fired + Recovered currently under `_gate`),
  `OnChanged`'s factory-failure `ReportError` (currently under `_gate`),
  `OnWatcherError`'s Error→Recovered→Fired sequence, and `ReportError`/`ReportRecovered`
  themselves (the flag-set/deliver split is subsumed: the error/recovered *decision*
  becomes part of the locked transition, delivery is ordered by the queue).
- `ScheduledTriggerSource`: same for `OnTimer`'s fire/error/recovery, including the
  finally-block `ReportRecovered` that can currently land after `Stop()`.
- `PollingWatchTriggerSource`: already computes-then-delivers; converting it to the
  shared notifier removes its bespoke `ReportError`/`ReportRecovered` copies.
- Fix the one non-notifier item in the same pass: `Start()`'s resume path gets the same
  rollback the fresh-create path has (`_started = false` on a failed re-enable).

### D3. Tests

Deterministic interleaving tests using the existing fake-timer/fake-watcher harness:
crossing Error/Recovered reporters ends with the *last delivered* event matching the
flag state (the stuck-badge scenario); Stop between decide and deliver suppresses the
delivery; a Fired subscriber that blocks does not hold the source's gate (assert
Stop() completes while a delivery is in flight); resume-path failure leaves the source
restartable. Existing trigger tests must stay green — the pinned Stop guarantees are
unchanged, only delivery mechanics move.

## Plan E — distinguish an unplugged source from an empty one

`PhysicalFileSystem.EnumerateFiles` silently yields nothing for a missing root, so a
Mirror of an unplugged drive into an empty destination reports Success.

- **PhysicalFileSystem**: remove the silent empty-yield for a missing root — throw
  `DirectoryNotFoundException` (matching what `Directory.EnumerateFiles` does natively).
  The engine already routes enumeration failures into a per-destination failure
  (`sourceEnumerationError`), so Mirror of an unplugged drive becomes
  Failed("source unavailable") for every destination — including the empty-destination
  case the no-op currently masks.
- **PollingWatchTriggerSource** picks this up for free: a vanished share now surfaces
  through the existing poll boundary catch as the Error badge, with Recovered when it
  returns (verify with a test; the boundary catch already exists).
- **InMemoryFileSystem parity**: the fake has no directory concept, so "no files under
  root" and "root missing" are currently indistinguishable. Give it one (e.g.
  `CreateDirectory(path)` tracked alongside files; `EnumerateFiles` throws
  `DirectoryNotFoundException` when the root neither exists as a directory nor has
  files under it). Seed existing tests via `AddFile` (implies the root) so only tests
  that *mean* "missing root" change behavior.
- **Tests**: engine — Mirror + missing source root + empty destination → Failed
  "source unavailable" (this is the regression test for the masking bug); genuinely
  empty root (created, zero files) + empty destination → the Plan-A/no-op Success is
  preserved; polling source — missing root → Error, restored root → Recovered without
  a spurious fire.

## Sequencing and verification

A and B are independent; C's editor plumbing touches the same `EditDestinationAsync`
seam as B, so implement B and C together (or B first). D and E are independent of the
conventions and of each other. Each plan is a small, self-contained change set
(guard/mechanism + editor + deletions + tests). Standard gate after each:

```
dotnet build SyncMaid.sln
dotnet test SyncMaid.sln
dotnet publish SyncMaid/SyncMaid.csproj -c Release   # AOT, warning-free
```

plus a manual editor pass (type nested paths both directions, try adding a second
destination to a Move task, try overlapping another task's source/destination).
