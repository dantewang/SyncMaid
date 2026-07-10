# Robustness Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve every actionable finding in `docs/robustness-review-2026-07.md`, with one independently verified commit per finding ID.

**Architecture:** Work in dependency order while preserving one-ID commit boundaries. Each correctness change follows red-green-refactor: add the smallest regression test named below, verify the expected failure, implement only that ID, run its focused test project, inspect the staged diff, and commit. Shared mechanisms are extracted in their own D-series commits before the dependent correctness commit.

**Tech Stack:** .NET 10, C#, Avalonia UI, CommunityToolkit.Mvvm, xUnit, Cronos, Windows filesystem APIs.

## Global Constraints

- Follow `AGENT.md`: use declarative XAML whenever UI behavior can be expressed in XAML.
- Do not add defensive checks at consumers; normalize at construction and guard only real process/thread/P/Invoke boundaries.
- Do not add AI attribution trailers to commits.
- Each ID below produces exactly one conventional commit and includes only files needed by that ID.
- Before every commit run the focused tests listed for the task; before completion run `dotnet build SyncMaid.sln`, `dotnet test SyncMaid.sln`, and `dotnet publish SyncMaid/SyncMaid.csproj -c Release`.
- For tasks touching editors, triggers, or `TaskNodeViewModel`, include the review document's manual UI scenario in the final manual pass.

---

## Dependency-ordered tasks

### Task 1: D2 — Centralize root/relative path operations

**Files:** create `SyncMaid.Core/IO/RelativePaths.cs`; modify `SyncPlanner.cs`, `LocalDestinationProvider.cs`, `PollingWatchTriggerSource.cs`; test `SyncMaid.Core.Tests/IO/RelativePathsTests.cs`.

- [ ] Add tests `Join_normalizes_root_separator` and `Relationship_is_case_and_separator_insensitive`; run the new test class and confirm missing API failures.
- [ ] Implement one `RelativePaths.Join` plus full-path equality/nesting helpers using `Path.GetFullPath` and Windows `OrdinalIgnoreCase`; replace the three duplicate joins.
- [ ] Run `dotnet test SyncMaid.Core.Tests/SyncMaid.Core.Tests.csproj --filter FullyQualifiedName~RelativePathsTests` and the full Core test project.
- [ ] Commit `refactor(core): centralize relative path handling`.

### Task 2: A1 — Reject destructive Move destinations

**Files:** modify `SyncEngine.cs`, `DestinationEditorViewModel.cs`; tests `SyncEngineGuardTests.cs`, `DestinationEditorViewModelTests.cs`.

- [ ] Add engine cases for source-equal and source-nested Move destinations proving failure status and intact source, plus an editor case covering case/separator-insensitive equality; confirm failures.
- [ ] Use the D2 helper at the destination boundary; reject equal/nested Move roots before planning and expose the existing path hint in the editor.
- [ ] Run the two focused test classes, then Core and UI test projects.
- [ ] Commit `fix(sync): reject destructive move destinations`.

### Task 3: A2 — Normalize filter patterns at construction

**Files:** modify `FilterRule.cs`; tests `FilterRuleTests.cs`; create `SyncMaid.UiTests/ViewModels/FilterGroupViewModelTests.cs`.

- [ ] Add constructor-form tests for `photos/`, `/photos/`, `*.jpg`, `.jpg`, and `jpg`, plus one `AddRule` round-trip; confirm current mismatches.
- [ ] Normalize once in record constructors and cache match strings used by `Matches`, with no per-call normalization/allocation.
- [ ] Run filtering Core tests and the focused UI test, then both test projects.
- [ ] Commit `fix(filters): normalize user patterns at construction`.

### Task 4: A3 — Guard zero filtered source files

**Files:** modify `SyncEngine.cs`; test `SyncEngineGuardTests.cs`.

- [ ] Add `Mirror_with_no_filtered_source_files_fails_without_deletions` for both small destination and zero threshold; assert the filters-specific status text and confirm the test fails.
- [ ] Pass the filtered count into the hard empty-source guard and distinguish the no-match message.
- [ ] Run the focused class and full Core tests.
- [ ] Commit `fix(sync): guard mirror when filters match nothing`.

### Task 5: A4 — Make config-location switching transactional

**Files:** modify `ConfigLocationService.cs`; test `ConfigLocationServiceTests.cs`.

- [ ] Add fault-injection cases for marker-write failure retaining the active source and source-delete failure still completing the switch; confirm failures.
- [ ] Reorder phases to copy, flip marker, then best-effort source cleanup.
- [ ] Run the focused class and full Core tests.
- [ ] Commit `fix(config): switch locations before cleanup`.

### Task 6: D1 — Extract the shared JSON config-file pipeline

**Files:** create `JsonConfigFile.cs`; modify the three JSON stores; add `JsonConfigFileTests.cs`; keep source-generation contexts behavior unchanged.

- [ ] Add helper-level happy-path, primary-corrupt/backup-valid, whitespace, missing-file, and save tests; confirm missing helper failure.
- [ ] Move primary/backup load and `AtomicFile` save into generic `TryLoadWithBackup<T>`/`Save<T>` APIs taking `JsonTypeInfo<T>`; make stores thin adapters.
- [ ] Run all persistence tests and full Core tests.
- [ ] Commit `refactor(config): share JSON store persistence pipeline`.

### Task 7: A5 — Recover stores from read and required-member failures

**Files:** modify `JsonConfigFile.cs` and JSON source-generation contexts/options; test `JsonConfigFileTests.cs` plus store tests.

- [ ] Add helper fault-injection for `IOException` primary reads and task JSON missing `Destinations`/`Trigger`, asserting backup then empty-default behavior; cover all three store adapters; confirm failures.
- [ ] Treat `IOException`, `UnauthorizedAccessException`, and `JsonException` as recoverable per attempt and enable `RespectRequiredConstructorParameters` on every relevant context.
- [ ] Run all persistence tests and full Core tests.
- [ ] Commit `fix(config): recover from unreadable or incomplete JSON`.

### Task 8: B4 — Surface and restart watcher errors

**Files:** modify `ITriggerSource.cs`, `WatchTriggerSource.cs`, `TaskNodeViewModel.cs`, test fakes and `WatchTriggerSourceTests.cs`/`TaskNodeViewModelTests.cs`.

- [ ] Add tests for one in-place watcher restart, failed restart raising `Error`, and the task node showing its existing trigger-error badge; confirm failures.
- [ ] Add a default no-op-compatible `Error` event contract, recreate the watcher after native errors, and route raised errors through the existing badge/log path. Leave buffer sizing to E4.
- [ ] Run trigger and node focused tests, then both test projects.
- [ ] Commit `fix(triggers): recover and report watcher failures`.

### Task 9: B1 — Harden scheduled trigger lifecycle

**Files:** modify `ScheduledTriggerSource.cs`; create `ScheduledTriggerSourceTests.cs`.

- [ ] Add injectable clock/timer seam tests for long-delay clamping/recheck, fire-and-rearm, stop racing fire, dispose during fire, and a throwing handler; confirm failures.
- [ ] Clamp each arm to the timer maximum, re-check occurrence on early wake, track stopped state under `_gate`, and catch callback-boundary exceptions through `Error`.
- [ ] Run scheduled-trigger tests and full Core tests.
- [ ] Commit `fix(triggers): harden scheduled timer lifecycle`.

### Task 10: B2 — Contain polling enumeration failures

**Files:** modify `PollingWatchTriggerSource.cs`; test `PollingWatchTriggerSourceTests.cs`.

- [ ] Add a filesystem iterator that throws mid-enumeration, then succeeds unchanged; assert no throw/fire and comparison against the last good snapshot; confirm failure.
- [ ] Guard the entire timer poll boundary for I/O/access failures, retain the old snapshot, raise `Error`, and retry on the next tick.
- [ ] Run polling-trigger tests and full Core tests.
- [ ] Commit `fix(triggers): contain polling enumeration failures`.

### Task 11: B3 — Make task-run cleanup unconditional

**Files:** modify `TaskNodeViewModel.cs`, `FakeUiDispatcher.cs`; test `TaskNodeViewModelTests.cs`.

- [ ] Add throwing-engine tests proving the interlock releases, children become Failed with the message, a follow-up manual run works, dispatcher owns child enumeration, and completion-time Stop does not throw; confirm failures.
- [ ] Enter `try` immediately after the interlock, snapshot children through `IUiDispatcher`, mark failure statuses in `catch`, and use a local CTS without null/dispose races; document the fake dispatcher's single-thread limitation.
- [ ] Run node tests and full UI tests.
- [ ] Commit `fix(ui): make task run lifecycle exception safe`.

### Task 12: B5 — Snapshot persisted statuses under lock

**Files:** modify `MainWindowViewModel.cs`, `TaskNodeViewModel.cs`; test `MainWindowViewModelTests.cs`.

- [ ] Add a test that mutates the original dictionary after node creation and proves the node saw a read-only snapshot; confirm current live-instance behavior.
- [ ] Copy `_statuses` under `_statusGate` and require `IReadOnlyDictionary` in the node constructor.
- [ ] Run main-window and status-restore tests, then full UI tests.
- [ ] Commit `fix(ui): snapshot statuses before node creation`.

### Task 13: E5 — Centralize trigger-failure reporting

**Files:** modify `TaskNodeViewModel.cs`; test `TaskNodeViewModelTests.cs`.

- [ ] Add/extend tests to assert identical log and badge behavior for start and runtime trigger failures.
- [ ] Extract one private log-plus-badge helper and replace both sequences without changing behavior.
- [ ] Run node tests and full UI tests.
- [ ] Commit `refactor(ui): centralize trigger failure reporting`.

### Task 14: B6 — Coalesce run requests instead of stopping triggers

**Files:** modify `TaskNodeViewModel.cs`; extend `FakeSyncEngine.cs`/`FakeTriggerSource.cs`; tests `TaskNodeViewModelTests.cs` and `PollingWatchTriggerSourceTests.cs`.

- [ ] Add gated-engine tests for many in-flight trigger requests yielding exactly one follow-up, confirmed mass-delete winning the pending slot, and an external mid-run change being included; confirm failures.
- [ ] Remove Stop/restart suppression; add one latest-request pending slot protected by the run interlock and launch one follow-up from `finally`.
- [ ] Run node, polling, and engine focused tests, then both test projects.
- [ ] Commit `fix(sync): coalesce requests while a task is running`.

### Task 15: B7 — Cancel task runs during edit/delete

**Files:** modify `TaskNodeViewModel.cs`, `MainWindowViewModel.cs`; tests `TaskNodeViewModelTests.cs`, `MainWindowViewModelTests.cs`.

- [ ] Add gated-engine tests proving delete cancels its token and edit never overlaps old/new executions; confirm failures.
- [ ] Make node disposal cancel its active CTS and make edit sequencing wait for the old node's run before activating its replacement trigger.
- [ ] Run focused UI classes and full UI tests.
- [ ] Commit `fix(ui): cancel in-flight runs when replacing tasks`.

### Task 16: C1 — Exercise physical filesystem safety paths

**Files:** create `PhysicalFileSystemIntegrationTests.cs` and any serialized xUnit collection definition; no production change unless a test exposes a real defect belonging to C1.

- [ ] Add temp-directory tests for create/write-through/replace/stamps, `SafeFileTransfer.Copy` happy and verification failure, and `AtomicFile` preserving the original across a pre-replace failure; mark Windows-only cases explicitly.
- [ ] Run the new integration class twice to verify cleanup and isolation, then full Core tests.
- [ ] Commit `test(io): cover physical filesystem safety paths`.

### Task 17: D3 — Remove unused filesystem copy/move members

**Files:** modify `IFileSystem.cs`, `PhysicalFileSystem.cs`, `InMemoryFileSystem.cs`, and test decorators/references.

- [ ] Use `rg "CopyFile|MoveFile"` to record callers and confirm only dead interface/implementations/tests are involved.
- [ ] Remove both members and implementations; update test doubles to compile without adding replacements.
- [ ] Run build and full Core tests; re-run `rg` and confirm no removed-member references.
- [ ] Commit `refactor(io): remove unused copy and move members`.

### Task 18: C2 — Test the production Move safety path

**Files:** modify `SafeFileTransfer.cs`, `SafeFileTransferTests.cs`, `SyncApplierTests.cs`.

- [ ] Add applier fault cases for write failure and stamp mismatch retaining source, plus verified copy deleting it; confirm the new tests catch production behavior.
- [ ] Delete dead `SafeFileTransfer.Move` and its tests; keep the provider-seam applier path as the only Move implementation.
- [ ] Run applier/transfer tests and full Core tests; confirm `rg "SafeFileTransfer\.Move"` is empty.
- [ ] Commit `test(sync): cover the production move safety path`.

### Task 19: C3 — Align fake missing-path and retry semantics

**Files:** modify `InMemoryFileSystem.cs`, `TransientRetry.cs`; tests `InMemoryFileSystemTests.cs`, `TransientRetryTests.cs`, engine retry tests.

- [ ] Add exact exception-type tests, pin `FileNotFoundException` as non-transient, and cover deletion between plan/apply end-to-end; confirm failures.
- [ ] Throw physical-equivalent missing file/directory exceptions and exclude vanished files from transient retries.
- [ ] Run the three focused classes and full Core tests.
- [ ] Commit `fix(tests): align fake filesystem retry semantics`.

### Task 20: C4 — Pin the UI empty-source contract

**Files:** modify `FakeSyncEngine.cs`; test `TaskNodeViewModelTests.cs`.

- [ ] Add an `EmptySource` result case asserting Failed state and no review/confirm command path; confirm the missing coverage.
- [ ] Document the exact production `SyncEngine` contract mirrored by the fake without changing its behavior.
- [ ] Run node tests and full UI tests.
- [ ] Commit `test(ui): cover the empty-source confirmation contract`.

### Task 21: C5 — Define local-time cron semantics

**Files:** modify `ScheduledTriggerSource.cs`, editor hint in the relevant `.axaml`/viewmodel resource; tests `CronScheduleTests.cs` and scheduled-trigger tests.

- [ ] Add tests pinning `TimeZoneInfo.Local`, a DST transition, and month-end `0 0 31 * *`; confirm UTC behavior fails the semantic assertion.
- [ ] Evaluate occurrences with Cronos' local timezone overload and declare the interpretation in the existing cron hint UI.
- [ ] Run cron/trigger tests and full Core/UI tests.
- [ ] Commit `fix(triggers): evaluate cron schedules in local time`.

### Task 22: D4 — Centralize leaf-filter descriptions

**Files:** modify `FilterDescriber.cs`, `DestinationNodeViewModel.cs`, `FilterRuleViewModel.cs`; relevant UI tests.

- [ ] Add parameterized description tests for all rule kinds and fallback; confirm both callers currently own switches.
- [ ] Move the leaf switch into `FilterDescriber` and delegate from both viewmodels.
- [ ] Run filter-related UI tests and full UI tests; use `rg` to confirm one switch remains.
- [ ] Commit `refactor(ui): centralize leaf filter descriptions`.

### Task 23: D5 — Share editor dialog mechanics

**Files:** create `EditorDialogViewModel.cs`; modify both editor viewmodels and their tests.

- [ ] Pin Enter/request-accept, browse, cancel, ID preservation, and missing-directory hint behavior for both editors.
- [ ] Extract the duplicated mechanics into `EditorDialogViewModel<T>` while leaving editor-specific validation in derived classes.
- [ ] Run both editor test classes and full UI tests.
- [ ] Commit `refactor(ui): share editor dialog mechanics`.

### Task 24: D6 — Enumerate each sync tree once

**Files:** modify `SyncEngine.cs`, `SyncPlanner.cs` and plan result types; extend counting filesystem tests in `SyncEngineStatusTests.cs`/`SyncPlannerTests.cs`.

- [ ] Add counting tests proving one source walk per run, one destination walk per Mirror plan, and no separate destination count walk; confirm current counts fail.
- [ ] Hoist the source snapshot to `Execute`, build one destination path/stamp snapshot in the planner, and carry destination count with the plan result for `MirrorGuard`.
- [ ] Run planner/engine/guard tests and full Core tests.
- [ ] Commit `perf(sync): enumerate source and destination trees once`.

### Task 25: D7 — Defer polling baseline off the UI thread

**Files:** modify `PollingWatchTriggerSource.cs`; test `PollingWatchTriggerSourceTests.cs`.

- [ ] Add a blocking/counting filesystem test proving `Start()` returns without enumeration and first tick establishes a no-fire baseline; confirm current synchronous walk.
- [ ] Arm due-time zero and let the first callback establish the baseline while preserving later diff behavior.
- [ ] Run polling tests and full Core tests.
- [ ] Commit `perf(triggers): defer polling baseline to timer callback`.

### Task 26: D8 — Use declarative UI resources for status and maximize state

**Files:** modify `SyncOutcomeToBrushConverter.cs`, `App.axaml`, `MainWindow.axaml`, `MainWindow.axaml.cs`; related converter/headless tests.

- [ ] Add tests resolving every status brush from keyed resources and verifying the maximized selector selects the restore glyph.
- [ ] Replace converter hex literals with resource lookup and move maximize/restore glyph switching into a `Window[WindowState=Maximized]` style; leave only decoration margin handling in code-behind.
- [ ] Run focused headless UI tests and full UI tests.
- [ ] Commit `refactor(ui): declare status resources and window glyphs in XAML`.

### Task 27: E1 — Normalize recycle paths for Win32 interop

**Files:** modify `PhysicalFileSystem.cs`; extend physical filesystem integration tests where shell side effects can be avoided through a path-normalization seam.

- [ ] Add a seam-level assertion that mixed separators become `Path.GetFullPath` backslash form before `SHFileOperation`; confirm failure.
- [ ] Normalize once at the P/Invoke boundary.
- [ ] Run physical filesystem tests and full Core tests.
- [ ] Commit `fix(io): normalize recycle paths for Win32`.

### Task 28: E2 — Preserve transfer exceptions when cleanup fails

**Files:** modify `SafeFileTransfer.cs`; test `SafeFileTransferTests.cs`.

- [ ] Add a fault test where transfer and temp cleanup both fail, asserting the transfer exception remains the observed error; confirm cleanup currently replaces it.
- [ ] Best-effort only the catch-block temp deletion, without masking the original exception.
- [ ] Run transfer tests and full Core tests.
- [ ] Commit `fix(sync): preserve transfer failure during cleanup`.

### Task 29: E3 — Clamp persisted thresholds before decimal conversion

**Files:** modify `DestinationEditorViewModel.cs`; test `DestinationEditorViewModelTests.cs`.

- [ ] Add editor-open cases for very large positive/negative persisted doubles and assert 1..100 percent; confirm current overflow.
- [ ] Apply `Math.Clamp(existing.MassDeleteThreshold * 100, 1, 100)` before casting to decimal.
- [ ] Run destination editor tests and full UI tests.
- [ ] Commit `fix(ui): clamp persisted delete threshold safely`.

### Task 30: E4 — Increase watcher buffer capacity

**Files:** modify `WatchTriggerSource.cs`; test `WatchTriggerSourceTests.cs` through the watcher-factory seam introduced for B4.

- [ ] Add an assertion that newly created watchers use `64 * 1024`; confirm current 8 KB default.
- [ ] Set `InternalBufferSize = 64 * 1024` during watcher creation.
- [ ] Run watcher tests and full Core tests.
- [ ] Commit `fix(triggers): increase filesystem watcher buffer`.

## Final verification and review

- [ ] Re-read every acceptance criterion in `docs/robustness-review-2026-07.md` and map it to a passing test, code inspection, or manual scenario.
- [ ] Run `dotnet build SyncMaid.sln` and require exit code 0 with zero warnings.
- [ ] Run `dotnet test SyncMaid.sln` and record exact passed/skipped/failed totals.
- [ ] Run `dotnet publish SyncMaid/SyncMaid.csproj -c Release` and require exit code 0 with zero warnings.
- [ ] Run the app and manually exercise: run/stop, mass-delete approval, edit/delete during an active run, watcher failure badge, both editors, and maximize/restore glyph.
- [ ] Run `git status --short`, `git log --reverse --format="%h %s" master..HEAD`, and verify every ID has exactly one corresponding commit and no unrelated files are present.
